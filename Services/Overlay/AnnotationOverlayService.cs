using Microsoft.UI.Dispatching;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Tracking;
using ScreenRecorderApp.Views;
using Windows.UI;

namespace ScreenRecorderApp.Services.Overlay;

/// <summary>
/// Orchestrates Phase 6's live annotation overlay: owns the transparent, click-through
/// <see cref="AnnotationOverlayWindow"/> and the <see cref="GlobalHotkeyHook"/> that toggles it. Both
/// are armed only for the duration of a recording session (<see cref="Arm"/>/<see cref="Disarm"/>) —
/// see <see cref="GlobalHotkeyHook"/>'s remarks for why a hotkey hook shouldn't run for the whole app
/// session.
/// </summary>
public sealed class AnnotationOverlayService : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private AnnotationOverlayWindow? _window;
    private GlobalHotkeyHook? _hook;

    public AnnotationOverlayService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public bool IsArmed => _window is not null;

    /// <summary>Shows the overlay over <paramref name="monitor"/> with the given initial pen color/thickness, and starts listening for the annotation hotkeys. No-op if already armed.</summary>
    public void Arm(MonitorInfo monitor, Color penColor, double penThickness)
    {
        if (IsArmed) return;

        _window = new AnnotationOverlayWindow();
        _window.UpdateDrawingAttributes(penColor, penThickness);
        _window.ShowOverMonitor(monitor);

        _hook = new GlobalHotkeyHook();
        // Hook events fire on GlobalHotkeyHook's own dedicated thread (see its remarks) — touching the
        // WinUI window from there directly would violate WinUI's UI-thread affinity, so every callback
        // is marshaled back via the DispatcherQueue captured at construction, same pattern MainViewModel
        // already uses for VideoCaptureService's off-thread CaptureTargetLost event.
        _hook.ToggleDrawingModeRequested += () => _dispatcherQueue.TryEnqueue(() =>
            _window?.SetDrawingMode(!_window.IsDrawingModeEnabled));
        _hook.ClearRequested += () => _dispatcherQueue.TryEnqueue(() => _window?.ClearInk());
        _hook.Start();
    }

    /// <summary>Pushes a new pen color/thickness to the live overlay, if one is armed — safe to call at any time, including mid-recording (see AnnotationOverlayWindow.UpdateDrawingAttributes). No-op if not armed.</summary>
    public void UpdateDrawingAttributes(Color penColor, double penThickness) =>
        _window?.UpdateDrawingAttributes(penColor, penThickness);

    /// <summary>Stops the hotkey hook and tears down the overlay window. No-op if not armed.</summary>
    public void Disarm()
    {
        _hook?.Dispose();
        _hook = null;

        _window?.HideOverlay();
        _window?.Close();
        _window = null;
    }

    public void Dispose() => Disarm();
}
