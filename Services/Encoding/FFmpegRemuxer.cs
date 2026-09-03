using System.Diagnostics;

namespace ScreenRecorderApp.Services.Encoding;

/// <summary>
/// Rewrites a finished recording into a normal, front-loaded-<c>moov</c> MP4 with a stream copy (no
/// re-encode).
/// </summary>
/// <remarks>
/// <see cref="FFmpegEncoderService"/> records to <em>fragmented</em> MP4 on purpose — an
/// <c>empty_moov</c> plus a <c>moof</c>/<c>mdat</c> pair per GOP means a crash or a forced kill
/// mid-recording still leaves a file that's valid up to the last flushed fragment, with none of the
/// expensive rewrite-on-stop that <c>+faststart</c> would force during capture. The cost is that
/// Windows Media Foundation (which backs <see cref="Windows.Media.Playback.MediaPlayer"/> in the
/// Review &amp; Export window) can't reliably decode an <c>empty_moov</c> fragmented MP4 — it already
/// can't read its duration (see <see cref="MediaDurationProbe"/>) and often fails the video track
/// outright with "Video could not be decoded".
///
/// So once recording has actually stopped and there's time to spare, the fragmented file is remuxed
/// here into a conventional indexed MP4: <c>-c copy</c> makes this an I/O-bound container rewrite
/// (seconds, even for a multi-GB file), and a kill mid-remux just leaves the already-valid fragmented
/// source untouched — strictly safer than writing <c>+faststart</c> during capture.
/// </remarks>
public static class FFmpegRemuxer
{
    /// <summary>
    /// Stream-copies <paramref name="inputPath"/> into <paramref name="outputPath"/> as a faststart
    /// MP4. Returns true only if ffmpeg exited cleanly and the output exists with real content.
    /// Never throws.
    /// </summary>
    public static async Task<bool> ToFaststartAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        var ffmpeg = FFmpegLocator.FindFFmpeg();
        if (ffmpeg is null) return false;

        try
        {
            var startInfo = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");
            startInfo.ArgumentList.Add("-movflags");
            startInfo.ArgumentList.Add("+faststart");
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo);
            if (process is null) return false;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));

            // Drain stderr so the pipe buffer never fills and stalls ffmpeg on a large file.
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
                await stderrTask;
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                try { await stderrTask; } catch { /* observe it so it's not an unhandled task exception */ }
                return false;
            }

            if (process.ExitCode != 0) return false;

            var info = new FileInfo(outputPath);
            return info.Exists && info.Length > 0;
        }
        catch
        {
            // Best effort: the caller falls back to the fragmented source file.
            return false;
        }
    }
}
