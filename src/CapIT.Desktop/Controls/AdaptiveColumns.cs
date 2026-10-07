using Avalonia;
using Avalonia.Controls;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Two-column page layout that collapses to one column on narrow windows. The first child takes the
/// remaining width; the second (the context panel — preview, summary) gets <see cref="SideWidth"/>. Below
/// <see cref="Breakpoint"/> the side panel moves underneath (or above, with <see cref="SideFirstWhenStacked"/>)
/// at full width. No overlap, no horizontal scrolling at any size.
/// </summary>
public sealed class AdaptiveColumns : Panel
{
    public static readonly StyledProperty<double> BreakpointProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(Breakpoint), 1040);

    public static readonly StyledProperty<double> SideWidthProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(SideWidth), 380);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(Spacing), 24);

    public static readonly StyledProperty<bool> SideFirstWhenStackedProperty =
        AvaloniaProperty.Register<AdaptiveColumns, bool>(nameof(SideFirstWhenStacked));

    public static readonly DirectProperty<AdaptiveColumns, bool> IsStackedProperty =
        AvaloniaProperty.RegisterDirect<AdaptiveColumns, bool>(nameof(IsStacked), o => o.IsStacked);

    private bool _isStacked;

    static AdaptiveColumns()
    {
        AffectsMeasure<AdaptiveColumns>(BreakpointProperty, SideWidthProperty, SpacingProperty, SideFirstWhenStackedProperty);
    }

    public double Breakpoint { get => GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double SideWidth { get => GetValue(SideWidthProperty); set => SetValue(SideWidthProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public bool SideFirstWhenStacked { get => GetValue(SideFirstWhenStackedProperty); set => SetValue(SideFirstWhenStackedProperty, value); }
    public bool IsStacked { get => _isStacked; private set => SetAndRaise(IsStackedProperty, ref _isStacked, value); }

    private (Control? Main, Control? Side) Parts()
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        return (visible.ElementAtOrDefault(0), visible.ElementAtOrDefault(1));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (main, side) = Parts();
        var width = double.IsInfinity(availableSize.Width) ? Breakpoint : availableSize.Width;
        IsStacked = side is null || width < Breakpoint;

        if (IsStacked)
        {
            var total = new Size();
            foreach (var child in new[] { main, side })
            {
                if (child is null) continue;
                child.Measure(new Size(width, double.PositiveInfinity));
                total = new Size(Math.Max(total.Width, child.DesiredSize.Width), total.Height + child.DesiredSize.Height);
            }
            if (main is not null && side is not null) total = total.WithHeight(total.Height + Spacing);
            return total.WithWidth(Math.Min(total.Width, width));
        }

        var sideWidth = Math.Min(SideWidth, width * .45);
        var mainWidth = Math.Max(0, width - sideWidth - Spacing);
        main!.Measure(new Size(mainWidth, availableSize.Height));
        side!.Measure(new Size(sideWidth, availableSize.Height));
        return new Size(width, Math.Max(main.DesiredSize.Height, side.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (main, side) = Parts();
        if (IsStacked)
        {
            var order = SideFirstWhenStacked ? new[] { side, main } : new[] { main, side };
            var y = 0d;
            foreach (var child in order)
            {
                if (child is null) continue;
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height + Spacing;
            }
            return finalSize;
        }

        var sideWidth = Math.Min(SideWidth, finalSize.Width * .45);
        var mainWidth = Math.Max(0, finalSize.Width - sideWidth - Spacing);
        main!.Arrange(new Rect(0, 0, mainWidth, finalSize.Height));
        side!.Arrange(new Rect(mainWidth + Spacing, 0, sideWidth, side.DesiredSize.Height));
        return finalSize;
    }
}
