using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        // Geometry is cheap. Commit it together with the matching static artwork to avoid stale shadows.
        try
        {
            await Task.Delay(120, ct);
            var assets = await Task.Run(() => CompositionAssetRenderer.Render(snapshot, sourceWidth, sourceHeight, ct), ct);
            var bg = await DecodeAsync(assets.Background);
            var overlay = await DecodeAsync(assets.Overlay);
            if (generation != _generation || ct.IsCancellationRequested) return;
            _layout = assets.Layout; _lastCrop = null;
            var l = _layout;
            CompositionCanvas.Width = BackgroundLayer.Width = OverlayLayer.Width = l.Width;
            CompositionCanvas.Height = BackgroundLayer.Height = OverlayLayer.Height = l.Height;
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
            BackgroundLayer.Source = bg; OverlayLayer.Source = overlay;
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
        var crop = CompositionLayout.SourceCrop(_sourceWidth, _sourceHeight, _layout.Video, CompositionLayout.ActiveZoom(regions, seconds));
        if (crop == _lastCrop) return;
        _lastCrop = crop;
        var sx = (double)_layout.Video.Width / crop.Width; var sy = (double)_layout.Video.Height / crop.Height;
        VideoPlayer.Width = _sourceWidth * sx; VideoPlayer.Height = _sourceHeight * sy;
        Canvas.SetLeft(VideoPlayer, -crop.X * sx); Canvas.SetTop(VideoPlayer, -crop.Y * sy);
    }
    private static async Task<BitmapImage> DecodeAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
    }
    public void Dispose() { ++_generation; _renderCts?.Cancel(); _renderCts?.Dispose(); _renderCts = null; }
}
