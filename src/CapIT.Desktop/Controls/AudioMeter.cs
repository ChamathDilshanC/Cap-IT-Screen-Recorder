using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Live level meter. Pure render control: a level change only invalidates its own visual — never
/// layout — so the 150 ms meter updates cost nothing beyond a tiny redraw.
/// </summary>
public sealed class AudioMeter : Control
{
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<AudioMeter, double>(nameof(Level));

    public static readonly StyledProperty<bool> IsAvailableProperty =
        AvaloniaProperty.Register<AudioMeter, bool>(nameof(IsAvailable), true);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<AudioMeter, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<AudioMeter, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> PeakBrushProperty =
        AvaloniaProperty.Register<AudioMeter, IBrush?>(nameof(PeakBrush));

    public static readonly StyledProperty<IBrush?> IdleBrushProperty =
        AvaloniaProperty.Register<AudioMeter, IBrush?>(nameof(IdleBrush));

    /// <summary>Number of segments; 0 draws one continuous bar.</summary>
    public static readonly StyledProperty<int> SegmentsProperty =
        AvaloniaProperty.Register<AudioMeter, int>(nameof(Segments), 24);

    static AudioMeter()
    {
        AffectsRender<AudioMeter>(LevelProperty, IsAvailableProperty, TrackBrushProperty, FillBrushProperty,
            PeakBrushProperty, IdleBrushProperty, SegmentsProperty);
    }

    public double Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsAvailable { get => GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush? PeakBrush { get => GetValue(PeakBrushProperty); set => SetValue(PeakBrushProperty, value); }
    public IBrush? IdleBrush { get => GetValue(IdleBrushProperty); set => SetValue(IdleBrushProperty, value); }
    public int Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, 8);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var level = IsAvailable ? Math.Clamp(Level, 0, 1) : 0;
        var segments = Math.Max(0, Segments);
        var fill = level > .85 ? PeakBrush : level > .03 ? FillBrush : IdleBrush;

        if (segments == 0)
        {
            var radius = bounds.Height / 2;
            context.DrawRectangle(TrackBrush, null, bounds, radius, radius);
            if (level > 0) context.DrawRectangle(fill, null, new Rect(0, 0, Math.Max(bounds.Height, bounds.Width * level), bounds.Height), radius, radius);
            return;
        }

        const double gap = 2;
        var width = (bounds.Width - gap * (segments - 1)) / segments;
        if (width <= 0) return;
        var lit = (int)Math.Round(level * segments);
        for (var i = 0; i < segments; i++)
        {
            var rect = new Rect(i * (width + gap), 0, width, bounds.Height);
            var brush = i < lit ? (i >= segments * .85 ? PeakBrush : FillBrush) : TrackBrush;
            context.DrawRectangle(brush, null, rect, 1.5, 1.5);
        }
    }
}
