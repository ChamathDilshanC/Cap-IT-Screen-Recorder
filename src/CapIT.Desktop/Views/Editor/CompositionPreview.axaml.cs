using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Export;

namespace ScreenRecorderApp.Views.Editor;

/// <summary>
/// The editor canvas. Static artwork (background with shadow and device frame, border/watermark overlay,
/// text) comes from the same <see cref="CompositionAssetRenderer"/> the exporter uses, and the video is
/// placed with the same <see cref="CompositionLayout"/>, so what you see is what exports. Zoom regions crop
/// the decoded frame with <see cref="CompositionLayout.SourceCrop"/>, exactly like the export filter graph.
/// </summary>
public partial class CompositionPreview : UserControl
{
    public static readonly StyledProperty<Bitmap?> VideoSourceProperty =
        AvaloniaProperty.Register<CompositionPreview, Bitmap?>(nameof(VideoSource));

    public static readonly StyledProperty<long> VideoVersionProperty =
        AvaloniaProperty.Register<CompositionPreview, long>(nameof(VideoVersion));

    private CancellationTokenSource? _renderCts;
    private CompositionLayout? _layout;
    private int _sourceWidth = 1920, _sourceHeight = 1080;
    private int _generation;
    private string? _lastSettings;
    private IReadOnlyList<PresentationTextOverlay> _texts = [];
    private PresentationTextOverlay _activeText = new();
    private int _activeTextIndex;
    private double _renderedTextX, _renderedTextY;
    private double _lastTime;
    private IReadOnlyList<ZoomRegion> _lastRegions = [];
    private Services.Export.PixelRect? _lastCrop;
    private bool _draggingText;
    private Point _dragStart;
    private double _dragOriginalX, _dragOriginalY;

    public CompositionPreview() => InitializeComponent();

    public Bitmap? VideoSource { get => GetValue(VideoSourceProperty); set => SetValue(VideoSourceProperty, value); }
    public long VideoVersion { get => GetValue(VideoVersionProperty); set => SetValue(VideoVersionProperty, value); }

    /// <summary>A static asset (background/watermark image) couldn't be loaded; null clears it.</summary>
    public event Action<string?>? AssetWarning;

    /// <summary>The active text layer was dragged: (x, y, completed) in 0..1 canvas units.</summary>
    public event Action<double, double, bool>? TextPositionChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == VideoSourceProperty) VideoFrame.Source = VideoSource;
        else if (change.Property == VideoVersionProperty) VideoFrame.Version = VideoVersion;
    }

    public async Task UpdateAsync(PresentationSettings settings, int sourceWidth, int sourceHeight)
    {
        var key = System.Text.Json.JsonSerializer.Serialize(settings) + $"/{sourceWidth}/{sourceHeight}";
        if (key == _lastSettings) return;
        _lastSettings = key;
        _renderCts?.Cancel(); _renderCts?.Dispose();
        var cts = _renderCts = new(); var ct = cts.Token; var generation = ++_generation;
        _sourceWidth = sourceWidth; _sourceHeight = sourceHeight;
        var snapshot = settings.Clone(); snapshot.Normalize();
        var texts = snapshot.TextOverlays.Count > 0 ? snapshot.TextOverlays : [snapshot.TextOverlay];
        try
        {
            // Short debounce: slider drags settle before the (GDI+) artwork is re-rendered.
            await Task.Delay(90, ct);
            var assets = await Task.Run(() => CompositionAssetRenderer.Render(snapshot, sourceWidth, sourceHeight, ct), ct);
            var (bg, overlay, text) = await Task.Run(() => (Decode(assets.Background), Decode(assets.Overlay), Decode(assets.Text)), ct);
            if (generation != _generation || ct.IsCancellationRequested) return;

            _texts = texts;
            _activeTextIndex = Math.Clamp(_activeTextIndex, 0, Math.Max(0, texts.Count - 1));
            _activeText = texts.ElementAtOrDefault(_activeTextIndex) ?? new();

            var l = _layout = assets.Layout; _lastCrop = null;
            CompositionCanvas.Width = BackgroundLayer.Width = OverlayLayer.Width = TextLayer.Width = LettersCanvas.Width = l.Width;
            CompositionCanvas.Height = BackgroundLayer.Height = OverlayLayer.Height = TextLayer.Height = LettersCanvas.Height = l.Height;
            VideoViewport.Width = VideoFrame.Width = l.Video.Width;
            VideoViewport.Height = VideoFrame.Height = l.Video.Height;
            Canvas.SetLeft(VideoViewport, l.Video.X); Canvas.SetTop(VideoViewport, l.Video.Y);
            VideoViewport.CornerRadius = new CornerRadius(l.Radius);
            ApplyTransform(snapshot, l);

            BackgroundLayer.Source = bg; OverlayLayer.Source = overlay; TextLayer.Source = text;
            BuildLetters(l, texts);
            _renderedTextX = _activeText.X; _renderedTextY = _activeText.Y;
            SetPosition(_lastTime, _lastRegions);
            AssetWarning?.Invoke(assets.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _generation) AssetWarning?.Invoke("The composition preview couldn't update. Try a smaller canvas. " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// Z rotation matches the exporter (rotate, then fit the rotated bounds back into the video rectangle).
    /// X/Y tilt and depth are drawn as a preview perspective, as the editor always has.
    /// </summary>
    private void ApplyTransform(PresentationSettings settings, CompositionLayout layout)
    {
        var angle = settings.RotationZ;
        if (Math.Abs(angle) > .01)
        {
            var radians = angle * Math.PI / 180;
            double w = layout.Video.Width, h = layout.Video.Height;
            var rotW = Math.Abs(w * Math.Cos(radians)) + Math.Abs(h * Math.Sin(radians));
            var rotH = Math.Abs(w * Math.Sin(radians)) + Math.Abs(h * Math.Cos(radians));
            VideoFrame.RenderTransform = new TransformGroup
            {
                Children = { new RotateTransform(angle), new ScaleTransform(w / rotW, h / rotH) },
            };
        }
        else
        {
            VideoFrame.RenderTransform = null;
        }

        VideoViewport.RenderTransform = Math.Abs(settings.RotationX) > .01 || Math.Abs(settings.RotationY) > .01
            ? new Rotate3DTransform(settings.RotationX, settings.RotationY, 0, 0, 0, 0, settings.PerspectiveDepth)
            : null;
    }

    public void SetActiveTextIndex(int index)
    {
        _activeTextIndex = Math.Clamp(index, 0, Math.Max(0, _texts.Count - 1));
        _activeText = _texts.ElementAtOrDefault(_activeTextIndex) ?? new();
        _renderedTextX = _activeText.X; _renderedTextY = _activeText.Y;
    }

    /// <summary>Applies the zoom crop and text animation for <paramref name="seconds"/> (original-recording time).</summary>
    public void SetPosition(double seconds, IReadOnlyList<ZoomRegion> regions)
    {
        _lastTime = seconds; _lastRegions = regions;
        if (_layout is null) return;
        UpdateTextAnimation(seconds);
        var crop = CompositionLayout.SourceCrop(_sourceWidth, _sourceHeight, _layout.Video, CompositionLayout.ActiveZoom(regions, seconds));
        if (crop == _lastCrop) return;
        _lastCrop = crop;
        VideoFrame.SourceCrop = new Rect((double)crop.X / _sourceWidth, (double)crop.Y / _sourceHeight,
            (double)crop.Width / _sourceWidth, (double)crop.Height / _sourceHeight);
    }

    private void BuildLetters(CompositionLayout layout, IReadOnlyList<PresentationTextOverlay> texts)
    {
        LettersCanvas.Children.Clear();
        var bouncing = texts.Where(t => t.Animation == "BounceLetters" && t.IsVisible).ToList();
        LettersCanvas.IsVisible = bouncing.Count > 0;
        foreach (var text in bouncing)
        {
            var width = text.FontSize * .62;
            var start = AlignedOrigin(text.X * layout.Width, text.Text.Length * width, text.HorizontalAlignment);
            var top = AlignedOrigin(text.Y * layout.Height, text.FontSize * 1.6, text.VerticalAlignment);
            for (var i = 0; i < text.Text.Length; i++)
            {
                var letter = new TextBlock
                {
                    Text = text.Text[i].ToString(), FontSize = text.FontSize,
                    FontFamily = new FontFamily(text.FontFamily),
                    Foreground = new SolidColorBrush(Color.TryParse(text.Color, out var c) ? c : Colors.White) { Opacity = text.Opacity },
                    FontWeight = text.Bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = text.Italic ? FontStyle.Italic : FontStyle.Normal,
                    Tag = text.AnimationDuration,
                };
                Canvas.SetLeft(letter, start + i * width);
                Canvas.SetTop(letter, top);
                LettersCanvas.Children.Add(letter);
            }
        }
    }

    private void UpdateTextAnimation(double seconds)
    {
        if (_layout is null) return;
        var text = _activeText;
        var duration = Math.Max(.1, text.AnimationDuration);
        var progress = Math.Clamp(seconds / duration, 0, 1);
        var dx = (text.X - _renderedTextX) * _layout.Width;
        var dy = (text.Y - _renderedTextY) * _layout.Height;

        if (text.Animation == "BounceLetters")
        {
            TextLayer.Opacity = 1;
            LettersCanvas.RenderTransform = new TranslateTransform(dx, dy);
            var count = LettersCanvas.Children.Count;
            for (var i = 0; i < count; i++)
            {
                if (LettersCanvas.Children[i] is not Control letter) continue;
                var letterDuration = letter.Tag is double value ? value : duration;
                var stagger = i * Math.Min(.08, letterDuration / Math.Max(1, count * 2d));
                var local = Math.Clamp((seconds - stagger) / letterDuration, 0, 1);
                letter.RenderTransform = new TranslateTransform(0,
                    _layout.Height * .12 * (1 - local) - Math.Sin(local * Math.PI * 2) * _layout.Height * .055 * (1 - local));
                letter.Opacity = seconds >= stagger ? 1 : 0;
            }
            return;
        }

        double scale = 1, offsetX = 0, offsetY = 0, opacity = 1;
        switch (text.Animation)
        {
            case "Bounce": offsetX = Math.Sin(progress * Math.PI * 3) * _layout.Width * .018 * (1 - progress); break;
            case "Fade": opacity = progress; break;
            case "Pop": scale = .75 + .25 * progress; break;
            case "SlideUp": offsetY = _layout.Height * .08 * (1 - progress); break;
        }
        TextLayer.Opacity = opacity;
        var cx = text.X * _layout.Width; var cy = text.Y * _layout.Height;
        TextLayer.RenderTransformOrigin = new RelativePoint(cx, cy, RelativeUnit.Absolute);
        TextLayer.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(scale, scale), new TranslateTransform(dx + offsetX, dy + offsetY) },
        };
    }

    // ---- Dragging the active text layer -------------------------------------------------------------

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_layout is null || !_activeText.IsVisible || !e.GetCurrentPoint(CompositionCanvas).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(CompositionCanvas);
        var width = Math.Max(_activeText.FontSize * .62, _activeText.Text.Length * _activeText.FontSize * .62);
        var height = _activeText.FontSize * 1.6;
        var left = AlignedOrigin(_activeText.X * _layout.Width, width, _activeText.HorizontalAlignment);
        var top = AlignedOrigin(_activeText.Y * _layout.Height, height, _activeText.VerticalAlignment);
        if (point.X < left || point.X > left + width || point.Y < top || point.Y > top + height) return;
        _draggingText = true; _dragStart = point;
        _dragOriginalX = _activeText.X; _dragOriginalY = _activeText.Y;
        e.Pointer.Capture(CompositionCanvas);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_layout is null) return;
        if (!_draggingText)
        {
            Cursor = IsOverText(e.GetPosition(CompositionCanvas)) ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            return;
        }
        var point = e.GetPosition(CompositionCanvas);
        _activeText.X = Math.Clamp(_dragOriginalX + (point.X - _dragStart.X) / _layout.Width, 0, 1);
        _activeText.Y = Math.Clamp(_dragOriginalY + (point.Y - _dragStart.Y) / _layout.Height, 0, 1);
        TextPositionChanged?.Invoke(_activeText.X, _activeText.Y, false);
        UpdateTextAnimation(_lastTime);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_draggingText) return;
        _draggingText = false;
        e.Pointer.Capture(null);
        TextPositionChanged?.Invoke(_activeText.X, _activeText.Y, true);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_draggingText) return;
        _draggingText = false;
        _activeText.X = _dragOriginalX; _activeText.Y = _dragOriginalY;
        TextPositionChanged?.Invoke(_activeText.X, _activeText.Y, true);
    }

    private bool IsOverText(Point point)
    {
        if (_layout is null || !_activeText.IsVisible) return false;
        var width = Math.Max(_activeText.FontSize * .62, _activeText.Text.Length * _activeText.FontSize * .62);
        var height = _activeText.FontSize * 1.6;
        var left = AlignedOrigin(_activeText.X * _layout.Width, width, _activeText.HorizontalAlignment);
        var top = AlignedOrigin(_activeText.Y * _layout.Height, height, _activeText.VerticalAlignment);
        return point.X >= left && point.X <= left + width && point.Y >= top && point.Y <= top + height;
    }

    private static double AlignedOrigin(double anchor, double size, string alignment) => alignment switch
    {
        "Left" or "Top" => anchor,
        "Right" or "Bottom" => anchor - size,
        _ => anchor - size / 2,
    };

    private static Bitmap? Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    public void Release()
    {
        ++_generation;
        _renderCts?.Cancel(); _renderCts?.Dispose(); _renderCts = null;
    }
}
