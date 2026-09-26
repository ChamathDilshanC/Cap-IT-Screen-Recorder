using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Views.Controls;

public sealed partial class EditorTimeline : UserControl
{
    public event Action<double>? SeekRequested;
    public event Action<double, double>? TrimChanged;
    private double _duration = 1, _start, _end = 1, _position;
    private IReadOnlyList<ZoomRegion> _regions = [];
    public EditorTimeline() => InitializeComponent();
    public void SetThumbnails(string path) => Thumbnails.Source = new BitmapImage(new Uri(path));
    public void Update(double duration, double start, double end, double position, IReadOnlyList<ZoomRegion> regions)
    {
        _duration = Math.Max(.01, duration); _start = start; _end = end; _position = position; _regions = regions;
        Draw();
    }
    public void SetPosition(double position) { _position = position; DrawPlayhead(); }
    private void OnStartDrag(object sender, DragDeltaEventArgs e) => MoveHandle(true, e.HorizontalChange / Math.Max(1, ActualWidth - 24) * _duration);
    private void OnEndDrag(object sender, DragDeltaEventArgs e) => MoveHandle(false, e.HorizontalChange / Math.Max(1, ActualWidth - 24) * _duration);
    private void MoveHandle(bool start, double delta)
    {
        if (start) _start = Math.Clamp(_start + delta, 0, Math.Max(0, _end - .01));
        else _end = Math.Clamp(_end + delta, Math.Min(_duration, _start + .01), _duration);
        TrimChanged?.Invoke(_start, _end); Draw();
    }
    private void OnHandleKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)) return;
        MoveHandle(ReferenceEquals(sender, StartHandle), e.Key == Windows.System.VirtualKey.Left ? -.1 : .1); e.Handled = true;
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Draw();
    private void OnSeekPointer(object sender, PointerRoutedEventArgs e) =>
        SeekRequested?.Invoke(Math.Clamp(e.GetCurrentPoint((UIElement)sender).Position.X / Math.Max(1, ActualWidth - 24), 0, 1) * _duration);
    private Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private void Draw()
    {
        if (RangeCanvas is null) return;
        var w = Math.Max(1, ActualWidth - 24);
        Ruler.Children.Clear(); RangeCanvas.Children.Clear(); ZoomTrack.Children.Clear();
        for (var i = 0; i <= 6; i++)
        {
            var label = new TextBlock { Text = TimeSpan.FromSeconds(_duration * i / 6).ToString(_duration < 10 ? @"ss\.f" : @"mm\:ss"), FontSize = 10, Opacity = .6 };
            Canvas.SetLeft(label, Math.Max(0, w * i / 6 - (i == 6 ? 32 : 0))); Ruler.Children.Add(label);
        }
        var beforeTrim = new Rectangle { Width = w * _start / _duration, Height = 40, Fill = Brush("StageBrush"), Opacity = .8 };
        var afterTrim = new Rectangle { Width = Math.Max(0, w * (1 - _end / _duration)), Height = 40, Fill = Brush("StageBrush"), Opacity = .8 };
        Canvas.SetLeft(afterTrim, w * _end / _duration);
        RangeCanvas.Children.Add(beforeTrim); RangeCanvas.Children.Add(afterTrim);
        var selection = new Border { Width = Math.Max(2, w * (_end - _start) / _duration), Height = 40, CornerRadius = new(8), BorderThickness = new(2), BorderBrush = Brush("SystemControlHighlightAccentBrush") };
        Canvas.SetLeft(selection, w * _start / _duration); RangeCanvas.Children.Add(selection);
        Canvas.SetLeft(StartHandle, w * _start / _duration - 6); Canvas.SetLeft(EndHandle, w * _end / _duration - 6);
        foreach (var r in _regions.Where(r => r.Enabled))
        {
            var marker = new Border { Height = 18, Width = Math.Max(4, w * (r.EndSeconds - r.StartSeconds) / _duration), CornerRadius = new(4), Background = Brush("SystemControlHighlightAccentBrush"), Opacity = .65,
                Child = new TextBlock { Text = $"  {r.Scale:0.#}×", FontSize = 10, Foreground = Brush("TextOnAccentFillColorPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center } };
            Canvas.SetLeft(marker, Math.Clamp(w * r.StartSeconds / _duration, 0, w)); ZoomTrack.Children.Add(marker);
        }
        DrawPlayhead();
    }
    private void DrawPlayhead()
    {
        if (PlayheadCanvas is null) return;
        Canvas.SetLeft(Playhead, Math.Clamp(_position / _duration, 0, 1) * Math.Max(1, ActualWidth - 24));
    }
}
