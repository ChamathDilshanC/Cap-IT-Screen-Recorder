namespace ScreenRecorderApp.Models;

public sealed record PresentationPreset(string Name, PresentationSettings Settings, bool IsBuiltIn = false)
{
    public override string ToString() => Name;
    public static IReadOnlyList<PresentationPreset> BuiltIns =>
    [
        new("Clean", new() { BackgroundColor = "#DCE2E8", BackgroundColor2 = "#ACB7CA" }, true),
        new("Floating", new() { Padding = 100, VideoScale = .85, CornerRadius = 28, ShadowBlur = 40, ShadowOpacity = .4, ShadowOffsetY = 24 }, true),
        new("Minimal", new() { Padding = 24, VideoScale = 1, BackgroundMode = "solid", BackgroundColor = "#E3E5E8", CornerRadius = 12, ShadowOpacity = .15 }, true),
        new("Presentation", new() { CanvasPreset = "16:9", Padding = 96, BackgroundColor = "#26374A", BackgroundColor2 = "#64808B" }, true),
        new("Vertical Social", new() { CanvasPreset = "9:16", Padding = 64, BackgroundColor = "#35334C", BackgroundColor2 = "#A0849D" }, true),
        new("Code Demo", new() { Padding = 32, VideoScale = 1, BackgroundMode = "solid", BackgroundColor = "#161B24", CornerRadius = 12, ShadowOpacity = .2 }, true)
    ];
}
