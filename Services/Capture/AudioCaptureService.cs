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
    private Thread? _pumpThread;
    private Thread? _micPumpThread;
    private volatile bool _running;
    private Stream? _outputStream;
    private Stream? _micOutputStream;

    public bool IsActive { get; private set; }

    /// <summary>While true, PumpLoop writes silence instead of live audio (used while recording is paused).</summary>
    public volatile bool IsMuted;

    /// <summary>Starts capturing with both sources pre-mixed into one stream. Returns false (and does nothing) if neither source is requested.</summary>
    public bool Start(bool captureSystemAudio, bool captureMicrophone, string? microphoneDeviceId, Stream outputStream)
    {
        if (!captureSystemAudio && !captureMicrophone) return false;

        _outputStream = outputStream;
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        var sources = new List<ISampleProvider>();

        using var enumerator = new MMDeviceEnumerator();

        if (captureSystemAudio)
        {
            sources.Add(BuildResampledProvider(OpenLoopback(enumerator).ToSampleProvider(), targetFormat));
        }

        if (captureMicrophone)
        {
            sources.Add(BuildResampledProvider(OpenMic(enumerator, microphoneDeviceId).ToSampleProvider(), targetFormat));
        }

        _mixer = new MixingSampleProvider(sources) { ReadFully = true };

        _loopback?.StartRecording();
        _mic?.StartRecording();

        _running = true;
        IsActive = true;
        _pumpThread = new Thread(() => PumpLoop(_mixer.ToWaveProvider16(), _outputStream)) { IsBackground = true, Name = "AudioPump" };
        _pumpThread.Start();

        return true;
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

        _running = true;
        IsActive = true;
        _pumpThread = new Thread(() => PumpLoop(provider, _outputStream)) { IsBackground = true, Name = "AudioPump-System" };
        _pumpThread.Start();
    }

    /// <summary>Dual-leg mode, stage 2: starts the microphone flowing into its own, separate pipe (to be noise-suppressed by ffmpeg). Call only after <see cref="StartDualSystemLeg"/>.</summary>
    public void StartDualMicLeg(string? microphoneDeviceId, Stream micOutputStream)
    {
        _micOutputStream = micOutputStream;
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        using var enumerator = new MMDeviceEnumerator();

        var provider = BuildResampledProvider(OpenMic(enumerator, microphoneDeviceId).ToSampleProvider(), targetFormat).ToWaveProvider16();
        _mic!.StartRecording();

        _micPumpThread = new Thread(() => PumpLoop(provider, _micOutputStream)) { IsBackground = true, Name = "AudioPump-Mic" };
        _micPumpThread.Start();
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

    private void PumpLoop(IWaveProvider provider16, Stream? outputStream)
    {
        var chunk = new byte[SampleRate / 100 * Channels * (BitsPerSample / 8)]; // 10ms chunks

        while (_running)
        {
            int read = provider16.Read(chunk, 0, chunk.Length);
            if (read <= 0) continue;

            if (IsMuted)
            {
                Array.Clear(chunk, 0, read);
            }

            try
            {
                outputStream?.Write(chunk, 0, read);
            }
            catch (IOException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false;
        _running = false;
        _pumpThread?.Join(1000);
        _pumpThread = null;
        _micPumpThread?.Join(1000);
        _micPumpThread = null;

        _loopback?.StopRecording();
        _loopback?.Dispose();
        _loopback = null;

        _mic?.StopRecording();
        _mic?.Dispose();
        _mic = null;

        _mixer = null;
        _outputStream = null;
        _micOutputStream = null;
    }

    public void Dispose() => Stop();
}
