using Microsoft.UI.Dispatching;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Tracking;
using Windows.UI;

namespace ScreenRecorderApp.Services.Overlay;

/// <summary>
/// Orchestrates the live annotation overlay: owns the transparent, click-through
/// <see cref="AnnotationOverlayWindow"/> and the <see cref="GlobalHotkeyHook"/> that toggles it.
/// </summary>
/// <remarks>
/// Armed whenever the Annotations feature is switched on and a display is selected — not only for the
/// duration of a recording, which is what it used to be. Tying it to an in-progress recording made the
/// feature look completely broken: you turned Annotations on, pressed Ctrl+Shift+D, and nothing
/// happened, because no overlay and no hotkey hook existed yet. It also left no way to check the pen
/// color or practise a stroke before going live. The hook is still scoped to the feature being on
/// (never the whole app session) for the reason described in <see cref="GlobalHotkeyHook"/>'s remarks.
/// </remarks>
public sealed class AnnotationOverlayService : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private AnnotationOverlayWindow? _window;
    private GlobalHotkeyHook? _hook;

    // Which monitor the armed overlay is currently positioned over, so Arm() can tell "already armed,
    // nothing to do" from "already armed, but the user picked a different display" — the latter has to
    // reposition the window rather than silently keep annotating the wrong screen.
    private nint _armedMonitorHandle;

    public AnnotationOverlayService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public bool IsArmed => _window is not null;

    /// <summary>
    /// Shows the overlay over <paramref name="monitor"/> with the given pen color/thickness and starts
    /// listening for the annotation hotkeys. Idempotent: calling it again for the same monitor does
    /// nothing, and calling it for a different monitor repositions the existing overlay rather than
    /// stacking up a second one. Must be called on the UI thread — the overlay is a Win32 window whose
    /// message loop is the one WinUI already pumps there.
    /// </summary>
    public void Arm(MonitorInfo monitor, Color penColor, double penThickness)
    {
        if (IsArmed)
        {
            _window!.UpdateDrawingAttributes(ToDrawingColor(penColor), penThickness);
            if (_armedMonitorHandle != monitor.Handle)
            {
                _window.ShowOverMonitor(monitor);
                _armedMonitorHandle = monitor.Handle;
            }
            return;
        }

        _window = new AnnotationOverlayWindow();
        _window.UpdateDrawingAttributes(ToDrawingColor(penColor), penThickness);
        _window.ShowOverMonitor(monitor);
        _armedMonitorHandle = monitor.Handle;

        _hook = new GlobalHotkeyHook();
        // Hook events fire on GlobalHotkeyHook's own dedicated thread (see its remarks). The overlay's
        // window was created on the UI thread and must only be touched from there, so every callback is
        // marshaled back via the DispatcherQueue captured at construction — the same pattern
        // MainViewModel already uses for VideoCaptureService's off-thread CaptureTargetLost event.
        _hook.ToggleDrawingModeRequested += () => _dispatcherQueue.TryEnqueue(() =>
            _window?.SetDrawingMode(!_window.IsDrawingModeEnabled));
        _hook.ClearRequested += () => _dispatcherQueue.TryEnqueue(() => _window?.ClearInk());
        _hook.UndoRequested += () => _dispatcherQueue.TryEnqueue(() => _window?.UndoLastStroke());
        _hook.Start();
    }

    /// <summary>Pushes a new pen color/thickness to the live overlay, if one is armed — safe to call at any time, including mid-recording (see AnnotationOverlayWindow.UpdateDrawingAttributes). No-op if not armed.</summary>
    public void UpdateDrawingAttributes(Color penColor, double penThickness) =>
        _window?.UpdateDrawingAttributes(ToDrawingColor(penColor), penThickness);

    /// <summary>The pen presets are WinRT colors (they double as XAML brushes on the Annotations tab); the overlay renders with GDI+, which wants System.Drawing.</summary>
    private static System.Drawing.Color ToDrawingColor(Color color) =>
        System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Stops the hotkey hook and tears down the overlay window. No-op if not armed.</summary>
    public void Disarm()
    {
        _hook?.Dispose();
        _hook = null;

        _window?.HideOverlay();
        _window?.Dispose();
        _window = null;
        _armedMonitorHandle = 0;
    }

    public void Dispose() => Disarm();
}
