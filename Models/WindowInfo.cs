namespace ScreenRecorderApp.Models;

/// <summary>Describes one open, recordable top-level window — the Application-Specific Capture counterpart to <see cref="MonitorInfo"/>.</summary>
public sealed class WindowInfo
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }

    public override string ToString() => $"{Title} — {ProcessName}";
}
