using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Draws one glyph from the Cap-IT icon set (Styles/Icons.axaml). Geometry is authored on a square
/// grid (<see cref="GridSize"/>, 24 by default) and scaled to <see cref="Size"/>; strokes scale with it so
/// a 14px and a 24px icon keep the same visual weight. Foreground is inherited like text.
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 16);

    public static readonly StyledProperty<double> GridSizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(GridSize), 24);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.75);

    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(IsFilled));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    private Pen? _pen;

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeThicknessProperty, IsFilledProperty, GridSizeProperty);
        AffectsMeasure<Icon>(SizeProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<Icon>(false);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double GridSize { get => GetValue(GridSizeProperty); set => SetValue(GridSizeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public bool IsFilled { get => GetValue(IsFilledProperty); set => SetValue(IsFilledProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty || change.Property == StrokeThicknessProperty) _pen = null;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data is null || brush is null || Size <= 0) return;

        var scale = Size / Math.Max(1, GridSize);
        var offsetX = (Bounds.Width - Size) / 2;
        var offsetY = (Bounds.Height - Size) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            if (IsFilled)
            {
                context.DrawGeometry(brush, null, data);
            }
            else
            {
                // Stroke is specified in output pixels; the transform scales it, so compensate.
                _pen ??= new Pen(brush, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                if (Math.Abs(_pen.Thickness - StrokeThickness / scale) > .001) _pen = new Pen(brush, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, _pen, data);
            }
        }
    }
}
