using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Independent WASAPI loopback capture used purely to measure live system/speaker output level (RMS,
/// normalized roughly 0-1) for a UI meter. Mirrors <see cref="MicLevelMonitorService"/> exactly, but on
/// the render (playback) endpoint via <see cref="WasapiLoopbackCapture"/> instead of the capture
/// endpoint — same reasoning: runs independently of <see cref="AudioCaptureService"/>'s own loopback
/// capture (which only exists during an actual recording) so the user can confirm system audio is
/// actually playing before Start Recording is ever pressed.
/// </summary>
public sealed class SpeakerLevelMonitorService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _sampleProvider;
    private readonly float[] _scratch = new float[2048];

    public volatile float CurrentLevel;

    public bool IsActive => _capture is not null;

    /// <summary>Starts monitoring the default playback device's output. Safe to call again — stops any previous capture first. Best-effort: silently no-ops if the device can't be opened rather than crashing the UI thread that called it.</summary>
    public void Start()
    {
        Stop();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _capture = new WasapiLoopbackCapture(device);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat) { DiscardOnBufferOverflow = true };
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
