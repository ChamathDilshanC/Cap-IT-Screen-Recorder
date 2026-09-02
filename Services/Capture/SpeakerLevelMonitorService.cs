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
/// <remarks>
/// Same threading contract as <see cref="MicLevelMonitorService"/> — <see cref="Start"/> / <see cref="Stop"/>
/// must run off the UI thread so <see cref="WasapiCapture"/>'s <c>RecordingStopped</c> / COM teardown
/// doesn't deadlock against the caller. See that class's remarks for the full explanation.
/// </remarks>
public sealed class SpeakerLevelMonitorService : IDisposable
{
    private readonly object _gate = new();
    private Session? _session;
    private bool _disposed;

    private volatile float _level;
    private long _lastReportTicks;

    /// <summary>
    /// Latest RMS level. Read from a UI timer tick rather than pushed via an event — a meter only ever
    /// needs the most recent value, not every intermediate one. Reads back as 0 once the last reading
    /// goes stale: WASAPI can simply stop delivering buffers when the endpoint goes fully idle rather
    /// than delivering silent ones, and without this the meter would freeze at whatever the last audible
    /// level happened to be instead of falling back to silence.
    /// </summary>
    public float CurrentLevel =>
        Environment.TickCount64 - Interlocked.Read(ref _lastReportTicks) > StaleAfterMs ? 0f : _level;

    private const int StaleAfterMs = 500;

    private void Report(float level)
    {
        _level = level;
        Interlocked.Exchange(ref _lastReportTicks, Environment.TickCount64);
    }

    private volatile bool _active;
    public bool IsActive => _active;

    /// <summary>Starts monitoring the default playback device's output. Safe to call again — begins a
    /// non-blocking teardown of any previous capture first. Best-effort. <b>Must be called off the UI thread.</b></summary>
    public void Start()
    {
        lock (_gate)
        {
            StopLocked();
            if (_disposed) return;

            try
            {
                _session = new Session(Report);
                _active = true;
            }
            catch
            {
                _session = null;
                _active = false;
                _level = 0;
            }
        }
    }

    /// <summary>Stops monitoring. Non-blocking. <b>Must be called off the UI thread.</b></summary>
    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    private void StopLocked()
    {
        var s = _session;
        _session = null;
        _active = false;
        _level = 0;
        Interlocked.Exchange(ref _lastReportTicks, 0);
        s?.BeginStop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopLocked();
        }
    }

    private sealed class Session
    {
        private readonly WasapiLoopbackCapture _capture;
        private readonly BufferedWaveProvider _buffer;
        private readonly ISampleProvider _sampleProvider;
        private readonly float[] _scratch = new float[2048];
        private readonly Action<float> _report;
        private volatile bool _stopped;

        public Session(Action<float> report)
        {
            _report = report;

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _capture = new WasapiLoopbackCapture(device);
            // ReadFully = false is load-bearing, not a tweak: BufferedWaveProvider defaults it to TRUE,
            // where Read() zero-pads and always returns the full requested count — so the drain loop in
            // OnDataAvailable never sees 0 and never exits. That spun a WASAPI capture thread at 100%
            // CPU forever, reporting an RMS of 0 from the zero padding on every iteration after the
            // first, which is exactly why the meters sat permanently at "No signal" / "Silent".
            _buffer = new BufferedWaveProvider(_capture.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };
            _sampleProvider = _buffer.ToSampleProvider();

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            try
            {
                _capture.StartRecording();
            }
            catch
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                throw;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_stopped) return;

            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // RMS is accumulated across the whole callback and reported once, rather than per chunk:
            // a single 2048-sample tail chunk would otherwise be the value the meter ends up showing,
            // which under-reads a loud burst that mostly landed in the earlier chunks.
            double sumSquares = 0;
            long total = 0;
            int read;
            while ((read = _sampleProvider.Read(_scratch, 0, _scratch.Length)) > 0)
            {
                for (int i = 0; i < read; i++) sumSquares += (double)_scratch[i] * _scratch[i];
                total += read;
            }

            if (total > 0 && !_stopped) _report((float)Math.Sqrt(sumSquares / total));
        }

        public void BeginStop()
        {
            _stopped = true;
            try { _capture.StopRecording(); }
            catch { /* already stopped / never fully started */ }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.Dispose(); } catch { /* best effort */ }
        }
    }
}
