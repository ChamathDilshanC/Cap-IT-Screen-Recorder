using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ScreenRecorderApp.Services.Encoding;

/// <summary>
/// Reads a media file's real duration by asking the bundled <c>ffmpeg.exe</c>.
/// </summary>
/// <remarks>
/// Exists because <c>MediaPlayer.PlaybackSession.NaturalDuration</c> cannot be trusted for this app's own
/// output. Recordings are written as <em>fragmented</em> MP4 (deliberately — see
/// FFmpegEncoderService, it avoids the expensive `+faststart` rewrite-on-stop that used to leave large
/// recordings unplayable), and a fragmented MP4 carries no overall duration in its `mvhd` header. Media
/// Foundation therefore reports 0, which left the trim window's range slider pinned at zero and made
/// Trim and GIF Export unusable on every recording the app produced.
///
/// ffmpeg parses the fragment index itself and reports the true duration, so it is the authority here.
/// Probing costs one short-lived process per opened file, which is nothing next to the export that
/// usually follows it.
/// </remarks>
public static class MediaDurationProbe
{
    // ffmpeg prints e.g. "  Duration: 00:01:16.28, start: 0.000000, bitrate: 212 kb/s" to stderr.
    private static readonly Regex DurationPattern =
        new(@"Duration:\s*(\d+):(\d{2}):(\d{2})\.(\d+)", RegexOptions.Compiled);

    /// <summary>Returns the file's duration, or null if ffmpeg isn't available, the probe failed, or the output couldn't be parsed. Never throws.</summary>
    public static async Task<TimeSpan?> TryGetDurationAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var ffmpeg = FFmpegLocator.FindFFmpeg();
        if (ffmpeg is null) return null;

        try
        {
            var startInfo = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(filePath);

            using var process = Process.Start(startInfo);
            if (process is null) return null;

            // "-i" with no output file makes ffmpeg print the stream summary and exit non-zero
            // ("At least one output file must be specified"). The non-zero exit is expected and
            // irrelevant — the duration line we want is already on stderr by then.
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var match = DurationPattern.Match(stderr);
            if (!match.Success) return null;

            var hours = int.Parse(match.Groups[1].Value);
            var minutes = int.Parse(match.Groups[2].Value);
            var seconds = int.Parse(match.Groups[3].Value);
            // The fractional group is however many digits ffmpeg printed (usually 2), so scale by its width.
            var fractionText = match.Groups[4].Value;
            var fraction = double.Parse("0." + fractionText, System.Globalization.CultureInfo.InvariantCulture);

            var duration = new TimeSpan(0, hours, minutes, seconds) + TimeSpan.FromSeconds(fraction);
            return duration > TimeSpan.Zero ? duration : null;
        }
        catch
        {
            // Best effort: the caller falls back to whatever the media player reports.
            return null;
        }
    }
}
