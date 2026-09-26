using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Input;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Export;
using System.Numerics;
using Windows.Storage.Streams;

namespace ScreenRecorderApp.Views.Controls;

public sealed partial class VideoPreviewCanvas : UserControl
{
    public MediaPlayerElement Player => VideoPlayer;
    public event Action<string?>? AssetWarning;
    public event Action<double, double, bool>? TextPositionChanged;
    private CancellationTokenSource? _renderCts;
    private CompositionRoundedRectangleGeometry? _geometry;
    private CompositionLayout? _layout;
    private int _sourceWidth = 1920, _sourceHeight = 1080;
    private PixelRect? _lastCrop;
    private int _generation;
    private string? _lastSettings;
    private PresentationTextOverlay _lastText = new();
    private IReadOnlyList<PresentationTextOverlay> _lastTexts = [];
    private int _activeTextIndex;
    private bool _draggingText;
    private uint _dragPointerId;
    private Windows.Foundation.Point _dragStart;
    private double _dragOriginalX, _dragOriginalY;
    private double _renderedTextX, _renderedTextY;

    public VideoPreviewCanvas() => InitializeComponent();

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
        _lastTexts = texts;
        _activeTextIndex = Math.Clamp(_activeTextIndex, 0, Math.Max(0, texts.Count - 1));
        _lastText = texts.ElementAtOrDefault(_activeTextIndex) ?? new();
        // Geometry is cheap. Commit it together with the matching static artwork to avoid stale shadows.
        try
        {
            await Task.Delay(120, ct);
            var assets = await Task.Run(() => CompositionAssetRenderer.Render(snapshot, sourceWidth, sourceHeight, ct), ct);
            var bg = await DecodeAsync(assets.Background);
            var overlay = await DecodeAsync(assets.Overlay);
            var text = await DecodeAsync(assets.Text);
            if (generation != _generation || ct.IsCancellationRequested) return;
            _layout = assets.Layout; _lastCrop = null;
            var l = _layout;
            CompositionCanvas.Width = BackgroundLayer.Width = OverlayLayer.Width = TextLayer.Width = l.Width;
            CompositionCanvas.Height = BackgroundLayer.Height = OverlayLayer.Height = TextLayer.Height = l.Height;
            CompositionCanvas.Clip = new RectangleGeometry { Rect = new(0, 0, l.Width, l.Height) };
            VideoViewport.Width = l.Video.Width; VideoViewport.Height = l.Video.Height;
            Canvas.SetLeft(VideoViewport, l.Video.X); Canvas.SetTop(VideoViewport, l.Video.Y);
            var visual = ElementCompositionPreview.GetElementVisual(VideoViewport);
            if (_geometry is null)
            {
                _geometry = visual.Compositor.CreateRoundedRectangleGeometry();
                visual.Clip = visual.Compositor.CreateGeometricClip(_geometry);
            }
            _geometry.Size = new Vector2(l.Video.Width, l.Video.Height);
            _geometry.CornerRadius = new Vector2((float)l.Radius);
            BackgroundLayer.Source = bg; OverlayLayer.Source = overlay; TextLayer.Source = text;
            BuildLetterPreview(l, texts);
            _renderedTextX = _lastText.X; _renderedTextY = _lastText.Y;
            SetPosition(_lastTime, _lastRegions);
            AssetWarning?.Invoke(assets.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) AssetWarning?.Invoke("Composition preview could not update. Try a smaller canvas. " + ex.GetType().Name); }
    }
    private double _lastTime;
    private IReadOnlyList<ZoomRegion> _lastRegions = [];
    public void SetPosition(double seconds, IReadOnlyList<ZoomRegion> regions)
    {
        _lastTime = seconds; _lastRegions = regions;
        if (_layout is null) return;
        UpdateTextAnimation(seconds);
        var crop = CompositionLayout.SourceCrop(_sourceWidth, _sourceHeight, _layout.Video, CompositionLayout.ActiveZoom(regions, seconds));
        if (crop == _lastCrop) return;
        _lastCrop = crop;
        var sx = (double)_layout.Video.Width / crop.Width; var sy = (double)_layout.Video.Height / crop.Height;
        VideoPlayer.Width = _sourceWidth * sx; VideoPlayer.Height = _sourceHeight * sy;
        Canvas.SetLeft(VideoPlayer, -crop.X * sx); Canvas.SetTop(VideoPlayer, -crop.Y * sy);
    }
    private void BuildLetterPreview(CompositionLayout layout, IReadOnlyList<PresentationTextOverlay> texts)
    {
        TextLettersCanvas.Children.Clear();
        TextLettersCanvas.Width = layout.Width; TextLettersCanvas.Height = layout.Height;
        TextLettersCanvas.Visibility = texts.Any(t => t.Animation == "BounceLetters" && t.IsVisible) ? Visibility.Visible : Visibility.Collapsed;
        TextLayer.Visibility = Visibility.Visible;
        if (TextLettersCanvas.Visibility == Visibility.Collapsed) return;
        foreach (var text in texts.Where(t => t.Animation == "BounceLetters" && t.IsVisible))
        {
            var width = text.FontSize * .62;
            var totalWidth = text.Text.Length * width;
            var start = AlignedOrigin(text.X * layout.Width, totalWidth, text.HorizontalAlignment);
            for (var i = 0; i < text.Text.Length; i++)
            {
                var letter = new TextBlock
                {
                    Text = text.Text[i].ToString(), FontSize = text.FontSize,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(text.FontFamily),
                    Foreground = new SolidColorBrush(ParseTextColor(text.Color)) { Opacity = text.Opacity },
                    FontWeight = text.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    FontStyle = text.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                    Tag = text.AnimationDuration
                };
                Canvas.SetLeft(letter, start + i * width);
                Canvas.SetTop(letter, AlignedOrigin(text.Y * layout.Height, text.FontSize * 1.6, text.VerticalAlignment));
                TextLettersCanvas.Children.Add(letter);
            }
        }
    }
    private void UpdateTextAnimation(double seconds)
    {
        if (_layout is null) return;
        var text = _lastText;
        var duration = Math.Max(.1, text.AnimationDuration);
        var progress = Math.Clamp(seconds / duration, 0, 1);
        if (text.Animation == "BounceLetters")
        {
            TextLayer.Opacity = 1;
            TextLettersCanvas.RenderTransform = new TranslateTransform
            {
                X = (text.X - _renderedTextX) * _layout.Width,
                Y = (text.Y - _renderedTextY) * _layout.Height
            };
            for (var i = 0; i < TextLettersCanvas.Children.Count; i++)
            {
                if (TextLettersCanvas.Children[i] is not FrameworkElement element) continue;
                var letterDuration = element.Tag is double value ? value : duration;
                var stagger = i * Math.Min(.08, letterDuration / Math.Max(1, TextLettersCanvas.Children.Count * 2d));
                var local = Math.Clamp((seconds - stagger) / letterDuration, 0, 1);
                var letterTransform = new TranslateTransform
                {
                    Y = _layout.Height * .12 * (1 - local) -
                        Math.Sin(local * Math.PI * 2) * _layout.Height * .055 * (1 - local)
                };
                element.RenderTransform = letterTransform;
                element.Opacity = seconds >= stagger ? 1 : 0;
            }
            return;
        }
        TextLayer.Opacity = 1;
        var transform = new CompositeTransform
        {
            CenterX = text.X * _layout.Width, CenterY = text.Y * _layout.Height,
            ScaleX = 1, ScaleY = 1, TranslateY = 0, TranslateX = 0
        };
        transform.TranslateX = (text.X - _renderedTextX) * _layout.Width;
        transform.TranslateY = (text.Y - _renderedTextY) * _layout.Height;
        switch (text.Animation)
        {
            case "Bounce":
                transform.TranslateX += Math.Sin(progress * Math.PI * 3) * _layout.Width * .018 * (1 - progress);
                break;
            case "Fade": TextLayer.Opacity = progress; break;
            case "Pop":
                transform.ScaleX = transform.ScaleY = .75 + .25 * progress;
                break;
            case "SlideUp":
                transform.TranslateY += _layout.Height * .08 * (1 - progress);
                break;
            default: TextLayer.Opacity = 1; break;
        }
        TextLayer.RenderTransform = transform;
        if (text.Animation != "Fade") TextLayer.Opacity = 1;
    }
    private void OnCompositionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_layout is null || !_lastText.IsVisible) return;
        var point = e.GetCurrentPoint(CompositionCanvas).Position;
        var width = Math.Max(_lastText.FontSize * .62, _lastText.Text.Length * _lastText.FontSize * .62);
        var height = _lastText.FontSize * 1.6;
        var left = AlignedOrigin(_lastText.X * _layout.Width, width, _lastText.HorizontalAlignment);
        var top = AlignedOrigin(_lastText.Y * _layout.Height, height, _lastText.VerticalAlignment);
        if (point.X < left || point.X > left + width || point.Y < top || point.Y > top + height) return;
        _draggingText = true; _dragPointerId = e.Pointer.PointerId; _dragStart = point;
        _dragOriginalX = _lastText.X; _dragOriginalY = _lastText.Y;
        CompositionCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }
    public void SetActiveTextIndex(int index)
    {
        _activeTextIndex = Math.Clamp(index, 0, Math.Max(0, _lastTexts.Count - 1));
        _lastText = _lastTexts.ElementAtOrDefault(_activeTextIndex) ?? new();
    }
    private void OnCompositionPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingText || e.Pointer.PointerId != _dragPointerId || _layout is null) return;
        var point = e.GetCurrentPoint(CompositionCanvas).Position;
        _lastText.X = Math.Clamp(_dragOriginalX + (point.X - _dragStart.X) / _layout.Width, 0, 1);
        _lastText.Y = Math.Clamp(_dragOriginalY + (point.Y - _dragStart.Y) / _layout.Height, 0, 1);
        TextPositionChanged?.Invoke(_lastText.X, _lastText.Y, false);
        UpdateTextAnimation(_lastTime);
        e.Handled = true;
    }
    private void OnCompositionPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingText || e.Pointer.PointerId != _dragPointerId) return;
        _draggingText = false; CompositionCanvas.ReleasePointerCapture(e.Pointer);
        TextPositionChanged?.Invoke(_lastText.X, _lastText.Y, true);
        e.Handled = true;
    }
    private void OnCompositionPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingText || e.Pointer.PointerId != _dragPointerId) return;
        _draggingText = false; CompositionCanvas.ReleasePointerCapture(e.Pointer);
        _lastText.X = _dragOriginalX; _lastText.Y = _dragOriginalY;
        TextPositionChanged?.Invoke(_lastText.X, _lastText.Y, true);
    }
    private static Windows.UI.Color ParseTextColor(string value)
    {
        try
        {
            var hex = value.TrimStart('#');
            return Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
        }
        catch { return Colors.White; }
    }
    private static double AlignedOrigin(double anchor, double size, string alignment) =>
        alignment switch
        {
            "Left" or "Top" => anchor,
            "Right" or "Bottom" => anchor - size,
            _ => anchor - size / 2
        };
    private static async Task<BitmapImage> DecodeAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
    }
    public void Dispose() { ++_generation; _renderCts?.Cancel(); _renderCts?.Dispose(); _renderCts = null; }
}
