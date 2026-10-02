using ScreenRecorderApp.Avalonia.Models;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Encoding;
using ScreenRecorderApp.Services.Export;

namespace ScreenRecorderApp.Avalonia.Services;

/// <summary>
/// Bridges the Avalonia presentation shell to the existing recording/export engine.
/// No Avalonia type crosses this boundary and source recordings are never overwritten.
/// </summary>
public sealed class ExistingRecordingWorkspaceAdapter : IRecordingWorkspaceAdapter
{
    private readonly string _recordingsDirectory;

    public ExistingRecordingWorkspaceAdapter(string? recordingsDirectory = null)
    {
        _recordingsDirectory = recordingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Cap-IT Screen Recorder");
    }

    public async Task<RecordingSummary?> GetLatestRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_recordingsDirectory)) return null;
        var path = Directory.EnumerateFiles(_recordingsDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(x => x.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                        x.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
        if (path is null) return null;

        var probe = await MediaProbe.ProbeAsync(path, cancellationToken);
        return new RecordingSummary(Path.GetFileNameWithoutExtension(path), probe.Duration ?? TimeSpan.Zero,
            probe.Width, probe.Height, File.GetLastWriteTimeUtc(path), path);
    }

    public async Task ExportAsync(RecordingSummary recording, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recording.SourcePath) || !File.Exists(recording.SourcePath))
            throw new FileNotFoundException("The source recording is no longer available.", recording.SourcePath);
        var metadata = RecordingMetadata.Load(recording.SourcePath);
        var presentation = metadata?.Presentation ?? new PresentationSettings();
        var regions = ZoomRegionStore.Load(recording.SourcePath);
        await CompositionExportService.ExportAsync(recording.SourcePath, TimeSpan.Zero, recording.Duration,
            destinationPath, false, presentation, regions, new ExportSettings(), null, cancellationToken, metadata);
    }
}
