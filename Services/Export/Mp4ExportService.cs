using ScreenRecorderApp.Models;
namespace ScreenRecorderApp.Services.Export;

public static class Mp4ExportService
{
    public static Task ExportAsync(string inputPath, TimeSpan start, TimeSpan duration, string outputPath,
        string backgroundPath, int canvasWidth, int canvasHeight, double videoScale, double cornerRadius,
        IReadOnlyList<ZoomRegion>? zoomRegions = null, IProgress<GifExportProgress>? progress = null,
        CancellationToken cancellationToken = default, RecordingMetadata? metadata = null,
        PresentationSettings? presentation = null, ExportSettings? options = null) =>
        CompositionExportService.ExportAsync(inputPath, start, duration, outputPath, false,
            presentation ?? new PresentationSettings { CanvasWidth = canvasWidth, CanvasHeight = canvasHeight,
                BackgroundPath = backgroundPath, VideoScale = videoScale, CornerRadius = cornerRadius },
            zoomRegions ?? [], options ?? new(), progress, cancellationToken, metadata);
}
