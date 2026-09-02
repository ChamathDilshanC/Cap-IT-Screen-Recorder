using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    public static string BuildPaletteGenArguments(string inputPath, TimeSpan start, TimeSpan duration, string palettePath) =>
        $"-y -hide_banner -loglevel warning -stats -i \"{inputPath}\" -ss {FormatTime(start)} -t {FormatTime(duration)} " +
        $"-vf \"fps={GifFrameRate},scale={GifWidth}:-2:flags=lanczos,palettegen=stats_mode=diff\" " +
        $"\"{palettePath}\"";

    /// <summary>
    /// Pass 2: re-applies the identical trim/fps/scale filter chain from pass 1 (it must match exactly —
    /// paletteuse dithers on a per-pixel basis against frames that need to line up with what palettegen
    /// actually analyzed) and dithers onto the pass-1 palette. <c>sierra2_4a</c> is a good general-purpose
    /// default: visibly less color banding than no dithering, less "static-y" noise than Floyd-Steinberg
    /// tends to produce on flat, UI-heavy tutorial content.
    /// </summary>
    public static string BuildPaletteUseArguments(string inputPath, TimeSpan start, TimeSpan duration, string palettePath, string outputGifPath) =>
        $"-y -hide_banner -loglevel warning -stats -i \"{inputPath}\" -ss {FormatTime(start)} -t {FormatTime(duration)} -i \"{palettePath}\" " +
        $"-filter_complex \"fps={GifFrameRate},scale={GifWidth}:-2:flags=lanczos[x];[x][1:v]paletteuse=dither=sierra2_4a\" " +
        $"-loop 0 \"{outputGifPath}\"";

    private static string FormatTime(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs both ffmpeg passes end to end and writes <paramref name="outputGifPath"/>. Reports combined
    /// 0-100 progress across both passes via <paramref name="progress"/> — see <see cref="GifExportProgress"/>.
    /// The scratch palette PNG is always deleted before returning, whether this succeeds, is canceled, or throws.
    /// </summary>
    public static async Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputGifPath,
        IProgress<GifExportProgress>? progress = null, CancellationToken ct = default)
    {
        var ffmpegPath = FFmpegLocator.FindFFmpeg()
            ?? throw new FileNotFoundException("ffmpeg.exe was not found. Place it in an 'ffmpeg' subfolder next to the app, or install it and add it to PATH.");

        var palettePath = Path.Combine(Path.GetTempPath(), $"capit_palette_{Guid.NewGuid():N}.png");
        try
        {
            const string paletteStage = "Generating palette…";
            progress?.Report(new GifExportProgress(paletteStage, 0));
            var pass1Args = BuildPaletteGenArguments(inputPath, start, duration, palettePath);
            await ExecuteFFmpegCommandAsync(ffmpegPath, pass1Args, duration,
                fraction => progress?.Report(new GifExportProgress(paletteStage, fraction * 50)), ct).ConfigureAwait(false);

            const string gifStage = "Encoding GIF…";
            progress?.Report(new GifExportProgress(gifStage, 50));
            var pass2Args = BuildPaletteUseArguments(inputPath, start, duration, palettePath, outputGifPath);
            await ExecuteFFmpegCommandAsync(ffmpegPath, pass2Args, duration,
                fraction => progress?.Report(new GifExportProgress(gifStage, 50 + fraction * 50)), ct).ConfigureAwait(false);

            progress?.Report(new GifExportProgress("Done", 100));
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
