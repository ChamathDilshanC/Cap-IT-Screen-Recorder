using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Export;
using System.Numerics;
using Windows.Storage.Streams;

namespace ScreenRecorderApp.Views.Controls;

public sealed partial class VideoPreviewCanvas : UserControl
{
    public MediaPlayerElement Player => VideoPlayer;
    public event Action<string?>? AssetWarning;
    private CancellationTokenSource? _renderCts;
    private CompositionRoundedRectangleGeometry? _geometry;
    private CompositionLayout? _layout;
    private int _sourceWidth = 1920, _sourceHeight = 1080;
    private PixelRect? _lastCrop;
    private int _generation;
    private string? _lastSettings;
    private PresentationTextOverlay _lastText = new();

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
        _lastText = snapshot.TextOverlay;
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
            BuildLetterPreview(l, _lastText);
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
    private void BuildLetterPreview(CompositionLayout layout, PresentationTextOverlay text)
    {
        TextLettersCanvas.Children.Clear();
        TextLettersCanvas.Width = layout.Width; TextLettersCanvas.Height = layout.Height;
        TextLettersCanvas.Visibility = text.Animation == "BounceLetters" && text.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        TextLayer.Visibility = TextLettersCanvas.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (TextLettersCanvas.Visibility == Visibility.Collapsed) return;
        var width = text.FontSize * .62;
        var start = text.X * layout.Width - text.Text.Length * width / 2;
        for (var i = 0; i < text.Text.Length; i++)
        {
            var letter = new TextBlock
            {
                Text = text.Text[i].ToString(), FontSize = text.FontSize,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(text.FontFamily),
                Foreground = new SolidColorBrush(ParseTextColor(text.Color)),
                FontWeight = text.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                FontStyle = text.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
            };
            Canvas.SetLeft(letter, start + i * width); Canvas.SetTop(letter, text.Y * layout.Height - text.FontSize / 2);
            TextLettersCanvas.Children.Add(letter);
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
            TextLayer.Opacity = 0;
            for (var i = 0; i < TextLettersCanvas.Children.Count; i++)
            {
                if (TextLettersCanvas.Children[i] is not UIElement element) continue;
                var stagger = i * Math.Min(.08, duration / Math.Max(1, TextLettersCanvas.Children.Count * 2d));
                var local = Math.Clamp((seconds - stagger) / duration, 0, 1);
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
        switch (text.Animation)
        {
            case "Bounce":
                transform.TranslateX = Math.Sin(progress * Math.PI * 3) * _layout.Width * .018 * (1 - progress);
                break;
            case "Fade": TextLayer.Opacity = progress; break;
            case "Pop":
                transform.ScaleX = transform.ScaleY = .75 + .25 * progress;
                break;
            case "SlideUp":
                transform.TranslateY = _layout.Height * .08 * (1 - progress);
                break;
            default: TextLayer.Opacity = 1; break;
        }
        TextLayer.RenderTransform = transform;
        if (text.Animation != "Fade") TextLayer.Opacity = 1;
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
    private static async Task<BitmapImage> DecodeAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
    }
    public void Dispose() { ++_generation; _renderCts?.Cancel(); _renderCts?.Dispose(); _renderCts = null; }
}
