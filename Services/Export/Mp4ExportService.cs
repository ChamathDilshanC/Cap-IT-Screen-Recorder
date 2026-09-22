using System.Diagnostics;
using System.Globalization;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.Services.Export;

/// <summary>Exports a trimmed recording with a background and lightweight presentation styling in FFmpeg.</summary>
public static class Mp4ExportService
{
    public static async Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputPath,
        string backgroundPath, int canvasWidth, int canvasHeight, double videoScale, double cornerRadius,
        IReadOnlyList<ZoomRegion>? zoomRegions = null,
        IProgress<GifExportProgress>? progress = null,
        CancellationToken cancellationToken = default,
        RecordingMetadata? metadata = null, PresentationSettings? presentation = null)
    {
        var ffmpeg = FFmpegLocator.FindFFmpeg()
            ?? throw new FileNotFoundException("ffmpeg.exe was not found.");
        presentation ??= new PresentationSettings();
        presentation.BackgroundPath = string.IsNullOrWhiteSpace(presentation.BackgroundPath) ? backgroundPath : presentation.BackgroundPath;
        if (presentation.BackgroundMode.Equals("image", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(presentation.BackgroundPath))
            throw new FileNotFoundException("The selected background image was not found.", backgroundPath);

        var width = NormalizeDimension(canvasWidth);
        var height = NormalizeDimension(canvasHeight);
        var padding = Math.Clamp(presentation.Padding, 0, Math.Min(width, height) / 2 - 1);
        var videoWidth = Math.Max(2, (int)((width - padding * 2) * Math.Clamp(videoScale, .2, 1) / 2) * 2);
        var videoHeight = Math.Max(2, (int)((height - padding * 2) * Math.Clamp(videoScale, .2, 1) / 2) * 2);
        var radius = Math.Clamp(cornerRadius, 0, Math.Min(videoWidth, videoHeight) / 2);
        var filter = BuildFilter(width, height, videoWidth, videoHeight, radius, zoomRegions, duration, start, presentation);
        var args = $"-y -hide_banner -loglevel warning -stats -ss {FormatTime(start)} -t {FormatTime(duration)} " +
                   $"-i \"{inputPath}\" -loop 1 -i \"{presentation.BackgroundPath}\" " +
                   (presentation.WatermarkEnabled && File.Exists(presentation.WatermarkPath) ? $"-loop 1 -i \"{presentation.WatermarkPath}\" " : string.Empty) +
                   $"-filter_complex \"{filter}\" -map \"[outv]\" -map 0:a? -c:v libx264 -preset veryfast -crf 20 " +
                   $"-pix_fmt yuv420p -c:a aac -shortest {BuildMetadataArguments(metadata)} \"{outputPath}\"";
        progress?.Report(new GifExportProgress("Exporting MP4…", 5));
        await RunAsync(ffmpeg, args, cancellationToken).ConfigureAwait(false);
        if (metadata is not null)
        {
            try { metadata.Save(outputPath); } catch { /* export pixels are still valid without optional metadata */ }
        }
        progress?.Report(new GifExportProgress("Done", 100));
    }

    private static string BuildMetadataArguments(RecordingMetadata? metadata)
    {
        if (metadata?.Cursor is not { } cursor) return string.Empty;
        var style = cursor.Style.ToString();
        return $"-metadata comment=\"Cap-IT cursor: {style}, size {cursor.Size:0.##}x, " +
               $"smoothing {cursor.Smoothing:0.##}, idle-hide {cursor.HideWhenIdle}, " +
               $"click-emphasis {cursor.ClickEmphasis}, trail {cursor.Trail}\" " +
               $"-metadata capit_cursor_style=\"{style}\" -metadata capit_cursor_size=\"{cursor.Size:0.##}\"";
    }

    private static int NormalizeDimension(int value) => Math.Max(2, value / 2 * 2);

    private static string BuildFilter(int width, int height, int videoWidth, int videoHeight, double radius,
        IReadOnlyList<ZoomRegion>? zoomRegions, TimeSpan duration, TimeSpan trimStart, PresentationSettings presentation)
    {
        var radiusExpression = radius <= 0
            ? "255"
            : $"if(gt(abs(X-W/2),W/2-{radius.ToString(CultureInfo.InvariantCulture)})*gt(abs(Y-H/2),H/2-{radius.ToString(CultureInfo.InvariantCulture)}),if(lte((abs(X-W/2)-(W/2-{radius.ToString(CultureInfo.InvariantCulture)}))^2+(abs(Y-H/2)-(H/2-{radius.ToString(CultureInfo.InvariantCulture)}))^2,{radius.ToString(CultureInfo.InvariantCulture)}^2),255,0),255)";
        var source = BuildZoomSegments(videoWidth, videoHeight, zoomRegions, duration, trimStart);
        var background = presentation.BackgroundMode.ToLowerInvariant() switch
        {
            "solid" => $"color=c={presentation.BackgroundColor}:s={width}x{height}:r=30[bg];",
            "gradient" => $"gradients=s={width}x{height}:c0={presentation.BackgroundColor}:c1={presentation.BackgroundColor2}:x0=0:y0=0:x1=W:y1=H[bg];",
            _ => $"[1:v]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1[bg];"
        };
        var frame = $"[fg][mask]alphamerge[fgm];" +
                    (presentation.DeviceFrame ? $"[fgm]drawbox=x=0:y=0:w=iw-1:h=ih-1:color=white@0.75:t=3[fr];" : "[fgm]copy[fr];");
        var shadow = presentation.Shadow
            ? $"color=c=black@0.38:s={videoWidth + 18}x{videoHeight + 18}:d=1,format=rgba,boxblur=8:1,loop=loop=-1:size=1:start=0[shadow];[bg][shadow]overlay=(W-w)/2:(H-h)/2[bgshadow];"
            : "[bg]copy[bgshadow];";
        var watermark = presentation.WatermarkEnabled
            ? $"[2:v]format=rgba,colorchannelmixer=aa={Math.Clamp(presentation.WatermarkOpacity, 0, 1).ToString(CultureInfo.InvariantCulture)},scale=iw*{Math.Clamp(presentation.WatermarkScale, .03, .5).ToString(CultureInfo.InvariantCulture)}:-1[wm];[composed][wm]overlay=W-w-24:H-h-24:format=auto[outv]"
            : "[composed]copy[outv]";
        return background +
               source +
               $"[fg];" +
               // Build the rounded-corner mask once, then loop that single frame. Applying geq to the
               // video itself made export needlessly evaluate the same geometry for every pixel/frame.
               $"color=c=black:s={videoWidth}x{videoHeight}:d=1,trim=end_frame=1,format=gray,geq=lum='{radiusExpression}'," +
               $"loop=loop=-1:size=1:start=0,setpts=N/FRAME_RATE/TB[mask];" +
               frame + shadow +
               $"[bgshadow][fr]overlay=(W-w)/2:(H-h)/2:format=auto,format=rgba[composed];" +
               watermark;
    }

    private static string BuildZoomSegments(int videoWidth, int videoHeight,
        IReadOnlyList<ZoomRegion>? regions, TimeSpan duration, TimeSpan trimStart)
    {
        var active = (regions ?? []).Where(r => r.Enabled && r.EndSeconds > r.StartSeconds &&
                r.EndSeconds > trimStart.TotalSeconds && r.StartSeconds < trimStart.TotalSeconds + duration.TotalSeconds)
            .Select(r => new ZoomRegion
            {
                StartSeconds = r.StartSeconds - trimStart.TotalSeconds,
                EndSeconds = r.EndSeconds - trimStart.TotalSeconds,
                CenterX = r.CenterX, CenterY = r.CenterY, Scale = r.Scale, Enabled = true
            }).OrderBy(r => r.StartSeconds).ToList();
        if (active.Count == 0)
            return $"[0:v]scale={videoWidth}:{videoHeight}:force_original_aspect_ratio=decrease,pad={videoWidth}:{videoHeight}:(ow-iw)/2:(oh-ih)/2:color=black,format=rgba,setpts=PTS-STARTPTS";

        var end = duration.TotalSeconds;
        var boundaries = new SortedSet<double> { 0, end };
        foreach (var r in active)
        {
            boundaries.Add(Math.Clamp(r.StartSeconds, 0, end));
            boundaries.Add(Math.Clamp(r.EndSeconds, 0, end));
        }
        var points = boundaries.OrderBy(x => x).ToArray();
        var segments = new List<(double Start, double End, ZoomRegion? Region)>();
        for (var i = 0; i < points.Length - 1; i++)
        {
            if (points[i + 1] - points[i] < .001) continue;
            var mid = (points[i] + points[i + 1]) / 2;
            // Later regions win if a user accidentally overlaps two regions.
            var region = active.LastOrDefault(r => mid >= r.StartSeconds && mid <= r.EndSeconds);
            segments.Add((points[i], points[i + 1], region));
        }

        var graph = $"[0:v]split={segments.Count}" +
                    string.Concat(Enumerable.Range(0, segments.Count).Select(i => $"[z{i}]")) + ";";
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var trim = $"[z{i}]trim=start={F(s.Start)}:end={F(s.End)},setpts=PTS-STARTPTS";
            if (s.Region is { } r)
            {
                var scale = Math.Clamp(r.Scale, 1, 4).ToString("0.###", CultureInfo.InvariantCulture);
                var cx = Math.Clamp(r.CenterX, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
                var cy = Math.Clamp(r.CenterY, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
                var targetAspect = F((double)videoWidth / videoHeight);
                trim += $",crop='min(iw/{scale},ih/{scale}*{targetAspect})':'min(ih/{scale},iw/{scale}/{targetAspect})':" +
                        $"(iw-ow)*{cx}:(ih-oh)*{cy}";
            }
            trim += $",scale={videoWidth}:{videoHeight}:force_original_aspect_ratio=decrease,pad={videoWidth}:{videoHeight}:(ow-iw)/2:(oh-ih)/2:color=black,format=rgba[v{i}];";
            graph += trim;
        }
        graph += string.Concat(Enumerable.Range(0, segments.Count).Select(i => $"[v{i}]")) +
                 $"concat=n={segments.Count}:v=1:a=0,format=rgba,setpts=PTS-STARTPTS";
        return graph;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

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
