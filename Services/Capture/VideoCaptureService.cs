using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture.Interop;
using ScreenRecorderApp.Services.Tracking;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Captures either a whole monitor (DXGI Desktop Duplication) or a single application window
/// (Windows.Graphics.Capture), and keeps the most recently decoded BGRA frame — always at a fixed
/// <see cref="Width"/>/<see cref="Height"/> regardless of which path produced it — available for a
/// pacing thread to pull at a fixed FPS.
/// </summary>
/// <remarks>
/// Monitor capture used to be built on Windows.Graphics.Capture (WGC) too, and was rewritten onto DXGI
/// Desktop Duplication after the hand-written WinRT COM interop WGC requires (there's no public API to
/// create a GraphicsCaptureItem for an arbitrary HWND/HMONITOR otherwise) reliably crashed the whole
/// process a few seconds into recording with an unrecoverable AccessViolationException in a GC finalizer
/// releasing a WinRT object reference — a native/managed-boundary crash no try/catch can stop. Desktop
/// Duplication has no such interop at all, which is why it's still the monitor-capture path today.
///
/// Application-specific (single-window) capture has no Desktop Duplication equivalent — it's WGC-only —
/// so the same interop risk is unavoidable for that mode. It's mitigated, not eliminated, by strict
/// object-lifetime discipline: every WGC object (<see cref="_captureItem"/>, <see cref="_framePool"/>,
/// <see cref="_captureSession"/>, <see cref="_wgcDevice"/>) is a field, created in
/// <see cref="Prepare"/>/<see cref="BeginCapture"/> and disposed in a strict order in <see cref="Stop"/>
/// — never a local variable that could be garbage-collected while a native callback still references it,
/// which is the specific pattern the earlier crash followed. See
/// <see cref="Interop.GraphicsCaptureInterop"/> for the interop points themselves.
/// </remarks>
public sealed class VideoCaptureService : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _stagingTexture;

    private CaptureTargetKind _captureTarget = CaptureTargetKind.Monitor;
    private nint _targetWindowHandle;

    // Window capture (WGC) — see this class's remarks for why every one of these is a field, never a
    // local, and why Stop() disposes them in a specific order.
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private IDirect3DDevice? _wgcDevice;
    private ID3D11Texture2D? _wgcStagingTexture;
    private int _wgcStagingWidth;
    private int _wgcStagingHeight;
    private SizeInt32 _wgcPoolSize;

    // GraphicsCaptureItem.Closed turns out not to be a reliable signal in practice here: WGC simply stops
    // delivering FrameArrived at all once the target window is gone, rather than firing Closed — so an
    // independent poll is the only thing that reliably notices. IsWindow() is cheap and unambiguous, so a
    // slow poll (not every frame) is plenty; still hooked up to Closed too, since a poll can lag it by up
    // to the poll interval and there's no reason not to react the instant either signal fires.
    private System.Threading.Timer? _windowValidityTimer;
    private int _targetLostSignaled;

    /// <summary>Fires if the captured window is closed while recording/previewing (window mode only) — the caller should stop cleanly rather than let capture just go silent.</summary>
    public event Action? CaptureTargetLost;

    private void SignalCaptureTargetLostOnce()
    {
        if (System.Threading.Interlocked.Exchange(ref _targetLostSignaled, 1) == 0)
        {
            CaptureTargetLost?.Invoke();
        }
    }

    private Thread? _captureThread;
    private volatile bool _running;

    private readonly object _frameLock = new();
    private byte[]? _latestFrame;
    private int _frameWidth;
    private int _frameHeight;
    private bool _hasFrame;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsCapturing { get; private set; }
    private bool _prepared;

    private bool _captureCursor;
    private CursorStyle _cursorStyle;
    private bool _cursorVisible;
    private int _cursorX;
    private int _cursorY;

    // Mouse tracking zoom: both the zoom factor and the pan center are eased toward their targets every
    // frame (rather than snapping) so enabling/disabling the effect and following a moving cursor both
    // read as smooth camera motion instead of a jump cut. The *target* factor itself is gated on recent
    // interaction (see _lastActivityTicks) rather than being "on" continuously whenever zoom is enabled.
    private const double ZoomTimeConstant = 0.18; // seconds — smaller = snappier camera motion
    private const double IdleTimeoutSeconds = 1.5;
    private const int MovementActivityThresholdPx = 3;
    private bool _zoomEnabled;
    private double _zoomTargetFactor = 1.0;
    private double _zoomCurrentFactor = 1.0;
    private double _zoomCenterX;
    private double _zoomCenterY;
    private bool _zoomCenterInitialized;
    private byte[]? _zoomScratchBuffer;
    private readonly Stopwatch _zoomClock = Stopwatch.StartNew();
    private double _lastZoomFrameSeconds;

    // Written from the capture thread (mouse movement, via DXGI) and from the keyboard/mouse hook
    // threads (typing, clicks) — Volatile access instead of a lock since it's a single primitive value
    // and ApplyZoom only ever needs the most recent write, not strict ordering with anything else.
    private long _lastActivityTicks;

    // While typing, the zoom follows the text caret instead of the (possibly stale) mouse position —
    // set on every keypress via CaretLocator, cleared back to null on the next mouse movement/click so
    // whichever signal happened most recently "wins" the pan target. Coordinates are monitor-local
    // (same space as _cursorX/_cursorY), converted from CaretLocator's virtual-screen coordinates using
    // _monitorOriginX/Y.
    private double? _typingTargetX;
    private double? _typingTargetY;
    private int _monitorOriginX;
    private int _monitorOriginY;

    // Window mode's counterpart to _monitorOriginX/Y: the window can move *and* be resized, so instead
    // of a fixed origin resolved once, MapScreenToCanvas re-reads the window's current screen rect every
    // time it's called and combines it with the letterbox scale/offset (updated every WGC frame in
    // CopyWgcFrameToBuffer, since the window's aspect ratio relative to the fixed canvas can change).
    private double _letterboxScale = 1.0;
    private double _letterboxOffsetX;
    private double _letterboxOffsetY;

    // Guards the keyboard/mouse hook fields specifically. Effects that need them (zoom, keystroke
    // overlay, click ripples) can now be switched on mid-session from the UI thread (see
    // EnsureActivityHooks), which races Stop() tearing the same fields down on another thread.
    private readonly object _hookLock = new();
    private GlobalKeyboardHook? _keyboardHook;
    private GlobalMouseHook? _mouseHook;
    private bool _clickAtSubscribed;
    private KeystrokeOverlayRenderer? _keystrokeOverlay;

    // Cursor spotlight (Phase 4). Radius is stored in canvas pixels, the same space _cursorX/_cursorY
    // live in. The falloff table is rebuilt only when the radius actually changes (checked in
    // ApplySpotlight), not per frame — see BuildSpotlightFalloffTable's remarks for why a squared-distance
    // -indexed lookup table is what keeps this off the sqrt-per-pixel path entirely.
    private bool _spotlightEnabled;
    private int _spotlightRadius = 180;
    private const int SpotlightFeatherPx = 24; // fixed edge-softness, not user-configurable
    private const double SpotlightDimAlpha = 0.45;
    private byte[]? _spotlightFalloffTable;
    private int _spotlightFalloffTableRadius = -1;

    // Click ripples (Phase 4). OnMouseClickAt (fired from GlobalMouseHook's own thread) appends under
    // _rippleLock; ApplyRipples (called from the pacer thread via TryGetLatestFrame) prunes expired
    // entries and snapshots the rest under the same lock, then renders without holding it — a ripple's
    // whole lifetime is well under a second, so a plain locked List is simpler than anything lock-free
    // would buy here, and RemoveAll (arbitrary-position removal) is a better fit than a queue anyway.
    private readonly object _rippleLock = new();
    private readonly List<ActiveRipple> _activeRipples = [];
    private bool _clickRipplesEnabled;
    private const double RippleDurationSeconds = 0.4;
    private const double RippleMaxRadiusPx = 40;
    private const double RippleThicknessPx = 3;

    private readonly record struct ActiveRipple(double X, double Y, double StartSeconds);

    // Circular webcam PiP overlay (Phase 3). Deliberately life-cycled independently of Prepare()/Stop()
    // (see SetWebcam) — the camera has nothing to do with which monitor/window is being screen-captured,
    // so switching targets mid-session (which does a Stop()+Prepare() cycle) must not tear down and
    // re-initialize the whole camera pipeline just because the *screen* target changed. A dedicated lock
    // (distinct from _wgcCallbackLock/_frameLock) guards _webcam/_webcamDeviceId specifically because
    // SetWebcam can be called from a different thread than Prepare()/Stop() and needs its own
    // read-modify-write to stay atomic against a concurrent call.
    private readonly object _webcamLifecycleLock = new();
    private WebcamCaptureService? _webcam;
    private string? _webcamDeviceId;

    // Raw pointer-shape scratch buffer for GetFramePointerShape(), grown as needed and reused across
    // shape updates; the decoded/converted result is cached separately since the shape only changes
    // occasionally (e.g. hovering a different kind of control), not every frame.
    private nint _pointerShapeBuffer;
    private uint _pointerShapeBufferCapacity;
    private CursorIconBitmap? _systemCursorIcon;

    /// <summary>
    /// Resolves the real capture resolution (Width/Height) and stands up either the DXGI or the WGC
    /// acquisition path — but does not start pulling frames yet. Split out from <see cref="BeginCapture"/>
    /// so the caller can spawn/connect the ffmpeg encoder (which needs the resolution up front) before
    /// any frames start flowing. Exactly one of <paramref name="monitor"/>/<paramref name="window"/> must
    /// be non-null.
    /// </summary>
    public void Prepare(MonitorInfo? monitor, WindowInfo? window, bool captureCursor, CursorStyle cursorStyle = CursorStyle.Arrow,
        bool zoomEnabled = false, double zoomFactor = 2.0, bool keystrokeOverlayEnabled = false,
        bool spotlightEnabled = false, double spotlightRadius = 180, bool clickRipplesEnabled = false)
    {
        if (_prepared) return;
        if (monitor is null && window is null)
        {
            throw new ArgumentException("Either a monitor or a window must be specified.");
        }
        // Enforced rather than silently resolved: the capture path below is chosen purely by which of
        // these is non-null, so a caller passing both used to mean "window wins" — which is exactly how
        // an "Entire display" recording ended up capturing an arbitrary window instead. Callers
        // (RecordingManager.StartAsync/StartPreview) normalize to the user's chosen target kind first.
        if (monitor is not null && window is not null)
        {
            throw new ArgumentException("Specify either a monitor or a window to capture, not both.");
        }

        _captureTarget = window is not null ? CaptureTargetKind.Window : CaptureTargetKind.Monitor;

        _captureCursor = captureCursor;
        _cursorStyle = cursorStyle;

        _zoomEnabled = zoomEnabled;
        _zoomTargetFactor = zoomFactor;
        _zoomCurrentFactor = 1.0;
        _zoomCenterInitialized = false;
        _lastActivityTicks = 0; // starts idle: zoom eases in only once real activity is observed
        _lastZoomFrameSeconds = _zoomClock.Elapsed.TotalSeconds;
        _typingTargetX = null;
        _typingTargetY = null;
        _letterboxScale = 1.0;
        _letterboxOffsetX = 0;
        _letterboxOffsetY = 0;

        if (keystrokeOverlayEnabled)
        {
            _keystrokeOverlay = new KeystrokeOverlayRenderer();
        }

        _spotlightEnabled = spotlightEnabled;
        _spotlightRadius = Math.Max(1, (int)Math.Round(spotlightRadius));
        _clickRipplesEnabled = clickRipplesEnabled;
        lock (_rippleLock) { _activeRipples.Clear(); }

        // Deliberately no webcam start here — see SetWebcam and _webcamLifecycleLock's remarks. The
        // camera's lifecycle is independent of this method entirely now.

        // The keyboard hook doubles as a zoom-activity signal, so it's needed whenever *either* feature
        // wants it; OnKeyboardActivity always marks activity and only forwards to the overlay renderer
        // when that feature is actually on, decoupling "is typing activity" from "is the overlay visible."
        if (zoomEnabled || keystrokeOverlayEnabled)
        {
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyPressed += OnKeyboardActivity;
        }

        // A click needs a real hook for both zoom's activity signal and click ripples — DXGI reports
        // cursor *position* every frame already (used for movement-based activity in CaptureLoop), and
        // window mode polls position itself in OnWgcFrameArrived, but neither ever reports button state
        // on its own. ClickAt (screen coordinates) is only subscribed when ripples actually want it.
        if (zoomEnabled || clickRipplesEnabled)
        {
            _mouseHook = new GlobalMouseHook();
            _mouseHook.Click += OnMouseActivity;
            if (clickRipplesEnabled)
            {
                _mouseHook.ClickAt += OnMouseClickAt;
                _clickAtSubscribed = true;
            }
        }

        if (_captureTarget == CaptureTargetKind.Window)
        {
            PrepareWindowCapture(window!);
        }
        else
        {
            PrepareMonitorCapture(monitor!);
        }

        // _cursorX/_cursorY otherwise stay at their zero-default until DXGI/PollWindowCursor next reports
        // a position — harmless for cursor-icon rendering (a frame or two before the first real position
        // is unnoticeable), but the spotlight reads them immediately on the very first frame, so without
        // this it visibly starts in the top-left corner on a fresh session until the mouse first moves.
        if (NativeMethods.GetCursorPos(out var initialCursorPt))
        {
            MapScreenToCanvas(initialCursorPt.X, initialCursorPt.Y, out var initialCanvasX, out var initialCanvasY, out var initialVisible);
            if (initialVisible)
            {
                _cursorX = (int)initialCanvasX;
                _cursorY = (int)initialCanvasY;
            }
        }

        _prepared = true;
    }

    private void PrepareMonitorCapture(MonitorInfo monitor)
    {
        // Needed to convert CaretLocator's virtual-screen coordinates into this monitor's local pixel
        // space (0..Width, 0..Height) — the same space _cursorX/_cursorY already live in.
        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };
        if (NativeMethods.GetMonitorInfo(monitor.Handle, ref monitorInfo))
        {
            _monitorOriginX = monitorInfo.rcMonitor.Left;
            _monitorOriginY = monitorInfo.rcMonitor.Top;
        }

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        IDXGIAdapter1? targetAdapter = null;
        IDXGIOutput? targetOutput = null;

        for (uint ai = 0; factory.EnumAdapters1(ai, out var adapter).Success; ai++)
        {
            IDXGIOutput? matched = null;
            for (uint oi = 0; adapter.EnumOutputs(oi, out var output).Success; oi++)
            {
                if (output.Description.Monitor == monitor.Handle)
                {
                    matched = output;
                    break;
                }
                output.Dispose();
            }

            if (matched is not null)
            {
                targetAdapter = adapter;
                targetOutput = matched;
                break;
            }

            adapter.Dispose();
        }

        if (targetAdapter is null || targetOutput is null)
        {
            throw new InvalidOperationException($"Could not find a graphics adapter output for '{monitor.FriendlyName}'.");
        }

        using (targetAdapter)
        using (targetOutput)
        {
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0,
            };

            D3D11.D3D11CreateDevice(
                targetAdapter,
                Vortice.Direct3D.DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out var device).CheckError();
            _device = device;
            _context = device!.ImmediateContext;

            using var output1 = targetOutput.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(device);
        }

        var desc = _duplication.Description;
        Width = (int)desc.ModeDescription.Width;
        Height = (int)desc.ModeDescription.Height;
    }

    /// <summary>
    /// Stands up the WGC path for a specific window: a plain D3D11 device (not tied to any one adapter
    /// the way monitor capture is, since a window can move between displays), the interop calls to wrap
    /// it as the WinRT device WGC needs and to create a GraphicsCaptureItem for the HWND (see
    /// <see cref="Interop.GraphicsCaptureInterop"/>), and a frame pool sized to the window's current size
    /// — which becomes this session's fixed Width/Height for everything downstream, per this class's
    /// remarks on the fixed-pipe requirement.
    /// </summary>
    private void PrepareWindowCapture(WindowInfo window)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Windows.Graphics.Capture isn't supported on this system.");
        }

        // A stale selection (the window closed since it was picked, e.g. a saved-settings target that's
        // no longer running) makes WGC's CreateForWindow fail with a bare, unhelpful E_INVALIDARG —
        // "The parameter is incorrect." with no indication why. Catching it here up front gives the
        // caller a message that actually says what's wrong instead of a raw HRESULT translation.
        if (!NativeMethods.IsWindow(window.Handle))
        {
            throw new InvalidOperationException(
                $"'{window.Title}' is no longer open. Refresh the window list on the Settings tab and pick another window.");
        }

        _targetWindowHandle = window.Handle;

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        };
        D3D11.D3D11CreateDevice(
            null,
            Vortice.Direct3D.DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out var device).CheckError();
        _device = device;
        _context = device!.ImmediateContext;

        using var dxgiDevice = device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
        GraphicsCaptureInterop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var wgcDevicePtr);
        _wgcDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(wgcDevicePtr);

        _captureItem = GraphicsCaptureInterop.CreateItemForWindow(window.Handle);
        _captureItem.Closed += OnCaptureItemClosed;

        // Unlike a monitor resolution (always even in practice), an arbitrarily-resized window can land
        // on an odd pixel size — and libx264 flatly refuses an odd width/height for yuv420p ("height not
        // divisible by 2"), which fails the encoder open silently enough that ffmpeg exits "successfully"
        // having muxed nothing at all (a 0-byte output, no error surfaced to the UI). Round down to even;
        // losing at most one row/column of the letterbox canvas is unnoticeable.
        Width = _captureItem.Size.Width & ~1;
        Height = _captureItem.Size.Height & ~1;
        _wgcPoolSize = _captureItem.Size;

        // CreateFreeThreaded (not Create) is required here: Prepare() runs off a background Task.Run
        // thread with no DispatcherQueue, and Direct3D11CaptureFramePool.Create() needs one on the
        // calling thread to dispatch FrameArrived onto — without it, WGC captures frames into the pool
        // internally but never raises the event, so BeginCapture() "succeeds" yet zero frames ever
        // arrive. CreateFreeThreaded fires FrameArrived on an arbitrary MTA thread instead, which is
        // exactly what OnWgcFrameArrived's threading discipline (frameLock, no UI-affinitized calls) is
        // already built for.
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_wgcDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _wgcPoolSize);
        _framePool.FrameArrived += OnWgcFrameArrived;

        _captureSession = _framePool.CreateCaptureSession(_captureItem);
        _captureSession.IsCursorCaptureEnabled = _captureCursor;
        // Note: newer Windows versions can suppress WGC's yellow capture border via
        // GraphicsCaptureSession.IsBorderRequired, but that member isn't present in this SDK's
        // projection — a cosmetic gap only, not a functional one, so left as the OS default.

        _targetLostSignaled = 0;
        _windowValidityTimer = new System.Threading.Timer(_ =>
        {
            if (!NativeMethods.IsWindow(_targetWindowHandle)) SignalCaptureTargetLostOnce();
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Updates the cursor spotlight live, without a Prepare()/BeginCapture() restart — it's a pure
    /// compositing parameter read per frame by <see cref="ApplySpotlight"/> (via
    /// <see cref="TryGetLatestFrame"/>), with no device or hook to re-create, so it can change at any
    /// time including mid-recording. Radius used to be a Prepare()-time-only argument, which meant
    /// dragging the radius slider changed the saved setting but had no visible effect on the running
    /// preview at all — it only took hold on the next restart for some unrelated reason.
    ///
    /// The falloff lookup table isn't rebuilt here: ApplySpotlight already rebuilds it whenever the
    /// radius it's handed differs from the cached one, so a drag that crosses 30 values only pays for
    /// the radii that a frame is actually rendered at.
    /// </summary>
    public void UpdateSpotlight(bool enabled, double radius)
    {
        _spotlightRadius = Math.Max(1, (int)Math.Round(radius));
        _spotlightEnabled = enabled;
    }

    /// <summary>
    /// Live cursor rendering change — no restart. <see cref="_captureCursor"/>/<see cref="_cursorStyle"/>
    /// are read per frame by <see cref="CopyFrameToBuffer"/>, so flipping them takes effect on the very
    /// next frame of the preview or of an in-progress recording. Window (WGC) capture bakes the real
    /// system cursor in at the session level instead, so that path pushes the flag onto the live capture
    /// session; the style has no meaning there (see <see cref="CursorStyle"/>).
    /// </summary>
    public void UpdateCursor(bool captureCursor, CursorStyle cursorStyle)
    {
        _captureCursor = captureCursor;
        _cursorStyle = cursorStyle;

        if (_captureTarget == CaptureTargetKind.Window)
        {
            lock (_wgcCallbackLock)
            {
                try { if (_captureSession is not null) _captureSession.IsCursorCaptureEnabled = captureCursor; }
                catch { /* session may be mid-teardown — cosmetic either way */ }
            }
        }
    }

    /// <summary>
    /// Live smart-zoom change — no restart. The factor and enabled flag are eased toward per frame by
    /// <see cref="ApplyZoom"/>, so turning zoom off mid-recording glides back out to 1x rather than
    /// cutting. Zoom needs the keyboard/mouse hooks for its activity signal (see <see cref="Prepare"/>),
    /// which may not exist if the session started with zoom off — <see cref="EnsureActivityHooks"/>
    /// installs them on demand.
    /// </summary>
    public void UpdateZoom(bool enabled, double factor)
    {
        _zoomTargetFactor = factor;
        _zoomEnabled = enabled;
        if (enabled) EnsureActivityHooks(needKeyboard: true, needMouse: true, needClickAt: false);
    }

    /// <summary>
    /// Live keystroke-overlay toggle — no restart. Creates the renderer (and the keyboard hook that
    /// feeds it) the first time it's switched on mid-session; switching off drops the renderer so
    /// <see cref="ApplyKeystrokeOverlay"/> stops compositing, leaving the hook in place for whatever
    /// else may still want the activity signal.
    /// </summary>
    public void UpdateKeystrokeOverlay(bool enabled)
    {
        if (enabled)
        {
            _keystrokeOverlay ??= new KeystrokeOverlayRenderer();
            EnsureActivityHooks(needKeyboard: true, needMouse: false, needClickAt: false);
        }
        else
        {
            _keystrokeOverlay = null;
        }
    }

    /// <summary>Live click-ripple toggle — no restart. Needs the mouse hook's ClickAt signal, installed on demand.</summary>
    public void UpdateClickRipples(bool enabled)
    {
        _clickRipplesEnabled = enabled;
        if (enabled)
        {
            EnsureActivityHooks(needKeyboard: false, needMouse: true, needClickAt: true);
        }
        else
        {
            lock (_rippleLock) { _activeRipples.Clear(); }
        }
    }

    /// <summary>
    /// Installs the global keyboard/mouse hooks a live-enabled effect needs, if the session didn't
    /// already start with them. Only ever adds — a hook that's up stays up for the rest of the capture
    /// session and is torn down by <see cref="Stop"/>, since the cost of an installed low-level hook is
    /// the install itself, not keeping it. Guarded by <see cref="_hookLock"/> because these calls come
    /// from the UI thread while <see cref="Stop"/> may be tearing the same fields down.
    /// </summary>
    private void EnsureActivityHooks(bool needKeyboard, bool needMouse, bool needClickAt)
    {
        lock (_hookLock)
        {
            if (!_prepared) return;

            if (needKeyboard && _keyboardHook is null)
            {
                _keyboardHook = new GlobalKeyboardHook();
                _keyboardHook.KeyPressed += OnKeyboardActivity;
                if (IsCapturing) _keyboardHook.Start();
            }

            if (needMouse && _mouseHook is null)
            {
                _mouseHook = new GlobalMouseHook();
                _mouseHook.Click += OnMouseActivity;
                if (IsCapturing) _mouseHook.Start();
            }

            if (needClickAt && _mouseHook is not null && !_clickAtSubscribed)
            {
                _mouseHook.ClickAt += OnMouseClickAt;
                _clickAtSubscribed = true;
            }
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args) => SignalCaptureTargetLostOnce();

    /// <summary>Starts delivering frames on a dedicated background thread. Call <see cref="Prepare"/> first.</summary>
    public void BeginCapture()
    {
        if (!_prepared || IsCapturing) return;

        _running = true;
        IsCapturing = true;
        _keyboardHook?.Start();
        _mouseHook?.Start();

        if (_captureTarget == CaptureTargetKind.Window)
        {
            _captureSession!.StartCapture();
        }
        else
        {
            _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "DesktopDuplicationCapture" };
            _captureThread.Start();
        }
    }

    private void MarkActivity() => Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    /// <summary>Mouse movement/click: the pan target goes back to following the cursor.</summary>
    private void OnMouseActivity()
    {
        MarkActivity();
        _typingTargetX = null;
        _typingTargetY = null;
    }

    /// <summary>
    /// Click ripple trigger — fires on GlobalMouseHook's own thread, with the click's screen coordinates
    /// read straight out of the low-level hook (see GlobalMouseHook.ClickAt's remarks). Reuses
    /// MapScreenToCanvas, the same monitor/window-aware screen-to-canvas transform the caret-follow and
    /// cursor-polling code already depend on, so a click outside the captured area (e.g. a second
    /// monitor, when recording just one) is correctly ignored rather than clamped to a wrong edge.
    /// </summary>
    private void OnMouseClickAt(int screenX, int screenY)
    {
        MapScreenToCanvas(screenX, screenY, out var canvasX, out var canvasY, out var visible);
        if (!visible) return;

        lock (_rippleLock)
        {
            _activeRipples.Add(new ActiveRipple(canvasX, canvasY, _zoomClock.Elapsed.TotalSeconds));
        }
    }

    /// <summary>Keypress: the pan target follows the text caret instead, when one can be located.</summary>
    private void OnKeyboardActivity(string display)
    {
        MarkActivity();

        if (_zoomEnabled && CaretLocator.TryGetCaretScreenPosition(out var screenX, out var screenY))
        {
            MapScreenToCanvas(screenX, screenY, out var localX, out var localY, out var visible);
            // Ignore a caret that isn't within the captured area (e.g. typing on a second display, or
            // outside the captured window) rather than pinning the zoom uselessly at the frame's edge.
            if (visible)
            {
                _typingTargetX = localX;
                _typingTargetY = localY;
            }
            else
            {
                _typingTargetX = null;
                _typingTargetY = null;
            }
        }

        _keystrokeOverlay?.OnKeyPressed(display);
    }

    /// <summary>
    /// Maps a virtual-screen point (as CaretLocator and GetCursorPos report) into this capture's
    /// canvas-local pixel space (0..Width, 0..Height) — monitor mode via the fixed origin resolved once
    /// in <see cref="PrepareMonitorCapture"/>, window mode via the window's *current* screen position
    /// (it can move) combined with the letterbox scale/offset (updated every frame, since the window can
    /// be resized). <paramref name="visible"/> is false when the point falls outside the captured area.
    /// </summary>
    private void MapScreenToCanvas(double screenX, double screenY, out double canvasX, out double canvasY, out bool visible)
    {
        if (_captureTarget == CaptureTargetKind.Window)
        {
            NativeMethods.GetWindowRect(_targetWindowHandle, out var rect);
            canvasX = (screenX - rect.Left) * _letterboxScale + _letterboxOffsetX;
            canvasY = (screenY - rect.Top) * _letterboxScale + _letterboxOffsetY;
            visible = screenX >= rect.Left && screenX <= rect.Right && screenY >= rect.Top && screenY <= rect.Bottom;
        }
        else
        {
            canvasX = screenX - _monitorOriginX;
            canvasY = screenY - _monitorOriginY;
            visible = canvasX >= 0 && canvasX <= Width && canvasY >= 0 && canvasY <= Height;
        }
    }

    private void CaptureLoop()
    {
        while (_running)
        {
            try
            {
                var result = _duplication!.AcquireNextFrame(500, out var frameInfo, out var desktopResource);
                if (result.Failure)
                {
                    // WAIT_TIMEOUT just means the desktop hasn't changed in the last 500ms; anything
                    // else (e.g. ACCESS_LOST from a resolution change or a fullscreen-exclusive app) we
                    // simply retry on — there is no per-frame state to corrupt here, unlike the old
                    // WGC path.
                    continue;
                }

                // AcquireNextFrame also wakes up (with AccumulatedFrames == 0) on a pointer-only update —
                // e.g. the mouse moving over an otherwise-static desktop. PointerPosition is only valid
                // on the call where the pointer actually changed, though: whenever LastMouseUpdateTime is
                // 0, DXGI leaves PointerPosition zeroed out (Visible=false, Position=(0,0)) rather than
                // repeating the last known state, so that case has to be ignored and the previous
                // position/visibility retained — otherwise the cursor overlay would vanish on literally
                // the very next frame after every single mouse update.
                if (frameInfo.LastMouseUpdateTime != 0)
                {
                    var newX = frameInfo.PointerPosition.Position.X;
                    var newY = frameInfo.PointerPosition.Position.Y;
                    if (Math.Abs(newX - _cursorX) > MovementActivityThresholdPx || Math.Abs(newY - _cursorY) > MovementActivityThresholdPx)
                    {
                        OnMouseActivity();
                    }

                    _cursorVisible = frameInfo.PointerPosition.Visible;
                    _cursorX = newX;
                    _cursorY = newY;
                }

                // The shape itself (what the cursor actually looks like right now — arrow, I-beam,
                // resize handle, a custom app cursor, whatever the user's cursor theme provides) only
                // needs re-fetching when it changes, which DXGI signals via a nonzero buffer size here.
                if (frameInfo.PointerShapeBufferSize > 0)
                {
                    UpdateSystemCursorShape(frameInfo.PointerShapeBufferSize);
                }

                // The frame itself is re-copied unconditionally (not just when AccumulatedFrames > 0),
                // otherwise a moving cursor drawn into the buffer below would visibly lag behind real
                // mouse movement whenever the rest of the screen happens to be still.

                using (desktopResource)
                {
                    using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    CopyFrameToBuffer(texture);
                }

                _duplication.ReleaseFrame();
            }
            catch
            {
                // Best effort: skip this frame rather than tearing down the capture thread.
                try { _duplication?.ReleaseFrame(); } catch { /* already released or lost */ }
            }
        }
    }

    private void CopyFrameToBuffer(ID3D11Texture2D texture)
    {
        var desc = texture.Description;

        if (_stagingTexture is null)
        {
            var stagingDesc = new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            };
            _stagingTexture = _device!.CreateTexture2D(stagingDesc);
        }

        _context!.CopyResource(_stagingTexture, texture);

        MappedSubresource mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int width = (int)desc.Width;
            int height = (int)desc.Height;
            int rowBytes = width * 4;
            var buffer = new byte[rowBytes * height];

            nint srcPtr = mapped.DataPointer;
            int rowPitch = (int)mapped.RowPitch;
            for (int y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(srcPtr, y * rowPitch), buffer, y * rowBytes, rowBytes);
            }

            if (_captureCursor && _cursorVisible)
            {
                var icon = _cursorStyle == CursorStyle.SystemDefault ? _systemCursorIcon : CursorIcons.Get(_cursorStyle);
                if (icon is { } iconValue)
                {
                    BlendCursorIcon(buffer, width, height, iconValue, _cursorX, _cursorY);
                }
            }

            ApplyZoom(buffer, width, height);
            // Keystroke overlay, webcam, spotlight, and ripples are NOT applied here — see
            // TryGetLatestFrame's remarks on why the whole effects stack is composited at pull time
            // instead, decoupled from DXGI's own frame-arrival cadence.

            lock (_frameLock)
            {
                _latestFrame = buffer;
                _frameWidth = width;
                _frameHeight = height;
                _hasFrame = true;
            }
        }
        finally
        {
            _context.Unmap(_stagingTexture, 0);
        }
    }

    /// <summary>
    /// Guards every access to the WGC-path D3D11 device/context/staging texture that happens off this
    /// class's own call stack — i.e. everything <see cref="OnWgcFrameArrived"/> touches. WGC's
    /// FrameArrived fires on WGC's own thread, entirely independent of whatever thread called
    /// <see cref="Stop"/>; without this lock, Stop() can dispose <c>_device</c>/<c>_context</c>/
    /// <c>_wgcStagingTexture</c> while a frame-arrived callback is still mid-flight using them — a
    /// use-after-free race that reproduces the exact unrecoverable AccessViolationException this class's
    /// remarks describe from the earlier WGC integration. Held for the whole body of
    /// <see cref="OnWgcFrameArrived"/>, and by <see cref="Stop"/> before it touches any of those fields,
    /// so Stop() simply blocks until an in-flight callback finishes instead of racing it.
    /// </summary>
    private readonly object _wgcCallbackLock = new();

    /// <summary>WGC's frame-arrived callback — fires on WGC's own thread. See <see cref="_wgcCallbackLock"/>.</summary>
    private void OnWgcFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_wgcCallbackLock)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) return;

                // The frame pool's own buffer size has to track the window's actual current size — a WGC
                // requirement independent of (and separate from) letterboxing that frame onto our fixed
                // Width/Height canvas below.
                if (frame.ContentSize.Width != _wgcPoolSize.Width || frame.ContentSize.Height != _wgcPoolSize.Height)
                {
                    _wgcPoolSize = frame.ContentSize;
                    sender.Recreate(_wgcDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _wgcPoolSize);
                }

                CopyWgcFrameToBuffer(frame);
            }
            catch
            {
                // Best effort: skip this frame rather than tearing down capture (e.g. a transient
                // device-lost or a frame arriving mid-Recreate).
            }
        }
    }

    private void CopyWgcFrameToBuffer(Direct3D11CaptureFrame frame)
    {
        // See GraphicsCaptureInterop's remarks: a classic RCW cast/call here reliably throws
        // InvalidCastException in this process (CsWinRT's global ComWrappers registration), so this goes
        // through a raw COM vtable call instead.
        var texturePtr = GraphicsCaptureInterop.GetInterfaceFromWinRTObject(frame.Surface, GraphicsCaptureInterop.Id3D11Texture2DIid);
        using var sourceTexture = new ID3D11Texture2D(texturePtr);

        var desc = sourceTexture.Description;
        int srcWidth = (int)desc.Width;
        int srcHeight = (int)desc.Height;
        if (srcWidth <= 0 || srcHeight <= 0) return;

        if (_wgcStagingTexture is null || _wgcStagingWidth != srcWidth || _wgcStagingHeight != srcHeight)
        {
            _wgcStagingTexture?.Dispose();
            var stagingDesc = new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            };
            _wgcStagingTexture = _device!.CreateTexture2D(stagingDesc);
            _wgcStagingWidth = srcWidth;
            _wgcStagingHeight = srcHeight;
        }

        _context!.CopyResource(_wgcStagingTexture, sourceTexture);

        MappedSubresource mapped = _context.Map(_wgcStagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = srcWidth * 4;
            var srcBuffer = new byte[rowBytes * srcHeight];
            nint srcPtr = mapped.DataPointer;
            int rowPitch = (int)mapped.RowPitch;
            for (int y = 0; y < srcHeight; y++)
            {
                Marshal.Copy(IntPtr.Add(srcPtr, y * rowPitch), srcBuffer, y * rowBytes, rowBytes);
            }

            // Letterbox: scale-to-fit the captured window onto the fixed Width x Height canvas, centered,
            // with black bars for whatever the scale-to-fit doesn't cover — this is what lets the window
            // be resized live without ever changing the frame size ffmpeg was told to expect. The system
            // cursor is already baked into srcBuffer by WGC itself (IsCursorCaptureEnabled), so this is a
            // plain resample, not a composite.
            var canvas = new byte[Width * Height * 4]; // zero-initialized = black; alpha unused downstream
            double scale = Math.Min((double)Width / srcWidth, (double)Height / srcHeight);
            int destRectW = Math.Max(1, (int)Math.Round(srcWidth * scale));
            int destRectH = Math.Max(1, (int)Math.Round(srcHeight * scale));
            int destRectX = (Width - destRectW) / 2;
            int destRectY = (Height - destRectH) / 2;
            _letterboxScale = scale;
            _letterboxOffsetX = destRectX;
            _letterboxOffsetY = destRectY;

            ResampleCatmullRomInto(srcBuffer, srcWidth, srcHeight, 0, 0, srcWidth, srcHeight,
                canvas, Width, Height, destRectX, destRectY, destRectW, destRectH);

            PollWindowCursor();

            ApplyZoom(canvas, Width, Height);
            // Keystroke overlay, webcam, spotlight, and ripples are NOT applied here — see
            // TryGetLatestFrame's remarks on why the whole effects stack is composited at pull time
            // instead, decoupled from WGC's own frame-arrival cadence.

            lock (_frameLock)
            {
                _latestFrame = canvas;
                _frameWidth = Width;
                _frameHeight = Height;
                _hasFrame = true;
            }
        }
        finally
        {
            _context.Unmap(_wgcStagingTexture, 0);
        }
    }

    /// <summary>
    /// Window mode's counterpart to <see cref="CaptureLoop"/>'s DXGI pointer-position handling: WGC gives
    /// us no equivalent per-frame pointer event, so this polls <c>GetCursorPos</c> once per captured
    /// frame instead, purely to feed the smart-zoom activity/follow signal — the visible cursor pixels
    /// themselves are already baked in by WGC (<see cref="_captureCursor"/> → <c>IsCursorCaptureEnabled</c>),
    /// so nothing here draws anything.
    /// </summary>
    private void PollWindowCursor()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        MapScreenToCanvas(pt.X, pt.Y, out var localX, out var localY, out var visible);
        int ix = (int)localX;
        int iy = (int)localY;
        if (visible && (Math.Abs(ix - _cursorX) > MovementActivityThresholdPx || Math.Abs(iy - _cursorY) > MovementActivityThresholdPx))
        {
            OnMouseActivity();
        }
        _cursorVisible = visible;
        _cursorX = ix;
        _cursorY = iy;
    }

    // Reserved alpha value meaning "invert the destination pixel" instead of a normal opaque/blended
    // color — used to reproduce the XOR-mask trick some real cursor shapes rely on. Safe to repurpose
    // since none of the stylized CursorIcons builders ever emit exactly this alpha value.
    private const byte InvertAlphaSentinel = 254;

    /// <summary>
    /// Fetches and decodes the real Windows cursor bitmap for <see cref="CursorStyle.SystemDefault"/>,
    /// caching the result in <see cref="_systemCursorIcon"/> until the shape next changes. DXGI hands
    /// back one of three formats (monochrome AND/XOR masks, straight color, or color with a binary
    /// invert mask) — see "Track the Mouse Pointer" in the Desktop Duplication docs for the exact pixel
    /// rules being followed here.
    /// </summary>
    private void UpdateSystemCursorShape(uint requiredSize)
    {
        if (requiredSize > _pointerShapeBufferCapacity)
        {
            if (_pointerShapeBuffer != nint.Zero) Marshal.FreeHGlobal(_pointerShapeBuffer);
            _pointerShapeBuffer = Marshal.AllocHGlobal((int)requiredSize);
            _pointerShapeBufferCapacity = requiredSize;
        }

        _duplication!.GetFramePointerShape(requiredSize, _pointerShapeBuffer, out _, out var shapeInfo);

        int width = (int)shapeInfo.Width;
        int pitch = (int)shapeInfo.Pitch;

        switch (shapeInfo.Type)
        {
            case 1: // DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME
            {
                int height = (int)shapeInfo.Height / 2;
                var raw = new byte[pitch * height * 2];
                Marshal.Copy(_pointerShapeBuffer, raw, 0, raw.Length);

                var bgra = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int andBit = (raw[y * pitch + x / 8] >> (7 - x % 8)) & 1;
                        int xorBit = (raw[(y + height) * pitch + x / 8] >> (7 - x % 8)) & 1;
                        int i = (y * width + x) * 4;

                        if (andBit == 1 && xorBit == 0)
                        {
                            // Transparent: leave alpha at 0.
                        }
                        else if (andBit == 1 && xorBit == 1)
                        {
                            bgra[i + 3] = InvertAlphaSentinel;
                        }
                        else
                        {
                            byte shade = (byte)(xorBit == 1 ? 255 : 0);
                            bgra[i + 0] = shade; bgra[i + 1] = shade; bgra[i + 2] = shade; bgra[i + 3] = 255;
                        }
                    }
                }

                _systemCursorIcon = new CursorIconBitmap(bgra, width, height, (int)shapeInfo.HotSpot.X, (int)shapeInfo.HotSpot.Y);
                break;
            }

            case 4: // DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR
            {
                int height = (int)shapeInfo.Height;
                var bgra = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(IntPtr.Add(_pointerShapeBuffer, y * pitch), bgra, y * width * 4, width * 4);
                }

                for (int i = 3; i < bgra.Length; i += 4)
                {
                    // Only 0x00 (opaque color as-is) or 0xFF (XOR-invert using the same pixel) are used.
                    bgra[i] = bgra[i] == 0xFF ? InvertAlphaSentinel : (byte)255;
                }

                _systemCursorIcon = new CursorIconBitmap(bgra, width, height, (int)shapeInfo.HotSpot.X, (int)shapeInfo.HotSpot.Y);
                break;
            }

            default: // 2 = DXGI_OUTDUPL_POINTER_SHAPE_TYPE_COLOR: straight top-down BGRA, already meaningful per-pixel alpha.
            {
                int height = (int)shapeInfo.Height;
                var bgra = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(IntPtr.Add(_pointerShapeBuffer, y * pitch), bgra, y * width * 4, width * 4);
                }

                _systemCursorIcon = new CursorIconBitmap(bgra, width, height, (int)shapeInfo.HotSpot.X, (int)shapeInfo.HotSpot.Y);
                break;
            }
        }
    }

    /// <summary>Alpha-blends a small pre-rendered cursor marker onto <paramref name="frame"/> at the given position (in the same pixel coordinates DXGI reports pointer position in — top-left-relative to this output).</summary>
    private static void BlendCursorIcon(byte[] frame, int frameWidth, int frameHeight, CursorIconBitmap icon, int cursorX, int cursorY)
    {
        int originX = cursorX - icon.HotspotX;
        int originY = cursorY - icon.HotspotY;

        for (int y = 0; y < icon.Height; y++)
        {
            int fy = originY + y;
            if (fy < 0 || fy >= frameHeight) continue;

            for (int x = 0; x < icon.Width; x++)
            {
                int fx = originX + x;
                if (fx < 0 || fx >= frameWidth) continue;

                int iconIdx = (y * icon.Width + x) * 4;
                byte alpha = icon.Bgra[iconIdx + 3];
                if (alpha == 0) continue;

                int frameIdx = (fy * frameWidth + fx) * 4;
                if (alpha == InvertAlphaSentinel)
                {
                    // A handful of real Windows cursors (e.g. the classic I-beam's thin center line) use
                    // an XOR-with-screen trick instead of a fixed color, most commonly seen in
                    // monochrome/masked-color cursor shapes — inverting the destination is the standard
                    // way to reproduce that instead of guessing a fixed replacement color.
                    frame[frameIdx + 0] = (byte)(255 - frame[frameIdx + 0]);
                    frame[frameIdx + 1] = (byte)(255 - frame[frameIdx + 1]);
                    frame[frameIdx + 2] = (byte)(255 - frame[frameIdx + 2]);
                }
                else if (alpha == 255)
                {
                    frame[frameIdx + 0] = icon.Bgra[iconIdx + 0];
                    frame[frameIdx + 1] = icon.Bgra[iconIdx + 1];
                    frame[frameIdx + 2] = icon.Bgra[iconIdx + 2];
                }
                else
                {
                    float a = alpha / 255f;
                    frame[frameIdx + 0] = (byte)(icon.Bgra[iconIdx + 0] * a + frame[frameIdx + 0] * (1 - a));
                    frame[frameIdx + 1] = (byte)(icon.Bgra[iconIdx + 1] * a + frame[frameIdx + 1] * (1 - a));
                    frame[frameIdx + 2] = (byte)(icon.Bgra[iconIdx + 2] * a + frame[frameIdx + 2] * (1 - a));
                }
                // Frame alpha is left untouched — DXGI frames carry alpha=0 for opaque content and
                // ffmpeg's rawvideo->yuv420p path ignores the channel entirely either way.
            }
        }
    }

    /// <summary>
    /// Eases the zoom factor and pan center toward their targets, then — if the eased factor is
    /// meaningfully above 1x — resamples a centered crop of <paramref name="frame"/> back up to the full
    /// frame size in place, in effect a "camera" pushing in on and following the cursor. The target factor
    /// itself is gated on recent interaction: zoom eases back to 1x whenever the user has been idle
    /// (no mouse movement/click/keypress) for <see cref="IdleTimeoutSeconds"/>, not just when the feature
    /// is toggled off — that's what makes it "smart" rather than continuously zoomed while enabled.
    /// Runs every frame (not just while <see cref="_zoomEnabled"/>) so disabling zoom, or going idle,
    /// eases back out to 1x instead of snapping.
    /// </summary>
    private void ApplyZoom(byte[] frame, int width, int height)
    {
        var nowSeconds = _zoomClock.Elapsed.TotalSeconds;
        var dt = Math.Max(0, nowSeconds - _lastZoomFrameSeconds);
        _lastZoomFrameSeconds = nowSeconds;
        // Exponential ease toward the target, driven by real elapsed time rather than a fixed per-frame
        // blend factor — reads as smooth, consistent easing regardless of the capture thread's actual
        // (variable) frame timing, instead of assuming a fixed FPS.
        var alpha = dt <= 0 ? 0 : 1 - Math.Exp(-dt / ZoomTimeConstant);

        var idleSeconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Volatile.Read(ref _lastActivityTicks)).TotalSeconds;
        var isIdle = idleSeconds > IdleTimeoutSeconds;
        var targetFactor = (_zoomEnabled && !isIdle) ? _zoomTargetFactor : 1.0;
        _zoomCurrentFactor += (targetFactor - _zoomCurrentFactor) * alpha;

        // While typing, follow the text caret instead of the mouse — _typingTargetX/Y is set on every
        // keypress and cleared on the next mouse movement/click, so whichever happened most recently
        // decides the pan target.
        double targetCenterX, targetCenterY;
        if (_typingTargetX is double typingX && _typingTargetY is double typingY)
        {
            targetCenterX = typingX;
            targetCenterY = typingY;
        }
        else
        {
            targetCenterX = _cursorVisible ? _cursorX : width / 2.0;
            targetCenterY = _cursorVisible ? _cursorY : height / 2.0;
        }
        if (!_zoomCenterInitialized)
        {
            _zoomCenterX = targetCenterX;
            _zoomCenterY = targetCenterY;
            _zoomCenterInitialized = true;
        }
        _zoomCenterX += (targetCenterX - _zoomCenterX) * alpha;
        _zoomCenterY += (targetCenterY - _zoomCenterY) * alpha;

        if (_zoomCurrentFactor <= 1.001) return;

        double cropWidth = width / _zoomCurrentFactor;
        double cropHeight = height / _zoomCurrentFactor;
        double cropX = Math.Clamp(_zoomCenterX - cropWidth / 2, 0, width - cropWidth);
        double cropY = Math.Clamp(_zoomCenterY - cropHeight / 2, 0, height - cropHeight);

        if (_zoomScratchBuffer is null || _zoomScratchBuffer.Length != frame.Length)
        {
            _zoomScratchBuffer = new byte[frame.Length];
        }
        var dst = _zoomScratchBuffer;

        ResampleCatmullRomInto(frame, width, height, cropX, cropY, cropWidth, cropHeight,
            dst, width, height, 0, 0, width, height);

        Buffer.BlockCopy(dst, 0, frame, 0, frame.Length);
    }

    /// <summary>
    /// Catmull-Rom bicubic resampling (16-tap, separable), parallelized per output row: maps the
    /// <paramref name="srcRectW"/>x<paramref name="srcRectH"/> region of <paramref name="src"/> starting
    /// at (<paramref name="srcX"/>, <paramref name="srcY"/>) onto the
    /// <paramref name="dstRectW"/>x<paramref name="dstRectH"/> region of <paramref name="dst"/> starting
    /// at (<paramref name="dstX"/>, <paramref name="dstY"/>) — everything outside that destination
    /// region is left untouched, so callers that need letterbox bars fill <paramref name="dst"/> first.
    /// Three call sites: <see cref="ApplyZoom"/> (crop region → the full canvas),
    /// <see cref="CopyWgcFrameToBuffer"/> (the whole captured window → a centered sub-rectangle of the
    /// fixed canvas), and <see cref="WebcamCaptureService"/> (a cropped-square webcam frame → its
    /// 300x300 circular thumbnail — internal rather than private for exactly that reuse). One resample
    /// kernel either way.
    ///
    /// This used to be bilinear (4-tap): cheaper, but bilinear's weights are a plain positive-only
    /// average, which is exactly what makes it soften/blur edges — visibly noticeable on zoomed text.
    /// Catmull-Rom's small negative side lobes are what recover that lost edge contrast (the same
    /// property that makes it "sharper" than bilinear in every image resampler that offers it), fixing
    /// the blur at its root cause instead of papering over it with a post-hoc sharpen filter that can't
    /// distinguish real detail from an artifact and risks haloing high-contrast UI text. It's ~4x
    /// bilinear's per-pixel cost (16 taps vs 4), which is a bounded, real-time-safe increase given
    /// bilinear was already proven fast enough here.
    /// </summary>
    internal static void ResampleCatmullRomInto(byte[] src, int srcW, int srcH, double srcX, double srcY, double srcRectW, double srcRectH,
        byte[] dst, int dstW, int dstH, int dstX, int dstY, int dstRectW, int dstRectH)
    {
        int maxX = srcW - 1;
        int maxY = srcH - 1;
        double scaleX = srcRectW / dstRectW;
        double scaleY = srcRectH / dstRectH;

        Parallel.For(0, dstRectH, row =>
        {
            int y = dstY + row;
            if (y < 0 || y >= dstH) return;

            double srcYf = srcY + row * scaleY;
            int iy = (int)Math.Floor(srcYf);
            double ty = srcYf - iy;
            double wy0 = CatmullRomWeight(ty + 1), wy1 = CatmullRomWeight(ty), wy2 = CatmullRomWeight(ty - 1), wy3 = CatmullRomWeight(ty - 2);
            int ry0 = Math.Clamp(iy - 1, 0, maxY) * srcW * 4;
            int ry1 = Math.Clamp(iy, 0, maxY) * srcW * 4;
            int ry2 = Math.Clamp(iy + 1, 0, maxY) * srcW * 4;
            int ry3 = Math.Clamp(iy + 2, 0, maxY) * srcW * 4;
            int dstRow = y * dstW * 4;

            for (int col = 0; col < dstRectW; col++)
            {
                int x = dstX + col;
                if (x < 0 || x >= dstW) continue;

                double srcXf = srcX + col * scaleX;
                int ix = (int)Math.Floor(srcXf);
                double tx = srcXf - ix;
                double wx0 = CatmullRomWeight(tx + 1), wx1 = CatmullRomWeight(tx), wx2 = CatmullRomWeight(tx - 1), wx3 = CatmullRomWeight(tx - 2);
                int cx0 = Math.Clamp(ix - 1, 0, maxX) * 4;
                int cx1 = Math.Clamp(ix, 0, maxX) * 4;
                int cx2 = Math.Clamp(ix + 1, 0, maxX) * 4;
                int cx3 = Math.Clamp(ix + 2, 0, maxX) * 4;

                int dstIdx = dstRow + x * 4;
                for (int c = 0; c < 4; c++)
                {
                    double r0 = wx0 * src[ry0 + cx0 + c] + wx1 * src[ry0 + cx1 + c] + wx2 * src[ry0 + cx2 + c] + wx3 * src[ry0 + cx3 + c];
                    double r1 = wx0 * src[ry1 + cx0 + c] + wx1 * src[ry1 + cx1 + c] + wx2 * src[ry1 + cx2 + c] + wx3 * src[ry1 + cx3 + c];
                    double r2 = wx0 * src[ry2 + cx0 + c] + wx1 * src[ry2 + cx1 + c] + wx2 * src[ry2 + cx2 + c] + wx3 * src[ry2 + cx3 + c];
                    double r3 = wx0 * src[ry3 + cx0 + c] + wx1 * src[ry3 + cx1 + c] + wx2 * src[ry3 + cx2 + c] + wx3 * src[ry3 + cx3 + c];
                    double sum = wy0 * r0 + wy1 * r1 + wy2 * r2 + wy3 * r3;
                    // Unlike bilinear, cubic weights can be negative and the weighted sum can overshoot
                    // past [0, 255] at hard edges — has to be clamped, not just cast.
                    dst[dstIdx + c] = (byte)Math.Clamp(sum + 0.5, 0.0, 255.0);
                }
            }
        });
    }

    /// <summary>
    /// The Catmull-Rom cardinal spline kernel (Mitchell-Netravali with B=0, C=0.5) — the standard choice
    /// for a resampler that wants to stay sharper than bilinear without the visible ringing of a
    /// wider-support/less-damped cubic. <paramref name="t"/> is the distance from the sample point in
    /// source-pixel units; zero outside [-2, 2].
    /// </summary>
    private static double CatmullRomWeight(double t)
    {
        t = Math.Abs(t);
        if (t <= 1.0) return 1.5 * t * t * t - 2.5 * t * t + 1.0;
        if (t < 2.0) return -0.5 * t * t * t + 2.5 * t * t - 4.0 * t + 2.0;
        return 0.0;
    }

    private void ApplyKeystrokeOverlay(byte[] frame, int width, int height)
    {
        if (_keystrokeOverlay is null) return;
        if (!_keystrokeOverlay.TryGetOverlay(out var overlay, out var overlayWidth, out var overlayHeight)) return;

        const int margin = 28;
        int originX = (width - overlayWidth) / 2;
        int originY = height - overlayHeight - margin;
        BlendOverlay(frame, width, height, overlay, overlayWidth, overlayHeight, originX, originY);
    }

    /// <summary>
    /// Builds a squared-distance-indexed lookup table mapping distSq → "clear factor" (255 = fully
    /// original/undimmed at the cursor, 0 = fully dimmed beyond the radius+feather edge). This is the
    /// answer to "where does the precomputation happen so we aren't rebuilding it every frame": once,
    /// here, cached in <see cref="_spotlightFalloffTable"/> and only rebuilt if the radius actually
    /// changes (checked in <see cref="ApplySpotlight"/>) — not per frame, and critically not per pixel.
    /// Math.Sqrt runs at most (radius+feather)² times total for a table build, never in the per-pixel
    /// hot path, which only ever does an integer squared-distance compute plus an array index.
    /// </summary>
    private static byte[] BuildSpotlightFalloffTable(int radius, int featherPx)
    {
        int outerRadius = radius + featherPx;
        int maxDistSq = outerRadius * outerRadius;
        var table = new byte[maxDistSq + 1];

        for (int distSq = 0; distSq <= maxDistSq; distSq++)
        {
            double dist = Math.Sqrt(distSq);
            double clear;
            if (dist <= radius) clear = 1.0;
            else if (dist >= outerRadius) clear = 0.0;
            else clear = (outerRadius - dist) / featherPx; // linear falloff across the feather band

            table[distSq] = (byte)Math.Clamp(clear * 255.0 + 0.5, 0, 255);
        }
        return table;
    }

    /// <summary>
    /// Dims the whole frame except a sharp circle around the cursor — bottom layer of the effects stack
    /// (drawn under the webcam/keystroke overlays, over the base frame and any ripples already on it).
    /// One full pass over the frame, unsafe/pointer-based per <c>fixed</c> below: rows entirely outside
    /// the spotlight (the common case once the cursor isn't near that row) take a cheap flat-dim
    /// fast path with no per-pixel distance math at all; only rows within reach of the radius do the
    /// per-pixel squared-distance lookup.
    /// </summary>
    private unsafe void ApplySpotlight(byte[] canvas, int width, int height, int cursorX, int cursorY, int radius)
    {
        if (radius <= 0) return;

        if (_spotlightFalloffTable is null || _spotlightFalloffTableRadius != radius)
        {
            _spotlightFalloffTable = BuildSpotlightFalloffTable(radius, SpotlightFeatherPx);
            _spotlightFalloffTableRadius = radius;
        }
        var table = _spotlightFalloffTable;
        int outerRadius = radius + SpotlightFeatherPx;
        int maxDistSq = outerRadius * outerRadius;
        const double dimAlpha = SpotlightDimAlpha;
        const double dimmedMul = 1.0 - dimAlpha;

        fixed (byte* basePtr = canvas)
        {
            byte* baseP = basePtr;
            Parallel.For(0, height, y =>
            {
                byte* row = baseP + (long)y * width * 4;
                int dy = y - cursorY;
                int dySq = dy * dy;

                if (dySq > maxDistSq)
                {
                    // Entire row is beyond the spotlight — flat-dim it in one pass, no per-pixel distance
                    // math needed at all.
                    for (int x = 0; x < width; x++)
                    {
                        byte* px = row + x * 4;
                        px[0] = (byte)(px[0] * dimmedMul);
                        px[1] = (byte)(px[1] * dimmedMul);
                        px[2] = (byte)(px[2] * dimmedMul);
                    }
                    return;
                }

                for (int x = 0; x < width; x++)
                {
                    int dx = x - cursorX;
                    int distSq = dx * dx + dySq;
                    byte* px = row + x * 4;

                    if (distSq > maxDistSq)
                    {
                        px[0] = (byte)(px[0] * dimmedMul);
                        px[1] = (byte)(px[1] * dimmedMul);
                        px[2] = (byte)(px[2] * dimmedMul);
                        continue;
                    }

                    byte clear = table[distSq];
                    if (clear == 255) continue; // fully inside the sharp circle — untouched

                    double mul = dimmedMul + dimAlpha * (clear / 255.0);
                    px[0] = (byte)(px[0] * mul);
                    px[1] = (byte)(px[1] * mul);
                    px[2] = (byte)(px[2] * mul);
                }
            });
        }
    }

    /// <summary>
    /// Renders every active ripple, pruning expired ones first — drawn under the spotlight (so a ripple
    /// outside the spotlight's clear circle gets dimmed along with everything else, matching where a
    /// click far from the current cursor position should visually recede) but over the base frame.
    /// Snapshots the ripple list under <see cref="_rippleLock"/> and renders without holding it, so
    /// OnMouseClickAt (a different thread) never blocks on this.
    /// </summary>
    private unsafe void ApplyRipples(byte[] canvas, int width, int height)
    {
        double now = _zoomClock.Elapsed.TotalSeconds;
        List<ActiveRipple>? snapshot = null;
        lock (_rippleLock)
        {
            _activeRipples.RemoveAll(r => now - r.StartSeconds > RippleDurationSeconds);
            if (_activeRipples.Count > 0) snapshot = [.. _activeRipples];
        }
        if (snapshot is null) return;

        fixed (byte* basePtr = canvas)
        {
            foreach (var ripple in snapshot)
            {
                double t = (now - ripple.StartSeconds) / RippleDurationSeconds;
                if (t is < 0 or > 1) continue;

                double radius = RippleMaxRadiusPx * t;
                double opacity = 1.0 - t;
                DrawRippleRing(basePtr, width, height, ripple.X, ripple.Y, radius, opacity);
            }
        }
    }

    /// <summary>Draws one ripple's ring, iterating only its own bounding box — never the full frame, regardless of how many ripples are active or how big the canvas is.</summary>
    private static unsafe void DrawRippleRing(byte* basePtr, int width, int height, double centerX, double centerY, double radius, double opacity)
    {
        // A cyan-ish accent reads clearly against most desktop/app content without looking like an error
        // indicator the way a red ring might.
        const byte colorB = 255, colorG = 220, colorR = 60;

        double outer = radius + RippleThicknessPx / 2.0;
        double inner = Math.Max(0, radius - RippleThicknessPx / 2.0);
        double outerSq = outer * outer;
        double innerSq = inner * inner;

        int boxLeft = Math.Max(0, (int)(centerX - outer) - 1);
        int boxTop = Math.Max(0, (int)(centerY - outer) - 1);
        int boxRight = Math.Min(width - 1, (int)(centerX + outer) + 1);
        int boxBottom = Math.Min(height - 1, (int)(centerY + outer) + 1);

        for (int y = boxTop; y <= boxBottom; y++)
        {
            double dy = y - centerY;
            double dySq = dy * dy;
            byte* row = basePtr + (long)y * width * 4;

            for (int x = boxLeft; x <= boxRight; x++)
            {
                double dx = x - centerX;
                double distSq = dx * dx + dySq;
                if (distSq < innerSq || distSq > outerSq) continue;

                byte* px = row + x * 4;
                px[0] = (byte)(colorB * opacity + px[0] * (1 - opacity));
                px[1] = (byte)(colorG * opacity + px[1] * (1 - opacity));
                px[2] = (byte)(colorR * opacity + px[2] * (1 - opacity));
            }
        }
    }

    /// <summary>
    /// Bottom-right picture-in-picture placement — deliberately a different corner from the keystroke
    /// overlay's bottom-center toast so the two never fight for the same screen space. Drawn above the
    /// spotlight (see TryGetLatestFrame's compositing order) so the presenter's own face is never dimmed
    /// by it, and — like the keystroke overlay — stays a fixed size/position on screen regardless of zoom
    /// level rather than getting caught up in the zoom crop like the underlying capture content does.
    /// </summary>
    private void ApplyWebcamOverlay(byte[] frame, int width, int height)
    {
        // Unguarded read of _webcam (no _webcamLifecycleLock here): if SetWebcam swaps/clears it
        // concurrently, the worst case is one more call into an instance that's mid-teardown — safe,
        // since WebcamCaptureService fully protects its own state with its own lock (TryGetOverlay just
        // sees either its last real frame or "no frame" a beat early). Taking the lifecycle lock on this
        // hot, every-frame path for that isn't worth it.
        var webcam = _webcam;
        if (webcam is null) return;
        if (!webcam.TryGetOverlay(out var overlay, out var size)) return;

        const int margin = 24;
        int originX = width - size - margin;
        int originY = height - size - margin;
        BlendOverlay(frame, width, height, overlay, size, size, originX, originY);
    }

    /// <summary>
    /// Starts, stops, or switches the webcam PiP overlay — entirely independent of Prepare()/Stop(),
    /// which handle the DXGI/WGC screen-capture engine only. Safe to call at any time (idle, previewing,
    /// or recording), from any thread, any number of times: a no-op if the requested state already
    /// matches what's running (same enabled flag, same device id), so callers don't need to track
    /// "did anything actually change" themselves — e.g. RestartPreviewIfIdle can call this unconditionally
    /// every time any setting changes, the same way it already does for Prepare()'s parameters.
    /// </summary>
    public void SetWebcam(bool enabled, string? deviceId)
    {
        lock (_webcamLifecycleLock)
        {
            if (enabled && deviceId is not null)
            {
                if (_webcam is not null && _webcamDeviceId == deviceId) return; // already running this camera

                var old = _webcam;
                var webcam = new WebcamCaptureService();
                _webcam = webcam;
                _webcamDeviceId = deviceId;
                _ = webcam.StartAsync(deviceId).ContinueWith(_ => { /* best effort: see StartAsync's own remarks */ },
                    TaskContinuationOptions.OnlyOnFaulted);
                if (old is not null) _ = old.StopAsync();
            }
            else
            {
                StopWebcamLocked();
            }
        }
    }

    /// <summary>The only thing that actually tears the camera down — called from SetWebcam(false, ...) and Dispose(). Must be called under <see cref="_webcamLifecycleLock"/>.</summary>
    private void StopWebcamLocked()
    {
        var old = _webcam;
        _webcam = null;
        _webcamDeviceId = null;
        if (old is not null) _ = old.StopAsync();
    }

    /// <summary>Straight-alpha blends a small BGRA overlay bitmap onto <paramref name="frame"/> at a fixed position — the keystroke-overlay counterpart to <see cref="BlendCursorIcon"/>, without that method's cursor-specific invert-alpha handling.</summary>
    private static void BlendOverlay(byte[] frame, int frameWidth, int frameHeight, byte[] overlay, int overlayWidth, int overlayHeight, int originX, int originY)
    {
        for (int y = 0; y < overlayHeight; y++)
        {
            int fy = originY + y;
            if (fy < 0 || fy >= frameHeight) continue;

            for (int x = 0; x < overlayWidth; x++)
            {
                int fx = originX + x;
                if (fx < 0 || fx >= frameWidth) continue;

                int oi = (y * overlayWidth + x) * 4;
                byte alpha = overlay[oi + 3];
                if (alpha == 0) continue;

                int fi = (fy * frameWidth + fx) * 4;
                if (alpha == 255)
                {
                    frame[fi + 0] = overlay[oi + 0];
                    frame[fi + 1] = overlay[oi + 1];
                    frame[fi + 2] = overlay[oi + 2];
                }
                else
                {
                    float a = alpha / 255f;
                    frame[fi + 0] = (byte)(overlay[oi + 0] * a + frame[fi + 0] * (1 - a));
                    frame[fi + 1] = (byte)(overlay[oi + 1] * a + frame[fi + 1] * (1 - a));
                    frame[fi + 2] = (byte)(overlay[oi + 2] * a + frame[fi + 2] * (1 - a));
                }
            }
        }
    }

    /// <summary>
    /// Copies the most recent frame into <paramref name="destination"/>. Returns false if no frame has
    /// arrived yet.
    /// </summary>
    /// <remarks>
    /// The full effects stack — ripples, spotlight, webcam, keystroke overlay — is composited here, at
    /// pull time, on every pacer tick, rather than baked into <see cref="_latestFrame"/> back when
    /// CaptureLoop/CopyWgcFrameToBuffer ran (which is where ApplyZoom still runs — that one's fine tied
    /// to screen-frame-arrival timing since it's itself screen-activity-driven). The others aren't:
    /// DXGI/WGC only produce a *new* frame when the screen actually changes, so on a mostly static screen
    /// (a presenter talking, mouse idle) CaptureLoop could run rarely while the webcam has fresh frames
    /// arriving, a click ripple is mid-animation, or the spotlight needs to track cursor movement, the
    /// whole time — compositing any of those back at screen-frame-arrival time left them visibly lagging
    /// real time (the bug this restructuring actually fixed for the webcam first). Pulling everything
    /// fresh here, at the pacer's fixed cadence regardless of screen activity, fixes that at the root.
    ///
    /// Z-order (bottom to top): base frame → ripples → spotlight (dims everything below it, including
    /// any ripple outside its clear circle) → webcam (always full brightness, never dimmed) → keystroke
    /// overlay (topmost, same reasoning — a toast dimmed by the spotlight would be nonsensical).
    /// </remarks>
    public bool TryGetLatestFrame(byte[] destination)
    {
        int width, height;
        lock (_frameLock)
        {
            if (!_hasFrame || _latestFrame is null) return false;
            Buffer.BlockCopy(_latestFrame, 0, destination, 0, _latestFrame.Length);
            width = _frameWidth;
            height = _frameHeight;
        }

        if (_clickRipplesEnabled) ApplyRipples(destination, width, height);
        if (_spotlightEnabled) ApplySpotlight(destination, width, height, _cursorX, _cursorY, _spotlightRadius);
        ApplyWebcamOverlay(destination, width, height);
        ApplyKeystrokeOverlay(destination, width, height);
        return true;
    }

    // Uses Width/Height (known as soon as Prepare() resolves the output's mode), not
    // _frameWidth/_frameHeight (only populated once the first real frame has been processed) — callers
    // need a correct byte size immediately after Prepare()/BeginCapture(), before any frame has arrived.
    public int FrameByteSize => Width * Height * 4;

    public void Stop()
    {
        if (!_prepared) return;
        IsCapturing = false;
        _prepared = false;

        _running = false;
        _captureThread?.Join(1000);
        _captureThread = null;

        // WGC teardown, in the documented-safe order: session first (stops new frames), then unhook
        // FrameArrived before disposing the pool (avoid a race where a frame arrives mid-dispose), then
        // the pool, then release the item/device references. See this class's remarks.
        _windowValidityTimer?.Dispose();
        _windowValidityTimer = null;
        if (_captureSession is not null)
        {
            try { _captureSession.Dispose(); } catch { /* best effort */ }
            _captureSession = null;
        }
        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnWgcFrameArrived;
            try { _framePool.Dispose(); } catch { /* best effort */ }
            _framePool = null;
        }
        if (_captureItem is not null)
        {
            _captureItem.Closed -= OnCaptureItemClosed;
            _captureItem = null;
        }
        // From here down, _device/_context/_wgcStagingTexture get disposed — the exact fields
        // OnWgcFrameArrived's callback thread reads/writes (see _wgcCallbackLock's remarks). FrameArrived
        // is already unsubscribed above, so no *new* callback can start, but this still has to wait for
        // one already in flight rather than race it.
        lock (_wgcCallbackLock)
        {
            _wgcDevice = null;
            _wgcStagingTexture?.Dispose();
            _wgcStagingTexture = null;
            _wgcStagingWidth = 0;
            _wgcStagingHeight = 0;

            lock (_hookLock)
            {
                if (_keyboardHook is not null)
                {
                    _keyboardHook.KeyPressed -= OnKeyboardActivity;
                    _keyboardHook.Dispose();
                    _keyboardHook = null;
                }
                if (_mouseHook is not null)
                {
                    _mouseHook.Click -= OnMouseActivity;
                    if (_clickAtSubscribed) _mouseHook.ClickAt -= OnMouseClickAt;
                    _mouseHook.Dispose();
                    _mouseHook = null;
                }
                _clickAtSubscribed = false;
            }
            _keystrokeOverlay = null;
            // Deliberately no webcam teardown here — see SetWebcam's remarks. Stop() tears down the
            // DXGI/WGC screen-capture engine only; the camera keeps running across a Stop()+Prepare()
            // cycle (e.g. switching monitors) so the PiP overlay doesn't visibly drop out every time the
            // screen target changes. StopWebcam() (called from SetWebcam(false, ...) and Dispose()) is
            // the only thing that actually tears the camera down.
            _typingTargetX = null;
            _typingTargetY = null;
            _zoomCurrentFactor = 1.0;
            _zoomCenterInitialized = false;

            _stagingTexture?.Dispose();
            _stagingTexture = null;

            _duplication?.Dispose();
            _duplication = null;

            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }

        if (_pointerShapeBuffer != nint.Zero)
        {
            Marshal.FreeHGlobal(_pointerShapeBuffer);
            _pointerShapeBuffer = nint.Zero;
            _pointerShapeBufferCapacity = 0;
        }
        _systemCursorIcon = null;

        lock (_frameLock)
        {
            _hasFrame = false;
            _latestFrame = null;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_webcamLifecycleLock) { StopWebcamLocked(); }
    }
}
