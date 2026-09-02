using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// An independent, lightweight WASAPI capture used purely to measure live microphone input level (RMS,
/// normalized roughly 0-1) for a UI meter. Deliberately separate from <see cref="AudioCaptureService"/>'s
/// own mic capture, which only exists during an actual recording — this one runs whenever microphone
/// capture is enabled, including before Start Recording is ever pressed, so the user can confirm their
/// mic is actually picking up sound while setting up. Both can run concurrently without conflict:
/// <see cref="WasapiCapture"/> defaults to shared mode, which Windows explicitly supports multiple
/// simultaneous readers of.
/// </summary>
public sealed class MicLevelMonitorService : IDisposable
{
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _sampleProvider;
    private readonly float[] _scratch = new float[2048];

    /// <summary>Latest RMS level. Read from a UI timer tick rather than pushed via an event — a meter only ever needs the most recent value, not every intermediate one.</summary>
    public volatile float CurrentLevel;

    public bool IsActive => _capture is not null;

    /// <summary>Starts monitoring the given microphone (or the system default if null/empty). Safe to call again to switch devices — stops any previous capture first. Best-effort: silently no-ops if the device can't be opened (e.g. no microphone hardware present) rather than crashing the UI thread that called it.</summary>
    public void Start(string? microphoneDeviceId)
    {
        Stop();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = string.IsNullOrEmpty(microphoneDeviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                : enumerator.GetDevice(microphoneDeviceId);

            _capture = new WasapiCapture(device);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat) { DiscardOnBufferOverflow = true };
            // ToSampleProvider() normalizes whatever the device's native format is (PCM16/24/32, IEEE
            // float) into -1..1 floats — the same conversion AudioCaptureService relies on for its own
            // mic leg — so the RMS math below only ever has to handle one format.
            _sampleProvider = _buffer.ToSampleProvider();

            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
        }
        catch
        {
            Stop();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer!.AddSamples(e.Buffer, 0, e.BytesRecorded);

        int read;
        while ((read = _sampleProvider!.Read(_scratch, 0, _scratch.Length)) > 0)
        {
            double sumSquares = 0;
            for (int i = 0; i < read; i++) sumSquares += (double)_scratch[i] * _scratch[i];
            CurrentLevel = (float)Math.Sqrt(sumSquares / read);
        }
    }

    public void Stop()
    {
        if (_capture is null) return;

        _capture.DataAvailable -= OnDataAvailable;
        try { _capture.StopRecording(); } catch { /* best effort */ }
        _capture.Dispose();
        _capture = null;
        _buffer = null;
        _sampleProvider = null;
        CurrentLevel = 0;
    }

    public void Dispose() => Stop();
}
