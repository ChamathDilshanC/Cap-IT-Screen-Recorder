using System.IO.Compression;
using System.Net.Http;

namespace ScreenRecorderApp.Services.Encoding;

/// <summary>
/// Downloads a static Windows ffmpeg build and installs just ffmpeg.exe into the app's local "ffmpeg"
/// folder (the first place <see cref="FFmpegLocator.FindFFmpeg"/> looks), so a user who hits "Start
/// Recording" without ffmpeg already in place can get going without manually downloading/extracting
/// anything themselves.
/// </summary>
public static class FFmpegDownloader
{
    // gyan.dev's "release-essentials" build lives at a fixed URL that's updated in place rather than
    // versioned per-release — the same one commonly pointed to by other Windows tools (e.g. yt-dlp's
    // docs) for a no-account, no-API static ffmpeg.exe download.
    private const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    /// <summary>Downloads and extracts ffmpeg.exe, reporting 0-100 via <paramref name="progress"/>. Throws on failure/cancellation.</summary>
    public static async Task DownloadAndInstallAsync(IProgress<double> progress, CancellationToken ct = default)
    {
        var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        Directory.CreateDirectory(ffmpegDir);
        var targetPath = Path.Combine(ffmpegDir, "ffmpeg.exe");

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"capit_ffmpeg_{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long readSoFar = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    readSoFar += read;
                    if (totalBytes is > 0)
                    {
                        // Download is treated as 0-95%; the remaining 5% covers locating/extracting the
                        // single ffmpeg.exe entry from the archive below.
                        progress.Report(Math.Min(95.0, readSoFar * 95.0 / totalBytes.Value));
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            using (var archive = ZipFile.OpenRead(tempZipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("The downloaded archive did not contain ffmpeg.exe.");

                var tempExePath = targetPath + ".download";
                entry.ExtractToFile(tempExePath, overwrite: true);
                File.Move(tempExePath, targetPath, overwrite: true);
            }

            progress.Report(100);
        }
        finally
        {
            try { File.Delete(tempZipPath); } catch { /* best effort */ }
        }
    }
}
