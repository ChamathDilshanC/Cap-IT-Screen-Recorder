using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Review timeline: time ruler, thumbnail strip, trim range with draggable handles, zoom-region lane and
/// a scrubbable playhead. Drawn in one pass (no child controls), so playback only costs a repaint.
/// Keyboard: ←/→ step the playhead (Shift = 1 s), I / O set the trim in/out points at the playhead.
/// </summary>
public sealed class EditorTimeline : Control
{
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<EditorTimeline, double>(nameof(Duration), 1);
    public static readonly StyledProperty<double> TrimStartProperty =
        AvaloniaProperty.Register<EditorTimeline, double>(nameof(TrimStart), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> TrimEndProperty =
        AvaloniaProperty.Register<EditorTimeline, double>(nameof(TrimEnd), 1, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<EditorTimeline, double>(nameof(Position), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<IEnumerable<ZoomRegion>?> RegionsProperty =
        AvaloniaProperty.Register<EditorTimeline, IEnumerable<ZoomRegion>?>(nameof(Regions));
    public static readonly StyledProperty<Bitmap?> ThumbnailsProperty =
        AvaloniaProperty.Register<EditorTimeline, Bitmap?>(nameof(Thumbnails));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<EditorTimeline, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<EditorTimeline, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> AccentForegroundBrushProperty = AvaloniaProperty.Register<EditorTimeline, IBrush?>(nameof(AccentForegroundBrush));
    public static readonly StyledProperty<IBrush?> TextBrushProperty = AvaloniaProperty.Register<EditorTimeline, IBrush?>(nameof(TextBrush));
    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty = AvaloniaProperty.Register<EditorTimeline, IBrush?>(nameof(PlayheadBrush));
    public static readonly StyledProperty<IBrush?> DimBrushProperty = AvaloniaProperty.Register<EditorTimeline, IBrush?>(nameof(DimBrush));
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        Avalonia.Controls.Documents.TextElement.FontFamilyProperty.AddOwner<EditorTimeline>();

    private const double SidePad = 12, RulerHeight = 20, TrackTop = 24, TrackHeight = 52, LaneTop = 82, LaneHeight = 20, HandleWidth = 10;
    private enum Drag { None, Start, End, Playhead }
    private Drag _drag;
    private INotifyCollectionChanged? _observedCollection;

    static EditorTimeline()
    {
        AffectsRender<EditorTimeline>(DurationProperty, TrimStartProperty, TrimEndProperty, PositionProperty, RegionsProperty,
            ThumbnailsProperty, TrackBrushProperty, AccentBrushProperty, TextBrushProperty, PlayheadBrushProperty, DimBrushProperty);
        FocusableProperty.OverrideDefaultValue<EditorTimeline>(true);
    }

    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double TrimStart { get => GetValue(TrimStartProperty); set => SetValue(TrimStartProperty, value); }
    public double TrimEnd { get => GetValue(TrimEndProperty); set => SetValue(TrimEndProperty, value); }
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public IEnumerable<ZoomRegion>? Regions { get => GetValue(RegionsProperty); set => SetValue(RegionsProperty, value); }
    public Bitmap? Thumbnails { get => GetValue(ThumbnailsProperty); set => SetValue(ThumbnailsProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? AccentForegroundBrush { get => GetValue(AccentForegroundBrushProperty); set => SetValue(AccentForegroundBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush? PlayheadBrush { get => GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }
    public IBrush? DimBrush { get => GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    private double SafeDuration => Math.Max(.01, Duration);
    private double TrackWidth => Math.Max(1, Bounds.Width - SidePad * 2);
    private double X(double seconds) => SidePad + Math.Clamp(seconds / SafeDuration, 0, 1) * TrackWidth;
    private double Seconds(double x) => Math.Clamp((x - SidePad) / TrackWidth, 0, 1) * SafeDuration;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, LaneTop + LaneHeight + 2);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RegionsProperty) return;
        if (_observedCollection is not null) _observedCollection.CollectionChanged -= OnRegionsChanged;
        foreach (var r in change.GetOldValue<IEnumerable<ZoomRegion>?>() ?? []) r.PropertyChanged -= OnRegionPropertyChanged;
        _observedCollection = change.GetNewValue<IEnumerable<ZoomRegion>?>() as INotifyCollectionChanged;
        if (_observedCollection is not null) _observedCollection.CollectionChanged += OnRegionsChanged;
        foreach (var r in Regions ?? []) r.PropertyChanged += OnRegionPropertyChanged;
    }

    private void OnRegionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ZoomRegion r in e.OldItems ?? Array.Empty<ZoomRegion>()) r.PropertyChanged -= OnRegionPropertyChanged;
        foreach (ZoomRegion r in e.NewItems ?? Array.Empty<ZoomRegion>()) r.PropertyChanged += OnRegionPropertyChanged;
        InvalidateVisual();
    }

    private void OnRegionPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var w = TrackWidth;
        var typeface = new Typeface(FontFamily);

        // Ruler
        const int divisions = 8;
        for (var i = 0; i <= divisions; i++)
        {
            var x = SidePad + w * i / divisions;
            context.DrawRectangle(TextBrush, null, new Rect(x, RulerHeight - 5, 1, 5));
            var label = FormatTime(SafeDuration * i / divisions);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, TextBrush);
            var tx = i == divisions ? x - text.Width : i == 0 ? x : x - text.Width / 2;
            context.DrawText(text, new Point(tx, 0));
        }

        // Track + thumbnails
        var track = new Rect(SidePad, TrackTop, w, TrackHeight);
        var trackRound = new RoundedRect(track, 8);
        context.DrawRectangle(TrackBrush, null, trackRound);
        if (Thumbnails is { } strip)
        {
            using (context.PushClip(trackRound))
            using (context.PushOpacity(.85))
                context.DrawImage(strip, new Rect(strip.Size), track);
        }

        // Outside the trim range
        var startX = X(TrimStart); var endX = X(TrimEnd);
        using (context.PushClip(trackRound))
        {
            context.DrawRectangle(DimBrush, null, new Rect(SidePad, TrackTop, Math.Max(0, startX - SidePad), TrackHeight));
            context.DrawRectangle(DimBrush, null, new Rect(endX, TrackTop, Math.Max(0, SidePad + w - endX), TrackHeight));
        }

        // Selection + handles
        var accentPen = new Pen(AccentBrush, 2);
        context.DrawRectangle(null, accentPen, new RoundedRect(new Rect(startX, TrackTop + 1, Math.Max(2, endX - startX), TrackHeight - 2), 7));
        DrawHandle(context, startX - HandleWidth / 2);
        DrawHandle(context, endX - HandleWidth / 2);

        // Zoom lane
        foreach (var r in Regions ?? [])
        {
            if (!r.Enabled) continue;
            var x = X(r.StartSeconds);
            var rect = new Rect(x, LaneTop, Math.Max(6, X(r.EndSeconds) - x), LaneHeight);
            using (context.PushOpacity(.85))
                context.DrawRectangle(AccentBrush, null, new RoundedRect(rect, 5));
            if (rect.Width > 34)
            {
                var label = new FormattedText($"{r.Scale:0.#}×", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, weight: FontWeight.SemiBold), 10, AccentForegroundBrush);
                context.DrawText(label, new Point(rect.X + 7, rect.Y + (LaneHeight - label.Height) / 2));
            }
        }

        // Playhead
        var px = X(Position);
        context.DrawRectangle(PlayheadBrush, null, new Rect(px - 1, TrackTop - 6, 2, TrackHeight + LaneHeight + 12));
        context.DrawEllipse(PlayheadBrush, null, new Point(px, TrackTop - 6), 5, 5);

        if (IsFocused && IsKeyboardFocusWithin)
            context.DrawRectangle(null, new Pen(AccentBrush, 1, DashStyle.Dash), new RoundedRect(track.Inflate(3), 10));
    }

    private void DrawHandle(DrawingContext context, double x)
    {
        context.DrawRectangle(AccentBrush, null, new RoundedRect(new Rect(x, TrackTop, HandleWidth, TrackHeight), 4));
        context.DrawRectangle(AccentForegroundBrush, null, new RoundedRect(new Rect(x + HandleWidth / 2 - 1, TrackTop + TrackHeight / 2 - 8, 2, 16), 1));
    }

    private static string FormatTime(double seconds) => seconds < 60
        ? seconds.ToString(seconds < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + "s"
        : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.CurrentCulture);

    // ---- Interaction --------------------------------------------------------------------------------

    private Drag HitTest(Point p)
    {
        if (p.Y >= TrackTop - 2 && p.Y <= TrackTop + TrackHeight + 2)
        {
            if (Math.Abs(p.X - X(TrimStart)) <= HandleWidth) return Drag.Start;
            if (Math.Abs(p.X - X(TrimEnd)) <= HandleWidth) return Drag.End;
        }
        return Drag.Playhead;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var p = e.GetPosition(this);
        _drag = HitTest(p);
        e.Pointer.Capture(this);
        Apply(p.X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag == Drag.None)
        {
            Cursor = HitTest(p) is Drag.Start or Drag.End ? new Cursor(StandardCursorType.SizeWestEast) : new Cursor(StandardCursorType.Hand);
            return;
        }
        Apply(p.X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = Drag.None;
    }

    private void Apply(double x)
    {
        var t = Seconds(x);
        switch (_drag)
        {
            case Drag.Start: SetCurrentValue(TrimStartProperty, Math.Clamp(t, 0, Math.Max(0, TrimEnd - .01))); break;
            case Drag.End: SetCurrentValue(TrimEndProperty, Math.Clamp(t, Math.Min(SafeDuration, TrimStart + .01), SafeDuration)); break;
            case Drag.Playhead: SetCurrentValue(PositionProperty, t); break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : .1;
        switch (e.Key)
        {
            case Key.Left: SetCurrentValue(PositionProperty, Math.Max(0, Position - step)); break;
            case Key.Right: SetCurrentValue(PositionProperty, Math.Min(SafeDuration, Position + step)); break;
            case Key.I: SetCurrentValue(TrimStartProperty, Math.Clamp(Position, 0, Math.Max(0, TrimEnd - .01))); break;
            case Key.O: SetCurrentValue(TrimEndProperty, Math.Clamp(Position, Math.Min(SafeDuration, TrimStart + .01), SafeDuration)); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }
}
