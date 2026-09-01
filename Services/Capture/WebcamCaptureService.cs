using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Captures a webcam via MediaCapture + MediaFrameReader and keeps a small, pre-masked circular BGRA
/// thumbnail ready for <see cref="VideoCaptureService"/> to blend onto the recording — the webcam
/// counterpart to <see cref="KeystrokeOverlayRenderer"/>, following the exact same shape: a renderer
/// VideoCaptureService owns, polled once per captured frame via <see cref="TryGetOverlay"/>.
///
/// Unlike Windows.Graphics.Capture, MediaCapture/MediaFrameReader is a fully-projected WinRT API with no
/// missing public surface — <c>CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8)</c> does the
/// pixel-format conversion for us, and <see cref="MediaCaptureMemoryPreference.Cpu"/> means frames arrive
/// as CPU-readable <see cref="SoftwareBitmap"/>s, so there's no D3D device/texture-readback dance to
/// repeat here. That said, <c>FrameArrived</c> still fires on the frame reader's own thread, entirely
/// independent of whatever thread calls <see cref="StopAsync"/> — so the same lifetime discipline
/// VideoCaptureService's WGC path needs applies here too: every WinRT object is a field (never a local a
/// callback could outlive), and a single lock is shared between the frame-arrived callback and teardown,
/// so StopAsync() waits for an in-flight callback to finish instead of racing it disposing
/// <c>_mediaCapture</c> out from under it.
/// </summary>
public sealed class WebcamCaptureService
{
    // 300x300 picture-in-picture — big enough to read a face clearly, small enough that resampling and
    // masking it costs nothing worth measuring next to a 1080p+ capture frame (a tiny fraction of the
    // pixel count VideoCaptureService's Catmull-Rom resample already handles in real time during zoom).
    private const int Diameter = 300;
    private const double EdgeFeatherPx = 1.5;

    // Computed once, ever — see BuildCircularMaskAlpha's remarks on why a static Lazy<T> is the right
    // place for this rather than per-frame or even per-instance: the mask only depends on Diameter, a
    // compile-time constant, so there is nothing frame-specific about it at all.
    private static readonly Lazy<byte[]> CircularMaskAlpha = new(BuildCircularMaskAlpha);

    private readonly object _lock = new();

    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private byte[]? _rawFrameBuffer;
    private byte[]? _cachedOverlay;
    private bool _stopped;

    /// <summary>
    /// Initializes the camera and starts delivering frames. Throws on failure (bad device id, camera
    /// already in use, permission denied) — callers should treat this the same "best effort, don't take
    /// down the recording over it" way RestartPreviewIfIdle already treats preview failures.
    /// </summary>
    public async Task StartAsync(string deviceId)
    {
        var mediaCapture = new MediaCapture();
        await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = deviceId,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            // Forces SoftwareBitmap (CPU-readable) frame delivery instead of a Direct3D surface — avoids
            // needing a second D3D device/texture-map path alongside VideoCaptureService's existing one.
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        });

        var frameSource = mediaCapture.FrameSources.Values.FirstOrDefault(
            fs => fs.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? throw new InvalidOperationException("The selected webcam has no color video source.");

        // Smallest available resolution: we're about to downsample to a 300x300 circle regardless, so
        // capturing at the camera's native 1080p (or higher) every frame would be pure waste.
        var smallestFormat = frameSource.SupportedFormats
            .OrderBy(f => f.VideoFormat.Width * f.VideoFormat.Height)
            .FirstOrDefault();
        if (smallestFormat is not null)
        {
            await frameSource.SetFormatAsync(smallestFormat);
        }

        var frameReader = await mediaCapture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8);
        frameReader.FrameArrived += OnFrameArrived;
        await frameReader.StartAsync();

        lock (_lock)
        {
            if (_stopped)
            {
                // StopAsync() was called while we were still initializing — undo immediately rather than
                // leave a live camera session (and its privacy-indicator LED) running for a session
                // nobody wants anymore. Fire-and-forget teardown mirrors StopAsync's own reasoning below.
                frameReader.FrameArrived -= OnFrameArrived;
                _ = frameReader.StopAsync().AsTask();
                frameReader.Dispose();
                mediaCapture.Dispose();
                return;
            }

            _mediaCapture = mediaCapture;
            _frameReader = frameReader;
        }
    }

    /// <summary>Returns the current circular-masked BGRA overlay (straight alpha, <see cref="Diameter"/>-square) if a frame has arrived yet.</summary>
    public bool TryGetOverlay(out byte[] bgra, out int size)
    {
        lock (_lock)
        {
            if (_cachedOverlay is null)
            {
                bgra = [];
                size = 0;
                return false;
            }
            bgra = _cachedOverlay;
            size = Diameter;
            return true;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        lock (_lock)
        {
            if (_stopped) return;

            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null) return;

            // CreateFrameReaderAsync already requested Bgra8/Ignore, so this conversion is normally a
            // no-op fast path — just a defensive fallback, not the expected common case.
            SoftwareBitmap? converted = null;
            try
            {
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bitmap.BitmapAlphaMode != BitmapAlphaMode.Ignore)
                {
                    converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                    bitmap = converted;
                }

                ProcessFrame(bitmap);
            }
            catch
            {
                // Best effort: skip this frame (e.g. a transient device-lost) rather than tearing down
                // the whole overlay — the same "don't let one bad frame kill the session" stance
                // VideoCaptureService.OnWgcFrameArrived already takes.
            }
            finally
            {
                converted?.Dispose();
            }
        }
    }

    private void ProcessFrame(SoftwareBitmap bitmap)
    {
        int srcW = bitmap.PixelWidth;
        int srcH = bitmap.PixelHeight;
        if (srcW <= 0 || srcH <= 0) return;

        int byteSize = srcW * srcH * 4;
        if (_rawFrameBuffer is null || _rawFrameBuffer.Length != byteSize)
        {
            _rawFrameBuffer = new byte[byteSize];
        }
        // SoftwareBitmap's own buffer is always tightly packed (no row-pitch padding to account for,
        // unlike the D3D11 staging textures VideoCaptureService reads from for screen capture), so a
        // single copy suffices — no per-row Marshal.Copy loop needed.
        bitmap.CopyToBuffer(_rawFrameBuffer.AsBuffer());

        // Crop to a centered square before resampling, so a 16:9 webcam frame doesn't get squashed into
        // the circle — cropping the long side is the same "fill, don't letterbox" choice most PiP webcam
        // widgets make.
        int cropSize = Math.Min(srcW, srcH);
        int cropX = (srcW - cropSize) / 2;
        int cropY = (srcH - cropSize) / 2;

        var masked = new byte[Diameter * Diameter * 4];
        // Reuses VideoCaptureService's Catmull-Rom resampler — at 300x300 this is a tiny fraction of the
        // pixel count it already handles for a full zoomed frame in real time, so there's no reason to
        // duplicate a cheaper (and blurrier) bilinear resize just for this.
        VideoCaptureService.ResampleCatmullRomInto(_rawFrameBuffer, srcW, srcH, cropX, cropY, cropSize, cropSize,
            masked, Diameter, Diameter, 0, 0, Diameter, Diameter);

        ApplyCircularMask(masked);

        _cachedOverlay = masked;
    }

    /// <summary>Sets each pixel's alpha from the precomputed circular mask — the source frame is fully opaque, so this replaces alpha outright rather than blending it.</summary>
    private static void ApplyCircularMask(byte[] bgra)
    {
        var mask = CircularMaskAlpha.Value;
        for (int i = 0; i < mask.Length; i++)
        {
            bgra[i * 4 + 3] = mask[i];
        }
    }

    /// <summary>
    /// Built once, lazily, on first use — the mask depends only on <see cref="Diameter"/>, a compile-time
    /// constant, so nothing about it is frame-specific; recomputing it per frame (or even per instance)
    /// would just be wasted work for an identical result every time. A ~1.5px linear alpha falloff at the
    /// boundary avoids a hard-jagged circle edge.
    /// </summary>
    private static byte[] BuildCircularMaskAlpha()
    {
        var mask = new byte[Diameter * Diameter];
        double center = (Diameter - 1) / 2.0;
        double radius = Diameter / 2.0;

        for (int y = 0; y < Diameter; y++)
        {
            for (int x = 0; x < Diameter; x++)
            {
                double dx = x - center;
                double dy = y - center;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                double alpha = (radius - dist) / EdgeFeatherPx * 255.0;
                mask[y * Diameter + x] = (byte)Math.Clamp(alpha, 0, 255);
            }
        }
        return mask;
    }

    /// <summary>
    /// Stops frame delivery and releases the camera. Fire-and-forget from VideoCaptureService.Stop()
    /// (which is synchronous) rather than blocked on — blocking a hot, frame-critical Stop() path on a
    /// WinRT device-teardown call risks the same kind of stall FFmpegEncoderService's own async shutdown
    /// already has to budget time for, and unlike that shutdown, losing this race just means the camera's
    /// privacy LED stays lit a few hundred ms longer, not a corrupted recording.
    /// </summary>
    public async Task StopAsync()
    {
        MediaFrameReader? frameReader;
        MediaCapture? mediaCapture;

        lock (_lock)
        {
            _stopped = true;
            frameReader = _frameReader;
            mediaCapture = _mediaCapture;
            _frameReader = null;
            _mediaCapture = null;
            _cachedOverlay = null;
        }

        if (frameReader is not null)
        {
            frameReader.FrameArrived -= OnFrameArrived;
            try { await frameReader.StopAsync(); } catch { /* best effort */ }
            frameReader.Dispose();
        }
        mediaCapture?.Dispose();
    }
}
