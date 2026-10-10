using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordingChecks;

/// <summary>
/// A deterministic audio marker: a 1 kHz, 60 ms tone burst at the start of every second of the stream it
/// generates (sample-counted, so the burst spacing is exact regardless of scheduling). The test source plays it
/// through the real output device while it paints its millisecond clock, so a recording of the pair carries both
/// a visual timeline and an audible one that can be compared.
/// </summary>
internal sealed class BeepProvider : ISampleProvider
{
    private readonly Func<long> _clockMs;
    private long _samples;
    private bool _anchored;
    public long AnchorMs { get; private set; } = -1;
    public event Action<long>? Anchored;

    public const int SampleRate = 48000;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public BeepProvider(Func<long> clockMs) => _clockMs = clockMs;

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_anchored)
        {
            // The first buffer is requested just before playback starts; the clock reading here is the
            // reference "sample 0" instant. Whatever fixed latency lies between this and the speaker is
            // the same constant in every recording, which is all the drift measurement depends on.
            AnchorMs = _clockMs();
            _anchored = true;
            Anchored?.Invoke(AnchorMs);
        }

        for (int i = 0; i < count; i += 2)
        {
            double t = _samples / (double)SampleRate;
            double phase = t - Math.Floor(t);
            double amp = 0;
            if (phase < 0.060)
            {
                double env = Math.Min(1.0, Math.Min(phase / 0.004, (0.060 - phase) / 0.004));
                amp = 0.18 * env * Math.Sin(2 * Math.PI * 1000 * t);
            }
            buffer[offset + i] = (float)amp;
            buffer[offset + i + 1] = (float)amp;
            _samples++;
        }
        return count;
    }
}

internal sealed class BeepPlayer : IDisposable
{
    private readonly WasapiOut _out;
    public BeepProvider Provider { get; }

    public BeepPlayer(Func<long> clockMs)
    {
        Provider = new BeepProvider(clockMs);
        _out = new WasapiOut(AudioClientShareMode.Shared, 30);
        _out.Init(Provider);
    }

    public void Start() => _out.Play();
    public void Dispose() { try { _out.Stop(); } catch { } _out.Dispose(); }
}

/// <summary>
/// Measures the latency of the audio chain the recorder sees: when each beep's first loud packet is delivered by
/// WASAPI loopback, relative to the beep's nominal time. The same constant sits inside every beat-versus-picture
/// comparison, so subtracting it leaves the part that belongs to the recorder.
/// </summary>
internal sealed class LoopbackCalibration : IDisposable
{
    private readonly WasapiLoopbackCapture _cap = new();
    private readonly List<double> _lat = new();
    private long _lastDetect = -100000;

    public LoopbackCalibration(Func<long> clockMs, Func<long> anchorMs, Action<double> done, int beeps = 6)
    {
        _cap.DataAvailable += (_, e) =>
        {
            long now = clockMs();
            long anchor = anchorMs();
            if (anchor < 0 || _lat.Count >= beeps) return;
            float peak = 0;
            int fmtBytes = _cap.WaveFormat.BitsPerSample / 8;
            if (_cap.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                for (int i = 0; i + 4 <= e.BytesRecorded; i += 4) peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(e.Buffer, i)));
            else
                for (int i = 0; i + 2 <= e.BytesRecorded; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
            if (peak > 0.05f && now - _lastDetect > 600)
            {
                _lastDetect = now;
                double rel = (now - anchor) % 1000.0;
                if (rel > 500) rel -= 1000;
                _lat.Add(rel);
                if (_lat.Count >= beeps) { var sorted = _lat.OrderBy(v => v).ToList(); done(sorted[sorted.Count / 2]); }
            }
        };
    }

    public void Start() => _cap.StartRecording();
    public void Dispose() { try { _cap.StopRecording(); } catch { } _cap.Dispose(); }
}
