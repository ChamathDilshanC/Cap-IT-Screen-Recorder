using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Captures system (loopback) audio and/or microphone audio via NAudio/WASAPI, mixes them into a
/// single stream, and continuously pumps 16-bit PCM bytes into the provided output stream (the
/// FFmpeg audio named pipe).
/// </summary>
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
    private volatile bool _running;
    private Stream? _outputStream;

    public bool IsActive { get; private set; }

    /// <summary>While true, PumpLoop writes silence instead of live audio (used while recording is paused).</summary>
    public volatile bool IsMuted;

    /// <summary>Starts capturing. Returns false (and does nothing) if neither source is requested.</summary>
    public bool Start(bool captureSystemAudio, bool captureMicrophone, string? microphoneDeviceId, Stream outputStream)
    {
        if (!captureSystemAudio && !captureMicrophone) return false;

        _outputStream = outputStream;
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        var sources = new List<ISampleProvider>();

        using var enumerator = new MMDeviceEnumerator();

        if (captureSystemAudio)
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
            sources.Add(BuildResampledProvider(_loopbackBuffer.ToSampleProvider(), targetFormat));
        }

        if (captureMicrophone)
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
            sources.Add(BuildResampledProvider(_micBuffer.ToSampleProvider(), targetFormat));
        }

        _mixer = new MixingSampleProvider(sources) { ReadFully = true };

        _loopback?.StartRecording();
        _mic?.StartRecording();

        _running = true;
        IsActive = true;
        _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "AudioPump" };
        _pumpThread.Start();

        return true;
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

    private void PumpLoop()
    {
        var provider16 = _mixer!.ToWaveProvider16();
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
                _outputStream?.Write(chunk, 0, read);
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

        _loopback?.StopRecording();
        _loopback?.Dispose();
        _loopback = null;

        _mic?.StopRecording();
        _mic?.Dispose();
        _mic = null;

        _mixer = null;
        _outputStream = null;
    }

    public void Dispose() => Stop();
}
