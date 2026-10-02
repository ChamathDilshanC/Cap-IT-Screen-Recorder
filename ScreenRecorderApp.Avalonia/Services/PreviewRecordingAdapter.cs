using ScreenRecorderApp.Avalonia.Models;

namespace ScreenRecorderApp.Avalonia.Services;

/// <summary>
/// Safe startup adapter used until the platform capture/export implementation is ported.
/// It deliberately performs no file or device I/O.
/// </summary>
public sealed class PreviewRecordingAdapter : IRecordingWorkspaceAdapter
{
    public Task<RecordingSummary?> GetLatestRecordingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<RecordingSummary?>(new RecordingSummary(
            "Welcome to Cap-IT",
            TimeSpan.FromSeconds(42),
            1920,
            1080,
            DateTimeOffset.Now));

    public Task ExportAsync(
        RecordingSummary recording,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Export is not wired yet. Supply a production IRecordingWorkspaceAdapter at startup.");
}
