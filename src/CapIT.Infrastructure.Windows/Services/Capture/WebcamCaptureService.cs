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
    private readonly object _lock = new();
    private string _template = "circle";

    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private byte[]? _rawFrameBuffer;
    private byte[]? _cachedOverlay;
    private bool _stopped;
    private double _brightness;
    private double _contrast = 1;
    private double _saturation = 1;
    private double _warmth;
    private double _smoothing;

    public event Action<byte[], int, int>? FrameReady;

    /// <summary>
    /// Initializes the camera and starts delivering frames. Throws on failure (bad device id, camera
    /// already in use, permission denied) — callers should treat this the same "best effort, don't take
    /// down the recording over it" way RestartPreviewIfIdle already treats preview failures.
    /// </summary>
    public async Task StartAsync(string deviceId, string template = "circle",
        double brightness = 0, double contrast = 1, double saturation = 1,
        double warmth = 0, double smoothing = 0)
    {
        _template = template;
        UpdateAdjustments(brightness, contrast, saturation, warmth, smoothing);
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
        // The documented "minimize latency, don't buffer for completeness" hint — tells the driver
        // pipeline itself to drop backlog before frames even reach FrameArrived, rather than relying
        // solely on TryAcquireLatestFrame's app-level discard (which only helps once a frame has already
        // made it all the way through the driver's own internal buffering).
        frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
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

    public void UpdateAdjustments(double brightness, double contrast, double saturation, double warmth, double smoothing)
    {
        lock (_lock)
        {
            _brightness = Math.Clamp(brightness, -1, 1);
            _contrast = Math.Clamp(contrast, 0.5, 1.5);
            _saturation = Math.Clamp(saturation, 0, 2);
            _warmth = Math.Clamp(warmth, -1, 1);
            _smoothing = Math.Clamp(smoothing, 0, 1);
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

        ApplyBeautyAdjustments(masked, _brightness, _contrast, _saturation, _warmth, _smoothing);
        ApplyTemplateMask(masked, _template);

        _cachedOverlay = masked;
        FrameReady?.Invoke((byte[])masked.Clone(), Diameter, Diameter);
    }

    private static void ApplyBeautyAdjustments(byte[] bgra, double brightness, double contrast,
        double saturation, double warmth, double smoothing)
    {
        if (smoothing > 0)
        {
            var source = (byte[])bgra.Clone();
            int radius = Math.Max(1, (int)Math.Round(smoothing * 2));
            for (int y = 0; y < Diameter; y++)
                for (int x = 0; x < Diameter; x++)
                {
                    int count = 0, b = 0, g = 0, r = 0;
                    for (int oy = -radius; oy <= radius; oy++)
                        for (int ox = -radius; ox <= radius; ox++)
                        {
                            int sx = Math.Clamp(x + ox, 0, Diameter - 1);
                            int sy = Math.Clamp(y + oy, 0, Diameter - 1);
                            int i = (sy * Diameter + sx) * 4;
                            b += source[i]; g += source[i + 1]; r += source[i + 2]; count++;
                        }
                    int target = (y * Diameter + x) * 4;
                    double mix = smoothing * 0.45;
                    bgra[target] = (byte)(bgra[target] * (1 - mix) + b / (double)count * mix);
                    bgra[target + 1] = (byte)(bgra[target + 1] * (1 - mix) + g / (double)count * mix);
                    bgra[target + 2] = (byte)(bgra[target + 2] * (1 - mix) + r / (double)count * mix);
                }
        }

        for (int i = 0; i < bgra.Length; i += 4)
        {
            double b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            double luminance = r * 0.299 + g * 0.587 + b * 0.114;
            r = luminance + (r - luminance) * saturation;
            g = luminance + (g - luminance) * saturation;
            b = luminance + (b - luminance) * saturation;
            r = (r - 128) * contrast + 128 + brightness * 255 + warmth * 18;
            g = (g - 128) * contrast + 128 + brightness * 255 + warmth * 7;
            b = (b - 128) * contrast + 128 + brightness * 255 - warmth * 18;
            bgra[i] = (byte)Math.Clamp(b, 0, 255);
            bgra[i + 1] = (byte)Math.Clamp(g, 0, 255);
            bgra[i + 2] = (byte)Math.Clamp(r, 0, 255);
        }
    }

    /// <summary>Sets each pixel's alpha from the precomputed circular mask — the source frame is fully opaque, so this replaces alpha outright rather than blending it.</summary>
    private static void ApplyTemplateMask(byte[] bgra, string template)
    {
        double center = (Diameter - 1) / 2.0;
        double half = Diameter / 2.0;
        for (int y = 0; y < Diameter; y++)
            for (int x = 0; x < Diameter; x++)
            {
                double dx = Math.Abs(x - center);
                double dy = Math.Abs(y - center);
                double distance = template switch
                {
                    "rounded" => Math.Max(dx - half + 30, dy - half + 30),
                    "square" => Math.Max(dx, dy) - half,
                    "landscape" => Math.Max(dx / 1.35, dy) - half,
                    _ => Math.Sqrt(dx * dx + dy * dy) - half,
                };
                double alpha = Math.Clamp(-distance / EdgeFeatherPx * 255.0, 0, 255);
                bgra[(y * Diameter + x) * 4 + 3] = (byte)alpha;
            }
        if (template == "neon")
        {
            for (int y = 0; y < Diameter; y++)
                for (int x = 0; x < Diameter; x++)
                {
                    double dx = x - center, dy = y - center;
                    double distance = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - (half - 5));
                    if (distance < 7 && bgra[(y * Diameter + x) * 4 + 3] > 0)
                    {
                        int i = (y * Diameter + x) * 4;
                        bgra[i] = 220; bgra[i + 1] = 80; bgra[i + 2] = 255;
                    }
                }
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
