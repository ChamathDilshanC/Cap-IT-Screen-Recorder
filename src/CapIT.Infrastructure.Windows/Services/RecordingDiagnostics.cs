using System.Diagnostics;

namespace ScreenRecorderApp.Services;

/// <summary>
/// A duration/interval statistic with a coarse log-spaced histogram, cheap enough to update on the
/// capture/pacer hot paths: no allocation, no lock. Each instance has exactly one writing thread (the
/// capture thread, the pacer or the writer); the snapshot is read after that thread has stopped or, for a
/// live read, tolerates a slightly stale value, which is all a diagnostic summary needs.
/// </summary>
public sealed class RunningStat
{
    // Bucket upper bounds in milliseconds. Chosen around the frame budgets that matter: 8.3ms (120fps),
    // 16.7ms (60fps), 33.3ms (30fps) and multiples of them, so "how many frames blew the budget" can be
    // read straight off the histogram instead of inferred from an average.
    private static readonly double[] BucketBoundsMs = [1, 2, 4, 8, 12, 16.7, 25, 33.4, 50, 67, 100, 200, 500];

    private readonly long[] _buckets = new long[BucketBoundsMs.Length + 1];
    private long _count;
    private double _sumMs;
    private double _maxMs;

    public void Add(double ms)
    {
        _count++;
        _sumMs += ms;
        if (ms > _maxMs) _maxMs = ms;

        int i = 0;
        while (i < BucketBoundsMs.Length && ms > BucketBoundsMs[i]) i++;
        _buckets[i]++;
    }

    public void AddTicks(long startTimestamp) => Add(Elapsed(startTimestamp, Stopwatch.GetTimestamp()));

    public static double Elapsed(long startTimestamp, long endTimestamp)
        => (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    public StatSnapshot Snapshot()
    {
        long count = _count;
        if (count == 0) return new StatSnapshot(0, 0, 0, 0, 0);

        // p99 from the histogram: the upper bound of the bucket the 99th percentile sample falls in.
        // Coarse by design — it answers "is the tail inside the frame budget", not "what is the tail".
        long target = (long)Math.Ceiling(count * 0.99);
        long running = 0;
        double p99 = _maxMs;
        for (int i = 0; i < _buckets.Length; i++)
        {
            running += _buckets[i];
            if (running >= target)
            {
                p99 = i < BucketBoundsMs.Length ? Math.Min(BucketBoundsMs[i], _maxMs) : _maxMs;
                break;
            }
        }

        return new StatSnapshot(count, _sumMs / count, p99, _maxMs, _sumMs);
    }

    public long CountOver(double thresholdMs)
    {
        long over = 0;
        for (int i = 0; i < _buckets.Length; i++)
        {
            double lower = i == 0 ? 0 : BucketBoundsMs[i - 1];
            if (lower >= thresholdMs) over += _buckets[i];
        }
        return over;
    }
}

public readonly record struct StatSnapshot(long Count, double MeanMs, double P99Ms, double MaxMs, double TotalMs);

/// <summary>
/// Per-recording counters answering "where did continuity go". Nothing here stores or logs captured
/// pixels, keystrokes or window titles — only counts and durations.
/// </summary>
public sealed class RecordingDiagnostics
{
    // --- capture thread ---
    /// <summary>Wall time of one compose (GPU passes + readback, or CPU crop/sharpen) on the capture thread.</summary>
    public readonly RunningStat ComposeMs = new();
    /// <summary>Time between consecutive published (composed) frames — the cadence the pacer actually has to sample from.</summary>
    public readonly RunningStat PublishIntervalMs = new();
    /// <summary>GPU path only: CPU time spent recording the frame's draw/dispatch/copy commands.</summary>
    public readonly RunningStat GpuRecordMs = new();
    /// <summary>GPU path only: time blocked waiting for the GPU to finish the frame (the first staging Map). A synchronous readback makes the capture thread pay the GPU's whole frame time here.</summary>
    public readonly RunningStat GpuWaitMs = new();
    /// <summary>GPU path only: CPU time copying the mapped NV12 planes (and preview BGRA) into managed arrays.</summary>
    public readonly RunningStat GpuReadbackMs = new();
    /// <summary>Slot-driven capture only: how late after its slot's deadline each compose actually started.</summary>
    public readonly RunningStat SlotLatenessMs = new();
    public long SlotComposes;
    public long SlotSkips;
    public long DesktopFramesAcquired;
    public long AnimationTicks;
    public long FramesWithZoomActive;

    // --- pacer ---
    /// <summary>Interval between consecutive pacer wakeups. Anything near or above 2x the frame interval is a missed tick.</summary>
    public readonly RunningStat PacerGapMs = new();
    public long FramesEmitted;
    /// <summary>Frames emitted as a repeat of the previous one because every pooled buffer was in flight (encoder behind).</summary>
    public long RepeatsEncoderBehind;
    /// <summary>Frames emitted as a repeat to keep the timeline whole after the pacer itself woke up late.</summary>
    public long RepeatsPacerCatchUp;
    public long PacerLateWakeups;
    /// <summary>Output frame slots that passed without the pacer running for them (estimated from wake gaps). Before the catch-up fix these were frames that never existed, i.e. timeline lost.</summary>
    public long MissedTicks;
    public int MaxFramesOwedAtOnce;
    /// <summary>Time to copy the published frame into a pooled buffer on the pacer thread.</summary>
    public readonly RunningStat PacerCopyMs = new();

    // --- writer ---
    public readonly RunningStat PipeWriteMs = new();
    public int MaxQueueDepth;

    // --- process ---
    public int Gen0Start, Gen1Start, Gen2Start;
    public double GcPauseMsStart;

    public double WallSeconds;

    // --- audio ---
    /// <summary>Seconds of audio the pump has handed to the encoder (bytes written / bytes per second). Compared with the wall clock it shows whether audio is being produced at real-time rate.</summary>
    public double AudioSecondsWritten;
    /// <summary>Seconds between the video timeline's start and the first audio write — the offset between the two streams' t=0 that has to be padded out.</summary>
    public double AudioStartOffsetSeconds;
    public double AudioLeadSilenceSeconds;
    /// <summary>Captured audio discarded as stale (loopback's initial burst, device-clock drift).</summary>
    public double AudioStaleDroppedMs;
    /// <summary>Silence generated for intervals in which the device delivered nothing.</summary>
    public double AudioUnderrunMs;
    /// <summary>Largest amount of audio queued for the pipe at once (an encoder stall absorbed by the queue).</summary>
    public double AudioMaxQueuedMs;
    public double AudioWriteStallMs;
    public bool AudioDrainTimedOut;
    public long TailSlotsEmitted;

    public void BeginProcessCounters()
    {
        Gen0Start = GC.CollectionCount(0);
        Gen1Start = GC.CollectionCount(1);
        Gen2Start = GC.CollectionCount(2);
        GcPauseMsStart = GC.GetTotalPauseDuration().TotalMilliseconds;
    }

    public string Summarize(int fps)
    {
        double frameMs = 1000.0 / fps;
        var compose = ComposeMs.Snapshot();
        var publish = PublishIntervalMs.Snapshot();
        var gRec = GpuRecordMs.Snapshot();
        var gWait = GpuWaitMs.Snapshot();
        var gRead = GpuReadbackMs.Snapshot();
        var gap = PacerGapMs.Snapshot();
        var copy = PacerCopyMs.Snapshot();
        var write = PipeWriteMs.Snapshot();
        int gen0 = GC.CollectionCount(0) - Gen0Start;
        int gen1 = GC.CollectionCount(1) - Gen1Start;
        int gen2 = GC.CollectionCount(2) - Gen2Start;
        double gcPause = GC.GetTotalPauseDuration().TotalMilliseconds - GcPauseMsStart;

        return string.Join(Environment.NewLine,
            $"frame budget            : {frameMs:F2} ms ({fps} fps)",
            $"desktop frames acquired : {DesktopFramesAcquired}   animation ticks: {AnimationTicks}   zoom-active composes: {FramesWithZoomActive}",
            $"compose  (capture thr.) : n={compose.Count} mean={compose.MeanMs:F2} p99<={compose.P99Ms:F1} max={compose.MaxMs:F1} ms   over budget: {ComposeMs.CountOver(frameMs)}",
            $"  slot composes: {SlotComposes}  skipped slots: {SlotSkips}  start lateness mean/max: {SlotLatenessMs.Snapshot().MeanMs:F2}/{SlotLatenessMs.Snapshot().MaxMs:F1} ms",
            $"  gpu record / wait / readback: {gRec.MeanMs:F2} / {gWait.MeanMs:F2} (max {gWait.MaxMs:F1}) / {gRead.MeanMs:F2} (max {gRead.MaxMs:F1}) ms",
            $"publish interval        : n={publish.Count} mean={publish.MeanMs:F2} p99<={publish.P99Ms:F1} max={publish.MaxMs:F1} ms   > 2x budget: {PublishIntervalMs.CountOver(frameMs * 2)}",
            $"pacer wake gap          : n={gap.Count} mean={gap.MeanMs:F2} p99<={gap.P99Ms:F1} max={gap.MaxMs:F1} ms   late wakeups (>1.5x): {PacerLateWakeups}   missed ticks: {MissedTicks}   max frames owed at once: {MaxFramesOwedAtOnce}",
            $"pacer copy              : n={copy.Count} mean={copy.MeanMs:F2} max={copy.MaxMs:F1} ms",
            $"frames emitted          : {FramesEmitted}   repeats (encoder behind): {RepeatsEncoderBehind}   repeats (pacer catch-up): {RepeatsPacerCatchUp}",
            $"pipe write              : n={write.Count} mean={write.MeanMs:F2} p99<={write.P99Ms:F1} max={write.MaxMs:F1} ms   max queue depth: {MaxQueueDepth}",
            $"audio                   : produced {AudioSecondsWritten:F3} s   leading silence {AudioLeadSilenceSeconds * 1000:F0} ms   stale dropped {AudioStaleDroppedMs:F0} ms   device-idle silence {AudioUnderrunMs:F0} ms   max queued {AudioMaxQueuedMs:F0} ms   writer stalls {AudioWriteStallMs:F0} ms   drain timed out: {AudioDrainTimedOut}   tail video slots {TailSlotsEmitted}",
            $"GC during recording     : gen0={gen0} gen1={gen1} gen2={gen2}  total pause={gcPause:F1} ms");
    }
}
