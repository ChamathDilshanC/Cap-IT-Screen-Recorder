using Avalonia;
using Avalonia.Controls;

namespace ScreenRecorderApp.Controls;

/// <summary>Sizes its child to the available width at a fixed <see cref="Ratio"/> (width ÷ height), capped by <see cref="MaxContentHeight"/>.</summary>
public sealed class AspectRatioBox : Decorator
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioBox, double>(nameof(Ratio), 16d / 9);

    public static readonly StyledProperty<double> MaxContentHeightProperty =
        AvaloniaProperty.Register<AspectRatioBox, double>(nameof(MaxContentHeight), double.PositiveInfinity);

    static AspectRatioBox()
    {
        AffectsMeasure<AspectRatioBox>(RatioProperty, MaxContentHeightProperty);
    }

    public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    public double MaxContentHeight { get => GetValue(MaxContentHeightProperty); set => SetValue(MaxContentHeightProperty, value); }

    private Size Fit(Size available)
    {
        var ratio = Ratio > 0 ? Ratio : 16d / 9;
        var width = double.IsInfinity(available.Width) ? 640 : available.Width;
        var height = Math.Min(width / ratio, MaxContentHeight);
        if (!double.IsInfinity(available.Height)) height = Math.Min(height, available.Height);
        return new Size(width, height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Fit(availableSize);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
