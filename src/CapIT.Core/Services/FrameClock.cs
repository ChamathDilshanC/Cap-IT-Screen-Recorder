namespace ScreenRecorderApp.Services;

/// <summary>
/// The recording's frame schedule: which output frame slots the wall clock has already opened, how many
/// of them the pacer has filled, and how much time to leave out for a pause.
/// </summary>
/// <remarks>
/// Slot <c>k</c> opens at <c>start + k / fps</c>, computed from an absolute start timestamp every time
/// rather than by adding a rounded interval to the previous deadline. Both of the obvious alternatives
/// are wrong in a way that costs timeline: <c>PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / fps))</c>
/// truncates 16.67ms to 16ms (a 60fps pacer that really runs at 62.5fps, so the video comes out ~4% longer
/// than the audio), and any interval-based timer silently drops a tick it was too late for, which — since
/// ffmpeg is fed untimestamped rawvideo — is a frame that never exists. With absolute slots a late wakeup
/// is not a loss: <see cref="Owed"/> says how many slots have opened since the last one was filled, and
/// the caller fills every one of them, so the encoded timeline always equals the elapsed (un-paused)
/// wall-clock time to within one frame.
///
/// Pure arithmetic over caller-supplied timestamps, with no clock of its own, so it can be tested
/// deterministically. Thread-safe: the pacer thread reads and fills slots while the UI thread
/// pauses/resumes.
/// </remarks>
public sealed class FrameClock
{
    private readonly object _gate = new();
    private readonly long _frequency;
    private readonly int _fps;
    private readonly long _start;
    private long _pausedTotal;
    private long _pauseBegan = -1;
    private long _emitted;

    public FrameClock(int fps, long timestampFrequency, long startTimestamp)
    {
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
        if (timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        _fps = fps;
        _frequency = timestampFrequency;
        _start = startTimestamp;
    }

    public int Fps => _fps;

    /// <summary>When slot <paramref name="index"/> opens, with every pause so far already cut out.</summary>
    public long SlotTimestamp(long index)
    {
        lock (_gate)
        {
            return _start + _pausedTotal + OpenTicks(index);
        }
    }

    /// <summary>Frame slots filled so far.</summary>
    public long Emitted { get { lock (_gate) return _emitted; } }

    public bool IsPaused { get { lock (_gate) return _pauseBegan >= 0; } }

    /// <summary>Total time the clock has spent paused as of <paramref name="now"/>, including a pause still in progress.</summary>
    public long PausedTicks(long now)
    {
        lock (_gate)
        {
            return _pausedTotal + (_pauseBegan >= 0 ? Math.Max(0, now - _pauseBegan) : 0);
        }
    }

    /// <summary>Stops the schedule's clock: slots stop opening until <see cref="Resume"/>. Idempotent.</summary>
    public void Pause(long now)
    {
        lock (_gate)
        {
            if (_pauseBegan < 0) _pauseBegan = now;
        }
    }

    /// <summary>Restarts the clock. The paused interval is cut out of the timeline, not caught up on. Idempotent.</summary>
    public void Resume(long now)
    {
        lock (_gate)
        {
            if (_pauseBegan < 0) return;
            _pausedTotal += Math.Max(0, now - _pauseBegan);
            _pauseBegan = -1;
        }
    }

    /// <summary>How many slots have opened that have not been filled yet. Slot 0 opens at the start, so this is 1 immediately.</summary>
    public long Owed(long now)
    {
        lock (_gate)
        {
            long active = Math.Max(0, (_pauseBegan >= 0 ? _pauseBegan : now) - _start - _pausedTotal);
            return Math.Max(0, active * _fps / _frequency + 1 - _emitted);
        }
    }

    /// <summary>The timestamp at which the next unfilled slot opens (in the past if one is already owed).</summary>
    public long NextSlotTimestamp()
    {
        lock (_gate)
        {
            return _start + _pausedTotal + OpenTicks(_emitted);
        }
    }

    // The first whole tick at or after k/fps seconds. Rounded up, not down, so it agrees exactly with Owed():
    // a slot counts as open once active*fps/freq has reached k in integer arithmetic, i.e. from this tick on.
    private long OpenTicks(long slot) => (slot * _frequency + _fps - 1) / _fps;

    public void MarkEmitted(long count)
    {
        lock (_gate) _emitted += count;
    }
}
