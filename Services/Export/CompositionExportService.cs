using System.Diagnostics;
using System.Globalization;
using System.Text;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.Services.Export;

/// <summary>One composition graph for MP4 and both GIF passes. Source files are never overwritten.</summary>
public static class CompositionExportService
{
    public static async Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputPath,
        bool gif, PresentationSettings presentation, IReadOnlyList<ZoomRegion> regions, ExportSettings options,
        IProgress<GifExportProgress>? progress, CancellationToken ct, RecordingMetadata? metadata = null)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different file to keep the original recording safe.");
        var ffmpeg = FFmpegLocator.FindFFmpeg() ?? throw new FileNotFoundException("FFmpeg is not installed.");
        var p = presentation.Clone(); p.Normalize();
        var probe = await MediaProbe.ProbeAsync(inputPath, ct).ConfigureAwait(false);
        var sw = probe.Width > 0 ? probe.Width : metadata?.CaptureWidth ?? 1920;
        var sh = probe.Height > 0 ? probe.Height : metadata?.CaptureHeight ?? 1080;
        if (sw < 2 || sh < 2) throw new InvalidOperationException("The source video dimensions could not be read.");
        var fps = options.FrameRate switch { "24" => 24d, "30" => 30d, "60" => 60d, _ => probe.FrameRate > 0 ? probe.FrameRate : metadata?.Fps > 0 ? metadata.Fps : 30d };
        fps = Math.Clamp(fps, 1, 240);
        var folder = Path.Combine(Path.GetTempPath(), "CapIT-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, ".capit-" + Guid.NewGuid().ToString("N") + (gif ? ".gif" : ".mp4"));
        try
        {
            progress?.Report(new("Preparing composition…", 0));
            var assets = await Task.Run(() => CompositionAssetRenderer.Render(p, sw, sh, ct), ct).ConfigureAwait(false);
            var bg = Path.Combine(folder, "background.png"); var overlay = Path.Combine(folder, "overlay.png"); var mask = Path.Combine(folder, "mask.png");
            await File.WriteAllBytesAsync(bg, assets.Background, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(overlay, assets.Overlay, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(mask, assets.Mask, ct).ConfigureAwait(false);
            var filter = BuildFilter(assets.Layout, sw, sh, regions, start.TotalSeconds, duration.TotalSeconds, fps);
            List<string> Inputs() => ["-y", "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1",
                "-filter_complex_threads", "2", "-ss", F(start.TotalSeconds), "-t", F(duration.TotalSeconds), "-i", inputPath,
                "-loop", "1", "-framerate", F(fps), "-i", bg, "-loop", "1", "-framerate", F(fps), "-i", mask,
                "-loop", "1", "-framerate", F(fps), "-i", overlay];
            if (gif)
            {
                var palette = Path.Combine(folder, "palette.png");
                var tail = ";[composed]fps=12,scale=720:-2:flags=lanczos";
                var args = Inputs();
                args.AddRange(["-filter_complex", filter + tail + ",palettegen=stats_mode=diff[palette]", "-map", "[palette]", "-frames:v", "1", palette]);
                await RunAsync(ffmpeg, args, duration, v => progress?.Report(new("Building GIF palette…", 5 + v * 40)), ct).ConfigureAwait(false);
                args = Inputs(); args.AddRange(["-i", palette, "-filter_complex", filter + tail + "[small];[small][4:v]paletteuse=dither=sierra2_4a[out]",
                    "-map", "[out]", "-an", "-loop", "0", "-t", F(duration.TotalSeconds), partial]);
                await RunAsync(ffmpeg, args, duration, v => progress?.Report(new("Encoding GIF…", 45 + v * 54)), ct).ConfigureAwait(false);
            }
            else
            {
                var args = Inputs(); args.AddRange(["-filter_complex", filter, "-map", "[composed]"]);
                if (options.IncludeAudio) args.AddRange(["-map", "0:a?", "-c:a", "aac", "-b:a", "192k"]); else args.Add("-an");
                args.AddRange(options.Encoder switch
                {
                    "NVIDIA NVENC" => new[] { "-c:v", "h264_nvenc", "-cq", options.Crf.ToString(), "-b:v", "0" },
                    "Intel Quick Sync" => new[] { "-c:v", "h264_qsv", "-global_quality", options.Crf.ToString() },
                    "AMD AMF" => new[] { "-c:v", "h264_amf", "-rc", "cqp", "-qp_i", options.Crf.ToString(), "-qp_p", options.Crf.ToString() },
                    _ => new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", options.Crf.ToString() }
                });
                args.AddRange(["-pix_fmt", "yuv420p", "-movflags", "+faststart", "-t", F(duration.TotalSeconds), partial]);
                await RunAsync(ffmpeg, args, duration, v => progress?.Report(new("Exporting video…", 5 + v * 94)), ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(partial, outputPath, true);
            // The output already contains styling. Do not apply it a second time if reopened.
            var outputMetadata = new RecordingMetadata
            {
                CaptureWidth = gif ? 720 : assets.Layout.Width,
                CaptureHeight = gif ? CompositionLayout.Even(720d * assets.Layout.Height / assets.Layout.Width) : assets.Layout.Height,
                DurationSeconds = duration.TotalSeconds, Fps = gif ? 12 : (int)Math.Round(fps),
                Cursor = metadata?.Cursor.Clone() ?? new(), PresentationBaked = true,
                Presentation = new() { Padding = 0, VideoScale = 1, CornerRadius = 0, Shadow = false, BackgroundMode = "none" }
            };
            try
            {
                await outputMetadata.SaveAsync(outputPath).ConfigureAwait(false);
                if (File.Exists(ZoomRegionStore.GetPath(outputPath)))
                    await File.WriteAllTextAsync(ZoomRegionStore.GetPath(outputPath), "[]").ConfigureAwait(false);
            }
            catch { /* Completed pixels remain usable. */ }
            progress?.Report(new("Export complete", 100));
        }
        finally
        {
            try { File.Delete(partial); } catch { }
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    public static string BuildFilter(CompositionLayout layout, int sw, int sh, IReadOnlyList<ZoomRegion> regions,
        double start, double duration, double fps)
    {
        var boundaries = new SortedSet<double> { 0, duration };
        foreach (var region in regions.Where(r => r.Enabled && double.IsFinite(r.StartSeconds) && double.IsFinite(r.EndSeconds) && r.EndSeconds > r.StartSeconds))
        {
            boundaries.Add(Math.Clamp(region.StartSeconds - start, 0, duration));
            boundaries.Add(Math.Clamp(region.EndSeconds - start, 0, duration));
        }
        var points = boundaries.ToArray(); var count = points.Length - 1;
        var graph = new StringBuilder();
        graph.Append("[0:v]setpts=PTS-STARTPTS,split=").Append(count);
        for (var i = 0; i < count; i++) graph.Append($"[s{i}]");
        graph.Append(';');
        for (var i = 0; i < count; i++)
        {
            var zoom = CompositionLayout.ActiveZoom(regions, start + (points[i] + points[i + 1]) / 2);
            var crop = CompositionLayout.SourceCrop(sw, sh, layout.Video, zoom);
            graph.Append($"[s{i}]trim=start={F(points[i])}:end={F(points[i + 1])},setpts=PTS-STARTPTS,");
            graph.Append($"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}:exact=1,scale={layout.Video.Width}:{layout.Video.Height}:flags=lanczos,setsar=1,format=rgba[v{i}];");
        }
        for (var i = 0; i < count; i++) graph.Append($"[v{i}]");
        graph.Append($"concat=n={count}:v=1:a=0,fps={F(fps)}[video];[2:v]format=gray[mask];[video][mask]alphamerge=shortest=1[rounded];");
        graph.Append($"[1:v][rounded]overlay=x={layout.Video.X}:y={layout.Video.Y}:shortest=1:format=rgb[base];");
        graph.Append("[base][3:v]overlay=0:0:shortest=1:format=rgb,setsar=1[composed]");
        return graph.ToString();
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static async Task RunAsync(string executable, IEnumerable<string> args, TimeSpan duration, Action<double>? progress, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errors = new Queue<string>();
        var readErrors = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            { if (errors.Count >= 80) errors.Dequeue(); errors.Enqueue(line); }
        });
        var readProgress = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                if (line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out var us))
                    progress?.Invoke(Math.Clamp(us / 1_000_000d / Math.Max(.001, duration.TotalSeconds), 0, 1));
        });
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.WhenAll(readErrors, readProgress).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
