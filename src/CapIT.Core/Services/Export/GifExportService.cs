using ScreenRecorderApp.Models;
namespace ScreenRecorderApp.Services.Export;

public readonly record struct GifExportProgress(string Stage, double PercentComplete);

public static class GifExportService
{
    public const int GifFrameRate = 12;
    public const int GifWidth = 720;
    public static Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputGifPath,
        IProgress<GifExportProgress>? progress = null, CancellationToken ct = default,
        IReadOnlyList<ZoomRegion>? zoomRegions = null, RecordingMetadata? metadata = null,
        PresentationSettings? presentation = null) =>
        CompositionExportService.ExportAsync(inputPath, start, duration, outputGifPath, true,
            presentation ?? new(), zoomRegions ?? [], new(), progress, ct, metadata);
}
