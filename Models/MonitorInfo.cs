namespace ScreenRecorderApp.Models;

/// <summary>Describes one physical display that can be selected as a capture source.</summary>
public sealed class MonitorInfo
{
    public required nint Handle { get; init; }
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool IsPrimary { get; init; }

    public override string ToString() => $"{FriendlyName} ({Width}x{Height}){(IsPrimary ? " - Primary" : string.Empty)}";
}
