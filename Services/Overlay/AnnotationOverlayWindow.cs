using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Overlay;

/// <summary>
/// Full-screen, click-through, genuinely transparent overlay the user draws annotations on. Rendered
/// with GDI+ into a per-pixel-alpha layered window via <c>UpdateLayeredWindow</c>. Freehand pen,
/// straight line, arrow, rectangle, ellipse and a click-to-type text tool — the tool is chosen on the
/// separate <see cref="AnnotationToolbarWindow"/>, which this window knows nothing about.
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
/// <para><b>Text input</b> is fed in from <see cref="Tracking.GlobalHotkeyHook"/> (via
/// <see cref="AnnotationOverlayService"/>) rather than real keyboard focus — this window is
/// WS_EX_NOACTIVATE and never takes focus. <see cref="TextCaptureChanged"/> tells the service when to
/// start/stop routing keystrokes here.</para>
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
    private const uint AnnotationTimerId = 2;
    private const int AnnotationTimerMs = 100;

    private readonly List<AnnotationObject> _annotations = [];
    private AnnotationObject? _activeAnnotation;
    private AnnotationObject? _selectedAnnotation;
    private bool _editingSelection;
    private bool _resizingSelection;
    private int _resizeHandle = -1;
    private PointF _lastPointer;
    private PointF _drawStart;
    private bool _shiftHeld;
    private bool _altHeld;

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
    private AnnotationTool _currentTool = AnnotationTool.Pen;
    private bool _textCaptureActive;
    private bool _pillVisible;
    private bool _disposed;
    private int _fadeSeconds;

    /// <summary>Raised (on the UI thread) when the text tool starts/stops accepting keystrokes — the service uses it to route <see cref="Tracking.GlobalHotkeyHook"/> character events here and back.</summary>
    public event Action<bool>? TextCaptureChanged;

    // SetWindowsHookEx-style lifetime rule: the class's WndProc is stored by Windows as a raw function
    // pointer, so the delegate has to outlive the window or the CLR frees the thunk under it.
    private static WndProcDelegate? _classWndProc;
    private static bool _classRegistered;

    // Maps HWND back to instance, so the static class WndProc can dispatch to the right overlay. Keyed
    // rather than assumed-singleton because Arm/Disarm can legitimately overlap during a monitor switch.
    private static readonly Dictionary<nint, AnnotationOverlayWindow> Instances = [];

    public bool IsDrawingModeEnabled { get; private set; }
    public bool IsTextCaptureActive => _textCaptureActive;
    public AnnotationTool CurrentTool => _currentTool;
    public AnnotationObject? SelectedObject => _selectedAnnotation;

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

        if (!enabled)
        {
            EndAnnotation();
            CommitActiveText();
        }

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

    public void SetFadeSeconds(int seconds)
    {
        _fadeSeconds = Math.Max(0, seconds);
        if (_hwnd == nint.Zero) return;
        if (_fadeSeconds > 0)
            SetTimer(_hwnd, AnnotationTimerId, AnnotationTimerMs, nint.Zero);
        else
            KillTimer(_hwnd, AnnotationTimerId);
    }

    /// <summary>Selects the active drawing tool. Any in-progress text is committed first.</summary>
    public void SetTool(AnnotationTool tool)
    {
        if (_currentTool == tool) return;
        CommitActiveText();
        _currentTool = tool;
    }

    /// <summary>Wipes every annotation. Wired to the Esc hotkey (when not typing).</summary>
    public void ClearInk()
    {
        CancelActiveText();
        _annotations.Clear();
        _activeAnnotation = null;
        Render();
    }

    /// <summary>Removes the most recent completed annotation. Wired to the Ctrl+Shift+Z hotkey.</summary>
    public void UndoLastStroke()
    {
        if (_annotations.Count == 0) return;
        _annotations.RemoveAt(_annotations.Count - 1);
        Render();
    }

    public void DeleteSelected()
    {
        if (_selectedAnnotation is null) return;
        _annotations.Remove(_selectedAnnotation);
        _selectedAnnotation = null;
        Render();
    }

    public void DuplicateSelected()
    {
        if (_selectedAnnotation is null) return;
        var copy = _selectedAnnotation.Clone();
        _annotations.Add(copy);
        _selectedAnnotation = copy;
        Render();
    }

    public void BringSelectedToFront()
    {
        if (_selectedAnnotation is null) return;
        _annotations.Remove(_selectedAnnotation); _annotations.Add(_selectedAnnotation); Render();
    }

    public void SendSelectedToBack()
    {
        if (_selectedAnnotation is null) return;
        _annotations.Remove(_selectedAnnotation); _annotations.Insert(0, _selectedAnnotation); Render();
    }

    public void HideOverlay()
    {
        CommitActiveText();
        if (_hwnd != nint.Zero) ShowWindow(_hwnd, SwHide);
    }

    // --- Text tool ------------------------------------------------------------------------------

    /// <summary>Appends typed text to the active label. No-op if the text tool isn't mid-entry.</summary>
    public void TextAppend(string s)
    {
        if (_activeAnnotation is not { Tool: AnnotationTool.Text }) return;
        _activeAnnotation.Text += s;
        Render();
    }

    public void TextBackspace()
    {
        if (_activeAnnotation is not { Tool: AnnotationTool.Text } a || string.IsNullOrEmpty(a.Text)) return;
        a.Text = a.Text[..^1];
        Render();
    }

    public void TextNewline() => TextAppend("\n");

    /// <summary>Finalizes the active label (keeping it only if it has content) and leaves text-entry mode.</summary>
    public void TextCommit() => CommitActiveText();

    private void CommitActiveText()
    {
        if (_activeAnnotation is not { Tool: AnnotationTool.Text } a)
        {
            SetTextCapture(false);
            return;
        }
        if (!string.IsNullOrEmpty(a.Text))
        {
            if (_fadeSeconds > 0) a.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(_fadeSeconds);
            _annotations.Add(a);
        }
        _activeAnnotation = null;
        SetTextCapture(false);
        Render();
    }

    private void CancelActiveText()
    {
        if (_activeAnnotation is { Tool: AnnotationTool.Text }) _activeAnnotation = null;
        SetTextCapture(false);
    }

    private void SetTextCapture(bool on)
    {
        if (_textCaptureActive == on) return;
        _textCaptureActive = on;
        TextCaptureChanged?.Invoke(on);
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

        foreach (var annotation in _annotations) DrawAnnotation(annotation, active: false);
        if (_activeAnnotation is not null) DrawAnnotation(_activeAnnotation, active: true);
        if (_pillVisible) DrawStatusPill();

        _graphics.Flush(FlushIntention.Sync);
        Present();
    }

    private void DrawAnnotation(AnnotationObject ann, bool active)
    {
        if (_graphics is null || ann.Points.Count == 0) return;

        var strokeColor = ann.Tool == AnnotationTool.Highlighter
            ? Color.FromArgb(Math.Min(110, (int)ann.Color.A), ann.Color)
            : ann.Color;
        using var pen = new Pen(strokeColor, ann.Thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        if (ann.IsDashed || ann.Tool is AnnotationTool.DashedLine or AnnotationTool.ElbowLine)
            pen.DashStyle = DashStyle.Dash;

        switch (ann.Tool)
        {
            case AnnotationTool.Pen:
            case AnnotationTool.Highlighter:
            case AnnotationTool.Marker:
                DrawFreehand(ann, pen);
                break;

            case AnnotationTool.Line when ann.Points.Count >= 2:
                _graphics.DrawLine(pen, ann.Points[0], ann.Points[1]);
                break;
            case AnnotationTool.DashedLine when ann.Points.Count >= 2:
                _graphics.DrawLine(pen, ann.Points[0], ann.Points[1]);
                break;
            case AnnotationTool.CurvedLine:
            case AnnotationTool.Bezier when ann.Points.Count >= 2:
                using (var curve = new GraphicsPath())
                {
                    var a = ann.Points[0]; var b = ann.Points[^1];
                    curve.AddBezier(a, new PointF((a.X+b.X)/2, a.Y-60), new PointF((a.X+b.X)/2, b.Y+60), b);
                    _graphics.DrawPath(pen, curve);
                }
                break;
            case AnnotationTool.ElbowLine when ann.Points.Count >= 2:
                var e1 = ann.Points[0]; var e2 = ann.Points[1];
                _graphics.DrawLines(pen, [e1, new PointF(e2.X, e1.Y), e2]);
                break;
            case AnnotationTool.Measurement when ann.Points.Count >= 2:
                DrawArrow(pen, ann.Points[0], ann.Points[1], ann.Thickness);
                DrawArrow(pen, ann.Points[1], ann.Points[0], ann.Thickness);
                break;

            case AnnotationTool.Arrow when ann.Points.Count >= 2:
                DrawArrow(pen, ann.Points[0], ann.Points[1], ann.Thickness);
                break;
            case AnnotationTool.DoubleArrow when ann.Points.Count >= 2:
                DrawArrow(pen, ann.Points[0], ann.Points[1], ann.Thickness);
                DrawArrow(pen, ann.Points[1], ann.Points[0], ann.Thickness);
                break;

            case AnnotationTool.Rectangle when ann.Points.Count >= 2:
            {
                var r = Normalize(ann.Points[0], ann.Points[1]);
                _graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                if (ann.IsFilled) using (var b = new SolidBrush(Color.FromArgb(ann.FillOpacity, ann.Color))) _graphics.FillRectangle(b, r);
                break;
            }
            case AnnotationTool.Square:
            case AnnotationTool.Circle when ann.Points.Count >= 2:
            {
                var r = Normalize(ann.Points[0], ann.Points[1]);
                var side = Math.Min(r.Width, r.Height);
                r = new RectangleF(r.X, r.Y, side, side);
                if (ann.Tool == AnnotationTool.Square) _graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                else _graphics.DrawEllipse(pen, r);
                break;
            }

            case AnnotationTool.Ellipse when ann.Points.Count >= 2:
            {
                var r = Normalize(ann.Points[0], ann.Points[1]);
                _graphics.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                if (ann.IsFilled) using (var b = new SolidBrush(Color.FromArgb(ann.FillOpacity, ann.Color))) _graphics.FillEllipse(b, r);
                break;
            }
            case AnnotationTool.RoundedRectangle when ann.Points.Count >= 2:
            case AnnotationTool.Callout when ann.Points.Count >= 2:
            case AnnotationTool.SpeechBubble when ann.Points.Count >= 2:
            case AnnotationTool.ThoughtBubble when ann.Points.Count >= 2:
            case AnnotationTool.Label when ann.Points.Count >= 2:
            case AnnotationTool.Tag when ann.Points.Count >= 2:
            case AnnotationTool.PointerCallout when ann.Points.Count >= 2:
            {
                var r = Normalize(ann.Points[0], ann.Points[1]);
                using var path = RoundedRect(r, Math.Min(16, Math.Min(r.Width, r.Height) / 3));
                _graphics.DrawPath(pen, path);
                if (ann.IsFilled) using (var b = new SolidBrush(Color.FromArgb(ann.FillOpacity, ann.Color))) _graphics.FillPath(b, path);
                break;
            }
            case AnnotationTool.FlowchartProcess:
            case AnnotationTool.StartEnd:
            case AnnotationTool.Server:
            case AnnotationTool.Monitor:
            case AnnotationTool.Mobile:
            case AnnotationTool.Folder:
            case AnnotationTool.CodeFrame:
            case AnnotationTool.TerminalFrame:
            case AnnotationTool.BrowserFrame:
            case AnnotationTool.SpotlightRectangle:
            case AnnotationTool.Blur:
            case AnnotationTool.Pixelate:
            case AnnotationTool.Magnifier:
            {
                var r = Normalize(ann.Points[0], ann.Points[^1]);
                using var path = new GraphicsPath();
                if (ann.Tool == AnnotationTool.StartEnd) path.AddPath(RoundedRect(r, Math.Min(r.Height / 2, 24)), false);
                else path.AddRectangle(r);
                _graphics.DrawPath(pen, path);
                if (ann.Tool is AnnotationTool.Blur or AnnotationTool.Pixelate)
                {
                    using var hatch = new HatchBrush(ann.Tool == AnnotationTool.Pixelate ? HatchStyle.LargeCheckerBoard : HatchStyle.Percent50, Color.FromArgb(100, ann.Color), Color.Transparent);
                    _graphics.FillRectangle(hatch, r);
                }
                if (ann.Tool == AnnotationTool.BrowserFrame)
                    _graphics.DrawLine(pen, r.Left, r.Top + 20, r.Right, r.Top + 20);
                break;
            }
            case AnnotationTool.FlowchartDecision when ann.Points.Count >= 2:
            case AnnotationTool.Database when ann.Points.Count >= 2:
            case AnnotationTool.Document when ann.Points.Count >= 2:
            case AnnotationTool.User when ann.Points.Count >= 2:
            {
                var r = Normalize(ann.Points[0], ann.Points[^1]);
                using var path = new GraphicsPath();
                if (ann.Tool == AnnotationTool.FlowchartDecision)
                    path.AddPolygon(new PointF[] { new(r.X+r.Width/2,r.Y), new(r.Right,r.Y+r.Height/2), new(r.X+r.Width/2,r.Bottom), new(r.X,r.Y+r.Height/2) });
                else if (ann.Tool == AnnotationTool.Database) path.AddEllipse(r);
                else path.AddRectangle(r);
                _graphics.DrawPath(pen, path);
                break;
            }
            case AnnotationTool.Underline:
            case AnnotationTool.StrikeThrough:
            case AnnotationTool.Check:
            case AnnotationTool.Cross:
            case AnnotationTool.Warning:
            case AnnotationTool.Info:
            case AnnotationTool.Question:
            case AnnotationTool.Cursor:
            case AnnotationTool.Click:
            case AnnotationTool.KeyboardBadge:
            case AnnotationTool.Crosshair:
            case AnnotationTool.Braces:
            {
                var a = ann.Points[0]; var b = ann.Points[^1];
                if (ann.Tool == AnnotationTool.Underline || ann.Tool == AnnotationTool.StrikeThrough)
                    _graphics.DrawLine(pen, a, b);
                else if (ann.Tool == AnnotationTool.Check)
                    _graphics.DrawLines(pen, [a, new PointF((a.X+b.X)/2,b.Y), b]);
                else if (ann.Tool == AnnotationTool.Cross)
                    { _graphics.DrawLine(pen,a,b); _graphics.DrawLine(pen,new PointF(a.X,b.Y),new PointF(b.X,a.Y)); }
                else if (ann.Tool == AnnotationTool.Crosshair)
                    { _graphics.DrawLine(pen,new PointF(a.X,b.Y),new PointF(b.X,a.Y)); _graphics.DrawLine(pen,new PointF(a.X,(a.Y+b.Y)/2),new PointF(b.X,(a.Y+b.Y)/2)); }
                else
                {
                    var r = Normalize(a,b);
                    _graphics.DrawEllipse(pen,r);
                    using var font = new Font("Segoe UI", Math.Max(12, r.Height*.55f), FontStyle.Bold, GraphicsUnit.Pixel);
                    var glyph = ann.Tool switch { AnnotationTool.Warning => "!", AnnotationTool.Info => "i", AnnotationTool.Question => "?", AnnotationTool.Cursor => "↖", AnnotationTool.Click => "●", AnnotationTool.KeyboardBadge => "⌨", _ => "{" };
                    _graphics.DrawString(glyph, font, new SolidBrush(ann.Color), r.X+r.Width*.35f, r.Y+r.Height*.18f);
                }
                break;
            }
            case AnnotationTool.Triangle:
            case AnnotationTool.Diamond:
            case AnnotationTool.Cloud:
            case AnnotationTool.Spotlight:
            case AnnotationTool.NumberedStep:
            {
                var r = Normalize(ann.Points[0], ann.Points[^1]);
                using var path = new GraphicsPath();
                var points = ann.Tool == AnnotationTool.Diamond
                    ? new[] { new PointF(r.X+r.Width/2,r.Y), new PointF(r.Right,r.Y+r.Height/2), new PointF(r.X+r.Width/2,r.Bottom), new PointF(r.X,r.Y+r.Height/2) }
                    : new[] { new PointF(r.X+r.Width/2,r.Y), new PointF(r.Right,r.Bottom), new PointF(r.X,r.Bottom) };
                path.AddPolygon(points);
                _graphics.DrawPath(pen, path);
                if (ann.Tool == AnnotationTool.NumberedStep)
                {
                    using var font = new Font("Segoe UI", Math.Max(12, r.Height * .45f), FontStyle.Bold, GraphicsUnit.Pixel);
                    using var brush = new SolidBrush(ann.Color);
                    _graphics.DrawString("1", font, brush, r.X + r.Width / 2 - 5, r.Y + r.Height / 2 - 8);
                }
                break;
            }

            case AnnotationTool.Text:
                DrawText(ann, active);
                break;
        }
        if (ReferenceEquals(ann, _selectedAnnotation) && !active)
        {
            var b = ann.GetBounds();
            using var selectPen = new Pen(Color.FromArgb(210, 80, 170, 255), 1) { DashStyle = DashStyle.Dot };
            _graphics.DrawRectangle(selectPen, b.X, b.Y, b.Width, b.Height);
            foreach (var h in Handles(b)) _graphics.FillRectangle(Brushes.White, h.X - 3, h.Y - 3, 6, 6);
        }
    }

    private static IEnumerable<PointF> Handles(RectangleF r) =>
        [new(r.Left,r.Top), new(r.Right,r.Top), new(r.Left,r.Bottom), new(r.Right,r.Bottom)];

    private void DrawFreehand(AnnotationObject ann, Pen pen)
    {
        if (_graphics is null) return;

        if (ann.Points.Count == 1)
        {
            // A click with no drag is still a mark the user meant to make — a dot, not nothing.
            var p = ann.Points[0];
            var r = ann.Thickness / 2f;
            using var brush = new SolidBrush(ann.Color);
            _graphics.FillEllipse(brush, p.X - r, p.Y - r, ann.Thickness, ann.Thickness);
            return;
        }

        _graphics.DrawLines(pen, ann.Points.ToArray());
    }

    private void DrawArrow(Pen pen, PointF a, PointF b, float thickness)
    {
        if (_graphics is null) return;
        _graphics.DrawLine(pen, a, b);

        var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        if (double.IsNaN(angle)) return;
        float head = Math.Max(12f, thickness * 4f);
        const double spread = 25 * Math.PI / 180;

        var w1 = new PointF(
            (float)(b.X - head * Math.Cos(angle - spread)),
            (float)(b.Y - head * Math.Sin(angle - spread)));
        var w2 = new PointF(
            (float)(b.X - head * Math.Cos(angle + spread)),
            (float)(b.Y - head * Math.Sin(angle + spread)));
        _graphics.DrawLine(pen, b, w1);
        _graphics.DrawLine(pen, b, w2);
    }

    private void DrawText(AnnotationObject ann, bool active)
    {
        if (_graphics is null) return;

        var origin = ann.Points[0];
        var text = ann.Text ?? "";
        float size = Math.Max(14f, ann.Thickness * 2.5f);
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);

        var measured = _graphics.MeasureString(text.Length == 0 ? " " : text, font);

        if (active)
        {
            using var backing = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            _graphics.FillRectangle(backing, origin.X - 4, origin.Y - 2, measured.Width + 8, measured.Height + 4);
        }

        if (text.Length > 0)
        {
            using var brush = new SolidBrush(ann.Color);
            _graphics.DrawString(text, font, brush, origin.X, origin.Y);
        }

        if (active)
        {
            var lines = text.Split('\n');
            var lastLine = lines[^1];
            float lineHeight = font.GetHeight(_graphics);
            float caretX = origin.X + (lastLine.Length == 0 ? 0 : _graphics.MeasureString(lastLine, font).Width);
            float caretY = origin.Y + (lines.Length - 1) * lineHeight;
            using var caretPen = new Pen(ann.Color, 2f);
            _graphics.DrawLine(caretPen, caretX, caretY + 2, caretX, caretY + lineHeight - 2);
        }
    }

    private static RectangleF Normalize(PointF a, PointF b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void DrawStatusPill()
    {
        if (_graphics is null) return;

        var text = IsDrawingModeEnabled
            ? _textCaptureActive
                ? "Typing…  ·  Enter for a new line  ·  Esc to place the text"
                : "Drawing Mode: On  ·  Ctrl+Shift+D to stop  ·  Ctrl+Shift+Z undo  ·  Esc clears"
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

    private void OnLeftButtonDown(int x, int y)
    {
        var point = new PointF(x, y);
        _drawStart = point;
        _shiftHeld = IsKeyDown(VkShift);
        _altHeld = IsKeyDown(VkMenu);
        if (_currentTool == AnnotationTool.Select)
        {
            _selectedAnnotation = _annotations.AsEnumerable().Reverse().FirstOrDefault(a => a.HitTest(point));
            _editingSelection = _selectedAnnotation is not null;
            _lastPointer = point;
            if (_selectedAnnotation is not null)
            {
                var b = _selectedAnnotation.GetBounds();
                var handles = Handles(b).ToArray();
                _resizeHandle = handles.Select((h, i) => (h, i)).FirstOrDefault(v => Distance(v.h, point) <= 10).i;
                _resizingSelection = _resizeHandle >= 0 && Distance(handles[_resizeHandle], point) <= 10;
                SetCapture(_hwnd);
            }
            Render();
            return;
        }
        if (_currentTool == AnnotationTool.Text)
        {
            CommitActiveText();
            _activeAnnotation = new AnnotationObject
            {
                Tool = AnnotationTool.Text,
                Points = [new PointF(x, y)],
                Color = _penColor,
                Thickness = _penThickness,
                Text = "",
            };
            SetTextCapture(true);
            _pillVisible = true;
            KillTimer(_hwnd, PillTimerId);
            Render();
            return;
        }

        _activeAnnotation = new AnnotationObject
        {
            Tool = _currentTool,
            Points = _currentTool is AnnotationTool.Pen or AnnotationTool.Highlighter or AnnotationTool.Marker
                ? [new PointF(x, y)]
                : [new PointF(x, y), new PointF(x, y)],
            IsDashed = _currentTool == AnnotationTool.Line,
            IsFilled = _currentTool is AnnotationTool.Rectangle or AnnotationTool.Ellipse
                or AnnotationTool.RoundedRectangle or AnnotationTool.Callout or AnnotationTool.Spotlight,
            FillOpacity = 35,
            Color = _penColor,
            Thickness = _penThickness,
        };
        SetCapture(_hwnd);
        Render();
    }

    private void ExtendAnnotation(int x, int y)
    {
        if (_editingSelection && _selectedAnnotation is not null)
        {
            var dx = x - _lastPointer.X; var dy = y - _lastPointer.Y;
            if (_resizingSelection && _selectedAnnotation.Points.Count >= 2)
            {
                var p = _selectedAnnotation.Points[0]; var q = _selectedAnnotation.Points[^1];
                if ((_resizeHandle & 1) == 0) p.X = x; else q.X = x;
                if ((_resizeHandle & 2) == 0) p.Y = y; else q.Y = y;
                _selectedAnnotation.Points[0] = p; _selectedAnnotation.Points[^1] = q;
            }
            else for (var i = 0; i < _selectedAnnotation.Points.Count; i++)
            {
                var p = _selectedAnnotation.Points[i];
                _selectedAnnotation.Points[i] = new PointF(p.X + dx, p.Y + dy);
            }
            _lastPointer = new PointF(x, y); Render(); return;
        }
        if (_activeAnnotation is null || _activeAnnotation.Tool == AnnotationTool.Text) return;

        if (_activeAnnotation.Tool is AnnotationTool.Pen or AnnotationTool.Highlighter or AnnotationTool.Marker)
        {
            // Skip sub-pixel jitter: it bloats the point list without changing the rendered curve.
            var last = _activeAnnotation.Points[^1];
            if (Math.Abs(last.X - x) < 1 && Math.Abs(last.Y - y) < 1) return;
            _activeAnnotation.Points.Add(new PointF(x, y));
        }
        else
        {
            var end = new PointF(x, y);
            if (_altHeld)
            {
                var dx = x - _drawStart.X;
                var dy = y - _drawStart.Y;
                end = new PointF(_drawStart.X + dx, _drawStart.Y + dy);
                _activeAnnotation.Points[0] = new PointF(_drawStart.X - dx, _drawStart.Y - dy);
            }
            if (_shiftHeld && _activeAnnotation.Tool is AnnotationTool.Rectangle or AnnotationTool.Square
                or AnnotationTool.Ellipse or AnnotationTool.Circle or AnnotationTool.RoundedRectangle)
            {
                var dx = end.X - _activeAnnotation.Points[0].X;
                var dy = end.Y - _activeAnnotation.Points[0].Y;
                var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                end = new PointF(
                    _activeAnnotation.Points[0].X + MathF.Sign(dx) * side,
                    _activeAnnotation.Points[0].Y + MathF.Sign(dy) * side);
            }
            _activeAnnotation.Points[1] = end;
        }
        Render();
    }

    private void EndAnnotation()
    {
        if (_editingSelection)
        {
            _editingSelection = false; _resizingSelection = false; _resizeHandle = -1;
            if (GetCapture() == _hwnd) ReleaseCapture(); Render(); return;
        }
        if (_activeAnnotation is null || _activeAnnotation.Tool == AnnotationTool.Text) return;

        bool keep;
        if (_activeAnnotation.Tool is AnnotationTool.Pen or AnnotationTool.Highlighter or AnnotationTool.Marker)
        {
            keep = _activeAnnotation.Points.Count >= 1;
        }
        else
        {
            var a = _activeAnnotation.Points[0];
            var b = _activeAnnotation.Points[1];
            keep = Math.Abs(a.X - b.X) > 3 || Math.Abs(a.Y - b.Y) > 3;
        }

        if (keep)
        {
            var annotation = RecognizeFreehand(_activeAnnotation, _shiftHeld);
            if (_fadeSeconds > 0)
                annotation.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(_fadeSeconds);
            _annotations.Add(annotation);
        }
        _activeAnnotation = null;
        if (GetCapture() == _hwnd) ReleaseCapture();
        Render();
    }

    private static AnnotationObject RecognizeFreehand(AnnotationObject annotation, bool forceSquareOrCircle)
    {
        if (annotation.Tool != AnnotationTool.Pen || annotation.Points.Count < 5)
            return annotation;

        var points = annotation.Points;
        var start = points[0];
        var end = points[^1];
        var bounds = Bounds(points);
        var diagonal = Math.Max(1, MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height));
        var closure = Distance(start, end);

        // A closed path with four dominant corners is more likely to be a rectangle than a loose pen
        // stroke. A closed path with a smooth, round perimeter is treated as an ellipse.
        if (closure <= diagonal * 0.3f && bounds.Width > 8 && bounds.Height > 8)
        {
            var cornerCount = CountDirectionChanges(points);
            var aspect = bounds.Width / bounds.Height;
            if (forceSquareOrCircle)
            {
                var side = Math.Max(bounds.Width, bounds.Height);
                var square = RectangleF.FromLTRB(bounds.Left, bounds.Top, bounds.Left + side, bounds.Top + side);
                annotation.Tool = cornerCount >= 3 ? AnnotationTool.Rectangle : AnnotationTool.Ellipse;
                annotation.Points = [new PointF(square.Left, square.Top), new PointF(square.Right, square.Bottom)];
            }
            else if (cornerCount >= 3 && cornerCount <= 6)
            {
                annotation.Tool = AnnotationTool.Rectangle;
                annotation.Points = [new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Bottom)];
            }
            else if (aspect is > 0.2f and < 5f)
            {
                annotation.Tool = AnnotationTool.Ellipse;
                annotation.Points = [new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Bottom)];
            }
            return annotation;
        }

        // A mostly straight freehand stroke becomes a line. Preserve the original pen stroke when
        // it is visibly curved, so recognition never destroys intentional freehand drawing.
        var lineLength = Distance(start, end);
        if (lineLength > 24 && points.Count >= 7)
        {
            var overall = MathF.Atan2(end.Y - start.Y, end.X - start.X);
            var previous = points[^2];
            var headAngle = MathF.Atan2(end.Y - previous.Y, end.X - previous.X);
            if (Distance(previous, end) >= annotation.Thickness * 2 &&
                MathF.Abs(headAngle - overall) > 0.45f)
            {
                annotation.Tool = AnnotationTool.Arrow;
                annotation.Points = [start, end];
                return annotation;
            }
        }
        if (lineLength > 24 && PathLength(points) / lineLength < 1.15f)
        {
            annotation.Tool = AnnotationTool.Line;
            annotation.Points = [start, end];
        }
        return annotation;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetKeyState(virtualKey) & 0x8000) != 0;

    private static RectangleF Bounds(IReadOnlyList<PointF> points)
    {
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    private static float Distance(PointF a, PointF b) =>
        MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));

    private static float PathLength(IReadOnlyList<PointF> points)
    {
        float length = 0;
        for (var i = 1; i < points.Count; i++) length += Distance(points[i - 1], points[i]);
        return length;
    }

    private static int CountDirectionChanges(IReadOnlyList<PointF> points)
    {
        var changes = 0;
        var previous = 0f;
        for (var i = 2; i < points.Count; i++)
        {
            var current = MathF.Atan2(points[i].Y - points[i - 1].Y, points[i].X - points[i - 1].X);
            if (i > 2 && MathF.Abs(current - previous) > 0.45f) changes++;
            previous = current;
        }
        return changes;
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmLButtonDown:
                OnLeftButtonDown(LoWord(lParam), HiWord(lParam));
                return 0;

            case WmMouseMove:
                if (_activeAnnotation is not null) ExtendAnnotation(LoWord(lParam), HiWord(lParam));
                return 0;

            case WmLButtonUp:
            case WmCaptureChanged:
                EndAnnotation();
                return 0;

            case WmTimer when wParam == PillTimerId:
                KillTimer(_hwnd, PillTimerId);
                if (!IsDrawingModeEnabled)
                {
                    _pillVisible = false;
                    Render();
                }
                return 0;

            case WmTimer when wParam == AnnotationTimerId:
            {
                var now = DateTime.UtcNow;
                var removed = _annotations.RemoveAll(a => a.ExpiresAtUtc is { } expiry && expiry <= now);
                if (removed > 0) Render();
                return 0;
            }

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
            KillTimer(_hwnd, AnnotationTimerId);
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
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;

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
    private static extern short GetKeyState(int virtualKey);

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
