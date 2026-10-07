namespace ScreenRecorderApp.Models;

public sealed record BackgroundPreset(string Key, string Label, string Color1, string Color2, bool Pattern = false, bool Animated = false)
{
    public static IReadOnlyList<BackgroundPreset> All { get; } =
    [
        new("None", "None", "#000000", "#000000"),
        new("Midnight Gold", "Midnight Gold", "#000814", "#000814", true),
        new("Warm Grid", "Warm Grid", "#F4F1EA", "#FFFFFF", true),
        new("Aurora Orbit", "Aurora Orbit", "#020617", "#1E1B4B", false, true),
        new("Deep Space", "Deep Space", "#050816", "#18213D"),
        new("Midnight Gradient", "Midnight Gradient", "#182435", "#46526D"),
        new("Soft Lavender", "Soft Lavender", "#343052", "#B8AED7"),
        new("Ocean Glow", "Ocean Glow", "#12384B", "#71AFC0"),
        new("Warm Sunset", "Warm Sunset", "#754B5B", "#E6BB9C"),
        new("Graphite", "Graphite", "#252A32", "#646E7E"),
        new("Frost", "Frost", "#91ABB8", "#DCE9EE"),
        new("Studio Dark", "Studio Dark", "#111318", "#303642"),
        new("Studio Light", "Studio Light", "#E9EDF2", "#FFFFFF"),
    ];
}
