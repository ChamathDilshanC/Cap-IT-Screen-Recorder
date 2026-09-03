using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Overlay;

/// <summary>
/// The floating annotation tool palette — pen / line / arrow / rectangle / ellipse / text, the six
/// preset colours, three thickness presets, and undo/clear. A hand-rolled Win32 layered window drawn
/// with GDI+ (same mechanism as <see cref="AnnotationOverlayWindow"/>), deliberately kept as its own
/// window so it can be flagged <c>WDA_EXCLUDEFROMCAPTURE</c>: the presenter sees and uses it, but it
/// never appears in the recording or the live preview — only the strokes it produces (which live on
/// <see cref="AnnotationOverlayWindow"/>) do.
/// </summary>
/// <remarks>
/// Created and driven entirely on the UI thread, like <see cref="AnnotationOverlayWindow"/>. Raises
/// its events on that thread; <see cref="AnnotationOverlayService"/> forwards them to the overlay
/// window and up to the view model.
/// </remarks>
internal sealed class AnnotationToolbarWindow : IDisposable
{
    private const string WindowClassName = "CapITAnnotationToolbarWindow";

    private const int BarHeight = 54;
    private const int Pad = 10;
    private const int Gap = 6;
    private const int GripW = 16;
    private const int ToolW = 30;
    private const int SwatchW = 22;
    private const int ThickW = 26;
    private const int SepW = 11;

    private static readonly (AnnotationTool Tool, string Name)[] Tools =
    [
        (AnnotationTool.Pen, "Pen"),
        (AnnotationTool.Line, "Line"),
        (AnnotationTool.Arrow, "Arrow"),
        (AnnotationTool.Rectangle, "Rectangle"),
        (AnnotationTool.Ellipse, "Ellipse"),
        (AnnotationTool.Text, "Text"),
    ];

    // Matches AnnotationColorOption.All (kept as System.Drawing here to avoid a WinRT dependency in a
    // pure-GDI+ file — AnnotationOverlayService maps back by RGB).
    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 57, 255, 20),
        Color.FromArgb(255, 255, 32, 32),
        Color.FromArgb(255, 255, 221, 0),
        Color.FromArgb(255, 255, 0, 220),
        Color.FromArgb(255, 0, 220, 255),
        Color.FromArgb(255, 255, 255, 255),
    ];

    // S / M / L. The Annotations-tab slider still allows any 2..20 value; picking one here snaps to it.
    private static readonly float[] Thicknesses = [4f, 8f, 14f];

    private int _width;

    private nint _hwnd;
    private int _x, _y;

    private nint _memoryDc;
    private nint _dibSection;
    private nint _previousBitmap;
    private nint _dibBits;
    private Bitmap? _surface;
    private Graphics? _graphics;

    private AnnotationTool _activeTool = AnnotationTool.Pen;
    private Color _activeColor = Palette[0];
    private float _activeThickness = 6f;

    private readonly List<(RectangleF Bounds, Action OnClick)> _hits = [];

    private bool _dragging;
    private PointStruct _dragAnchorScreen;
    private int _dragAnchorX, _dragAnchorY;

    private int _monitorX, _monitorY, _monitorW, _monitorH;
    private bool _disposed;

    public event Action<AnnotationTool>? ToolSelected;
    public event Action<Color>? ColorSelected;
    public event Action<float>? ThicknessSelected;
    public event Action? UndoRequested;
    public event Action? ClearRequested;

    private static WndProcDelegate? _classWndProc;
    private static bool _classRegistered;
    private static readonly Dictionary<nint, AnnotationToolbarWindow> Instances = [];

    public void ShowOverMonitor(MonitorInfo monitor)
    {
        EnsureWindowClass();

        // Mirror the left-to-right advance in Render() exactly: grip, tools, sep, swatches, sep,
        // thickness, sep, undo, clear, then trailing padding.
        _width = Pad + (GripW + Gap)
                 + Tools.Length * (ToolW + Gap) + SepW
                 + Palette.Length * (SwatchW + Gap) + SepW
                 + Thicknesses.Length * (ThickW + Gap) + SepW
                 + (ToolW + Gap) + ToolW + Pad;

        _monitorX = monitor.X;
        _monitorY = monitor.Y;
        _monitorW = Math.Max(1, monitor.Width);
        _monitorH = Math.Max(1, monitor.Height);

        _x = _monitorX + Math.Max(0, (_monitorW - _width) / 2);
        _y = _monitorY + 16;

        if (_hwnd == nint.Zero)
        {
            _hwnd = CreateWindowExW(
                WsExLayered | WsExToolWindow | WsExNoActivate | WsExTopmost,
                WindowClassName, string.Empty, WsPopup,
                _x, _y, _width, BarHeight,
                nint.Zero, nint.Zero, GetModuleHandleW(null), nint.Zero);

            if (_hwnd == nint.Zero) return;
            Instances[_hwnd] = this;

            // The whole point of a separate window: keep the palette out of DXGI Desktop Duplication /
            // WGC output. Available since Windows 10 2004 (the app's min target); if it somehow fails
            // the toolbar just becomes visible in the capture, which is cosmetic, not broken.
            try { SetWindowDisplayAffinity(_hwnd, WdaExcludeFromCapture); } catch { /* best effort */ }
        }

        SetWindowPos(_hwnd, HwndTopmost, _x, _y, _width, BarHeight, SwpNoActivate | SwpShowWindow);
        ShowWindow(_hwnd, SwShowNoActivate);

        RebuildSurface();
        Render();
    }

    public void Show()
    {
        if (_hwnd != nint.Zero) ShowWindow(_hwnd, SwShowNoActivate);
    }

    public void Hide()
    {
        if (_hwnd != nint.Zero) ShowWindow(_hwnd, SwHide);
    }

    /// <summary>Reflects a tool/colour/thickness change made elsewhere (e.g. the Annotations tab) so the toolbar's highlights stay honest.</summary>
    public void SetActiveState(AnnotationTool tool, Color color, float thickness)
    {
        _activeTool = tool;
        _activeColor = color;
        _activeThickness = thickness;
        Render();
    }

    // --- Rendering -----------------------------------------------------------------------------

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
                biHeight = -BarHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            _dibSection = CreateDIBSection(_memoryDc, ref header, 0, out _dibBits, nint.Zero, 0);
            if (_dibSection == nint.Zero) return;

            _previousBitmap = SelectObject(_memoryDc, _dibSection);
            _surface = new Bitmap(_width, BarHeight, _width * 4, PixelFormat.Format32bppPArgb, _dibBits);
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
        if (_hwnd == nint.Zero || _graphics is null) return;

        _hits.Clear();
        _graphics.Clear(Color.Transparent);

        var panel = new RectangleF(0, 0, _width, BarHeight);
        using (var bg = new SolidBrush(Color.FromArgb(238, 26, 26, 30)))
        using (var border = new Pen(Color.FromArgb(255, 72, 72, 84), 1f))
        using (var path = RoundedRect(new RectangleF(0.5f, 0.5f, _width - 1, BarHeight - 1), 10))
        {
            _graphics.FillPath(bg, path);
            _graphics.DrawPath(border, path);
        }

        float x = Pad;
        float mid = BarHeight / 2f;

        // Drag grip
        var gripRect = new RectangleF(x, 0, GripW, BarHeight);
        using (var dot = new SolidBrush(Color.FromArgb(255, 120, 120, 132)))
        {
            for (int gy = -1; gy <= 1; gy++)
            for (int gx = 0; gx <= 1; gx++)
                _graphics.FillEllipse(dot, x + 3 + gx * 6, mid + gy * 6 - 1.5f, 3, 3);
        }
        _ = gripRect; // grip has no button action — a click here falls through to window drag
        x += GripW + Gap;

        // Tools
        foreach (var (tool, _) in Tools)
        {
            var r = new RectangleF(x, (BarHeight - ToolW) / 2f, ToolW, ToolW);
            DrawButtonBackground(r, _activeTool == tool);
            DrawToolGlyph(tool, r);
            var captured = tool;
            _hits.Add((r, () => SelectTool(captured)));
            x += ToolW + Gap;
        }

        x += Separator(x);

        // Colours
        foreach (var color in Palette)
        {
            var r = new RectangleF(x, (BarHeight - SwatchW) / 2f, SwatchW, SwatchW);
            using (var b = new SolidBrush(color))
                _graphics.FillEllipse(b, r.X, r.Y, r.Width, r.Height);
            if (ColorsEqual(color, _activeColor))
                using (var ring = new Pen(Color.White, 2.5f))
                    _graphics.DrawEllipse(ring, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            else
                using (var ring = new Pen(Color.FromArgb(255, 90, 90, 100), 1f))
                    _graphics.DrawEllipse(ring, r.X, r.Y, r.Width, r.Height);
            var captured = color;
            _hits.Add((r, () => SelectColor(captured)));
            x += SwatchW + Gap;
        }

        x += Separator(x);

        // Thickness S / M / L
        for (int i = 0; i < Thicknesses.Length; i++)
        {
            var r = new RectangleF(x, (BarHeight - ThickW) / 2f, ThickW, ThickW);
            bool on = Math.Abs(Thicknesses[i] - _activeThickness) < 0.5f;
            DrawButtonBackground(r, on);
            float dotSize = 4 + i * 4;
            using (var b = new SolidBrush(Color.FromArgb(255, 235, 235, 240)))
                _graphics.FillEllipse(b, r.X + (r.Width - dotSize) / 2, r.Y + (r.Height - dotSize) / 2, dotSize, dotSize);
            var captured = Thicknesses[i];
            _hits.Add((r, () => SelectThickness(captured)));
            x += ThickW + Gap;
        }

        x += Separator(x);

        // Undo
        var undoRect = new RectangleF(x, (BarHeight - ToolW) / 2f, ToolW, ToolW);
        DrawButtonBackground(undoRect, false);
        DrawUndoGlyph(undoRect);
        _hits.Add((undoRect, () => UndoRequested?.Invoke()));
        x += ToolW + Gap;

        // Clear
        var clearRect = new RectangleF(x, (BarHeight - ToolW) / 2f, ToolW, ToolW);
        DrawButtonBackground(clearRect, false);
        DrawClearGlyph(clearRect);
        _hits.Add((clearRect, () => ClearRequested?.Invoke()));

        _graphics.Flush(FlushIntention.Sync);
        Present();
    }

    private float Separator(float x)
    {
        if (_graphics is not null)
            using (var p = new Pen(Color.FromArgb(255, 70, 70, 82), 1f))
                _graphics.DrawLine(p, x + SepW / 2f, 12, x + SepW / 2f, BarHeight - 12);
        return SepW;
    }

    private void DrawButtonBackground(RectangleF r, bool active)
    {
        if (_graphics is null) return;
        if (active)
        {
            using var b = new SolidBrush(Color.FromArgb(255, 92, 70, 168));
            using var path = RoundedRect(r, 7);
            _graphics.FillPath(b, path);
        }
    }

    private void DrawToolGlyph(AnnotationTool tool, RectangleF r)
    {
        if (_graphics is null) return;
        using var pen = new Pen(Color.FromArgb(255, 236, 236, 240), 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        float pad = 8;
        var box = new RectangleF(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);

        switch (tool)
        {
            case AnnotationTool.Pen:
                _graphics.DrawCurve(pen,
                [
                    new PointF(box.Left, box.Bottom),
                    new PointF(box.Left + box.Width * 0.35f, box.Top + box.Height * 0.2f),
                    new PointF(box.Left + box.Width * 0.65f, box.Bottom - box.Height * 0.1f),
                    new PointF(box.Right, box.Top),
                ]);
                break;

            case AnnotationTool.Line:
                _graphics.DrawLine(pen, box.Left, box.Bottom, box.Right, box.Top);
                break;

            case AnnotationTool.Arrow:
                _graphics.DrawLine(pen, box.Left, box.Bottom, box.Right, box.Top);
                _graphics.DrawLine(pen, box.Right, box.Top, box.Right - box.Width * 0.45f, box.Top);
                _graphics.DrawLine(pen, box.Right, box.Top, box.Right, box.Top + box.Height * 0.45f);
                break;

            case AnnotationTool.Rectangle:
                _graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                break;

            case AnnotationTool.Ellipse:
                _graphics.DrawEllipse(pen, box.X, box.Y, box.Width, box.Height);
                break;

            case AnnotationTool.Text:
                using (var font = new Font("Segoe UI", r.Height * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.FromArgb(255, 236, 236, 240)))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    _graphics.DrawString("A", font, brush, r, fmt);
                break;
        }
    }

    private void DrawUndoGlyph(RectangleF r)
    {
        if (_graphics is null) return;
        using var pen = new Pen(Color.FromArgb(255, 236, 236, 240), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float pad = 8;
        var box = new RectangleF(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
        _graphics.DrawArc(pen, box.X, box.Y, box.Width, box.Height, 30, 300);
        _graphics.DrawLine(pen, box.Left + box.Width * 0.15f, box.Top, box.Left + box.Width * 0.15f, box.Top + box.Height * 0.45f);
        _graphics.DrawLine(pen, box.Left + box.Width * 0.15f, box.Top, box.Left + box.Width * 0.55f, box.Top);
    }

    private void DrawClearGlyph(RectangleF r)
    {
        if (_graphics is null) return;
        using var pen = new Pen(Color.FromArgb(255, 240, 120, 120), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float pad = 9;
        _graphics.DrawLine(pen, r.X + pad, r.Y + pad, r.Right - pad, r.Bottom - pad);
        _graphics.DrawLine(pen, r.Right - pad, r.Y + pad, r.X + pad, r.Bottom - pad);
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

    private void Present()
    {
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero) return;
        try
        {
            var destination = new PointStruct { X = _x, Y = _y };
            var size = new SizeStruct { Cx = _width, Cy = BarHeight };
            var source = new PointStruct { X = 0, Y = 0 };
            var blend = new BlendFunction
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 0x01,
            };
            UpdateLayeredWindow(_hwnd, screenDc, ref destination, ref size, _memoryDc, ref source, 0, ref blend, 0x02);
        }
        finally
        {
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    // --- Input --------------------------------------------------------------------------------

    private void SelectTool(AnnotationTool tool)
    {
        _activeTool = tool;
        Render();
        ToolSelected?.Invoke(tool);
    }

    private void SelectColor(Color color)
    {
        _activeColor = color;
        Render();
        ColorSelected?.Invoke(color);
    }

    private void SelectThickness(float thickness)
    {
        _activeThickness = thickness;
        Render();
        ThicknessSelected?.Invoke(thickness);
    }

    private static bool ColorsEqual(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmLButtonDown:
            {
                int cx = (short)((long)lParam & 0xFFFF);
                int cy = (short)(((long)lParam >> 16) & 0xFFFF);
                foreach (var (bounds, onClick) in _hits)
                {
                    if (bounds.Contains(cx, cy)) { onClick(); return 0; }
                }
                // Missed every button — start dragging the whole bar.
                GetCursorPos(out _dragAnchorScreen);
                _dragAnchorX = _x;
                _dragAnchorY = _y;
                _dragging = true;
                SetCapture(_hwnd);
                return 0;
            }

            case WmMouseMove when _dragging:
            {
                GetCursorPos(out var now);
                int nx = _dragAnchorX + (now.X - _dragAnchorScreen.X);
                int ny = _dragAnchorY + (now.Y - _dragAnchorScreen.Y);
                nx = Math.Clamp(nx, _monitorX, _monitorX + _monitorW - _width);
                ny = Math.Clamp(ny, _monitorY, _monitorY + _monitorH - BarHeight);
                _x = nx;
                _y = ny;
                SetWindowPos(_hwnd, HwndTopmost, _x, _y, _width, BarHeight, SwpNoActivate);
                return 0;
            }

            case WmLButtonUp:
            case WmCaptureChanged:
                if (_dragging)
                {
                    _dragging = false;
                    if (GetCapture() == _hwnd) ReleaseCapture();
                }
                return 0;

            case WmMouseActivate:
                return MaNoActivate;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

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
            hCursor = LoadCursorW(nint.Zero, IdcSizeAll),
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
            Instances.Remove(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
        ReleaseSurface();
    }

    // --- Interop -----------------------------------------------------------------------------

    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    private const int WsExLayered = 0x00080000;
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
    private const int MaNoActivate = 3;

    private const uint WdaExcludeFromCapture = 0x00000011;
    private static readonly nint IdcSizeAll = new(32646);

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

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointStruct point);

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
