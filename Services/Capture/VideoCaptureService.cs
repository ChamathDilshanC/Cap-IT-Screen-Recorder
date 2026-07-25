using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;
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
    public void Prepare(MonitorInfo monitor, bool captureCursor, CursorStyle cursorStyle = CursorStyle.Arrow)
    {
        if (_prepared) return;

        _captureCursor = captureCursor;
        _cursorStyle = cursorStyle;

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
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "DesktopDuplicationCapture" };
        _captureThread.Start();
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
                    _cursorVisible = frameInfo.PointerPosition.Visible;
                    _cursorX = frameInfo.PointerPosition.Position.X;
                    _cursorY = frameInfo.PointerPosition.Position.Y;
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
