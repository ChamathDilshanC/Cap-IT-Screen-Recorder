using System.Diagnostics;
using System.Threading.Channels;
using NAudio.Wave;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Feeds one 16-bit stereo PCM stream to an ffmpeg audio pipe so that the stream's length is the wall-clock
/// length of the recording, and the audio at position <c>p</c> is the audio that was captured at time <c>p</c>.
/// </summary>
/// <remarks>
/// The pump this replaces read from the capture buffer and wrote to the pipe in one loop. That made the audio
/// timeline a function of how fast ffmpeg happened to <em>accept</em> bytes rather than of the clock, with
/// three measurable consequences:
/// <list type="number">
/// <item>ffmpeg does not open its audio input until after the first video frames have been probed, so the first
/// <c>Write</c> blocked for ~1 s; the capture buffer filled to ~1.1–1.8 s of its 2 s capacity and stayed there
/// for the whole recording (the pump only ever consumed at real-time rate, so it never caught up). Anything
/// that arrived while the buffer was full was dropped.</item>
/// <item>On stop, whatever was still sitting in that backlog was lost, and how much of it had been written by
/// the instant the loop was told to stop was a race: the audio tail came out anywhere from 0 to ~0.9 s short.</item>
/// <item>WASAPI loopback hands over a burst of already-queued audio when capture starts; because the pump
/// preserved order and did not trim, that stale burst shifted the whole stream's content (±0.3 s run to run).</item>
/// </list>
/// Here a <b>reader</b> thread produces exactly the number of samples the clock says are owed (so a silent
/// device yields real-time silence, never faster or slower), trims backlog that cannot be legitimate, and hands
/// chunks to an unbounded queue; a separate <b>writer</b> drains that queue into the pipe. A stalled pipe only
/// lengthens the queue (192 KB/s), it can no longer back up into the capture buffer. On stop the reader
/// produces up to the stop instant and the caller waits for the queue to empty, so nothing that was captured is
/// left behind, and no artificial silence is added to hide a real truncation.
/// </remarks>
internal sealed class PcmLegPump
{
    private const int Rate = AudioCaptureService.SampleRate;
    private const int FrameBytes = AudioCaptureService.Channels * AudioCaptureService.BitsPerSample / 8;
    private const int ChunkFrames = Rate / 100; // 10 ms

    // Capture backlog beyond what the clock accounts for is stale (the initial loopback burst) or drift. It is
    // trimmed down to a small standing cushion rather than allowed to become audio latency.
    private const double TrimAboveMs = 50;
    private const double TrimToMs = 20;

    private readonly IWaveProvider _source;
    private readonly Stream _output;
    private readonly Func<long, double> _elapsedAt;
    private readonly Func<double> _backlogMs;
    private readonly Action _discardBacklog;
    private readonly Func<bool> _isPaused;
    private readonly Channel<byte[]> _queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private Thread? _reader;
    private Task? _writer;
    private volatile bool _stopRequested;
    private volatile bool _aborted;
    private double _stopElapsed;
    private long _queuedBytes;
    private long _writtenBytes;
    private long _firstWriteTimestamp;
    private long _writeStallTicks;

    public double LeadSilenceSeconds { get; private set; }
    public long WrittenBytes => Interlocked.Read(ref _writtenBytes);
    public long FirstWriteTimestamp => Interlocked.Read(ref _firstWriteTimestamp);
    /// <summary>Captured audio discarded because it was older than the clock allows (initial burst, device drift).</summary>
    public double StaleDroppedMs { get; private set; }
    /// <summary>Silence generated because the device delivered nothing for an interval the clock says elapsed (an idle loopback device legitimately does this).</summary>
    public double UnderrunMs { get; private set; }
    /// <summary>Largest amount of audio waiting for the pipe at any moment — the size of the stall the queue absorbed.</summary>
    public double MaxQueuedMs { get; private set; }
    /// <summary>Total time the writer spent inside pipe writes beyond the 5 ms it takes to move 10 ms of audio.</summary>
    public double WriteStallMs => Interlocked.Read(ref _writeStallTicks) * 1000.0 / Stopwatch.Frequency;

    public PcmLegPump(IWaveProvider source16, Stream output, Func<long, double> elapsedAt, Func<double> backlogMs, Action discardBacklog, Func<bool> isPaused)
    {
        _source = source16;
        _output = output;
        _elapsedAt = elapsedAt;
        _backlogMs = backlogMs;
        _discardBacklog = discardBacklog;
        _isPaused = isPaused;
    }

    public void Start()
    {
        _writer = Task.Factory.StartNew(WriterLoopAsync, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        _reader = new Thread(ReaderLoop) { IsBackground = true, Name = "AudioPump-Reader", Priority = ThreadPriority.AboveNormal };
        _reader.Start();
    }

    /// <summary>Asks the reader to produce audio up to <paramref name="stopTimestamp"/> and finish.</summary>
    public void RequestStop(long stopTimestamp)
    {
        _stopElapsed = _elapsedAt(stopTimestamp);
        _stopRequested = true;
    }

    /// <summary>Waits for the reader to finish and the queue to drain into the pipe. Returns false on timeout.</summary>
    public bool WaitFinished(TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        if (_reader is { } r && !r.Join(timeout)) return false;
        var remaining = timeout - deadline.Elapsed;
        if (_writer is { } w) return w.Wait(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        return true;
    }

    /// <summary>Stops immediately without draining (failure or dispose paths).</summary>
    public void Abort()
    {
        _aborted = true;
        _stopRequested = true;
        _reader?.Join(1000);
        try { _writer?.Wait(1000); } catch { /* pipe already gone */ }
    }

    private void ReaderLoop()
    {
        try
        {
            // Silence for the interval between the video timeline starting and this pump starting (see
            // AudioCaptureService.TimelineStartTimestamp for why the two start at different moments).
            double startElapsed = Math.Max(0, _elapsedAt(Stopwatch.GetTimestamp()));
            long produced = (long)(startElapsed * Rate);
            LeadSilenceSeconds = startElapsed;
            EnqueueSilence(produced);

            while (!_aborted)
            {
                bool stopping = _stopRequested;
                if (!stopping && _isPaused())
                {
                    // A pause freezes the clock, so nothing is owed; drain what the device captured meanwhile
                    // so it is not replayed on resume.
                    _discardBacklog();
                    Thread.Sleep(5);
                    continue;
                }

                double target = stopping ? _stopElapsed : _elapsedAt(Stopwatch.GetTimestamp());
                long owed = (long)(target * Rate) - produced;
                if (owed >= ChunkFrames || (stopping && owed > 0))
                {
                    owed = Math.Min(owed, Rate); // at most one second per pass
                    double needMs = owed * 1000.0 / Rate;
                    double backlog = _backlogMs();

                    if (!stopping && backlog - needMs > TrimAboveMs)
                    {
                        long dropFrames = (long)((backlog - needMs - TrimToMs) * Rate / 1000.0);
                        Discard(dropFrames);
                        StaleDroppedMs += dropFrames * 1000.0 / Rate;
                    }
                    else if (backlog + 5 < needMs)
                    {
                        UnderrunMs += needMs - backlog;
                    }

                    var buf = new byte[owed * FrameBytes];
                    int read = _source.Read(buf, 0, buf.Length);
                    if (read < buf.Length) Array.Clear(buf, Math.Max(0, read), buf.Length - Math.Max(0, read));
                    Enqueue(buf);
                    produced += owed;
                }

                if (stopping && produced >= (long)(_stopElapsed * Rate)) break;
                Thread.Sleep(4);
            }
        }
        finally
        {
            _queue.Writer.TryComplete();
        }
    }

    private void Discard(long frames)
    {
        var scratch = new byte[Math.Min(frames, Rate) * FrameBytes];
        while (frames > 0)
        {
            int n = (int)Math.Min(frames, scratch.Length / FrameBytes);
            _source.Read(scratch, 0, n * FrameBytes);
            frames -= n;
        }
    }

    private void EnqueueSilence(long frames)
    {
        while (frames > 0)
        {
            int n = (int)Math.Min(frames, Rate);
            Enqueue(new byte[n * FrameBytes]);
            frames -= n;
        }
    }

    private void Enqueue(byte[] chunk)
    {
        long queued = Interlocked.Add(ref _queuedBytes, chunk.Length);
        double ms = queued / (double)FrameBytes * 1000.0 / Rate;
        if (ms > MaxQueuedMs) MaxQueuedMs = ms;
        _queue.Writer.TryWrite(chunk);
    }

    private async Task WriterLoopAsync()
    {
        bool broken = false;
        await foreach (var chunk in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Interlocked.Add(ref _queuedBytes, -chunk.Length);
            if (broken || _aborted) continue;
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                await _output.WriteAsync(chunk).ConfigureAwait(false);
            }
            catch (IOException) { broken = true; continue; }
            catch (ObjectDisposedException) { broken = true; continue; }

            long dt = Stopwatch.GetTimestamp() - t0;
            if (dt > Stopwatch.Frequency / 200) Interlocked.Add(ref _writeStallTicks, dt - Stopwatch.Frequency / 200);
            Interlocked.Add(ref _writtenBytes, chunk.Length);
            if (Volatile.Read(ref _firstWriteTimestamp) == 0) Interlocked.CompareExchange(ref _firstWriteTimestamp, Stopwatch.GetTimestamp(), 0);
        }
    }
}
