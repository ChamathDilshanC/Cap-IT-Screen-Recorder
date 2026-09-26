using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.Services.Export;

public static class TimelineThumbnailService
{
    public static async Task<string?> CreateAsync(string input, double duration, CancellationToken ct)
    {
        var ffmpeg = FFmpegLocator.FindFFmpeg(); if (ffmpeg is null) return null;
        var folder = Path.Combine(Path.GetTempPath(), "CapIT-thumbnails-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var output = Path.Combine(Path.GetTempPath(), "CapIT-timeline-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            for (var i = 0; i < 8; i++)
            {
                ct.ThrowIfCancellationRequested();
                var time = Math.Max(0, duration * (i + .25) / 8);
                await CompositionExportService.RunAsync(ffmpeg, ["-y", "-loglevel", "error", "-ss", time.ToString("0.###", CultureInfo.InvariantCulture), "-i", input,
                    "-frames:v", "1", "-vf", "scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2", Path.Combine(folder, i + ".png")], TimeSpan.FromSeconds(1), null, ct);
            }
            await Task.Run(() =>
            {
                using var strip = new Bitmap(1280, 90); using var g = Graphics.FromImage(strip);
                for (var i = 0; i < 8; i++) { ct.ThrowIfCancellationRequested(); using var frame = Image.FromFile(Path.Combine(folder, i + ".png")); g.DrawImageUnscaled(frame, i * 160, 0); }
                strip.Save(output, ImageFormat.Png);
            }, ct);
            return output;
        }
        catch { try { File.Delete(output); } catch { } throw; }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
}
