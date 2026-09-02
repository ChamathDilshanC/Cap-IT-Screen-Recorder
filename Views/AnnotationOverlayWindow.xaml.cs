using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture.Interop;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace ScreenRecorderApp.Views;

/// <summary>
/// Full-screen, borderless, transparent, always-on-top overlay for Phase 6's live screen annotations.
/// </summary>
/// <remarks>
/// Unlike the cursor spotlight/click-ripple effects from Phase 4 (synthetic — burned into frames by
/// VideoCaptureService because there's no real on-screen entity for them), this is a REAL desktop
/// window: when the recording target is "Entire display", DXGI Desktop Duplication captures the full
/// desktop composition, so whatever gets drawn here shows up in the recording automatically, with no
/// VideoCaptureService changes needed. That's also exactly why it's restricted to monitor-capture mode
/// (see MainViewModel.CanEnableAnnotations) — Windows Graphics Capture for a *specific window* captures
/// only that window's own surface, not other windows layered on top of it, so this overlay would be
/// invisible to a window-mode recording no matter how it's drawn.
///
/// Step 1 built the window, its transparency/click-through/topmost mechanics, and a status-pill
/// placeholder proving hit-testing actually toggles. Step 2 (this revision) adds the actual drawing
/// surface — see <see cref="OnPointerPressed"/>/<see cref="UpdateDrawingAttributes"/>/<see cref="ClearInk"/>.
/// Windows App SDK has no InkCanvas/InkPresenter for desktop apps (that control is UWP-only), so strokes
/// are hand-rolled here: each pointer drag becomes a <see cref="Polyline"/> added to DrawSurface. This
/// also sidesteps InkPresenter's "pen-only by default" quirk entirely — plain PointerPressed/Moved/
/// Released already fire uniformly for mouse, pen, and touch with no device-type opt-in needed.
/// </remarks>
public sealed partial class AnnotationOverlayWindow : Window
{
    private readonly nint _hwnd;
    private readonly AppWindow? _appWindow;

    // Keyed by pointer id (not a single "current stroke") so two simultaneous touch contacts each get
    // their own independent stroke instead of corrupting one shared Polyline's point list.
    private readonly Dictionary<uint, Polyline> _activeStrokes = new();

    private Color _penColor = Color.FromArgb(255, 57, 255, 20); // Neon Green — matches AnnotationColorOption's default
    private double _penThickness = 6;

    public bool IsDrawingModeEnabled { get; private set; }

    public AnnotationOverlayWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        if (_appWindow is not null)
        {
            _appWindow.IsShownInSwitchers = false; // keep it out of Alt+Tab
        }

        DrawSurface.PointerPressed += OnPointerPressed;
        DrawSurface.PointerMoved += OnPointerMoved;
        DrawSurface.PointerReleased += OnPointerReleaseOrCancel;
        DrawSurface.PointerCanceled += OnPointerReleaseOrCancel;
        DrawSurface.PointerCaptureLost += OnPointerReleaseOrCancel;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DrawSurface).Position;
        var stroke = new Polyline
        {
            Stroke = new SolidColorBrush(_penColor),
            StrokeThickness = _penThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        stroke.Points.Add(point);

        DrawSurface.Children.Add(stroke);
        _activeStrokes[e.Pointer.PointerId] = stroke;
        DrawSurface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_activeStrokes.TryGetValue(e.Pointer.PointerId, out var stroke)) return;
        stroke.Points.Add(e.GetCurrentPoint(DrawSurface).Position);
        e.Handled = true;
    }

    private void OnPointerReleaseOrCancel(object sender, PointerRoutedEventArgs e)
    {
        if (!_activeStrokes.Remove(e.Pointer.PointerId)) return;
        DrawSurface.ReleasePointerCapture(e.Pointer);
    }

    /// <summary>Shows the overlay positioned exactly over <paramref name="monitor"/>, starting in click-through (non-drawing) mode. Safe to call again to re-show/re-position an already-created window.</summary>
    public void ShowOverMonitor(MonitorInfo monitor)
    {
        // WS_EX_LAYERED signals DWM to alpha-composite this window instead of treating it as opaque —
        // required for RootGrid's Transparent background to actually let the desktop show through.
        // WS_EX_TOOLWINDOW keeps it out of the taskbar. WS_EX_NOACTIVATE stops it from ever stealing
        // keyboard focus, even while topmost — critical since the whole point is to sit on top of
        // whatever app the user is demoing without interrupting it.
        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GwlExStyle);
        exStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GwlExStyle, exStyle);

        _appWindow?.MoveAndResize(new RectInt32(monitor.X, monitor.Y, monitor.Width, monitor.Height));

        // AppWindow's presenter has no reliable cross-version "always on top" setter, so topmost z-order
        // is asserted directly — SWP_NOMOVE/SWP_NOSIZE since positioning was already handled above,
        // SWP_NOACTIVATE so even this call can't steal focus.
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        _appWindow?.Show(false); // show without activating — see WS_EX_NOACTIVATE comment above
        SetDrawingMode(false);
    }

    /// <summary>
    /// Toggles between click-through (mouse input passes to whatever is behind it, for normal desktop
    /// use) and hit-testable (this window captures clicks, for drawing) by flipping WS_EX_TRANSPARENT —
    /// the entire mechanism the "click-through dilemma" in the Phase 6 brief comes down to. Deliberately
    /// does not clear any drawings — a presenter often wants to leave an arrow on screen, toggle off to
    /// click something in the app underneath, then toggle back on to keep annotating.
    /// </summary>
    public void SetDrawingMode(bool enabled)
    {
        IsDrawingModeEnabled = enabled;

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GwlExStyle);
        exStyle = enabled
            ? exStyle & ~NativeMethods.WsExTransparent
            : exStyle | NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GwlExStyle, exStyle);

        StatusText.Text = enabled
            ? "Drawing Mode: On  (Ctrl+Shift+D to stop, Esc to clear)"
            : "Drawing Mode: Off  (Ctrl+Shift+D to draw)";
        StatusPill.Background = new SolidColorBrush(enabled
            ? Color.FromArgb(220, 20, 120, 40)
            : Color.FromArgb(200, 32, 32, 32));
    }

    /// <summary>
    /// Sets the pen color/thickness used for strokes drawn from this point on — existing strokes are
    /// untouched — and is safe to call at any time, including mid-recording, so a presenter can switch
    /// color without losing what's already on screen.
    /// </summary>
    public void UpdateDrawingAttributes(Color color, double thickness)
    {
        _penColor = color;
        _penThickness = thickness;
    }

    /// <summary>Wipes every stroke drawn so far. Wired to the Esc hotkey via AnnotationOverlayService.</summary>
    public void ClearInk()
    {
        DrawSurface.Children.Clear();
        _activeStrokes.Clear();
    }

    public void HideOverlay() => _appWindow?.Hide();
}
