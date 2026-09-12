using System.Runtime.InteropServices;

namespace ScreenRecorderApp.Services;

/// <summary>
/// Raises the Windows multimedia timer resolution to 1ms for as long as the scope is alive, and drops
/// it back on dispose.
/// </summary>
/// <remarks>
/// Windows' default timer granularity is ~15.6ms. Every .NET timer primitive — <c>PeriodicTimer</c>
/// included — is quantized to it, so a pacer asking for 16.67ms ticks (60fps) does not get them: it
/// gets 15.6ms and 31.2ms alternating, which is a ±50% error on every single frame interval. Because
/// the encoder is fed fixed-rate rawvideo, that error does not show up as wrong timestamps, it shows up
/// as frames sampled at the wrong moments — visible as uneven motion in the recording even though the
/// file's nominal framerate is exactly right.
///
/// <c>timeBeginPeriod</c> is process-wide and refcounted by the OS, so every call must be paired with
/// exactly one <c>timeEndPeriod</c> — hence the scope. It is deliberately held only for the duration of
/// a recording rather than for the app's lifetime: a raised timer resolution increases scheduler wakeups
/// system-wide, which costs battery on a laptop that is merely sitting on the home screen.
///
/// Windows 10 2004 and later honour this per-process rather than globally, so this no longer degrades
/// other applications' timers the way it did on older builds.
/// </remarks>
public sealed class TimerResolutionScope : IDisposable
{
    private const uint TargetPeriodMs = 1;
    private const uint TimerrNoerror = 0;

    private bool _active;

    public TimerResolutionScope()
    {
        try
        {
            _active = timeBeginPeriod(TargetPeriodMs) == TimerrNoerror;
        }
        catch (DllNotFoundException)
        {
            // winmm is present on every supported Windows build; if it somehow is not, a coarser pacer
            // is a quality regression, not a failure to record.
            _active = false;
        }
    }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        try { timeEndPeriod(TargetPeriodMs); } catch { /* best effort */ }
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint uPeriod);
}
