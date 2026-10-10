using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Captures system (loopback) audio and/or microphone audio via NAudio/WASAPI, and continuously pumps
/// 16-bit PCM bytes into the provided output stream(s) (FFmpeg audio named pipe(s)).
/// </summary>
/// <remarks>
/// Two capture modes:
/// <list type="bullet">
/// <item>Pre-mixed (<see cref="Start"/>): both sources are combined in-process via NAudio's
/// <see cref="MixingSampleProvider"/> and pumped into a single output stream. This is the original,
/// simplest path — used whenever noise suppression is off, or only one source is active.</item>
/// <item>Dual-leg (<see cref="StartDualSystemLeg"/> + <see cref="StartDualMicLeg"/>): system audio and
/// microphone are kept on two independent streams, unmixed. Used only when Studio Mic noise suppression
/// (Phase 5) is active with both sources enabled, so ffmpeg's own <c>afftdn</c>/<c>highpass</c> filter
/// chain can be applied to the mic leg alone before ffmpeg mixes it back with the untouched system audio
/// via <c>amix</c>. The two legs are started in two stages (system, then mic) to match
/// FFmpegEncoderService's sequential pipe-connection requirement — see its remarks.</item>
/// </list>
/// </remarks>
public sealed class AudioCaptureService : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    public static WaveFormat OutputWaveFormat => new(SampleRate, BitsPerSample, Channels);

    private WasapiLoopbackCapture? _loopback;
    private WasapiCapture? _mic;
    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _micBuffer;
    private MixingSampleProvider? _mixer;
    private PcmLegPump? _pump;
    private PcmLegPump? _micPump;
    private Stream? _outputStream;
    private Stream? _micOutputStream;

    // The mixer inputs currently feeding the pre-mixed path, kept so a source can be added or removed
    // mid-recording (see SetSystemAudioEnabled / SetMicrophone). Guarded by _liveLock, which serializes
    // those UI-driven changes against each other; NAudio's MixingSampleProvider already locks its own
    // source list, so the pump thread's Read is safe against them without extra coordination.
    private readonly object _liveLock = new();
    private ISampleProvider? _loopbackInput;
    private ISampleProvider? _micInput;
    private string? _currentMicDeviceId;

    public bool IsActive { get; private set; }

    /// <summary>
    /// True while the pre-mixed single-pipe session is running, meaning sources can be switched on and
    /// off without restarting the recording. False for the dual-leg noise-suppression path, where each
    /// source is its own ffmpeg input inside a fixed <c>amix</c> filter graph that can't lose a leg
    /// mid-stream, and false when no audio pipe was opened at all (both sources off at record start).
    /// </summary>
    public bool SupportsLiveSourceChanges { get; private set; }

    private static WaveFormat TargetFormat => WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    /// <summary>
    /// While true, <see cref="PumpLoop"/> keeps draining the capture buffers but writes <em>nothing</em>
    /// to the encoder, so the output's audio timeline stops advancing for the duration of a pause.
    /// </summary>
    /// <remarks>
    /// This used to write silence instead of skipping the write, which meant a pause still fed the
    /// encoder a full second of samples per real second — so a 4-second recording paused for 5 seconds
    /// came out 9 seconds long, and the on-screen elapsed timer (which correctly excludes paused time)
    /// disagreed with the file. Draining but not writing keeps it in step with the video pacer, which
    /// skips its write for exactly the same reason — see RecordingManager.PacerLoopAsync. Reading and
    /// discarding (rather than not reading at all) matters too: it stops the 2-second capture buffers
    /// filling up and replaying stale audio from during the pause once recording resumes.
    /// </remarks>
    public volatile bool IsPaused;

    /// <summary>
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value of the video timeline's t=0, set by the
    /// recording manager before audio starts; 0 means "don't align" (standalone use, e.g. the level meters).
    /// </summary>
    /// <remarks>
    /// ffmpeg timestamps each rawvideo/PCM input from zero at its own first byte, not from the wall clock,
    /// and it can only open the audio pipe after the video pipe is already carrying frames (see
    /// FFmpegEncoderService's remarks). So audio always starts some hundreds of milliseconds after video
    /// does, and without correction both streams call their first sample "t=0" — sound lands in the file
    /// that much earlier than the picture it belongs to. The pump therefore opens each stream with exactly
    /// that much silence, which puts audio and video back on the same clock.
    /// </remarks>
    public long TimelineStartTimestamp { get; set; }

    /// <summary>
    /// Elapsed recording time (pauses excluded) at a <see cref="System.Diagnostics.Stopwatch"/> timestamp. Supplied by
    /// the recording manager so audio and video share one clock; defaults to time since
    /// <see cref="TimelineStartTimestamp"/> when unset.
    /// </summary>
    public Func<long, double>? ElapsedSecondsAt { get; set; }

    private double ElapsedAt(long ts) =>
        ElapsedSecondsAt is { } f ? f(ts) : (ts - TimelineStartTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Seconds of leading silence written to align audio with the video timeline (system/pre-mixed pipe only) — diagnostic.</summary>
    public double LeadSilenceSeconds => _pump?.LeadSilenceSeconds ?? _lastStats.Lead;

    /// <summary>Bytes of PCM handed to the encoder on the system/pre-mixed pipe, leading silence included — diagnostic.</summary>
    public long BytesWritten => _pump?.WrittenBytes ?? _lastStats.Bytes;

    /// <summary>Counters of the pump that just finished, kept after teardown for the diagnostics summary.</summary>
    public (double Lead, long Bytes, double StaleMs, double UnderrunMs, double MaxQueuedMs, double WriteStallMs) PumpStats =>
        _pump is { } p ? (p.LeadSilenceSeconds, p.WrittenBytes, p.StaleDroppedMs, p.UnderrunMs, p.MaxQueuedMs, p.WriteStallMs) : _lastStats;
    private (double Lead, long Bytes, double StaleMs, double UnderrunMs, double MaxQueuedMs, double WriteStallMs) _lastStats;

    /// <summary>Starts capturing with both sources pre-mixed into one stream. Returns false (and does nothing) if neither source is requested.</summary>
    public bool Start(bool captureSystemAudio, bool captureMicrophone, string? microphoneDeviceId, Stream outputStream)
    {
        if (!captureSystemAudio && !captureMicrophone) return false;

        _outputStream = outputStream;

        // Constructed from the target format rather than from a source list, so the mixer stays valid
        // with zero inputs — that's what lets both sources be switched off mid-recording without the
        // pump losing its provider. ReadFully means it hands back silence in that state, so the output's
        // audio timeline keeps pace with the video instead of stalling.
        _mixer = new MixingSampleProvider(TargetFormat) { ReadFully = true };

        lock (_liveLock)
        {
            if (captureSystemAudio) AttachSystemAudioLocked();
            if (captureMicrophone) AttachMicrophoneLocked(microphoneDeviceId);
        }

        IsActive = true;
        SupportsLiveSourceChanges = true;
        _pump = new PcmLegPump(_mixer.ToWaveProvider16(), outputStream, ElapsedAt, MixedBacklogMs, DiscardBuffers, () => IsPaused);
        _pump.Start();

        return true;
    }

    /// <summary>
    /// Switches system (loopback) audio on or off on a running recording. No-op outside the pre-mixed
    /// path — see <see cref="SupportsLiveSourceChanges"/>. Opens/closes a WASAPI device, so it must be
    /// called off the UI thread (see MicLevelMonitorService's remarks for what happens otherwise).
    /// </summary>
    public void SetSystemAudioEnabled(bool enabled)
    {
        lock (_liveLock)
        {
            if (!IsActive || !SupportsLiveSourceChanges || _mixer is null) return;
            if (enabled) AttachSystemAudioLocked();
            else DetachSystemAudioLocked();
        }
    }

    /// <summary>Microphone equivalent of <see cref="SetSystemAudioEnabled"/>; also swaps the device when <paramref name="deviceId"/> changes.</summary>
    public void SetMicrophone(bool enabled, string? deviceId)
    {
        lock (_liveLock)
        {
            if (!IsActive || !SupportsLiveSourceChanges || _mixer is null) return;
            if (enabled) AttachMicrophoneLocked(deviceId);
            else DetachMicrophoneLocked();
        }
    }

    private void AttachSystemAudioLocked()
    {
        if (_loopbackInput is not null) return;
        using var enumerator = new MMDeviceEnumerator();
        _loopbackInput = BuildResampledProvider(OpenLoopback(enumerator).ToSampleProvider(), TargetFormat);
        _mixer!.AddMixerInput(_loopbackInput);
        _loopback!.StartRecording();
    }

    private void DetachSystemAudioLocked()
    {
        if (_loopbackInput is null) return;
        _mixer?.RemoveMixerInput(_loopbackInput);
        _loopbackInput = null;
        try { _loopback?.StopRecording(); } catch { /* already stopping */ }
        _loopback?.Dispose();
        _loopback = null;
        _loopbackBuffer = null;
    }

    private void AttachMicrophoneLocked(string? deviceId)
    {
        if (_micInput is not null && _currentMicDeviceId == deviceId) return;
        DetachMicrophoneLocked();

        using var enumerator = new MMDeviceEnumerator();
        _micInput = BuildResampledProvider(OpenMic(enumerator, deviceId).ToSampleProvider(), TargetFormat);
        _mixer!.AddMixerInput(_micInput);
        _mic!.StartRecording();
        _currentMicDeviceId = deviceId;
    }

    private void DetachMicrophoneLocked()
    {
        if (_micInput is null) return;
        _mixer?.RemoveMixerInput(_micInput);
        _micInput = null;
        _currentMicDeviceId = null;
        try { _mic?.StopRecording(); } catch { /* already stopping */ }
        _mic?.Dispose();
        _mic = null;
        _micBuffer = null;
    }

    /// <summary>
    /// Dual-leg mode, stage 1: starts system (loopback) audio flowing into its own pipe, unmixed. Call
    /// before <see cref="StartDualMicLeg"/> — the caller must wait for ffmpeg to connect this stream's
    /// pipe and this leg must already be writing before ffmpeg will open the mic pipe.
    /// </summary>
    public void StartDualSystemLeg(Stream systemOutputStream)
    {
        _outputStream = systemOutputStream;
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        using var enumerator = new MMDeviceEnumerator();

        var provider = BuildResampledProvider(OpenLoopback(enumerator).ToSampleProvider(), targetFormat).ToWaveProvider16();
        _loopback!.StartRecording();

        IsActive = true;
        _pump = new PcmLegPump(provider, systemOutputStream, ElapsedAt, () => _loopbackBuffer?.BufferedDuration.TotalMilliseconds ?? 0, () => _loopbackBuffer?.ClearBuffer(), () => IsPaused);
        _pump.Start();
    }

    /// <summary>Dual-leg mode, stage 2: starts the microphone flowing into its own, separate pipe (to be noise-suppressed by ffmpeg). Call only after <see cref="StartDualSystemLeg"/>.</summary>
    public void StartDualMicLeg(string? microphoneDeviceId, Stream micOutputStream)
    {
        _micOutputStream = micOutputStream;
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        using var enumerator = new MMDeviceEnumerator();

        var provider = BuildResampledProvider(OpenMic(enumerator, microphoneDeviceId).ToSampleProvider(), targetFormat).ToWaveProvider16();
        _mic!.StartRecording();

        _micPump = new PcmLegPump(provider, micOutputStream, ElapsedAt, () => _micBuffer?.BufferedDuration.TotalMilliseconds ?? 0, () => _micBuffer?.ClearBuffer(), () => IsPaused);
        _micPump.Start();
    }

    private BufferedWaveProvider OpenLoopback(MMDeviceEnumerator enumerator)
    {
        var renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _loopback = new WasapiLoopbackCapture(renderDevice);
        _loopbackBuffer = new BufferedWaveProvider(_loopback.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };
        _loopback.DataAvailable += (_, e) => _loopbackBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        return _loopbackBuffer;
    }

    private BufferedWaveProvider OpenMic(MMDeviceEnumerator enumerator, string? microphoneDeviceId)
    {
        var captureDevice = string.IsNullOrEmpty(microphoneDeviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : enumerator.GetDevice(microphoneDeviceId);
        _mic = new WasapiCapture(captureDevice);
        _micBuffer = new BufferedWaveProvider(_mic.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };
        _mic.DataAvailable += (_, e) => _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        return _micBuffer;
    }

    private static ISampleProvider BuildResampledProvider(ISampleProvider source, WaveFormat targetFormat)
    {
        var working = source;

        if (working.WaveFormat.SampleRate != targetFormat.SampleRate)
        {
            working = new WdlResamplingSampleProvider(working, targetFormat.SampleRate);
        }

        if (working.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
        {
            working = new MonoToStereoSampleProvider(working);
        }
        else if (working.WaveFormat.Channels == 2 && targetFormat.Channels == 1)
        {
            working = new StereoToMonoSampleProvider(working);
        }

        return working;
    }

    private double MixedBacklogMs()
    {
        var loop = _loopbackBuffer;
        var mic = _micBuffer;
        return Math.Max(loop?.BufferedDuration.TotalMilliseconds ?? 0, mic?.BufferedDuration.TotalMilliseconds ?? 0);
    }

    private void DiscardBuffers()
    {
        _loopbackBuffer?.ClearBuffer();
        _micBuffer?.ClearBuffer();
    }

    /// <summary>
    /// Ends the recording's audio at <paramref name="stopTimestamp"/>: produces everything up to that instant,
    /// waits for it to reach the encoder's pipe, then tears the devices down. The caller should close the video
    /// input first so ffmpeg is free to consume the audio tail (it will not interleave audio ahead of video that
    /// might still arrive).
    /// </summary>
    public void StopAndDrain(long stopTimestamp)
    {
        if (!IsActive) return;
        _pump?.RequestStop(stopTimestamp);
        _micPump?.RequestStop(stopTimestamp);
        bool drained = (_pump?.WaitFinished(TimeSpan.FromSeconds(10)) ?? true) & (_micPump?.WaitFinished(TimeSpan.FromSeconds(10)) ?? true);
        DrainTimedOut = !drained;
        Teardown();
    }

    /// <summary>True if the last <see cref="StopAndDrain"/> gave up waiting for the encoder to accept the audio tail.</summary>
    public bool DrainTimedOut { get; private set; }

    /// <summary>Stops immediately, without delivering what is still queued (failure and dispose paths).</summary>
    public void Stop()
    {
        if (!IsActive) return;
        _pump?.Abort();
        _micPump?.Abort();
        Teardown();
    }

    private void Teardown()
    {
        IsActive = false;
        SupportsLiveSourceChanges = false;
        if (_pump is { } p) _lastStats = (p.LeadSilenceSeconds, p.WrittenBytes, p.StaleDroppedMs, p.UnderrunMs, p.MaxQueuedMs, p.WriteStallMs);
        _pump = null;
        _micPump = null;

        lock (_liveLock)
        {
            try { _loopback?.StopRecording(); } catch { /* already stopping */ }
            _loopback?.Dispose();
            _loopback = null;

            try { _mic?.StopRecording(); } catch { /* already stopping */ }
            _mic?.Dispose();
            _mic = null;

            _loopbackInput = null;
            _micInput = null;
            _currentMicDeviceId = null;
            _loopbackBuffer = null;
            _micBuffer = null;
        }

        _mixer = null;
        _outputStream = null;
        _micOutputStream = null;
    }

    public void Dispose() => Stop();
}
