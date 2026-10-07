using System.Drawing;

namespace ScreenRecorderApp.Models;

/// <summary>Retained, editable annotation in the live drawer canvas.</summary>
public sealed class AnnotationObject
{
    public AnnotationTool Tool { get; set; }
    public List<PointF> Points { get; set; } = [];
    public Color Color { get; set; } = Color.Lime;
    public float Thickness { get; set; } = 6;
    public bool IsFilled { get; set; }
    public byte FillOpacity { get; set; } = 35;
    public bool IsDashed { get; set; }
    public string? Text { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public AnnotationObject Clone() => new()
    {
        Tool = Tool, Points = Points.Select(p => new PointF(p.X + 12, p.Y + 12)).ToList(),
        Color = Color, Thickness = Thickness, IsFilled = IsFilled, FillOpacity = FillOpacity,
        IsDashed = IsDashed, Text = Text, ExpiresAtUtc = ExpiresAtUtc
    };

    public RectangleF GetBounds()
    {
        if (Points.Count == 0) return RectangleF.Empty;
        var minX = Points.Min(p => p.X); var minY = Points.Min(p => p.Y);
        var maxX = Points.Max(p => p.X); var maxY = Points.Max(p => p.Y);
        var pad = Math.Max(6, Thickness);
        return RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
    }

    public bool HitTest(PointF point) => GetBounds().Contains(point);
}
