using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ScreenRecorderApp.Views.Controls;

/// <summary>
/// A segmented audio level meter — the row of little bars that lights up left-to-right with the signal.
/// </summary>
/// <remarks>
/// Replaces the plain <c>ProgressBar</c> the Home tab used to show mic/speaker level with. A ProgressBar
/// animates its fill, which smears exactly the fast transients a level meter exists to show, and reads
/// as a loading indicator rather than as audio. Discrete segments (each either lit or not, green →
/// amber → red as the signal approaches clipping) are the convention every audio tool uses, and they
/// make movement legible at a glance even at this size.
///
/// Built entirely in code rather than as a XAML control with a template: the visual is N sibling
/// rectangles whose count is a property, which a XAML template would have to build in code-behind
/// anyway, and there is no templating/restyling scenario for it here.
/// </remarks>
public sealed class LevelMeter : UserControl
{
    private const int DefaultSegmentCount = 14;

    private static readonly Color LitLow = Color.FromArgb(255, 16, 185, 90);      // healthy signal
    private static readonly Color LitMid = Color.FromArgb(255, 245, 180, 50);     // getting hot
    private static readonly Color LitHigh = Color.FromArgb(255, 232, 17, 35);     // near clipping
    private static readonly Color Unlit = Color.FromArgb(255, 68, 68, 78);
    private static readonly Color Unavailable = Color.FromArgb(255, 120, 118, 70); // device couldn't be opened

    private readonly StackPanel _panel;
    private readonly List<Rectangle> _segments = [];
    private readonly SolidColorBrush _unlitBrush = new(Unlit);
    private readonly SolidColorBrush _unavailableBrush = new(Unavailable);
    private readonly SolidColorBrush _litLowBrush = new(LitLow);
    private readonly SolidColorBrush _litMidBrush = new(LitMid);
    private readonly SolidColorBrush _litHighBrush = new(LitHigh);

    public LevelMeter()
    {
        _panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = _panel;
        BuildSegments();
    }

    /// <summary>Current level, 0..1. Driven from the view model's smoothed dBFS reading.</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter),
        new PropertyMetadata(0.0, (d, _) => ((LevelMeter)d).Refresh()));

    /// <summary>False when the underlying device couldn't be opened at all — the whole meter goes a muted
    /// olive instead of showing a (misleading) legitimately-silent green-capable meter.</summary>
    public bool IsAvailable
    {
        get => (bool)GetValue(IsAvailableProperty);
        set => SetValue(IsAvailableProperty, value);
    }

    public static readonly DependencyProperty IsAvailableProperty = DependencyProperty.Register(
        nameof(IsAvailable), typeof(bool), typeof(LevelMeter),
        new PropertyMetadata(true, (d, _) => ((LevelMeter)d).Refresh()));

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public static readonly DependencyProperty SegmentCountProperty = DependencyProperty.Register(
        nameof(SegmentCount), typeof(int), typeof(LevelMeter),
        new PropertyMetadata(DefaultSegmentCount, (d, _) => ((LevelMeter)d).BuildSegments()));

    /// <summary>Height of the shortest (leftmost) segment. Segments ramp up to <see cref="MaxSegmentHeight"/>.</summary>
    public double MinSegmentHeight
    {
        get => (double)GetValue(MinSegmentHeightProperty);
        set => SetValue(MinSegmentHeightProperty, value);
    }

    public static readonly DependencyProperty MinSegmentHeightProperty = DependencyProperty.Register(
        nameof(MinSegmentHeight), typeof(double), typeof(LevelMeter),
        new PropertyMetadata(5.0, (d, _) => ((LevelMeter)d).BuildSegments()));

    public double MaxSegmentHeight
    {
        get => (double)GetValue(MaxSegmentHeightProperty);
        set => SetValue(MaxSegmentHeightProperty, value);
    }

    public static readonly DependencyProperty MaxSegmentHeightProperty = DependencyProperty.Register(
        nameof(MaxSegmentHeight), typeof(double), typeof(LevelMeter),
        new PropertyMetadata(16.0, (d, _) => ((LevelMeter)d).BuildSegments()));

    /// <summary>
    /// Rebuilds the segment rectangles. Heights ramp from <see cref="MinSegmentHeight"/> to
    /// <see cref="MaxSegmentHeight"/> across the row, so the meter reads as a rising equalizer band
    /// rather than a flat bar — the shape alone conveys "louder to the right" before any color does.
    /// </summary>
    private void BuildSegments()
    {
        _panel.Children.Clear();
        _segments.Clear();

        var count = Math.Max(1, SegmentCount);
        for (int i = 0; i < count; i++)
        {
            var t = count == 1 ? 1.0 : i / (double)(count - 1);
            var rect = new Rectangle
            {
                Width = 3,
                Height = MinSegmentHeight + (MaxSegmentHeight - MinSegmentHeight) * t,
                RadiusX = 1.5,
                RadiusY = 1.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = _unlitBrush,
            };
            _segments.Add(rect);
            _panel.Children.Add(rect);
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_segments.Count == 0) return;

        var unlit = IsAvailable ? _unlitBrush : _unavailableBrush;
        // A level of exactly 0 lights nothing; anything audible lights at least the first segment, so a
        // faint-but-real signal is never rounded away into looking identical to silence.
        var lit = IsAvailable && Level > 0
            ? Math.Clamp((int)Math.Ceiling(Level * _segments.Count), 1, _segments.Count)
            : 0;

        for (int i = 0; i < _segments.Count; i++)
        {
            if (i >= lit)
            {
                _segments[i].Fill = unlit;
                _segments[i].Opacity = IsAvailable ? 0.35 : 0.5;
                continue;
            }

            var position = (i + 1) / (double)_segments.Count;
            _segments[i].Fill = position switch
            {
                > 0.88 => _litHighBrush,
                > 0.70 => _litMidBrush,
                _ => _litLowBrush,
            };
            _segments[i].Opacity = 1.0;
        }
    }
}
