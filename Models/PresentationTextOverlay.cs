using System.Text.Json.Serialization;

namespace ScreenRecorderApp.Models;

/// <summary>One editable text layer rendered into both the review preview and exported video.</summary>
public sealed class PresentationTextOverlay
{
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 48;
    public string Color { get; set; } = "#FFFFFF";
    public double Opacity { get; set; } = 1;
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public string HorizontalAlignment { get; set; } = "Center";
    public string VerticalAlignment { get; set; } = "Center";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string Animation { get; set; } = "None";
    public double AnimationDuration { get; set; } = 0.8;

    [JsonIgnore]
    public bool IsVisible => !string.IsNullOrWhiteSpace(Text);

    public PresentationTextOverlay Clone() => new()
    {
        Text = Text, FontFamily = FontFamily, FontSize = FontSize, Color = Color, Opacity = Opacity,
        X = X, Y = Y, HorizontalAlignment = HorizontalAlignment, VerticalAlignment = VerticalAlignment,
        Bold = Bold, Italic = Italic, Animation = Animation,
        AnimationDuration = AnimationDuration
    };

    public void Normalize()
    {
        Text ??= "";
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Segoe UI" : FontFamily.Trim();
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 8, 240) : 48;
        Color = PresentationSettings.Color(Color, "#FFFFFF");
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 1;
        X = double.IsFinite(X) ? Math.Clamp(X, 0, 1) : .5;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : .5;
        HorizontalAlignment = NormalizeOption(HorizontalAlignment, "Center", "Left", "Center", "Right");
        VerticalAlignment = NormalizeOption(VerticalAlignment, "Center", "Top", "Center", "Bottom");
        Animation = Animation == "Bounce" ? "BounceLetters" :
            Animation is "None" or "BounceLetters" or "Fade" or "Pop" or "SlideUp" ? Animation : "None";
        AnimationDuration = double.IsFinite(AnimationDuration) ? Math.Clamp(AnimationDuration, .1, 3) : .8;
    }

    private static string NormalizeOption(string? value, string fallback, params string[] allowed) =>
        allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
}
