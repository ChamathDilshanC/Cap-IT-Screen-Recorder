namespace ScreenRecorderApp.Models;

public sealed record ClickSoundOption(string Key, string Label, string FileName)
{
    public override string ToString() => Label;

    public static IReadOnlyList<ClickSoundOption> All { get; } =
    [
        new("fast-double-click", "Fast double click", "mixkit-fast-double-click-on-mouse-275.wav"),
        new("mouse-click-close", "Mouse click close", "mixkit-mouse-click-close-1113.wav"),
        new("soft-button-press", "Soft button press", "ui-soft-button-press-brukowskij-soft-button-gentle-ui-press-5-3-0m00s.mp3"),
        new("computer-mouse-click", "Computer mouse click", "computer-mouse-click-joshua-chivers-1-00-00.mp3"),
    ];
}
