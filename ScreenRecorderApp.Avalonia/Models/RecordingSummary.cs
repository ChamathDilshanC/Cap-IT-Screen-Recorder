namespace ScreenRecorderApp.Avalonia.Models;

public sealed record RecordingSummary(
    string Title,
    TimeSpan Duration,
    int Width,
    int Height,
    DateTimeOffset RecordedAt,
    string? SourcePath = null);
