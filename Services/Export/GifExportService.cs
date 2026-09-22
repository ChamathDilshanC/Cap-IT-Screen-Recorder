using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.Services.Export;

/// <summary>One stage of a two-pass GIF export, reported via <see cref="IProgress{T}"/>. <see cref="PercentComplete"/> spans the whole export (0-100), not just the current pass — palette generation is scaled to 0-50, GIF encoding to 50-100, so the number on screen only ever moves forward.</summary>
public readonly record struct GifExportProgress(string Stage, double PercentComplete);

/// <summary>
/// Builds the ffmpeg argument strings for a high-quality MP4-to-GIF export of a trimmed range (Phase 7).
/// Direct MP4-to-GIF conversion in a single ffmpeg pass looks noticeably banded/dithered because ffmpeg's
/// default GIF encoder falls back to a generic 256-color palette; this instead runs the standard two-pass
/// approach — <c>palettegen</c> builds an optimal palette for the exact clip first, then
/// <c>paletteuse</c> dithers the frames onto it — which is what gets GIF output close to the source
/// video's actual color fidelity.
/// </summary>
/// <remarks>
/// Step 1 built the argument-building logic; Step 2 (this revision) adds actually running it —
/// <see cref="ExportAsync"/> is the entry point, sharing <see cref="FFmpegLocator.FindFFmpeg"/> the same
/// way FFmpegEncoderService does for the live recording pipeline.
/// </remarks>
public static class GifExportService
{
    // Matches ffmpeg's periodic stderr status line, e.g. "...time=00:00:04.00 bitrate=...". This line is
    // controlled by -stats (on by default, independent of -loglevel), which the argument builders below
    // pass explicitly so progress reporting doesn't silently break if some ffmpeg build's default ever
    // changes — the whole reason -loglevel warning doesn't also suppress it.
    private static readonly Regex TimeRegex = new(@"time=(\d+):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);

    /// <summary>Frames per second for the exported GIF. 12 reads smoothly for tutorial-style screen motion (cursor moves, UI transitions) without the frame count — and file size — ballooning the way a full 30/60fps GIF would.</summary>
    public const int GifFrameRate = 12;

    /// <summary>Output width in pixels; height is derived to preserve the source's aspect ratio. -2 (not -1) keeps the derived height even, matching FFmpegEncoderService.BuildArguments' own scale-filter convention — odd dimensions break some pixel formats/players.</summary>
    public const int GifWidth = 720;

    /// <summary>
    /// Pass 1: analyzes the trimmed range and writes an optimal up-to-256-color palette to <paramref name="palettePath"/> (a PNG).
    /// </summary>
    /// <remarks>
    /// <paramref name="start"/>/<paramref name="duration"/> are applied via <c>-ss</c>/<c>-t</c> placed
    /// AFTER <c>-i</c> rather than before it. Seeking before <c>-i</c> is faster but snaps to the nearest
    /// preceding keyframe — fine for scrubbing, but our own recordings have no fixed keyframe interval
    /// (FFmpegEncoderService.BuildEncoderTuning sets no explicit <c>-g</c>), so a fast seek could silently
    /// start the export several seconds before the point the user actually dragged the thumb to. Seeking
    /// after <c>-i</c> forces a full decode from the start of the file, which costs time on a long
    /// recording, but guarantees the exported clip starts exactly where it was trimmed — correctness over
    /// speed for a feature whose entire point is picking an exact moment.
    /// </remarks>
    public static string BuildPaletteGenArguments(string inputPath, TimeSpan start, TimeSpan duration, string palettePath,
        IReadOnlyList<ZoomRegion>? zoomRegions = null, PresentationSettings? presentation = null) =>
        $"-y -hide_banner -loglevel warning -stats -i \"{inputPath}\" {BackgroundInput(presentation)}{WatermarkInput(presentation)}-ss {FormatTime(start)} -t {FormatTime(duration)} " +
        $"-filter_complex \"{BuildVideoFilter(duration, zoomRegions, start, presentation, UsesWatermark(presentation) ? (UsesImage(presentation) ? 2 : 1) : null)},fps={GifFrameRate},scale={GifWidth}:-2:flags=lanczos,palettegen=stats_mode=diff[palette]\" -map \"[palette]\" " +
        $"\"{palettePath}\"";

    /// <summary>
    /// Pass 2: re-applies the identical trim/fps/scale filter chain from pass 1 (it must match exactly —
    /// paletteuse dithers on a per-pixel basis against frames that need to line up with what palettegen
    /// actually analyzed) and dithers onto the pass-1 palette. <c>sierra2_4a</c> is a good general-purpose
    /// default: visibly less color banding than no dithering, less "static-y" noise than Floyd-Steinberg
    /// tends to produce on flat, UI-heavy tutorial content.
    /// </summary>
    public static string BuildPaletteUseArguments(string inputPath, TimeSpan start, TimeSpan duration, string palettePath, string outputGifPath,
        IReadOnlyList<ZoomRegion>? zoomRegions = null, PresentationSettings? presentation = null) =>
        $"-y -hide_banner -loglevel warning -stats -i \"{inputPath}\" {BackgroundInput(presentation)}-ss {FormatTime(start)} -t {FormatTime(duration)} -i \"{palettePath}\" {WatermarkInput(presentation)}" +
        $"-filter_complex \"{BuildVideoFilter(duration, zoomRegions, start, presentation, UsesWatermark(presentation) ? (UsesImage(presentation) ? 3 : 2) : null)},fps={GifFrameRate},scale={GifWidth}:-2:flags=lanczos[x];[x][{(UsesImage(presentation) ? 2 : 1)}:v]paletteuse=dither=sierra2_4a\" " +
        $"-loop 0 \"{outputGifPath}\"";

    private static string FormatTime(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    private static bool UsesImage(PresentationSettings? p) =>
        p is not null && p.BackgroundMode.Equals("image", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(p.BackgroundPath);
    private static string BackgroundInput(PresentationSettings? p) =>
        UsesImage(p) && !string.IsNullOrWhiteSpace(p?.BackgroundPath)
            ? $"-loop 1 -i \"{p.BackgroundPath}\" " : string.Empty;
    private static bool UsesWatermark(PresentationSettings? p) =>
        p?.WatermarkEnabled == true && File.Exists(p.WatermarkPath);
    private static string WatermarkInput(PresentationSettings? p) =>
        UsesWatermark(p) ? $"-loop 1 -i \"{p!.WatermarkPath}\" " : string.Empty;

    private static string BuildVideoFilter(TimeSpan duration, IReadOnlyList<ZoomRegion>? regions, TimeSpan trimStart, PresentationSettings? presentation = null, int? watermarkInput = null)
    {
        var active = (regions ?? []).Where(r => r.Enabled && r.EndSeconds > trimStart.TotalSeconds &&
                r.StartSeconds < trimStart.TotalSeconds + duration.TotalSeconds)
            .Select(r => new ZoomRegion
            {
                StartSeconds = r.StartSeconds - trimStart.TotalSeconds,
                EndSeconds = r.EndSeconds - trimStart.TotalSeconds,
                CenterX = r.CenterX, CenterY = r.CenterY, Scale = r.Scale, Enabled = true
            }).OrderBy(r => r.StartSeconds).ToList();
        presentation ??= new PresentationSettings { BackgroundMode = "solid" };
        var (canvasW, canvasH) = presentation.CanvasSize;
        var videoW = Math.Max(2, (int)(canvasW * Math.Clamp(presentation.VideoScale, .2, 1) / 2) * 2);
        var videoH = Math.Max(2, (int)(canvasH * Math.Clamp(presentation.VideoScale, .2, 1) / 2) * 2);
        var compose = $"[video]scale={videoW}:{videoH}:force_original_aspect_ratio=decrease,pad={videoW}:{videoH}:(ow-iw)/2:(oh-ih)/2:color=black,format=rgba" +
                      (presentation.DeviceFrame ? ",drawbox=x=0:y=0:w=iw-1:h=ih-1:color=white@0.75:t=3" : string.Empty) + "[fg];" +
                      (presentation.BackgroundMode == "solid"
                        ? $"color=c={presentation.BackgroundColor}:s={canvasW}x{canvasH}:d=1[bg];"
                        : presentation.BackgroundMode == "gradient"
                            ? $"gradients=s={canvasW}x{canvasH}:c0={presentation.BackgroundColor}:c1={presentation.BackgroundColor2}:x0=0:y0=0:x1=W:y1=H[bg];"
                            : $"[1:v]scale={canvasW}:{canvasH}:force_original_aspect_ratio=increase,crop={canvasW}:{canvasH}[bg];") +
                      $"[bg][fg]overlay=(W-w)/2:(H-h)/2:format=auto[composed];" +
                      (watermarkInput is int wm
                        ? $"[{wm}:v]format=rgba,colorchannelmixer=aa={Math.Clamp(presentation.WatermarkOpacity, 0, 1).ToString(CultureInfo.InvariantCulture)},scale=iw*{Math.Clamp(presentation.WatermarkScale, .03, .5).ToString(CultureInfo.InvariantCulture)}:-1[wm];[composed][wm]overlay=W-w-24:H-h-24:format=auto,format=yuv420p"
                        : "[composed]format=yuv420p");
        if (active.Count == 0) return $"[0:v]setpts=PTS-STARTPTS[video];{compose}";
        var end = duration.TotalSeconds;
        var points = new SortedSet<double> { 0, end };
        foreach (var r in active) { points.Add(Math.Clamp(r.StartSeconds, 0, end)); points.Add(Math.Clamp(r.EndSeconds, 0, end)); }
        var p = points.OrderBy(x => x).ToArray();
        var segments = new List<(double Start, double End, ZoomRegion? Region)>();
        for (var i = 0; i < p.Length - 1; i++)
        {
            if (p[i + 1] - p[i] < .001) continue;
            var mid = (p[i] + p[i + 1]) / 2;
            segments.Add((p[i], p[i + 1], active.LastOrDefault(r => mid >= r.StartSeconds && mid <= r.EndSeconds)));
        }
        var graph = $"[0:v]split={segments.Count}" + string.Concat(Enumerable.Range(0, segments.Count).Select(i => $"[g{i}]")) + ";";
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var chain = $"[g{i}]trim=start={F(s.Start)}:end={F(s.End)},setpts=PTS-STARTPTS";
            if (s.Region is { } r)
            {
                var z = Math.Clamp(r.Scale, 1, 4).ToString("0.###", CultureInfo.InvariantCulture);
                var x = Math.Clamp(r.CenterX, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
                var y = Math.Clamp(r.CenterY, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
                var targetAspect = F((double)canvasW / canvasH);
                chain += $",crop='min(iw/{z},ih/{z}*{targetAspect})':'min(ih/{z},iw/{z}/{targetAspect})':(iw-ow)*{x}:(ih-oh)*{y}";
            }
            graph += chain + $"[h{i}];";
        }
        return graph + string.Concat(Enumerable.Range(0, segments.Count).Select(i => $"[h{i}]")) +
               $"concat=n={segments.Count}:v=1:a=0,format=yuv420p[video];{compose}";
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs both ffmpeg passes end to end and writes <paramref name="outputGifPath"/>. Reports combined
    /// 0-100 progress across both passes via <paramref name="progress"/> — see <see cref="GifExportProgress"/>.
    /// The scratch palette PNG is always deleted before returning, whether this succeeds, is canceled, or throws.
    /// </summary>
    public static async Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputGifPath,
        IProgress<GifExportProgress>? progress = null, CancellationToken ct = default,
        IReadOnlyList<ZoomRegion>? zoomRegions = null, RecordingMetadata? metadata = null,
        PresentationSettings? presentation = null)
    {
        var ffmpegPath = FFmpegLocator.FindFFmpeg()
            ?? throw new FileNotFoundException("ffmpeg.exe was not found. Place it in an 'ffmpeg' subfolder next to the app, or install it and add it to PATH.");

        var palettePath = Path.Combine(Path.GetTempPath(), $"capit_palette_{Guid.NewGuid():N}.png");
        try
        {
            const string paletteStage = "Generating palette…";
            progress?.Report(new GifExportProgress(paletteStage, 0));
            var pass1Args = BuildPaletteGenArguments(inputPath, start, duration, palettePath, zoomRegions, presentation);
            await ExecuteFFmpegCommandAsync(ffmpegPath, pass1Args, duration,
                fraction => progress?.Report(new GifExportProgress(paletteStage, fraction * 50)), ct).ConfigureAwait(false);

            const string gifStage = "Encoding GIF…";
            progress?.Report(new GifExportProgress(gifStage, 50));
            var pass2Args = BuildPaletteUseArguments(inputPath, start, duration, palettePath, outputGifPath, zoomRegions, presentation);
            await ExecuteFFmpegCommandAsync(ffmpegPath, pass2Args, duration,
                fraction => progress?.Report(new GifExportProgress(gifStage, 50 + fraction * 50)), ct).ConfigureAwait(false);

            progress?.Report(new GifExportProgress("Done", 100));
            if (metadata is not null)
                metadata.Save(outputGifPath);
        }
        finally
        {
            // The palette is scratch data with no value to the user even if export failed partway
            // through, so cleanup is unconditional — success, cancellation, or exception all delete it.
            try { File.Delete(palettePath); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Runs one ffmpeg pass hidden, parsing its stderr for <c>time=</c> stats to report fractional
    /// (0.0-1.0) progress against <paramref name="expectedDuration"/> as it goes, and throws if it exits
    /// non-zero (including the captured stderr log, the same "attach ffmpeg's own explanation" convention
    /// FFmpegEncoderService's LastLog already follows for the live recording pipeline).
    /// </summary>
    private static async Task ExecuteFFmpegCommandAsync(string ffmpegPath, string arguments, TimeSpan expectedDuration,
        Action<double> onPassFraction, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var errorLog = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (errorLog) errorLog.AppendLine(e.Data);

            var match = TimeRegex.Match(e.Data);
            if (!match.Success) return;

            var elapsedSeconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
                + int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) / 100.0;

            var fraction = expectedDuration.TotalSeconds > 0
                ? Math.Clamp(elapsedSeconds / expectedDuration.TotalSeconds, 0, 1)
                : 0;
            onPassFraction(fraction);
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string log;
            lock (errorLog) log = errorLog.ToString();
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}. Output:\n{log}");
        }

        // Guarantees the pass visibly reaches 100% even if the final stats line was missed or rounded
        // down — otherwise a fast palette pass could leave the bar stuck at e.g. 47% for a moment before
        // the stage label flips, which reads as a stall.
        onPassFraction(1.0);
    }
}
