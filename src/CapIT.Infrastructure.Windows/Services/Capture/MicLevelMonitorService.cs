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
/// <remarks>
/// <para><b>Threading contract — this is why the app used to freeze when switching mic device:</b></para>
/// <para>
/// <see cref="WasapiCapture"/> captures <see cref="SynchronizationContext.Current"/> in its constructor
/// and marshals its <c>RecordingStopped</c> event back onto it. If it is constructed on the UI thread,
/// that event (and therefore the COM teardown of the underlying <c>AudioClient</c>) is posted back to the
/// UI thread. Tearing one capture down and opening the next — which is exactly what changing the mic
/// device does — then deadlocks: the UI thread is blocked inside the switch waiting for a capture thread
/// that is itself blocked waiting to post onto that same UI thread, and the half-disposed COM object
/// faults. So <see cref="Start"/> / <see cref="Stop"/> MUST be called from a background (thread-pool)
/// thread, never the UI thread — see <c>MainViewModel.RestartMicMonitor</c>. On a thread-pool thread
/// <c>SynchronizationContext.Current</c> is null, the stop event fires inline on the capture thread, and
/// the whole start/stop path is non-blocking.
/// </para>
/// </remarks>
public sealed class MicLevelMonitorService : IDisposable
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

    // Plain volatile field (not "=> _session is not null" under the lock): the UI thread reads this on
    // every 150ms meter tick and from several bindings, and must never block on a device switch that is
    // mid-flight on a background thread while holding _gate.
    private volatile bool _active;
    public bool IsActive => _active;

    /// <summary>
    /// Starts monitoring the given microphone (or the system default if null/empty). Safe to call again
    /// to switch devices — begins a non-blocking teardown of any previous capture first. Best-effort:
    /// silently no-ops if the device can't be opened (no mic hardware, mic privacy toggle off, invalid
    /// saved id, …) rather than throwing back at the caller.
    /// <b>Must be called off the UI thread</b> — see the class remarks.
    /// </summary>
    public void Start(string? microphoneDeviceId)
    {
        lock (_gate)
        {
            StopLocked();
            if (_disposed) return;

            try
            {
                _session = new Session(microphoneDeviceId, Report);
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

    /// <summary>Stops monitoring. Non-blocking: the actual COM teardown finishes on the capture thread. <b>Must be called off the UI thread.</b></summary>
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

    /// <summary>One live capture. Self-contained so a fast device switch can leave the previous session to
    /// finish tearing itself down on its own capture thread while the next one is already running.</summary>
    private sealed class Session
    {
        private readonly WasapiCapture _capture;
        private readonly BufferedWaveProvider _buffer;
        private readonly ISampleProvider _sampleProvider;
        private readonly float[] _scratch = new float[2048];
        private readonly Action<float> _report;
        private volatile bool _stopped;

        public Session(string? microphoneDeviceId, Action<float> report)
        {
            _report = report;

            using var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            try
            {
                device = string.IsNullOrEmpty(microphoneDeviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : enumerator.GetDevice(microphoneDeviceId);
            }
            catch
            {
                // Saved device id no longer resolves (unplugged / different machine) — fall back to the
                // default endpoint rather than failing the whole monitor.
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }

            _capture = new WasapiCapture(device);
            // ReadFully = false is load-bearing, not a tweak: BufferedWaveProvider defaults it to TRUE,
            // where Read() zero-pads and always returns the full requested count — so the drain loop in
            // OnDataAvailable never sees 0 and never exits. That spun a WASAPI capture thread at 100%
            // CPU forever, reporting an RMS of 0 from the zero padding on every iteration after the
            // first, which is exactly why the meters sat permanently at "No signal" / "Silent".
            _buffer = new BufferedWaveProvider(_capture.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };
            // ToSampleProvider() normalizes whatever the device's native format is (PCM16/24/32, IEEE
            // float) into -1..1 floats — the same conversion AudioCaptureService relies on for its own
            // mic leg — so the RMS math below only ever has to handle one format.
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

        /// <summary>Requests a stop and returns immediately. <see cref="WasapiCapture.StopRecording"/> only
        /// flips an internal flag; the capture thread then unwinds and raises <c>RecordingStopped</c>,
        /// where the COM objects are actually disposed — the one place it's safe to do so.</summary>
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
