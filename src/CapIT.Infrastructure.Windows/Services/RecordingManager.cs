using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
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
    private readonly ClickSoundService _clickSound = new();
    private readonly object _videoLock = new();

    private byte[] _blackFrame = [];
    private CancellationTokenSource? _pacerCts;
    private FrameClock? _frameClock;
    private long _videoTimelineStart;
    private int _poolSize;
    private Task? _pacerTask;
    private Task? _writerTask;
    private Channel<byte[]?>? _frameQueue;
    private TimerResolutionScope? _timerResolution;
    private readonly ConcurrentQueue<byte[]> _framePool = new();
    private long _repeatedFrameCount;

    /// <summary>
    /// How many frame buffers circulate between the pacer and the pipe writer: about a fifth of a second
    /// of frames, so an encoder stall of that length (a keyframe, a disk flush, a hardware encoder
    /// context-switching with the GPU work around it) is absorbed instead of turning into repeated
    /// frames — three buffers was only ~50ms at 60fps, which hardware encoders were seen to exceed.
    /// Bounded by memory rather than count at high resolutions (a 4K NV12 buffer is 12.4MB), but never
    /// below the original three.
    /// </summary>
    private static int FramePoolSizeFor(int fps, int frameBytes)
    {
        const int MinFrames = 3, MaxFrames = 14;
        const long MemoryBudgetBytes = 96L * 1024 * 1024;
        int wanted = (int)Math.Ceiling(fps * 0.2);
        int affordable = (int)Math.Max(MinFrames, MemoryBudgetBytes / Math.Max(1, frameBytes));
        return Math.Clamp(Math.Min(wanted, affordable), MinFrames, MaxFrames);
    }

    /// <summary>
    /// Frames the pacer had to emit as a repeat of the previous one because every pooled buffer was
    /// still in flight, i.e. the encoder could not keep up. The recording's length and audio sync are
    /// unaffected (a repeat still advances the timeline by exactly one frame); this is purely a measure
    /// of how much motion detail was lost to the encoder falling behind.
    /// </summary>
    public long RepeatedFrameCount => Interlocked.Read(ref _repeatedFrameCount);

    /// <summary>Counters for the recording in progress (or the last one). See <see cref="RecordingDiagnostics"/>.</summary>
    public RecordingDiagnostics Diagnostics { get; private set; } = new();

    // The path ffmpeg actually writes to during capture (a fragmented ".part.mp4"), and the final
    // path the user asked for. For MP4 output these differ: on stop the fragmented file is remuxed
    // into a normal faststart MP4 at _finalPath (see StopAsync / FFmpegRemuxer). For MKV they're the
    // same — ffmpeg writes straight to the final path.
    private string? _encodePath;
    private string? _finalPath;
    private RecordingSettings? _recordingSettings;

    private nint? _previewMonitorHandle;
    private nint? _previewWindowHandle;
    private bool _previewCursor;
    private CursorStyle _previewCursorStyle;
    private bool _previewZoomEnabled;
    private double _previewZoomFactor;
    private bool _previewZoomOnClickOnly;
    private bool _previewKeystrokeOverlay;
    private bool _previewSpotlightEnabled;
    private double _previewSpotlightRadius;
    private bool _previewClickRipplesEnabled;

    public RecordingManager()
    {
        _video.ClickDetected += _clickSound.Play;
    }

    /// <summary>Fires if a specific-window recording/preview's target window is closed out from under it (window mode only) — pass-through of <see cref="VideoCaptureService.CaptureTargetLost"/>.</summary>
    public event Action? CaptureTargetLost
    {
        add => _video.CaptureTargetLost += value;
        remove => _video.CaptureTargetLost -= value;
    }

    public event Action<byte[], int, int>? WebcamFrameReady
    {
        add => _video.WebcamFrameReady += value;
        remove => _video.WebcamFrameReady -= value;
    }

    private DateTime _startTimeUtc;
    private TimeSpan _pausedAccum;
    private DateTime? _pauseStartedUtc;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? LastOutputPath { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// "Screen pause": while true the pacer keeps feeding ffmpeg the last captured frame (so the
    /// recorded image freezes) but audio and the elapsed timer keep running normally. Independent of
    /// <see cref="State"/> — the session is still <see cref="RecordingState.Recording"/>. A full
    /// <see cref="Pause"/> overrides it visually anyway (same pacer guard) and it re-applies on resume.
    /// </summary>
    public bool IsScreenFrozen { get; private set; }

    /// <summary>Freezes the recorded image only. No-op unless actively recording.</summary>
    public void FreezeScreen()
    {
        if (State != RecordingState.Recording) return;
        IsScreenFrozen = true;
    }

    /// <summary>Resumes live frames after <see cref="FreezeScreen"/>.</summary>
    public void UnfreezeScreen() => IsScreenFrozen = false;

    /// <summary>Whether the running capture composes on the GPU or on the CPU kernels — diagnostic only.</summary>
    public bool UsesGpuPipeline => _video.UsesGpuPipeline;
    public string? GpuUnavailableReason => _video.GpuUnavailableReason;

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
        bool spotlightEnabled = false, double spotlightRadius = 180, bool clickRipplesEnabled = false,
        bool zoomOnClickOnly = false, string webcamTemplate = "circle",
        double webcamBrightness = 0, double webcamContrast = 1, double webcamSaturation = 1,
        double webcamWarmth = 0, double webcamSmoothing = 0, bool instantZoomOut = false,
        double zoomAnimationSpeedPercent = 0)
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
            _video.SetWebcam(webcamEnabled, webcamDeviceId, webcamTemplate,
                webcamBrightness, webcamContrast, webcamSaturation, webcamWarmth, webcamSmoothing);

            // Unlike the webcam, spotlight/ripples have no external device to keep alive across a
            // restart (no camera, no privacy LED) — they're plain Prepare() parameters like zoom/cursor
            // style, so it's fine (and simpler) for them to be part of the same dedup check and go
            // through the normal Stop()+Prepare() cycle like everything else here.
            if (_video.IsCapturing && _previewMonitorHandle == monitor?.Handle && _previewWindowHandle == window?.Handle
                && _previewCursor == captureCursor && _previewCursorStyle == cursorStyle
                && _previewZoomEnabled == zoomEnabled && _previewZoomFactor == zoomFactor
                && _previewZoomOnClickOnly == zoomOnClickOnly
                && _previewKeystrokeOverlay == keystrokeOverlayEnabled
                && _previewSpotlightEnabled == spotlightEnabled && _previewSpotlightRadius == spotlightRadius
                && _previewClickRipplesEnabled == clickRipplesEnabled) return;

            _video.Stop();
            try
            {
                _video.Prepare(monitor, window, captureCursor, cursorStyle, zoomEnabled, zoomFactor, keystrokeOverlayEnabled,
                    spotlightEnabled, spotlightRadius, clickRipplesEnabled, zoomOnClickOnly, instantZoomOut, zoomAnimationSpeedPercent);
                _video.BeginCapture();
                _previewMonitorHandle = monitor?.Handle;
                _previewWindowHandle = window?.Handle;
                _previewCursor = captureCursor;
                _previewCursorStyle = cursorStyle;
                _previewZoomEnabled = zoomEnabled;
                _previewZoomFactor = zoomFactor;
                _previewZoomOnClickOnly = zoomOnClickOnly;
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

    /// <summary>
    /// Live cursor rendering change — preview or recording, either one. Like
    /// <see cref="UpdateSpotlight"/>, this also refreshes the snapshot <see cref="StartPreview"/> dedups
    /// against, so a later preview call doesn't tear the pipeline down just because these look "changed"
    /// when the running capture already has them.
    /// </summary>
    public void UpdateCursor(bool captureCursor, CursorStyle cursorStyle)
    {
        _previewCursor = captureCursor;
        _previewCursorStyle = cursorStyle;
        lock (_videoLock) { _video.UpdateCursor(captureCursor, cursorStyle); }
    }

    /// <inheritdoc cref="UpdateCursor"/>
    public void UpdateZoom(bool enabled, double factor, bool clickOnly = false, bool instantZoomOut = false, double zoomAnimationSpeedPercent = 0)
    {
        _previewZoomEnabled = enabled;
        _previewZoomFactor = factor;
        _previewZoomOnClickOnly = clickOnly;
        lock (_videoLock) { _video.UpdateZoom(enabled, factor, clickOnly, instantZoomOut, zoomAnimationSpeedPercent); }
    }

    /// <inheritdoc cref="UpdateCursor"/>
    public void UpdateKeystrokeOverlay(bool enabled)
    {
        _previewKeystrokeOverlay = enabled;
        lock (_videoLock) { _video.UpdateKeystrokeOverlay(enabled); }
    }

    /// <inheritdoc cref="UpdateCursor"/>
    public void UpdateClickRipples(bool enabled)
    {
        _previewClickRipplesEnabled = enabled;
        lock (_videoLock) { _video.UpdateClickRipples(enabled); }
    }

    /// <summary>Starts/stops/switches the webcam PiP overlay live. Already independent of the screen-capture engine's lifecycle — see VideoCaptureService.SetWebcam.</summary>
    public void UpdateWebcam(bool enabled, string? deviceId, string template = "circle",
        double brightness = 0, double contrast = 1, double saturation = 1,
        double warmth = 0, double smoothing = 0) =>
        _video.SetWebcam(enabled, deviceId, template, brightness, contrast, saturation, warmth, smoothing);

    /// <summary>
    /// Whether system-audio / microphone / mic-device changes can be applied to the recording that's
    /// running right now. False when no audio pipe was opened at all (both sources were off at record
    /// start, so ffmpeg has no audio stream to feed) and for the dual-leg noise-suppression pipeline,
    /// whose <c>amix</c> filter graph can't lose an input mid-stream — see
    /// <see cref="AudioCaptureService.SupportsLiveSourceChanges"/>.
    /// </summary>
    public bool CanChangeAudioSourcesLive =>
        State is RecordingState.Recording or RecordingState.Paused && _audio.SupportsLiveSourceChanges;

    /// <summary>
    /// Applies an audio source change to the running recording. Opens/closes WASAPI devices, so call it
    /// from a background thread — never the UI thread.
    /// </summary>
    public void UpdateAudioSources(bool captureSystemAudio, bool captureMicrophone, string? microphoneDeviceId)
    {
        _audio.SetSystemAudioEnabled(captureSystemAudio);
        _audio.SetMicrophone(captureMicrophone, microphoneDeviceId);
    }

    public void UpdateClickSound(bool enabled, string fileName, double volume)
    {
        _clickSound.Enabled = enabled;
        _clickSound.SetSound(fileName);
        _clickSound.Volume = volume;
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
        IsScreenFrozen = false;

        try
        {
            _recordingSettings = settings;
            // Tear down any preview-only capture first so recording gets a freshly configured one — the
            // preview may be running against stale cursor settings or (in principle) a different target.
            lock (_videoLock) { _video.Stop(); }
            _previewMonitorHandle = null;
            _previewWindowHandle = null;

            // Independent of the screen-capture Prepare() below — a no-op if the preview already has the
            // right camera running, so starting an actual recording doesn't interrupt an already-live PiP
            // feed. See VideoCaptureService.SetWebcam.
            _video.SetWebcam(settings.WebcamEnabled, settings.WebcamDeviceId, settings.WebcamTemplate,
                settings.WebcamBrightness, settings.WebcamContrast, settings.WebcamSaturation,
                settings.WebcamWarmth, settings.WebcamSmoothing);

            // Prepare (but don't start) capture first so we know the real resolution.
            await Task.Run(() => { lock (_videoLock) { _video.Prepare(monitor, window, settings.CaptureCursor, settings.CursorStyle,
                settings.MouseTrackingZoomEnabled, settings.ZoomFactor, settings.KeystrokeOverlayEnabled,
                settings.SpotlightEnabled, settings.SpotlightRadius, settings.ClickRipplesEnabled,
                settings.ZoomOnClickOnly, settings.InstantZoomOut, settings.ZoomAnimationSpeedPercent); } });

            _finalPath = settings.BuildOutputFilePath();
            // MP4 is recorded to a fragmented ".part.mp4" and remuxed to a faststart MP4 on stop
            // (see StopAsync). MKV has no such step — the fragmented flags aren't applied to it and
            // Media Foundation can't play it regardless — so it's written straight to the final path.
            _encodePath = settings.Container == OutputContainer.Mp4
                ? Path.Combine(Path.GetDirectoryName(_finalPath)!,
                    Path.GetFileNameWithoutExtension(_finalPath) + ".part.mp4")
                : _finalPath;
            var outputPath = _encodePath;
            var audioRequested = settings.CaptureSystemAudio || settings.CaptureMicrophone;

            // ffmpeg's rawvideo demuxer blocks probing the video pipe until real bytes arrive, and
            // won't even attempt to open the audio pipe until that probe is satisfied. So: connect
            // video only, start writing frames immediately, THEN wait for audio to connect — waiting
            // for both pipes up front deadlocks since nothing is writing yet.
            await _ffmpeg.StartAsync(settings, _video.Width, _video.Height, audioRequested, outputPath);

            // NV12, not BGRA: every buffer from here to the pipe is 1.5 bytes per pixel. The black
            // frame has to be filled rather than merely allocated, because all-zero is not black in
            // NV12 — see Nv12Converter.FillBlack.
            int frameBytes = _video.Nv12FrameByteSize;
            _blackFrame = new byte[frameBytes];
            Nv12Converter.FillBlack(_blackFrame, _video.Width, _video.Height);
            _framePool.Clear();
            _poolSize = FramePoolSizeFor(settings.Fps, frameBytes);
            for (int i = 0; i < _poolSize; i++)
            {
                var pooled = new byte[frameBytes];
                Nv12Converter.FillBlack(pooled, _video.Width, _video.Height);
                _framePool.Enqueue(pooled);
            }
            Interlocked.Exchange(ref _repeatedFrameCount, 0);

            Diagnostics = new RecordingDiagnostics();
            Diagnostics.BeginProcessCounters();
            _video.Diagnostics = Diagnostics;

            // Held for the recording only — see TimerResolutionScope for why a 60fps pacer is not
            // actually a 60fps pacer without it. Taken before capture starts because the capture loop's
            // own deadlines are millisecond waits too.
            _timerResolution = new TimerResolutionScope();

            // The output schedule. On the GPU path the capture thread composes frame k at slot k, and the
            // readback ring hands that frame over one slot later, so the pacer samples slot k a slot and a
            // half after it opens: half a slot of margin on either side of the moment the frame is
            // published, which is what absorbs wakeup and compose jitter. Everywhere else (CPU path) there
            // is no such lag and the lead is zero.
            bool slotDriven = _video.UsesGpuPipeline;
            long leadTicks = slotDriven ? (long)(1.5 * Stopwatch.Frequency / settings.Fps) : 0;
            _frameClock = new FrameClock(settings.Fps, Stopwatch.Frequency, Stopwatch.GetTimestamp() + leadTicks);
            _video.SetComposeClock(slotDriven ? _frameClock : null, leadTicks);
            // t=0 of the recording is when the clock was created, not when its lead-shifted first slot opens:
            // frame k shows the screen as of slot k's *content* time. Audio is aligned to the same instant.
            _videoTimelineStart = _frameClock.SlotTimestamp(0) - leadTicks;
            _audio.TimelineStartTimestamp = _videoTimelineStart;
            var audioClock = _frameClock;
            long audioStart = _videoTimelineStart;
            _audio.ElapsedSecondsAt = ts => Math.Max(0, (ts - audioStart - audioClock.PausedTicks(ts)) / (double)Stopwatch.Frequency);
            lock (_videoLock) { _video.BeginCapture(); }

            _frameQueue = Channel.CreateUnbounded<byte[]?>(
                // SingleWriter stays false even though the pacer is the only thing that enqueues frames:
                // completion is a write too, and both the pacer's finally block and a failure/dispose
                // path off another thread can reach TryComplete for the same queue.
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

            _pacerCts = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoopAsync(_frameQueue.Reader));
            var pacerClock = _frameClock;
            var pacerQueue = _frameQueue.Writer;
            var pacerToken = _pacerCts.Token;
            _pacerTask = Task.Factory.StartNew(() => PacerLoop(pacerClock, pacerQueue, pacerToken, leadTicks),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

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

            LastOutputPath = _finalPath;
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

    /// <summary>
    /// Fills the output frame schedule: for every slot the wall clock has opened, hands
    /// <see cref="WriterLoopAsync"/> either the latest captured frame or a repeat marker. Deliberately
    /// does no I/O itself — see the remarks.
    /// </summary>
    /// <remarks>
    /// Two separate things used to cost this timeline frames, and both are handled here.
    ///
    /// The pipe write used to happen inline, so any ffmpeg stall (a keyframe, a disk flush, a slow
    /// software preset at 4K) held the loop past its next tick; the writer split removed that.
    ///
    /// The other one was the timer itself. The loop used a PeriodicTimer, which (a) truncates its period to
    /// whole milliseconds — 16.67ms became 16ms, so a "60fps" pacer ran at 62.5fps and the video came out
    /// ~4% longer than the audio — and (b) never makes up a tick it was late for. Because ffmpeg is fed
    /// untimestamped rawvideo, a tick that never fires is not a late frame, it is a frame that does not
    /// exist, and the video runs short while the audio does not. <see cref="FrameClock"/> replaces it:
    /// slot k opens at exactly start + k/fps, and whenever this thread wakes up late (a long GC pause, a
    /// starved core, a hiccup anywhere else) it fills every slot that opened in the meantime. The missed
    /// slots repeat the previous frame — the screen as it last verifiably was — and only the newest slot
    /// gets the fresh capture, so nothing is invented and the timeline stays whole. Repeats are counted
    /// (<see cref="RecordingDiagnostics.RepeatsPacerCatchUp"/>) rather than hidden.
    ///
    /// Runs on a dedicated thread, not the thread pool: a pacer whose wakeups can queue behind CPU-bound
    /// pool work (Parallel.For in the CPU kernels, the encoder's own helpers) is exactly the kind that
    /// wakes up late.
    /// </remarks>
    private void PacerLoop(FrameClock clock, ChannelWriter<byte[]?> queue, CancellationToken ct, long leadTicks)
    {
        var diag = Diagnostics;
        long frequency = Stopwatch.Frequency;
        long lastWake = 0;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            while (!ct.IsCancellationRequested)
            {
                // A full pause writes NOTHING to the encoder, so the output's video timeline stops
                // advancing along with the elapsed timer (the clock cuts the paused interval out of the
                // schedule entirely). AudioCaptureService.IsPaused stops its own writes for exactly the
                // same reason, so the two streams pause together and stay in sync.
                if (clock.IsPaused)
                {
                    lastWake = 0;
                    Thread.Sleep(2);
                    continue;
                }

                long now = Stopwatch.GetTimestamp();
                long owed = clock.Owed(now);
                if (owed <= 0)
                {
                    WaitUntil(clock.NextSlotTimestamp(), frequency, ct);
                    continue;
                }

                if (lastWake != 0) diag.PacerGapMs.Add(RunningStat.Elapsed(lastWake, now));
                lastWake = now;

                if (owed > 1)
                {
                    diag.PacerLateWakeups++;
                    diag.MissedTicks += owed - 1;
                    for (long i = 0; i < owed - 1; i++)
                    {
                        queue.TryWrite(null);
                        diag.RepeatsPacerCatchUp++;
                        diag.FramesEmitted++;
                    }
                }

                EmitCurrentSlot(queue, diag);
                clock.MarkEmitted(owed);
            }

            // Stop requested. The pacer samples a slot and a half after it opens (leadTicks), so the last
            // slots of the recording have not been due yet; fill them now so the video ends at the stop
            // instant rather than a frame or two before it, matching where the audio ends.
            if (!clock.IsPaused)
            {
                long tail = clock.Owed(Stopwatch.GetTimestamp() + leadTicks);
                if (tail > 0)
                {
                    for (long i = 0; i < tail - 1; i++) { queue.TryWrite(null); diag.FramesEmitted++; diag.TailSlotsEmitted++; }
                    EmitCurrentSlot(queue, diag);
                    diag.TailSlotsEmitted++;
                    clock.MarkEmitted(tail);
                }
            }
        }
        finally
        {
            queue.TryComplete();
        }
    }

    private void EmitCurrentSlot(ChannelWriter<byte[]?> queue, RecordingDiagnostics diag)
    {
        // Screen pause is deliberately different from a full pause: frames keep flowing at full rate,
        // they're just the same (frozen) frame, so audio and the timeline carry on normally. A null is
        // precisely that instruction to the writer, and costs neither a buffer nor a copy.
        if (IsScreenFrozen)
        {
            queue.TryWrite(null);
            diag.FramesEmitted++;
            return;
        }

        if (!_framePool.TryDequeue(out var buffer))
        {
            // Every pooled buffer is still queued or in flight: the encoder is behind. Emit a repeat
            // rather than skipping the slot, so the frame count still matches elapsed time.
            queue.TryWrite(null);
            Interlocked.Increment(ref _repeatedFrameCount);
            diag.RepeatsEncoderBehind++;
            diag.FramesEmitted++;
            return;
        }

        // Buffers currently out of the pool (queued for, or being written by, the writer): how far behind
        // the encoder is, in frames.
        int inFlight = _poolSize - _framePool.Count;
        if (inFlight > diag.MaxQueueDepth) diag.MaxQueueDepth = inFlight;

        long copyStart = Stopwatch.GetTimestamp();
        _video.TryGetLatestFrameNv12(buffer);
        diag.PacerCopyMs.AddTicks(copyStart);
        queue.TryWrite(buffer);
        diag.FramesEmitted++;
    }

    /// <summary>
    /// Sleeps most of the way to <paramref name="target"/> and spins the last stretch. Sleep alone lands
    /// anywhere within the timer resolution of the deadline (1ms at best, see
    /// <see cref="TimerResolutionScope"/>), which at 60fps is a noticeable slice of a 16.7ms slot.
    /// </summary>
    private static void WaitUntil(long target, long frequency, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long remaining = target - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;
            double ms = remaining * 1000.0 / frequency;
            if (ms > 2.5) Thread.Sleep((int)(ms - 1.5));
            else Thread.SpinWait(40);
        }
    }

    /// <summary>
    /// Drains the pacer's queue into ffmpeg's video pipe. Runs without a cancellation token on purpose:
    /// on stop it finishes whatever the pacer already queued rather than truncating it, and a genuinely
    /// wedged encoder still unblocks it, since <see cref="FFmpegEncoderService.StopAsync"/> disposes the
    /// pipe (and ultimately kills the process), which surfaces here as the IO exceptions caught below.
    /// </summary>
    private async Task WriterLoopAsync(ChannelReader<byte[]?> queue)
    {
        // The most recently written frame, held back from the pool so a repeat marker has something to
        // re-send. The pool gets the frame before it instead, one write behind.
        byte[]? lastWritten = null;
        try
        {
            await foreach (var frame in queue.ReadAllAsync().ConfigureAwait(false))
            {
                var pipe = _ffmpeg.VideoPipe;
                if (pipe is null) return;

                var diag = Diagnostics;
                long writeStart = Stopwatch.GetTimestamp();
                await pipe.WriteAsync(frame ?? lastWritten ?? _blackFrame).ConfigureAwait(false);
                diag.PipeWriteMs.AddTicks(writeStart);

                if (frame is null) continue;
                if (lastWritten is not null) _framePool.Enqueue(lastWritten);
                lastWritten = frame;
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
        IsScreenFrozen = false; // a full pause supersedes screen-freeze; resume comes back clean
        _audio.IsPaused = true;
        _frameClock?.Pause(Stopwatch.GetTimestamp());
        _pauseStartedUtc = DateTime.UtcNow;
    }

    public void Resume()
    {
        if (State != RecordingState.Paused) return;
        if (_pauseStartedUtc is { } p) _pausedAccum += DateTime.UtcNow - p;
        _pauseStartedUtc = null;
        _audio.IsPaused = false;
        _frameClock?.Resume(Stopwatch.GetTimestamp());
        State = RecordingState.Recording;
    }

    public async Task<string?> StopAsync()
    {
        if (State is RecordingState.Idle or RecordingState.Stopping) return LastOutputPath;

        State = RecordingState.Stopping;
        long stopTimestamp = Stopwatch.GetTimestamp();

        _pacerCts?.Cancel();
        if (_pacerTask is not null)
        {
            try { await _pacerTask; } catch { /* already logged inside the loop */ }
        }

        // The pacer completes the queue on its way out, so the writer finishes the last few frames it
        // was already handed rather than truncating them. Bounded, because a wedged ffmpeg must not be
        // able to hang Stop — StopAsync below kills it either way, which unblocks the writer for good.
        if (_writerTask is not null)
        {
            try { await _writerTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* drained or gave up */ }
        }

        // Video is finished: tell ffmpeg so it can consume the audio tail, which is still being delivered up to
        // the stop instant. Then end the audio there and wait for it to reach the pipe — see PcmLegPump.
        _ffmpeg.CompleteVideoInput();
        await Task.Run(() => _audio.StopAndDrain(stopTimestamp));
        var audioStats = _audio.PumpStats;
        Diagnostics.AudioSecondsWritten = _audio.BytesWritten / (double)(AudioCaptureService.SampleRate * AudioCaptureService.Channels * 2);
        Diagnostics.AudioLeadSilenceSeconds = audioStats.Lead;
        Diagnostics.AudioStaleDroppedMs = audioStats.StaleMs;
        Diagnostics.AudioUnderrunMs = audioStats.UnderrunMs;
        Diagnostics.AudioMaxQueuedMs = audioStats.MaxQueuedMs;
        Diagnostics.AudioWriteStallMs = audioStats.WriteStallMs;
        Diagnostics.AudioDrainTimedOut = _audio.DrainTimedOut;

        _timerResolution?.Dispose();
        _timerResolution = null;

        lock (_videoLock) { _video.Stop(); }

        await _ffmpeg.StopAsync();

        IsScreenFrozen = false;
        await FinalizeRecordingAsync();

        State = RecordingState.Idle;
        return LastOutputPath;
    }

    /// <summary>
    /// Turns the fragmented ".part.mp4" ffmpeg just finished writing into the normal faststart MP4 the
    /// user asked for. Falls back to renaming the fragmented file into place if the remux can't run or
    /// fails — a fragmented MP4 still opens fine in VLC/editors, only the in-app preview struggles with
    /// it, so a recording is never lost over this. No-op when encode and final paths are the same (MKV).
    /// </summary>
    private async Task FinalizeRecordingAsync()
    {
        if (_encodePath is not null && _finalPath is not null && _encodePath != _finalPath && File.Exists(_encodePath))
        {
            var remuxed = await FFmpegRemuxer.ToFaststartAsync(_encodePath, _finalPath);
            try
            {
                if (remuxed) File.Delete(_encodePath);
                else File.Move(_encodePath, _finalPath, overwrite: true);
            }
            catch { /* best effort — LastOutputPath still points at _finalPath either way */ }
        }

        if (_finalPath is not null && File.Exists(_finalPath) && _recordingSettings is not null)
        {
            try
            {
                new RecordingMetadata
                {
                    RecordedAtUtc = _startTimeUtc,
                    DurationSeconds = Elapsed.TotalSeconds,
                    CaptureWidth = _video.Width,
                    CaptureHeight = _video.Height,
                    Fps = _recordingSettings.Fps,
                    Cursor = (_recordingSettings.Cursor ?? new CursorSettings
                    {
                        Enabled = _recordingSettings.CaptureCursor,
                        Style = _recordingSettings.CursorStyle
                    }).Clone()
                }.Save(_finalPath);
            }
            catch { /* optional metadata must never make a recording fail */ }
        }
    }

    private async Task CleanupAfterFailureAsync()
    {
        _pacerCts?.Cancel();
        _frameQueue?.Writer.TryComplete();
        _timerResolution?.Dispose();
        _timerResolution = null;
        _audio.Stop();
        lock (_videoLock) { _video.Stop(); }
        try { await _ffmpeg.StopAsync(); } catch { /* best effort */ }

        // Don't leave a half-written ".part.mp4" behind after a failed start.
        try
        {
            if (_encodePath is not null && _encodePath != _finalPath && File.Exists(_encodePath))
            {
                File.Delete(_encodePath);
            }
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        _pacerCts?.Cancel();
        _frameQueue?.Writer.TryComplete();
        _timerResolution?.Dispose();
        _timerResolution = null;
        _audio.Dispose();
        _video.ClickDetected -= _clickSound.Play;
        _clickSound.Dispose();
        lock (_videoLock) { _video.Dispose(); }
        _ffmpeg.Dispose();
    }
}
