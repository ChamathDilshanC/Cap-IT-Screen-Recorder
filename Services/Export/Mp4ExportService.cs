using System.Diagnostics;
using System.Globalization;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.Services.Export;

/// <summary>Exports a trimmed recording with a background and lightweight presentation styling in FFmpeg.</summary>
public static class Mp4ExportService
{
    public static async Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputPath,
        string backgroundPath, double videoScale, double cornerRadius, IProgress<GifExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = FFmpegLocator.FindFFmpeg()
            ?? throw new FileNotFoundException("ffmpeg.exe was not found.");
        if (!File.Exists(backgroundPath))
            throw new FileNotFoundException("The selected background image was not found.", backgroundPath);

        const int width = 1920;
        const int height = 1080;
        var videoWidth = Math.Max(2, (int)(width * Math.Clamp(videoScale, .55, 1) / 2) * 2);
        var videoHeight = Math.Max(2, (int)(height * Math.Clamp(videoScale, .55, 1) / 2) * 2);
        var radius = Math.Clamp(cornerRadius, 0, Math.Min(videoWidth, videoHeight) / 2);
        var filter = BuildFilter(width, height, videoWidth, videoHeight, radius);
        var args = $"-y -hide_banner -loglevel warning -stats -ss {FormatTime(start)} -t {FormatTime(duration)} " +
                   $"-i \"{inputPath}\" -loop 1 -i \"{backgroundPath}\" " +
                   $"-filter_complex \"{filter}\" -map \"[outv]\" -map 0:a? -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{outputPath}\"";
        progress?.Report(new GifExportProgress("Exporting MP4…", 5));
        await RunAsync(ffmpeg, args, cancellationToken).ConfigureAwait(false);
        progress?.Report(new GifExportProgress("Done", 100));
    }

    private static string BuildFilter(int width, int height, int videoWidth, int videoHeight, double radius)
    {
        var radiusExpression = radius <= 0
            ? "255"
            : $"if(gt(abs(X-W/2),W/2-{radius.ToString(CultureInfo.InvariantCulture)})*gt(abs(Y-H/2),H/2-{radius.ToString(CultureInfo.InvariantCulture)}),if(lte((abs(X-W/2)-(W/2-{radius.ToString(CultureInfo.InvariantCulture)}))^2+(abs(Y-H/2)-(H/2-{radius.ToString(CultureInfo.InvariantCulture)}))^2,{radius.ToString(CultureInfo.InvariantCulture)}^2),255,0),255)";
        return $"[1:v]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1[bg];" +
               $"[0:v]scale={videoWidth}:{videoHeight}:force_original_aspect_ratio=decrease,pad={videoWidth}:{videoHeight}:(ow-iw)/2:(oh-ih)/2:color=black,format=rgba," +
               $"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='{radiusExpression}'[fg];" +
               $"[bg][fg]overlay=(W-w)/2:(H-h)/2:format=auto,format=yuv420p[outv]";
    }

    private static string FormatTime(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static async Task RunAsync(string executable, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable, Arguments = arguments, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        process.Start();
        try
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "FFmpeg MP4 export failed." : error.Trim());
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
