using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Overlay;

/// <summary>
/// Full-screen, click-through, genuinely transparent overlay the user draws annotations on. Rendered
/// with GDI+ into a per-pixel-alpha layered window via <c>UpdateLayeredWindow</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this is a hand-rolled Win32 window and not a WinUI page.</b></para>
/// <para>
/// It was a WinUI 3 <c>Window</c> with <c>Background="Transparent"</c> and WS_EX_LAYERED. That does not
/// produce a transparent window: a XAML island's swapchain is composited as opaque, so the overlay went
/// onto the desktop as a solid black sheet covering the entire display. Because this app captures the
/// desktop, everything downstream captured that sheet — the live preview, the source-picker thumbnail,
/// and the recording itself all turned black the moment the overlay was up. Annotations could therefore
/// never have worked: switching them on and recording produced a black video. Measured directly, a
/// sampled grid over the display went from 165/176 non-black pixels with the overlay gone to 0-6/176
/// with it up. Neither documented DWM escape hatch fixed it — <c>DwmEnableBlurBehindWindow</c> with an
/// empty region, nor <c>DwmExtendFrameIntoClientArea</c> with -1 margins.
/// </para>
/// <para>
/// <c>UpdateLayeredWindow</c> is the mechanism that does work, and it needs a premultiplied-ARGB
/// surface rather than a XAML tree — hence GDI+ strokes drawn into a DIB section here. It also gives
/// hit-testing for free in exactly the shape this feature needs: the OS routes clicks by the surface's
/// alpha channel, so fully transparent pixels pass input through on their own.
/// </para>
/// <para><b>Threading:</b> created and driven entirely on the UI thread. A plain Win32 window on that
/// thread has its <see cref="WndProc"/> pumped by the same message loop WinUI already runs, so no extra
/// thread or pump is needed.</para>
/// </remarks>
internal sealed class AnnotationOverlayWindow : IDisposable
{
    private const string WindowClassName = "CapITAnnotationOverlayWindow";

    /// <summary>
    /// Alpha painted over the whole surface while drawing mode is on. One is the smallest value that is
    /// still non-zero, which matters: <c>UpdateLayeredWindow</c> hit-tests by alpha, so a strictly
    /// transparent (0) canvas would pass every click through and the user could never start a stroke.
    /// At 1/255 over black it is imperceptible on screen and in the recording.
    /// </summary>
    private const byte DrawModeCanvasAlpha = 1;

    private const int PillVisibleMs = 3500;
    private const uint PillTimerId = 1;

    private sealed class Stroke
    {
        public required List<PointF> Points { get; init; }
        public required Color Color { get; init; }
        public required float Thickness { get; init; }
    }

    private readonly List<Stroke> _strokes = [];
    private Stroke? _activeStroke;

    private nint _hwnd;
    private int _x, _y, _width, _height;

    // Render surface: a top-down 32bpp DIB section, wrapped in a GDI+ Bitmap over the same memory so
    // strokes can be drawn with anti-aliasing and then handed straight to UpdateLayeredWindow.
    private nint _memoryDc;
    private nint _dibSection;
    private nint _previousBitmap;
    private nint _dibBits;
    private Bitmap? _surface;
    private Graphics? _graphics;

    private Color _penColor = Color.FromArgb(255, 57, 255, 20); // Neon Green — matches AnnotationColorOption's default
    private float _penThickness = 6;
    private bool _pillVisible;
    private bool _disposed;

    // SetWindowsHookEx-style lifetime rule: the class's WndProc is stored by Windows as a raw function
    // pointer, so the delegate has to outlive the window or the CLR frees the thunk under it.
    private static WndProcDelegate? _classWndProc;
    private static bool _classRegistered;

    // Maps HWND back to instance, so the static class WndProc can dispatch to the right overlay. Keyed
    // rather than assumed-singleton because Arm/Disarm can legitimately overlap during a monitor switch.
    private static readonly Dictionary<nint, AnnotationOverlayWindow> Instances = [];

    public bool IsDrawingModeEnabled { get; private set; }

    /// <summary>Creates (if needed) and shows the overlay positioned exactly over <paramref name="monitor"/>, in click-through mode. Safe to call again to move it to another display.</summary>
    public void ShowOverMonitor(MonitorInfo monitor)
    {
        EnsureWindowClass();

        _x = monitor.X;
        _y = monitor.Y;
        _width = Math.Max(1, monitor.Width);
        _height = Math.Max(1, monitor.Height);

        if (_hwnd == nint.Zero)
        {
            // WS_EX_LAYERED is what enables UpdateLayeredWindow. TOOLWINDOW keeps it out of the taskbar
            // and Alt+Tab; NOACTIVATE stops it stealing focus from the app being demoed; TOPMOST keeps
            // it above that app.
            _hwnd = CreateWindowExW(
                WsExLayered | WsExToolWindow | WsExNoActivate | WsExTopmost,
                WindowClassName, string.Empty, WsPopup,
                _x, _y, _width, _height,
                nint.Zero, nint.Zero, GetModuleHandleW(null), nint.Zero);

            if (_hwnd == nint.Zero) return;
            Instances[_hwnd] = this;
        }

        SetWindowPos(_hwnd, HwndTopmost, _x, _y, _width, _height, SwpNoActivate | SwpShowWindow);
        ShowWindow(_hwnd, SwShowNoActivate);

        RebuildSurface();
        SetDrawingMode(false);
    }

    /// <summary>
    /// Switches between click-through (input falls through to the apps underneath) and drawing.
    /// Deliberately leaves existing strokes alone — a presenter often wants to leave an arrow up,
    /// click something underneath it, then carry on annotating.
    /// </summary>
    public void SetDrawingMode(bool enabled)
    {
        IsDrawingModeEnabled = enabled;
        if (_hwnd == nint.Zero) return;

        // Belt and braces alongside the alpha-based hit-testing: WS_EX_TRANSPARENT guarantees
        // click-through even over pixels an already-drawn stroke has made opaque.
        var exStyle = GetWindowLongW(_hwnd, GwlExStyle);
        exStyle = enabled ? exStyle & ~WsExTransparent : exStyle | WsExTransparent;
        SetWindowLongW(_hwnd, GwlExStyle, exStyle);

        if (!enabled) EndStroke();

        // The toggle is a global hotkey pressed while another app has focus, so this pill is the only
        // confirmation the keypress registered. It stays up while drawing (where it also signals that
        // clicks are being captured) and auto-hides shortly after switching back.
        _pillVisible = true;
        KillTimer(_hwnd, PillTimerId);
        if (!enabled) SetTimer(_hwnd, PillTimerId, PillVisibleMs, nint.Zero);

        Render();
    }

    /// <summary>Sets the pen used for subsequent strokes. Existing strokes keep the pen they were drawn with, so switching color mid-recording never disturbs what is already on screen.</summary>
    public void UpdateDrawingAttributes(Color color, double thickness)
    {
        _penColor = color;
        _penThickness = (float)Math.Max(1, thickness);
    }

    /// <summary>Wipes every stroke. Wired to the Esc hotkey.</summary>
    public void ClearInk()
    {
        _strokes.Clear();
        _activeStroke = null;
        Render();
    }

    /// <summary>Removes the most recent completed stroke. Wired to the Ctrl+Shift+Z hotkey.</summary>
    public void UndoLastStroke()
    {
        if (_strokes.Count == 0) return;
        _strokes.RemoveAt(_strokes.Count - 1);
        Render();
    }

    public void HideOverlay()
    {
        if (_hwnd != nint.Zero) ShowWindow(_hwnd, SwHide);
    }

    // --- Rendering -------------------------------------------------------------------------------

    private void RebuildSurface()
    {
        ReleaseSurface();

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero) return;
        try
        {
            _memoryDc = CreateCompatibleDC(screenDc);
            if (_memoryDc == nint.Zero) return;

            var header = new BitmapInfoHeader
            {
                biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = _width,
                biHeight = -_height, // top-down, matching GDI+'s row order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BiRgb,
            };
            _dibSection = CreateDIBSection(_memoryDc, ref header, DibRgbColors, out _dibBits, nint.Zero, 0);
            if (_dibSection == nint.Zero) return;

            _previousBitmap = SelectObject(_memoryDc, _dibSection);

            // Format32bppPArgb over the DIB's own memory: GDI+ draws straight into the bytes
            // UpdateLayeredWindow will read, with no intermediate copy, and premultiplied is exactly
            // what AC_SRC_ALPHA blending expects.
            _surface = new Bitmap(_width, _height, _width * 4, PixelFormat.Format32bppPArgb, _dibBits);
            _graphics = Graphics.FromImage(_surface);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.CompositingQuality = CompositingQuality.HighQuality;
            _graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }
        finally
        {
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    private void Render()
    {
        if (_hwnd == nint.Zero || _graphics is null || _surface is null) return;

        // Clear() on a premultiplied surface writes the raw pixel value, which is what's wanted: an
        // all-but-invisible canvas that is still hit-testable while drawing.
        var canvasAlpha = IsDrawingModeEnabled ? DrawModeCanvasAlpha : (byte)0;
        _graphics.Clear(Color.FromArgb(canvasAlpha, 0, 0, 0));

        foreach (var stroke in _strokes) DrawStroke(stroke);
        if (_activeStroke is not null) DrawStroke(_activeStroke);
        if (_pillVisible) DrawStatusPill();

        _graphics.Flush(FlushIntention.Sync);
        Present();
    }

    private void DrawStroke(Stroke stroke)
    {
        if (_graphics is null || stroke.Points.Count == 0) return;

        using var pen = new Pen(stroke.Color, stroke.Thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        if (stroke.Points.Count == 1)
        {
            // A click with no drag is still a mark the user meant to make — a dot, not nothing.
            var p = stroke.Points[0];
            var r = stroke.Thickness / 2f;
            using var brush = new SolidBrush(stroke.Color);
            _graphics.FillEllipse(brush, p.X - r, p.Y - r, stroke.Thickness, stroke.Thickness);
            return;
        }

        _graphics.DrawLines(pen, stroke.Points.ToArray());
    }

    private void DrawStatusPill()
    {
        if (_graphics is null) return;

        var text = IsDrawingModeEnabled
            ? "Drawing Mode: On  ·  Ctrl+Shift+D to stop  ·  Ctrl+Shift+Z undo  ·  Esc clears"
            : "Drawing Mode: Off  ·  Ctrl+Shift+D to draw";

        using var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
        var size = _graphics.MeasureString(text, font);
        var pillWidth = size.Width + 28;
        var pillHeight = size.Height + 14;
        var rect = new RectangleF(_width - pillWidth - 24, 24, pillWidth, pillHeight);

        using var background = new SolidBrush(IsDrawingModeEnabled
            ? Color.FromArgb(225, 20, 120, 40)
            : Color.FromArgb(205, 32, 32, 32));
        using var path = RoundedRect(rect, 8);
        _graphics.FillPath(background, path);

        using var textBrush = new SolidBrush(Color.White);
        _graphics.DrawString(text, font, textBrush, rect.X + 14, rect.Y + 7);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Pushes the rendered surface to the screen, alpha and all.</summary>
    private void Present()
    {
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero) return;
        try
        {
            var destination = new PointStruct { X = _x, Y = _y };
            var size = new SizeStruct { Cx = _width, Cy = _height };
            var source = new PointStruct { X = 0, Y = 0 };
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha, // honor the surface's own per-pixel alpha
            };

            UpdateLayeredWindow(_hwnd, screenDc, ref destination, ref size, _memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    // --- Input -----------------------------------------------------------------------------------

    private void BeginStroke(int x, int y)
    {
        _activeStroke = new Stroke { Points = [new PointF(x, y)], Color = _penColor, Thickness = _penThickness };
        SetCapture(_hwnd);
        Render();
    }

    private void ExtendStroke(int x, int y)
    {
        if (_activeStroke is null) return;

        // Skip sub-pixel jitter: it bloats the point list without changing the rendered curve.
        var last = _activeStroke.Points[^1];
        if (Math.Abs(last.X - x) < 1 && Math.Abs(last.Y - y) < 1) return;

        _activeStroke.Points.Add(new PointF(x, y));
        Render();
    }

    private void EndStroke()
    {
        if (_activeStroke is null) return;
        _strokes.Add(_activeStroke);
        _activeStroke = null;
        if (GetCapture() == _hwnd) ReleaseCapture();
        Render();
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmLButtonDown:
                BeginStroke(LoWord(lParam), HiWord(lParam));
                return 0;

            case WmMouseMove:
                if (_activeStroke is not null) ExtendStroke(LoWord(lParam), HiWord(lParam));
                return 0;

            case WmLButtonUp:
            case WmCaptureChanged:
                EndStroke();
                return 0;

            case WmTimer when wParam == PillTimerId:
                KillTimer(_hwnd, PillTimerId);
                if (!IsDrawingModeEnabled)
                {
                    _pillVisible = false;
                    Render();
                }
                return 0;

            // Never take focus, even if something tries to hand it over — the point of the overlay is
            // to sit above the app being demoed without interrupting it.
            case WmMouseActivate:
                return MaNoActivate;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static int LoWord(nint value) => (short)((long)value & 0xFFFF);
    private static int HiWord(nint value) => (short)(((long)value >> 16) & 0xFFFF);

    private static void EnsureWindowClass()
    {
        if (_classRegistered) return;

        _classWndProc = static (hwnd, msg, wParam, lParam) =>
            Instances.TryGetValue(hwnd, out var instance)
                ? instance.WndProc(hwnd, msg, wParam, lParam)
                : DefWindowProcW(hwnd, msg, wParam, lParam);

        var windowClass = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_classWndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = WindowClassName,
            hCursor = LoadCursorW(nint.Zero, IdcCross),
        };

        RegisterClassExW(ref windowClass);
        _classRegistered = true;
    }

    private void ReleaseSurface()
    {
        _graphics?.Dispose();
        _graphics = null;
        _surface?.Dispose();
        _surface = null;

        if (_memoryDc != nint.Zero)
        {
            if (_previousBitmap != nint.Zero) SelectObject(_memoryDc, _previousBitmap);
            DeleteDC(_memoryDc);
            _memoryDc = nint.Zero;
            _previousBitmap = nint.Zero;
        }
        if (_dibSection != nint.Zero)
        {
            DeleteObject(_dibSection);
            _dibSection = nint.Zero;
        }
        _dibBits = nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != nint.Zero)
        {
            KillTimer(_hwnd, PillTimerId);
            Instances.Remove(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
        ReleaseSurface();
    }

    // --- Interop ---------------------------------------------------------------------------------

    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;
    private const int WsPopup = unchecked((int)0x80000000);

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmTimer = 0x0113;
    private const int MaNoActivate = 3;

    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const uint UlwAlpha = 0x00000002;

    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;
    private static readonly nint IdcCross = new(32515);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeStruct { public int Cx; public int Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLongW(nint hwnd, int index, int newLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    private static extern nint SetTimer(nint hwnd, uint id, uint elapseMs, nint callback);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(nint hwnd, uint id);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(nint hwnd, nint dstDc, ref PointStruct dstPoint, ref SizeStruct size,
        nint srcDc, ref PointStruct srcPoint, int colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);
}
