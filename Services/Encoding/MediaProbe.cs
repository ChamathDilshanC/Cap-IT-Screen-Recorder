using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ScreenRecorderApp.Services.Encoding;

/// <summary>What a single <c>ffmpeg -i</c> probe could read about a media file. Any field may be null.</summary>
/// <param name="Duration">Real duration, parsed from ffmpeg's own fragment-aware reading of the file.</param>
/// <param name="PixelFormat">e.g. <c>yuv420p</c>, <c>yuv444p</c>.</param>
/// <param name="Profile">The H.264 profile string, e.g. <c>High</c>, <c>High 4:4:4 Predictive</c>.</param>
public sealed record MediaProbeResult(TimeSpan? Duration, string? PixelFormat, string? Profile)
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public bool HasAudio { get; init; }
    /// <summary>
    /// Whether this file uses 4:4:4 chroma, which Windows' built-in H.264 decoder (Media Foundation)
    /// cannot handle — it tops out at High profile 4:2:0. Such a file is perfectly valid and plays in
    /// ffmpeg-based players like VLC, but fails to decode in this app's preview, in Movies &amp; TV and
    /// in Photos. Releases before 2.6.2 produced these when "Maximize text clarity" was on.
    /// </summary>
    public bool IsUndecodableByWindows =>
        (PixelFormat?.Contains("444", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Profile?.Contains("4:4:4", StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>
/// Reads what the bundled <c>ffmpeg.exe</c> can tell us about a media file, in one short-lived process.
/// </summary>
/// <remarks>
/// Exists because <c>MediaPlayer</c> cannot be trusted for this app's own output on either count.
///
/// <b>Duration</b> — recordings are written as <em>fragmented</em> MP4 (deliberately: see
/// FFmpegEncoderService, it avoids the expensive <c>+faststart</c> rewrite-on-stop that used to leave
/// large recordings unplayable), and a fragmented MP4 carries no overall duration in its <c>mvhd</c>
/// header. Media Foundation therefore reports 0, which left the trim window's range slider pinned at
/// zero and made Trim and GIF Export unusable on every recording the app produced. ffmpeg parses the
/// fragment index itself and reports the truth.
///
/// <b>Pixel format</b> — when preview playback fails, this is what distinguishes "the file is broken"
/// from "the file is fine, Windows just can't decode 4:4:4", which are the same opaque error as far as
/// <c>MediaPlayer.MediaFailed</c> is concerned. See <see cref="MediaProbeResult.IsUndecodableByWindows"/>.
/// </remarks>
public static class MediaProbe
{
    // ffmpeg prints e.g. "  Duration: 00:01:16.28, start: 0.000000, bitrate: 212 kb/s" to stderr.
    private static readonly Regex DurationPattern =
        new(@"Duration:\s*(\d+):(\d{2}):(\d{2})\.(\d+)", RegexOptions.Compiled);

    // ...and e.g. "Stream #0:0[0x1](und): Video: h264 (High 4:4:4 Predictive) (avc1 / 0x31637661),
    // yuv444p(progressive), 1920x1080, ...". The profile sits in the first parenthesised group after the
    // codec name; the pixel format is the token after the codec tag, optionally followed by its own
    // parenthesised qualifier ("(progressive)", "(tv, bt709)") which is deliberately not captured.
    private static readonly Regex VideoStreamPattern =
        new(@"Video:\s*\w+(?:\s*\(([^)]*)\))?[^,]*,\s*(\w+)", RegexOptions.Compiled);

    /// <summary>Probes the file. Returns a result whose fields are null where nothing could be read; never throws.</summary>
    public static async Task<MediaProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var stderr = await TryReadStreamSummaryAsync(filePath, cancellationToken);
        if (stderr is null) return new MediaProbeResult(null, null, null);

        var video = stderr.Split('\n').FirstOrDefault(line => line.Contains("Video:")) ?? "";
        var size = Regex.Match(video, @"\b(\d{2,5})x(\d{2,5})\b");
        var fps = Regex.Match(video, @"([\d.]+) fps");
        return new MediaProbeResult(ParseDuration(stderr), ParsePixelFormat(stderr), ParseProfile(stderr))
        {
            Width = size.Success ? int.Parse(size.Groups[1].Value) : 0,
            Height = size.Success ? int.Parse(size.Groups[2].Value) : 0,
            FrameRate = fps.Success ? double.Parse(fps.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            HasAudio = stderr.Contains("Audio:")
        };
    }

    /// <summary>Convenience wrapper for callers that only need the duration.</summary>
    public static async Task<TimeSpan?> TryGetDurationAsync(string filePath, CancellationToken cancellationToken = default)
        => (await ProbeAsync(filePath, cancellationToken)).Duration;

    /// <summary>
    /// Runs <c>ffmpeg -i &lt;file&gt;</c> with no output file and returns its stderr, or null if ffmpeg
    /// isn't available or the probe failed outright.
    /// </summary>
    private static async Task<string?> TryReadStreamSummaryAsync(string filePath, CancellationToken cancellationToken)
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
            using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });

            // "-i" with no output file makes ffmpeg print the stream summary and exit non-zero
            // ("At least one output file must be specified"). The non-zero exit is expected and
            // irrelevant — everything we want is already on stderr by then.
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return stderr;
        }
        catch
        {
            // Best effort: callers fall back to whatever the media player reports.
            return null;
        }
    }

    private static TimeSpan? ParseDuration(string stderr)
    {
        var match = DurationPattern.Match(stderr);
        if (!match.Success) return null;

        try
        {
            var hours = int.Parse(match.Groups[1].Value);
            var minutes = int.Parse(match.Groups[2].Value);
            var seconds = int.Parse(match.Groups[3].Value);
            // The fractional group is however many digits ffmpeg printed (usually 2), so parse it as a
            // decimal rather than assuming a fixed width.
            var fraction = double.Parse("0." + match.Groups[4].Value, CultureInfo.InvariantCulture);

            var duration = new TimeSpan(0, hours, minutes, seconds) + TimeSpan.FromSeconds(fraction);
            return duration > TimeSpan.Zero ? duration : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseProfile(string stderr)
    {
        var match = VideoStreamPattern.Match(stderr);
        var value = match.Success ? match.Groups[1].Value : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ParsePixelFormat(string stderr)
    {
        var match = VideoStreamPattern.Match(stderr);
        var value = match.Success ? match.Groups[2].Value : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
