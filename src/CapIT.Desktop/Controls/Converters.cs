using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScreenRecorderApp.Controls;

/// <summary>Small, allocation-free value converters shared by the control themes and views.</summary>
public static class Converters
{
    /// <summary>Grows a corner radius by 3px — used by focus rings drawn 3px outside a control.</summary>
    public static readonly IValueConverter CornerRadiusGrow = new FuncValueConverter<CornerRadius, CornerRadius>(r =>
        new CornerRadius(Grow(r.TopLeft), Grow(r.TopRight), Grow(r.BottomRight), Grow(r.BottomLeft)));

    /// <summary>"#RRGGBB" (or "#AARRGGBB") → brush; anything unparseable → transparent.</summary>
    public static readonly IValueConverter HexToBrush = new FuncValueConverter<string?, IBrush>(hex =>
        Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent);

    /// <summary>"#RRGGBB" ↔ <see cref="Color"/> for colour pickers.</summary>
    public static readonly IValueConverter HexToColor = new HexColorConverter();

    /// <summary>0..1 → "42%".</summary>
    public static readonly IValueConverter Percent = new FuncValueConverter<double, string>(v => $"{v * 100:0}%");

    /// <summary>Seconds → "mm:ss" / "h:mm:ss".</summary>
    public static readonly IValueConverter Duration = new FuncValueConverter<double, string>(FormatDuration);

    /// <summary>True when the bound number is greater than zero.</summary>
    public static readonly IValueConverter IsPositive = new FuncValueConverter<int, bool>(v => v > 0);

    /// <summary>True when the bound number is zero.</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(v => v == 0);

    /// <summary>True when the bound int equals the converter parameter (e.g. which settings section is shown).</summary>
    public static readonly IValueConverter IndexIs = new FuncValueConverter<int, object?, bool>((value, parameter) =>
        int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected) && value == expected);

    /// <summary>true ↔ 0, false ↔ 1 — a two-segment control bound to a bool.</summary>
    public static readonly IValueConverter BoolToIndex = new BoolIndexConverter();

    /// <summary>Webcam template key → thumbnail size/shape for the template picker.</summary>
    public static readonly IValueConverter WebcamTemplateWidth = new FuncValueConverter<string?, double>(k => k == "landscape" ? 68 : 46);
    public static readonly IValueConverter WebcamTemplateHeight = new FuncValueConverter<string?, double>(k => k == "landscape" ? 40 : 46);
    public static readonly IValueConverter WebcamTemplateRadius = new FuncValueConverter<string?, CornerRadius>(k => k switch
    {
        "circle" or "neon" => new CornerRadius(23),
        "rounded" => new CornerRadius(12),
        "landscape" => new CornerRadius(8),
        _ => new CornerRadius(3),
    });
    public static readonly IValueConverter IsNeonTemplate = new FuncValueConverter<string?, bool>(k => k == "neon");

    /// <summary>Annotation tool → icon geometry from the icon set (falls back to the pen).</summary>
    public static readonly IValueConverter AnnotationToolIcon = new FuncValueConverter<Models.AnnotationTool, Geometry?>(tool =>
        Services.AppResources.Icon(tool switch
        {
            Models.AnnotationTool.Select => "Icon.Tool.Select",
            Models.AnnotationTool.Highlighter or Models.AnnotationTool.Marker => "Icon.Tool.Highlighter",
            Models.AnnotationTool.Arrow or Models.AnnotationTool.DoubleArrow => "Icon.Tool.Arrow",
            Models.AnnotationTool.Line or Models.AnnotationTool.DashedLine or Models.AnnotationTool.CurvedLine or Models.AnnotationTool.ElbowLine => "Icon.Tool.Line",
            Models.AnnotationTool.Rectangle or Models.AnnotationTool.Square or Models.AnnotationTool.RoundedRectangle => "Icon.Tool.Rectangle",
            Models.AnnotationTool.Ellipse or Models.AnnotationTool.Circle => "Icon.Tool.Ellipse",
            Models.AnnotationTool.Text or Models.AnnotationTool.Label => "Icon.Tool.Text",
            Models.AnnotationTool.Blur or Models.AnnotationTool.Pixelate => "Icon.Tool.Blur",
            Models.AnnotationTool.NumberedStep => "Icon.Tool.Step",
            Models.AnnotationTool.Callout or Models.AnnotationTool.SpeechBubble or Models.AnnotationTool.ThoughtBubble => "Icon.Tool.Callout",
            _ => "Icon.Pen",
        }));

    public static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static double Grow(double value) => value > 500 ? value : value + 3;

    private sealed class BoolIndexConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 0 : 1;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is 0;
    }

    private sealed class HexColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string hex && Color.TryParse(hex, out var color) ? color : Colors.White;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
    }
}
