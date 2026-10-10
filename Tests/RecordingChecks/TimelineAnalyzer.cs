using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RecordingChecks;

internal sealed record TimelineReport(
    string File, int Width, int Height, double Fps,
    int Frames, int Undecodable, int Torn,
    int Duplicates, int Gaps, double MaxGapMs, double MaxJitterMs,
    double SourceSpanMs, double OutputSpanMs, double EndDriftMs, double MaxAbsDriftMs,
    int ContainerPtsGaps, double ContainerDurationSec, double VideoStreamSec, double AudioStreamSec,
    List<string> Events, List<long?> Codes, AudioReport? Audio = null)
{
    private string StepHistogram()
    {
        string[] names = ["0", "<=8", "<=25", "<=42", "<=58", "<=75", ">75"];
        var bins = new int[names.Length];
        for (int i = 1; i < Codes.Count; i++)
        {
            if (Codes[i] is not long a || Codes[i - 1] is not long b) continue;
            double step = a - b;
            int bin = step <= 0.5 ? 0 : step <= 8 ? 1 : step <= 25 ? 2 : step <= 42 ? 3 : step <= 58 ? 4 : step <= 75 ? 5 : 6;
            bins[bin]++;
        }
        return string.Join("  ", names.Select((n, i) => $"{n}:{bins[i]}"));
    }

    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"file                    : {Path.GetFileName(File)}  {Width}x{Height} @ {Fps:F2} fps");
        sb.AppendLine($"frames                  : {Frames}   undecodable: {Undecodable}   torn (top/bottom code disagree): {Torn}");
        sb.AppendLine($"source time covered     : {SourceSpanMs / 1000:F3} s   output timeline: {OutputSpanMs / 1000:F3} s   end drift: {EndDriftMs:+0.0;-0.0} ms   max |drift|: {MaxAbsDriftMs:F1} ms");
        sb.AppendLine($"timeline jumps (>=2 periods of source time in one output frame): {Gaps}   worst step: {MaxGapMs:F1} ms");
        sb.AppendLine($"duplicate frames (same instant shown twice): {Duplicates}");
        sb.AppendLine($"container pts gaps      : {ContainerPtsGaps}   container {ContainerDurationSec:F3} s   video stream {VideoStreamSec:F3} s   audio stream {(AudioStreamSec > 0 ? AudioStreamSec.ToString("F3") + " s" : "n/a")}");
        sb.AppendLine("source-time step histogram (ms between consecutive output frames): " + StepHistogram());
        if (Audio is { } a) sb.AppendLine(a.Format());
        foreach (var e in Events.Take(12)) sb.AppendLine("  · " + e);
        if (Events.Count > 12) sb.AppendLine($"  · ... {Events.Count - 12} more");
        return sb.ToString();
    }
}

internal static class TimelineAnalyzer
{
    public static TimelineReport Analyze(string ffmpeg, string file, long audioAnchorMs = -1, double calibMs = double.NaN)
    {
        var (w, h, fps) = Probe(ffmpeg, file);
        double periodMs = 1000.0 / fps;

        var codes = DecodeAll(ffmpeg, file, w, h, out int torn);
        var events = new List<string>();

        int undecodable = 0, duplicates = 0, gaps = 0;
        double maxGap = 0, maxJitter = 0, maxAbsDrift = 0, endDrift = 0;
        long? firstCode = null; int firstIndex = -1;
        long? prev = null; int prevIndex = -1;
        double sourceSpan = 0, outputSpan = 0;

        for (int k = 0; k < codes.Count; k++)
        {
            if (codes[k] is not long code) { undecodable++; continue; }

            if (firstCode is null) { firstCode = code; firstIndex = k; }
            double t = (double)(code - firstCode.Value);
            double expected = (k - firstIndex) * periodMs;
            double drift = t - expected;
            maxAbsDrift = Math.Max(maxAbsDrift, Math.Abs(drift));
            endDrift = drift;
            sourceSpan = t;
            outputSpan = expected;

            if (prev is long p)
            {
                // Only judge consecutive decodable frames — a run of undecodable frames in between makes the
                // per-frame step ambiguous, and those are reported on their own.
                if (k - prevIndex == 1)
                {
                    double step = code - p;
                    maxJitter = Math.Max(maxJitter, Math.Abs(step - periodMs));
                    if (step <= 0.5) duplicates++;
                    else if (step >= periodMs * 1.9 + 2)
                    {
                        gaps++;
                        maxGap = Math.Max(maxGap, step);
                        events.Add($"jump at frame {k} (t={k * periodMs / 1000:F3}s): source advanced {step:F1} ms in one {periodMs:F1} ms output frame");
                    }
                }
            }
            prev = code; prevIndex = k;
        }

        var (ptsGaps, containerSec, videoSec, audioSec) = ContainerTiming(ffmpeg, file, fps);

        var audio = audioSec > 0 ? AudioAnalyzer.Analyze(ffmpeg, file, codes, fps, audioAnchorMs, videoSec, calibMs) : null;
        return new TimelineReport(file, w, h, fps, codes.Count, undecodable, torn, duplicates, gaps, maxGap, maxJitter,
            sourceSpan, outputSpan, endDrift, maxAbsDrift, ptsGaps, containerSec, videoSec, audioSec, events, codes, audio);
    }

    private static (int W, int H, double Fps) Probe(string ffmpeg, string file)
    {
        string err = RunCapture(ffmpeg, $"-hide_banner -i \"{file}\"", out _);
        var m = Regex.Match(err, @"Video:.*?, (\d{2,5})x(\d{2,5})");
        var f = Regex.Match(err, @"(\d+(?:\.\d+)?) fps");
        if (!m.Success) throw new InvalidOperationException("Could not probe video stream:\n" + err);
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            f.Success ? double.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture) : 30);
    }

    private static List<long?> DecodeAll(string ffmpeg, string file, int w, int h, out int torn)
    {
        torn = 0;
        var result = new List<long?>();
        var psi = new ProcessStartInfo(ffmpeg, $"-v error -i \"{file}\" -map 0:v:0 -vf format=gray -f rawvideo -pix_fmt gray -")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();

        var frame = new byte[w * h];
        var stream = p.StandardOutput.BaseStream;
        // Five rows spread over the strip's height. A frame assembled from two different instants (a torn
        // hand-off between the compositor and the pacer) shows different codes in different rows.
        int[] rows = [h / 2 - 180, h / 2 - 90, h / 2, h / 2 + 90, h / 2 + 180];
        var decoded = new long?[rows.Length];
        while (true)
        {
            int read = 0;
            while (read < frame.Length)
            {
                int n = stream.Read(frame, read, frame.Length - read);
                if (n <= 0) break;
                read += n;
            }
            if (read < frame.Length) break;

            long? first = null; bool differs = false;
            for (int i = 0; i < rows.Length; i++)
            {
                decoded[i] = TimelineCode.DecodeRow(frame, rows[i] * w, w);
                if (decoded[i] is long v)
                {
                    if (first is null) first = v;
                    else if (first != v) differs = true;
                }
            }
            if (differs) torn++;
            result.Add(decoded[2] ?? first);
        }
        p.WaitForExit();
        return result;
    }

    private static (int PtsGaps, double Container, double Video, double Audio) ContainerTiming(string ffmpeg, string file, double fps)
    {
        // showinfo prints every frame's pts_time; in a CFR file they step by exactly 1/fps, so any larger
        // step is a hole in the container timeline (as opposed to a hole in the *content*, which the
        // decoded clock above catches).
        string err = RunCapture(ffmpeg, $"-hide_banner -i \"{file}\" -map 0:v:0 -vf showinfo -f null -", out _);
        double period = 1.0 / fps;
        double? last = null;
        int gaps = 0;
        foreach (Match m in Regex.Matches(err, @"pts_time:(-?\d+(?:\.\d+)?)"))
        {
            double t = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (last is double l && t - l > period * 1.5) gaps++;
            last = t;
        }

        double container = ParseDuration(RunCapture(ffmpeg, $"-hide_banner -i \"{file}\"", out _));
        double video = LastTime(RunCapture(ffmpeg, $"-hide_banner -i \"{file}\" -map 0:v:0 -c copy -f null -", out _));
        string audioErr = RunCapture(ffmpeg, $"-hide_banner -i \"{file}\" -map 0:a:0? -c copy -f null -", out _);
        double audio = audioErr.Contains("Audio:") ? LastTime(audioErr) : 0;
        return (gaps, container, video, audio);
    }

    private static double ParseDuration(string err)
    {
        var m = Regex.Match(err, @"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
        return m.Success
            ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
            : 0;
    }

    private static double LastTime(string err)
    {
        var matches = Regex.Matches(err, @"time=(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (matches.Count == 0) return 0;
        var m = matches[^1];
        return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    internal static string RunCapture(string exe, string args, out int exitCode)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        p.WaitForExit();
        exitCode = p.ExitCode;
        return stderr.Result;
    }
}


internal sealed record AudioReport(double AudioSeconds, double VideoSeconds, int Beeps, double FirstOffsetMs, double LastOffsetMs, double DriftMs,
    double JitterMs, double MaxSpacingErrorMs, double LastBeepToEndMs, string Note, double CalibMs = double.NaN)
{
    /// <summary>Positive = the audio stream is shorter than the video stream (audio tail missing).</summary>
    public double TailMs => (VideoSeconds - AudioSeconds) * 1000;

    public string Format() =>
        $"audio                  : {AudioSeconds:F3} s of PCM vs {VideoSeconds:F3} s of video  =>  audio tail {TailMs:+0;-0} ms ({(TailMs > 0 ? "audio SHORTER" : "audio longer")})\n" +
        (double.IsNaN(CalibMs) ? "" : $"audio-chain latency (loopback calibration) : {CalibMs:+0;-0} ms  =>  corrected A/V offset  first {FirstOffsetMs - CalibMs:+0;-0} ms · last {LastOffsetMs - CalibMs:+0;-0} ms  (positive = audio later than picture; display latency of the picture, ~16–33 ms, is not subtracted)\n") +
        $"A/V sync (beeps)       : {Beeps} beeps  offset first {FirstOffsetMs:+0;-0} ms · last {LastOffsetMs:+0;-0} ms · drift {DriftMs:+0;-0} ms · jitter ±{JitterMs:F0} ms · max spacing error {MaxSpacingErrorMs:F0} ms  {Note}";
}

internal static class AudioAnalyzer
{
    private const int Rate = 48000;

    public static AudioReport Analyze(string ffmpeg, string file, List<long?> codes, double fps, long anchorMs, double videoStreamSec, double calibMs)
    {
        var psi = new ProcessStartInfo(ffmpeg, $"-v error -i \"{file}\" -map 0:a:0 -ac 1 -ar {Rate} -f f32le -")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var ms = new MemoryStream();
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        var bytes = ms.GetBuffer();
        int n = (int)(ms.Length / 4);
        var x = new float[n];
        Buffer.BlockCopy(bytes, 0, x, 0, n * 4);
        double audioSec = n / (double)Rate;
        double videoSec = videoStreamSec > 0 ? videoStreamSec : codes.Count / fps;

        // 4 ms RMS windows, onset = rising edge through 35 % of the loudest window, 600 ms refractory.
        const int win = Rate / 250;
        int windows = n / win;
        var rms = new double[windows];
        double max = 0;
        for (int w = 0; w < windows; w++)
        {
            double acc = 0;
            for (int i = w * win; i < (w + 1) * win; i++) acc += x[i] * x[i];
            rms[w] = Math.Sqrt(acc / win);
            max = Math.Max(max, rms[w]);
        }
        var onsets = new List<double>();
        if (max > 0.01)
        {
            double thr = max * 0.35;
            int lastOnsetW = -1000;
            for (int w = 1; w < windows; w++)
                if (rms[w] >= thr && rms[w - 1] < thr && w - lastOnsetW > 150) { onsets.Add(w * win / (double)Rate); lastOnsetW = w; }
        }

        // source-time <-> file-time maps from the decoded visual clock
        var vt = new List<double>(); var vs = new List<double>();
        for (int k = 0; k < codes.Count; k++) if (codes[k] is long c) { vt.Add(k / fps); vs.Add(c); }
        double SrcAtFile(double t)
        {
            int i = vt.BinarySearch(t); if (i < 0) i = ~i; i = Math.Clamp(i, 0, vt.Count - 1); return vs[i];
        }
        double FileAtSrc(double s)
        {
            int lo = 0, hi = vs.Count - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (vs[mid] < s) lo = mid + 1; else hi = mid; }
            if (lo == 0) return vt[0];
            double f = (s - vs[lo - 1]) / Math.Max(1e-9, vs[lo] - vs[lo - 1]);
            return vt[lo - 1] + f * (vt[lo] - vt[lo - 1]);
        }

        var offsets = new List<double>(); double maxSpacing = 0; double? prev = null;
        if (anchorMs >= 0 && vs.Count > 10)
        {
            foreach (double a in onsets)
            {
                double srcNow = SrcAtFile(a);
                long j = (long)Math.Round((srcNow - anchorMs) / 1000.0);
                double te = anchorMs + 1000.0 * j;
                if (te < vs[0] || te > vs[^1]) continue;
                offsets.Add((a - FileAtSrc(te)) * 1000);
                if (prev is double pr) maxSpacing = Math.Max(maxSpacing, Math.Abs((a - pr) * 1000 - 1000));
                prev = a;
            }
        }
        double first = offsets.Count > 0 ? offsets.Take(Math.Min(3, offsets.Count)).Average() : double.NaN;
        double last = offsets.Count > 0 ? offsets.TakeLast(Math.Min(3, offsets.Count)).Average() : double.NaN;
        double mean = offsets.Count > 0 ? offsets.Average() : double.NaN;
        double jitter = offsets.Count > 1 ? Math.Sqrt(offsets.Average(o => (o - mean) * (o - mean))) : 0;
        double lastBeepToEnd = onsets.Count > 0 ? (audioSec - onsets[^1]) * 1000 : double.NaN;
        string note = anchorMs < 0 ? "(no anchor: sync not evaluated)" : "(offset includes the constant output+loopback latency; compare runs, not the absolute value)";
        return new AudioReport(audioSec, videoSec, onsets.Count, first, last, last - first, jitter, maxSpacing, lastBeepToEnd, note, calibMs);
    }
}
