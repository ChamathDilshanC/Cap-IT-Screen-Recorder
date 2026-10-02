using ScreenRecorderApp.Avalonia.Models;

namespace ScreenRecorderApp.Avalonia.Services;

/// <summary>
/// Platform-neutral seam for the existing capture, media probing, and export services.
/// The Avalonia shell owns presentation only; production wiring supplies the Win32 adapter here.
/// </summary>
public interface IRecordingWorkspaceAdapter
{
    Task<RecordingSummary?> GetLatestRecordingAsync(CancellationToken cancellationToken = default);
    Task ExportAsync(RecordingSummary recording, string destinationPath, CancellationToken cancellationToken = default);
}
