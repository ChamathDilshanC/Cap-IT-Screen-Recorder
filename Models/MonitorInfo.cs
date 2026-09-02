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

    // Top-left corner in virtual-screen coordinates (can be negative — e.g. a monitor positioned above
    // or to the left of the primary display). Unused until Phase 6's annotation overlay, which needs to
    // position a real window exactly over this monitor rather than always at the desktop origin.
    public int X { get; init; }
    public int Y { get; init; }

    public override string ToString() => $"{FriendlyName} ({Width}x{Height}){(IsPrimary ? " - Primary" : string.Empty)}";
}
