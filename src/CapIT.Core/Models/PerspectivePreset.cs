namespace ScreenRecorderApp.Models;

public sealed record PerspectivePreset(string Key, string Label, double X, double Y, double Z, double Scale)
{
    public static IReadOnlyList<PerspectivePreset> All { get; } =
    [
        new("Flat", "Flat", 0, 0, 0, .92),
        new("Left", "Perspective Left", 3, -18, -1, .90),
        new("Right", "Perspective Right", 3, 18, 1, .90),
        new("Hero", "Hero Tilt", 7, -10, -2, .92),
        new("Cinematic", "Cinematic Floating", 4, 6, 0, .90),
        new("IsometricLeft", "Isometric Left", 8, -28, -2, .86),
        new("IsometricRight", "Isometric Right", 8, 28, 2, .86),
    ];
}
