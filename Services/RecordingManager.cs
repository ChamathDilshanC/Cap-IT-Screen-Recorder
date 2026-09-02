using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.Services;

/// <summary>
/// Orchestrates video capture, audio capture, and the FFmpeg encoder into a single record/pause/stop
/// session. Decouples the event-driven screen capture from a fixed-FPS output by re-sending the most
/// recently captured frame on every pacer tick (duplicating it when the screen hasn't changed, or
/// while paused), which keeps the encoded video's frame count in sync with real elapsed time.
/// </summary>
public sealed class RecordingManager : IDisposable
{
    private readonly VideoCaptureService _video = new();
    private readonly AudioCaptureService _audio = new();
    private readonly FFmpegEncoderService _ffmpeg = new();
    private readonly object _videoLock = new();

    private byte[] _frameBuffer = [];
    private byte[] _blackFrame = [];
    private CancellationTokenSource? _pacerCts;
    private Task? _pacerTask;

    private nint? _previewMonitorHandle;
    private nint? _previewWindowHandle;
    private bool _previewCursor;
    private CursorStyle _previewCursorStyle;
    private bool _previewZoomEnabled;
    private double _previewZoomFactor;
    private bool _previewKeystrokeOverlay;
    private bool _previewSpotlightEnabled;
    private double _previewSpotlightRadius;
    private bool _previewClickRipplesEnabled;

    /// <summary>Fires if a specific-window recording/preview's target window is closed out from under it (window mode only) — pass-through of <see cref="VideoCaptureService.CaptureTargetLost"/>.</summary>
    public event Action? CaptureTargetLost
    {
        add => _video.CaptureTargetLost += value;
        remove => _video.CaptureTargetLost -= value;
    }

    private DateTime _startTimeUtc;
    private TimeSpan _pausedAccum;
    private DateTime? _pauseStartedUtc;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? LastOutputPath { get; private set; }
    public string? LastError { get; private set; }

    public int PreviewWidth => _video.Width;
    public int PreviewHeight => _video.Height;

    public TimeSpan Elapsed
    {
        get
        {
            if (State is RecordingState.Idle or RecordingState.Starting) return TimeSpan.Zero;
            var pausedSoFar = _pausedAccum + (_pauseStartedUtc is { } p ? DateTime.UtcNow - p : TimeSpan.Zero);
            return DateTime.UtcNow - _startTimeUtc - pausedSoFar;
        }
    }

    public List<MonitorInfo> GetMonitors() => MonitorEnumerator.GetMonitors();

    public List<AudioDeviceOption> GetMicrophones() => AudioDeviceEnumerator.GetMicrophones();

    public List<WindowInfo> GetWindows() => WindowEnumerator.GetWindows();

    /// <summary>Copies the most recently captured frame into <paramref name="buffer"/> for a live preview UI. Works whenever video capture is active — before recording (preview mode), while recording, or paused.</summary>
    public bool TryGetPreviewFrame(byte[] buffer) => _video.TryGetLatestFrame(buffer);

    /// <summary>
    /// Starts (or restarts, if the target/cursor setting changed) a preview-only capture of the given
    /// monitor or window so the UI can show live video before the user presses Start Recording. Exactly
    /// one of <paramref name="monitor"/>/<paramref name="window"/> should be non-null. No-op once a real
    /// recording is underway — call from a background thread, this blocks on device creation.
    /// </summary>
    public void StartPreview(CaptureTargetKind targetKind, MonitorInfo? monitor, WindowInfo? window,
        bool captureCursor, CursorStyle cursorStyle,
        bool zoomEnabled = false, double zoomFactor = 2.0, bool keystrokeOverlayEnabled = false,
        bool webcamEnabled = false, string? webcamDeviceId = null,
        bool spotlightEnabled = false, double spotlightRadius = 180, bool clickRipplesEnabled = false)
    {
        // Same single-authority rule StartAsync applies — the chosen target kind decides, so the preview
        // can never end up showing a different source than a recording started from the same selection.
        if (targetKind == CaptureTargetKind.Window) monitor = null;
        else window = null;

        lock (_videoLock)
        {
            if (State != RecordingState.Idle) return;

            // Independent of the screen-capture engine below — SetWebcam is its own no-op check
            // internally (matching device id + enabled state), so this can run unconditionally on every
            // call without ever tearing down and re-initializing the camera just because some *other*
            // setting (monitor, cursor style, zoom...) changed. See VideoCaptureService.SetWebcam.
            _video.SetWebcam(webcamEnabled, webcamDeviceId);

            // Unlike the webcam, spotlight/ripples have no external device to keep alive across a
            // restart (no camera, no privacy LED) — they're plain Prepare() parameters like zoom/cursor
            // style, so it's fine (and simpler) for them to be part of the same dedup check and go
            // through the normal Stop()+Prepare() cycle like everything else here.
            if (_video.IsCapturing && _previewMonitorHandle == monitor?.Handle && _previewWindowHandle == window?.Handle
                && _previewCursor == captureCursor && _previewCursorStyle == cursorStyle
                && _previewZoomEnabled == zoomEnabled && _previewZoomFactor == zoomFactor
                && _previewKeystrokeOverlay == keystrokeOverlayEnabled
                && _previewSpotlightEnabled == spotlightEnabled && _previewSpotlightRadius == spotlightRadius
                && _previewClickRipplesEnabled == clickRipplesEnabled) return;

            _video.Stop();
            try
            {
                _video.Prepare(monitor, window, captureCursor, cursorStyle, zoomEnabled, zoomFactor, keystrokeOverlayEnabled,
                    spotlightEnabled, spotlightRadius, clickRipplesEnabled);
                _video.BeginCapture();
                _previewMonitorHandle = monitor?.Handle;
                _previewWindowHandle = window?.Handle;
                _previewCursor = captureCursor;
                _previewCursorStyle = cursorStyle;
                _previewZoomEnabled = zoomEnabled;
                _previewZoomFactor = zoomFactor;
                _previewKeystrokeOverlay = keystrokeOverlayEnabled;
                _previewSpotlightEnabled = spotlightEnabled;
                _previewSpotlightRadius = spotlightRadius;
                _previewClickRipplesEnabled = clickRipplesEnabled;
            }
            catch
            {
                // Best effort: preview is a nice-to-have, not fatal to leave capture stopped here.
                _video.Stop();
                _previewMonitorHandle = null;
                _previewWindowHandle = null;
            }
        }
    }

    /// <summary>
    /// Applies a spotlight enable/radius change to the live capture immediately — preview or recording,
    /// either one. Also refreshes the dedup snapshot <see cref="StartPreview"/> compares against, so a
    /// later preview restart doesn't tear the pipeline down purely because these two values look
    /// "changed" when the running capture already has them.
    /// </summary>
    public void UpdateSpotlight(bool enabled, double radius)
    {
        _previewSpotlightEnabled = enabled;
        _previewSpotlightRadius = radius;
        _video.UpdateSpotlight(enabled, radius);
    }

    /// <summary>Stops preview-only capture. No-op while actually recording.</summary>
    public void StopPreview()
    {
        lock (_videoLock)
        {
            if (State != RecordingState.Idle) return;
            _video.Stop();
            _previewMonitorHandle = null;
            _previewWindowHandle = null;
        }
    }

    public async Task StartAsync(RecordingSettings settings, MonitorInfo? monitor, WindowInfo? window)
    {
        if (State != RecordingState.Idle) return;

        // Belt-and-braces normalization against the "recorded the wrong thing" class of bug:
        // VideoCaptureService.Prepare picks its acquisition path from whichever of these is non-null, so
        // the *settings'* declared target kind — the thing the user actually chose in the UI — is the
        // single authority for which one survives, no matter what the caller happened to pass.
        if (settings.CaptureTargetKind == CaptureTargetKind.Window) monitor = null;
        else window = null;

        if (settings.CaptureTargetKind == CaptureTargetKind.Window && window is null)
        {
            throw new InvalidOperationException("Window capture was requested but no window is selected.");
        }
        if (settings.CaptureTargetKind == CaptureTargetKind.Monitor && monitor is null)
        {
            throw new InvalidOperationException("Display capture was requested but no display is selected.");
        }

        State = RecordingState.Starting;
        LastError = null;

        try
        {
            // Tear down any preview-only capture first so recording gets a freshly configured one — the
            // preview may be running against stale cursor settings or (in principle) a different target.
            lock (_videoLock) { _video.Stop(); }
            _previewMonitorHandle = null;
            _previewWindowHandle = null;

            // Independent of the screen-capture Prepare() below — a no-op if the preview already has the
            // right camera running, so starting an actual recording doesn't interrupt an already-live PiP
            // feed. See VideoCaptureService.SetWebcam.
            _video.SetWebcam(settings.WebcamEnabled, settings.WebcamDeviceId);

            // Prepare (but don't start) capture first so we know the real resolution.
            await Task.Run(() => { lock (_videoLock) { _video.Prepare(monitor, window, settings.CaptureCursor, settings.CursorStyle,
                settings.MouseTrackingZoomEnabled, settings.ZoomFactor, settings.KeystrokeOverlayEnabled,
                settings.SpotlightEnabled, settings.SpotlightRadius, settings.ClickRipplesEnabled); } });

            var outputPath = settings.BuildOutputFilePath();
            var audioRequested = settings.CaptureSystemAudio || settings.CaptureMicrophone;

            // ffmpeg's rawvideo demuxer blocks probing the video pipe until real bytes arrive, and
            // won't even attempt to open the audio pipe until that probe is satisfied. So: connect
            // video only, start writing frames immediately, THEN wait for audio to connect — waiting
            // for both pipes up front deadlocks since nothing is writing yet.
            await _ffmpeg.StartAsync(settings, _video.Width, _video.Height, audioRequested, outputPath);

            _frameBuffer = new byte[_video.FrameByteSize];
            _blackFrame = new byte[_video.FrameByteSize];

            lock (_videoLock) { _video.BeginCapture(); }

            _pacerCts = new CancellationTokenSource();
            _pacerTask = Task.Run(() => PacerLoopAsync(settings.Fps, _pacerCts.Token));

            if (audioRequested)
            {
                await _ffmpeg.WaitForAudioConnectionAsync();

                // Dual-leg noise suppression: FFmpegEncoderService only opens the mic pipe once this
                // (system-audio) pipe's probe is satisfied, which needs real bytes flowing on it — so
                // the system leg must start pumping before we can wait for the mic pipe to connect.
                // See FFmpegEncoderService's class remarks for the full chain of why.
                if (settings.EnableMicNoiseSuppression && settings.CaptureSystemAudio && settings.CaptureMicrophone && _ffmpeg.MicAudioPipe is not null)
                {
                    _audio.StartDualSystemLeg(_ffmpeg.AudioPipe!);
                    await _ffmpeg.WaitForMicConnectionAsync();
                    _audio.StartDualMicLeg(settings.MicrophoneDeviceId, _ffmpeg.MicAudioPipe);
                }
                else
                {
                    _audio.Start(settings.CaptureSystemAudio, settings.CaptureMicrophone, settings.MicrophoneDeviceId, _ffmpeg.AudioPipe!);
                }
            }

            LastOutputPath = outputPath;
            _startTimeUtc = DateTime.UtcNow;
            _pausedAccum = TimeSpan.Zero;
            _pauseStartedUtc = null;

            State = RecordingState.Recording;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await CleanupAfterFailureAsync();
            State = RecordingState.Idle;
            throw;
        }
    }

    private async Task PacerLoopAsync(int fps, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / fps));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (State != RecordingState.Paused)
                {
                    _video.TryGetLatestFrame(_frameBuffer);
                }

                var pipe = _ffmpeg.VideoPipe;
                if (pipe is null) return;

                await pipe.WriteAsync(_frameBuffer.Length > 0 ? _frameBuffer : _blackFrame, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (IOException)
        {
            // Pipe closed by the encoder shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Pipe closed by the encoder shutting down.
        }
    }

    public void Pause()
    {
        if (State != RecordingState.Recording) return;
        State = RecordingState.Paused;
        _audio.IsMuted = true;
        _pauseStartedUtc = DateTime.UtcNow;
    }

    public void Resume()
    {
        if (State != RecordingState.Paused) return;
        if (_pauseStartedUtc is { } p) _pausedAccum += DateTime.UtcNow - p;
        _pauseStartedUtc = null;
        _audio.IsMuted = false;
        State = RecordingState.Recording;
    }

    public async Task<string?> StopAsync()
    {
        if (State is RecordingState.Idle or RecordingState.Stopping) return LastOutputPath;

        State = RecordingState.Stopping;

        _pacerCts?.Cancel();
        if (_pacerTask is not null)
        {
            try { await _pacerTask; } catch { /* already logged inside the loop */ }
        }

        _audio.Stop();
        lock (_videoLock) { _video.Stop(); }

        await _ffmpeg.StopAsync();

        State = RecordingState.Idle;
        return LastOutputPath;
    }

    private async Task CleanupAfterFailureAsync()
    {
        _pacerCts?.Cancel();
        _audio.Stop();
        lock (_videoLock) { _video.Stop(); }
        try { await _ffmpeg.StopAsync(); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        _pacerCts?.Cancel();
        _audio.Dispose();
        lock (_videoLock) { _video.Dispose(); }
        _ffmpeg.Dispose();
    }
}
