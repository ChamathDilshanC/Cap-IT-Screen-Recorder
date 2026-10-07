using System.Drawing;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Tracking;

namespace ScreenRecorderApp.Services.Overlay;

/// <summary>
/// Orchestrates the live annotation overlay: owns the transparent, click-through
/// <see cref="AnnotationOverlayWindow"/>, the capture-excluded <see cref="AnnotationToolbarWindow"/>,
/// and the <see cref="GlobalHotkeyHook"/> that toggles drawing mode and feeds the text tool.
/// </summary>
/// <remarks>
/// Armed whenever the Annotations feature is switched on and a display is selected — not only for the
/// duration of a recording. The hook is still scoped to the feature being on (never the whole app
/// session) for the reason described in <see cref="GlobalHotkeyHook"/>'s remarks.
/// </remarks>
public sealed class AnnotationOverlayService : IDisposable
{
    // The UI thread's context: the overlay and toolbar are plain Win32 windows owned by that thread, and
    // its message loop (whatever UI framework runs it) pumps their WndProcs.
    private readonly SynchronizationContext _uiContext;
    private AnnotationOverlayWindow? _window;
    private AnnotationToolbarWindow? _toolbar;
    private GlobalHotkeyHook? _hook;

    // Which monitor the armed overlay is currently positioned over, so Arm() can tell "already armed,
    // nothing to do" from "already armed, but the user picked a different display".
    private nint _armedMonitorHandle;

    // Last attributes pushed in, so a toolbar/page round-trip doesn't fight itself.
    private AnnotationTool _tool = AnnotationTool.Pen;
    private Color _penColor = AnnotationColorOption.All[0].Value;
    private double _penThickness = 6;
    private int _fadeSeconds;

    /// <param name="uiContext">The UI thread's synchronization context. Hook callbacks arrive on the hook's own thread and are posted through it.</param>
    public AnnotationOverlayService(SynchronizationContext uiContext)
    {
        _uiContext = uiContext;
    }

    private void Post(Action action) => _uiContext.Post(static state => ((Action)state!)(), action);

    public bool IsArmed => _window is not null;

    /// <summary>Raised when a tool / colour / thickness is picked on the floating toolbar, so the view model can mirror it into the Annotations tab and persist it. Fires on the UI thread.</summary>
    public event Action<AnnotationTool, Color, double>? AttributesChanged;

    /// <summary>
    /// Shows the overlay + toolbar over <paramref name="monitor"/> with the given tool/pen and starts
    /// listening for the annotation hotkeys. Idempotent for the same monitor; repositions both windows
    /// for a different one. Must be called on the UI thread.
    /// </summary>
    public void Arm(MonitorInfo monitor, AnnotationTool tool, Color penColor, double penThickness, int fadeSeconds = 0)
    {
        _tool = tool;
        _penColor = penColor;
        _penThickness = penThickness;
        _fadeSeconds = Math.Max(0, fadeSeconds);

        if (IsArmed)
        {
            PushAttributes();
            if (_armedMonitorHandle != monitor.Handle)
            {
                _window!.ShowOverMonitor(monitor);
                _toolbar!.ShowOverMonitor(monitor);
                if (!_window.IsDrawingModeEnabled) _toolbar.Hide();
                _armedMonitorHandle = monitor.Handle;
            }
            return;
        }

        _window = new AnnotationOverlayWindow();
        _window.ShowOverMonitor(monitor);

        _toolbar = new AnnotationToolbarWindow();
        _toolbar.ShowOverMonitor(monitor);
        _toolbar.Hide(); // only visible once Drawing Mode is on

        _toolbar.ToolSelected += t =>
        {
            _tool = t;
            _window?.SetTool(t);
            RaiseAttributesChanged();
        };
        _toolbar.ColorSelected += c =>
        {
            _penColor = c;
            _window?.UpdateDrawingAttributes(c, _penThickness);
            RaiseAttributesChanged();
        };
        _toolbar.ThicknessSelected += th =>
        {
            _penThickness = th;
            _window?.UpdateDrawingAttributes(_penColor, th);
            RaiseAttributesChanged();
        };
        _toolbar.UndoRequested += () => _window?.UndoLastStroke();
        _toolbar.ClearRequested += () => _window?.ClearInk();

        PushAttributes();
        _armedMonitorHandle = monitor.Handle;

        _hook = new GlobalHotkeyHook();
        // Hook events fire on GlobalHotkeyHook's own thread; the windows live on the UI thread, so every
        // callback is marshaled back through the UI context captured at construction.
        _hook.ToggleDrawingModeRequested += () => Post(ToggleDrawingMode);
        _hook.ClearRequested += () => Post(() => _window?.ClearInk());
        _hook.UndoRequested += () => Post(() => _window?.UndoLastStroke());
        _hook.DuplicateRequested += () => Post(() => _window?.DuplicateSelected());
        _hook.DeleteRequested += () => Post(() => _window?.DeleteSelected());
        _hook.TextCharTyped += ch => Post(() => _window?.TextAppend(ch.ToString()));
        _hook.TextBackspaceRequested += () => Post(() => _window?.TextBackspace());
        _hook.TextNewlineRequested += () => Post(() => _window?.TextNewline());
        _hook.TextCommitRequested += () => Post(() => _window?.TextCommit());
        _hook.Start();

        // Keep the low-level hook's swallow-keystrokes mode in step with the overlay's text tool.
        _window.TextCaptureChanged += active =>
        {
            if (_hook is not null) _hook.TextCaptureActive = active;
        };
    }

    /// <summary>Same as Ctrl+Shift+D: toggles drawing mode on the armed overlay. UI thread; no-op if not armed.</summary>
    public void ToggleDrawingMode()
    {
        if (_window is null) return;
        var enable = !_window.IsDrawingModeEnabled;
        _window.SetDrawingMode(enable);
        if (enable) _toolbar?.Show();
        else _toolbar?.Hide();
    }

    private void RaiseAttributesChanged() => AttributesChanged?.Invoke(_tool, _penColor, _penThickness);

    private void PushAttributes()
    {
        _window?.SetTool(_tool);
        _window?.UpdateDrawingAttributes(_penColor, _penThickness);
        _window?.SetFadeSeconds(_fadeSeconds);
        _toolbar?.SetActiveState(_tool, _penColor, (float)_penThickness);
    }

    /// <summary>Pushes a tool/pen change made on the Annotations tab into the live overlay + toolbar. Safe any time, no-op if not armed.</summary>
    public void UpdateDrawingAttributes(AnnotationTool tool, Color penColor, double penThickness)
    {
        _tool = tool;
        _penColor = penColor;
        _penThickness = penThickness;
        PushAttributes();
    }

    public void UpdateFadeSeconds(int seconds)
    {
        _fadeSeconds = Math.Max(0, seconds);
        _window?.SetFadeSeconds(_fadeSeconds);
    }

    /// <summary>Stops the hotkey hook and tears down both windows. No-op if not armed.</summary>
    public void Disarm()
    {
        _hook?.Dispose();
        _hook = null;

        _toolbar?.Dispose();
        _toolbar = null;

        _window?.HideOverlay();
        _window?.Dispose();
        _window = null;
        _armedMonitorHandle = 0;
    }

    public void Dispose() => Disarm();
}
