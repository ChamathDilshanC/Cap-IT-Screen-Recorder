using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture.Interop;
using ScreenRecorderApp.Services.Tracking;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Captures a monitor using the DXGI Desktop Duplication API and keeps the most recently decoded
/// BGRA frame available for a pacing thread to pull at a fixed FPS.
/// </summary>
/// <remarks>
/// This used to be built on Windows.Graphics.Capture, which requires bridging into WinRT via
/// hand-written COM interop (there's no public API to create a GraphicsCaptureItem for an arbitrary
/// monitor otherwise). That interop reliably crashed the whole process a few seconds into recording
/// with an unrecoverable AccessViolationException in a GC finalizer releasing a WinRT object reference
/// — a native/managed-boundary crash no try/catch can stop. Desktop Duplication is a plain DXGI/D3D11
/// COM API with no WinRT involved at all, which removes that entire class of bug; Vortice's bindings
/// for it are the same well-tested ones already used for device creation elsewhere in this file.
/// </remarks>
public sealed class VideoCaptureService : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _stagingTexture;

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

    private GlobalKeyboardHook? _keyboardHook;
    private GlobalMouseHook? _mouseHook;
    private KeystrokeOverlayRenderer? _keystrokeOverlay;

    // Raw pointer-shape scratch buffer for GetFramePointerShape(), grown as needed and reused across
    // shape updates; the decoded/converted result is cached separately since the shape only changes
    // occasionally (e.g. hovering a different kind of control), not every frame.
    private nint _pointerShapeBuffer;
    private uint _pointerShapeBufferCapacity;
    private CursorIconBitmap? _systemCursorIcon;

    /// <summary>
    /// Creates the D3D11 device (on the same adapter that owns the target monitor) and the output
    /// duplication, resolving the real capture resolution (Width/Height) — but does not start pulling
    /// frames yet. Split out from <see cref="BeginCapture"/> so the caller can spawn/connect the ffmpeg
    /// encoder (which needs the resolution up front) before any frames start flowing.
    /// </summary>
    public void Prepare(MonitorInfo monitor, bool captureCursor, CursorStyle cursorStyle = CursorStyle.Arrow,
        bool zoomEnabled = false, double zoomFactor = 2.0, bool keystrokeOverlayEnabled = false)
    {
        if (_prepared) return;

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

        if (keystrokeOverlayEnabled)
        {
            _keystrokeOverlay = new KeystrokeOverlayRenderer();
        }

        // The keyboard hook doubles as a zoom-activity signal, so it's needed whenever *either* feature
        // wants it; OnKeyboardActivity always marks activity and only forwards to the overlay renderer
        // when that feature is actually on, decoupling "is typing activity" from "is the overlay visible."
        if (zoomEnabled || keystrokeOverlayEnabled)
        {
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyPressed += OnKeyboardActivity;
        }

        // DXGI reports cursor *position* every frame already (used for movement-based activity below),
        // but never button state — a click needs a real hook.
        if (zoomEnabled)
        {
            _mouseHook = new GlobalMouseHook();
            _mouseHook.Click += OnMouseActivity;
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

        _prepared = true;
    }

    /// <summary>Starts delivering frames on a dedicated background thread. Call <see cref="Prepare"/> first.</summary>
    public void BeginCapture()
    {
        if (!_prepared || IsCapturing) return;

        _running = true;
        IsCapturing = true;
        _keyboardHook?.Start();
        _mouseHook?.Start();
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "DesktopDuplicationCapture" };
        _captureThread.Start();
    }

    private void MarkActivity() => Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    /// <summary>Mouse movement/click: the pan target goes back to following the cursor.</summary>
    private void OnMouseActivity()
    {
        MarkActivity();
        _typingTargetX = null;
        _typingTargetY = null;
    }

    /// <summary>Keypress: the pan target follows the text caret instead, when one can be located.</summary>
    private void OnKeyboardActivity(string display)
    {
        MarkActivity();

        if (_zoomEnabled && CaretLocator.TryGetCaretScreenPosition(out var screenX, out var screenY))
        {
            var localX = screenX - _monitorOriginX;
            var localY = screenY - _monitorOriginY;
            // Ignore a caret that isn't on the monitor being recorded (e.g. typing on a second
            // display) rather than pinning the zoom uselessly at the frame's edge.
            if (localX >= 0 && localX <= Width && localY >= 0 && localY <= Height)
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
            ApplyKeystrokeOverlay(buffer, width, height);

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
        double scaleX = cropWidth / width;
        double scaleY = cropHeight / height;

        if (_zoomScratchBuffer is null || _zoomScratchBuffer.Length != frame.Length)
        {
            _zoomScratchBuffer = new byte[frame.Length];
        }
        var dst = _zoomScratchBuffer;

        // Catmull-Rom bicubic resampling (16-tap, separable), parallelized per output row. This used to
        // be bilinear (4-tap): cheaper, but bilinear's weights are a plain positive-only average, which
        // is exactly what makes it soften/blur edges — visibly noticeable on zoomed text. Catmull-Rom's
        // small negative side lobes are what recover that lost edge contrast (the same property that
        // makes it "sharper" than bilinear in every image resampler that offers it), fixing the blur at
        // its root cause instead of papering over it with a post-hoc sharpen filter that can't
        // distinguish real detail from an artifact and risks haloing high-contrast UI text. It's ~4x
        // bilinear's per-pixel cost (16 taps vs 4), which is a bounded, real-time-safe increase given
        // bilinear was already proven fast enough here — and it only runs while actually zoomed.
        int maxX = width - 1;
        int maxY = height - 1;
        Parallel.For(0, height, y =>
        {
            double srcYf = cropY + y * scaleY;
            int iy = (int)Math.Floor(srcYf);
            double ty = srcYf - iy;
            double wy0 = CatmullRomWeight(ty + 1), wy1 = CatmullRomWeight(ty), wy2 = CatmullRomWeight(ty - 1), wy3 = CatmullRomWeight(ty - 2);
            int ry0 = Math.Clamp(iy - 1, 0, maxY) * width * 4;
            int ry1 = Math.Clamp(iy, 0, maxY) * width * 4;
            int ry2 = Math.Clamp(iy + 1, 0, maxY) * width * 4;
            int ry3 = Math.Clamp(iy + 2, 0, maxY) * width * 4;
            int dstRow = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                double srcXf = cropX + x * scaleX;
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
                    double row0 = wx0 * frame[ry0 + cx0 + c] + wx1 * frame[ry0 + cx1 + c] + wx2 * frame[ry0 + cx2 + c] + wx3 * frame[ry0 + cx3 + c];
                    double row1 = wx0 * frame[ry1 + cx0 + c] + wx1 * frame[ry1 + cx1 + c] + wx2 * frame[ry1 + cx2 + c] + wx3 * frame[ry1 + cx3 + c];
                    double row2 = wx0 * frame[ry2 + cx0 + c] + wx1 * frame[ry2 + cx1 + c] + wx2 * frame[ry2 + cx2 + c] + wx3 * frame[ry2 + cx3 + c];
                    double row3 = wx0 * frame[ry3 + cx0 + c] + wx1 * frame[ry3 + cx1 + c] + wx2 * frame[ry3 + cx2 + c] + wx3 * frame[ry3 + cx3 + c];
                    double sum = wy0 * row0 + wy1 * row1 + wy2 * row2 + wy3 * row3;
                    // Unlike bilinear, cubic weights can be negative and the weighted sum can overshoot
                    // past [0, 255] at hard edges — has to be clamped, not just cast.
                    dst[dstIdx + c] = (byte)Math.Clamp(sum + 0.5, 0.0, 255.0);
                }
            }
        });

        Buffer.BlockCopy(dst, 0, frame, 0, frame.Length);
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

    /// <summary>Copies the most recent frame into <paramref name="destination"/>. Returns false if no frame has arrived yet.</summary>
    public bool TryGetLatestFrame(byte[] destination)
    {
        lock (_frameLock)
        {
            if (!_hasFrame || _latestFrame is null) return false;
            Buffer.BlockCopy(_latestFrame, 0, destination, 0, _latestFrame.Length);
            return true;
        }
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

        if (_keyboardHook is not null)
        {
            _keyboardHook.KeyPressed -= OnKeyboardActivity;
            _keyboardHook.Dispose();
            _keyboardHook = null;
        }
        if (_mouseHook is not null)
        {
            _mouseHook.Click -= OnMouseActivity;
            _mouseHook.Dispose();
            _mouseHook = null;
        }
        _keystrokeOverlay = null;
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

    public void Dispose() => Stop();
}
