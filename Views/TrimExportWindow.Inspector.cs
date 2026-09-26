using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenRecorderApp.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenRecorderApp.Views;

public sealed partial class TrimExportWindow
{
    private readonly List<string> _recentColours = ["#202838", "#DCE2E8", "#26374A", "#A89B88", "#86596C", "#21463F"];
    private string? _thumbnailBackgroundPath;
    private void RefreshBackgroundControls()
    {
        var p = ViewModel.Presentation;
        GradientPalettePanel.Visibility = GradientSettingsPanel.Visibility = p.BackgroundMode == "gradient" ? Visibility.Visible : Visibility.Collapsed;
        ColourSettingsPanel.Visibility = p.BackgroundMode is "gradient" or "solid" ? Visibility.Visible : Visibility.Collapsed;
        ImageSettingsPanel.Visibility = p.BackgroundMode == "image" ? Visibility.Visible : Visibility.Collapsed;
        NoBackgroundNote.Visibility = p.BackgroundMode == "none" ? Visibility.Visible : Visibility.Collapsed;
        if (_thumbnailBackgroundPath != p.BackgroundPath)
        {
            _thumbnailBackgroundPath = p.BackgroundPath;
            try { BackgroundThumbnail.Source = File.Exists(p.BackgroundPath) ? new BitmapImage(new Uri(p.BackgroundPath)) : null; }
            catch { BackgroundThumbnail.Source = null; }
        }
    }
    private void RenderColourSwatches()
    {
        ColourSwatches.Children.Clear();
        foreach (var hex in _recentColours)
        {
            var button = new Button { Width = 36, Height = 32, MinHeight = 32, Padding = new(0), CornerRadius = new(8), Background = new SolidColorBrush(ParseColor(hex)), Tag = hex };
            ToolTipService.SetToolTip(button, hex); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Use colour " + hex);
            button.Click += (_, _) => { ViewModel.Presentation.BackgroundColor = hex; };
            ColourSwatches.Children.Add(button);
        }
    }
    private void RememberColour()
    {
        var hex = ViewModel.Presentation.BackgroundColor;
        _recentColours.Remove(hex); _recentColours.Insert(0, hex);
        if (_recentColours.Count > 6) _recentColours.RemoveAt(6);
        RenderColourSwatches();
    }
    private void OnColourPickerOpening(object? sender, object e)
    { _syncing = true; BackgroundColourPicker.Color = ParseColor(ViewModel.Presentation.BackgroundColor); _syncing = false; }
    private void OnColourPickerClosed(object? sender, object e) => RememberColour();
    private void OnHexLostFocus(object sender, RoutedEventArgs e) { if (_ready) RememberColour(); }
    private void OnInspectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InspectorSections is null || InspectorTabs.SelectedItem is not GridViewItem item) return;
        foreach (var child in InspectorSections.Children.OfType<FrameworkElement>())
            child.Visibility = child.Name == $"{item.Tag}Section" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing || AspectPicker.SelectedItem is not GridViewItem item) return;
        var p = ViewModel.Presentation;
        var size = p.ResolveCanvas(ViewModel.SourceWidth, ViewModel.SourceHeight);
        var custom = (string)item.Tag == "Custom";
        p.CanvasWidth = custom ? size.Width : 0; p.CanvasHeight = custom ? size.Height : 0; p.CanvasPreset = (string)item.Tag;
        _syncing = true; CanvasSizePicker.SelectedIndex = custom ? 4 : 0; _syncing = false;
        CustomCanvasFields.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnCanvasSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        var p = ViewModel.Presentation;
        CustomCanvasFields.Visibility = CanvasSizePicker.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (CanvasSizePicker.SelectedIndex == 0) { p.CanvasWidth = 0; p.CanvasHeight = 0; return; }
        var (w, h) = p.ResolveCanvas(ViewModel.SourceWidth, ViewModel.SourceHeight);
        if (CanvasSizePicker.SelectedIndex == 4) { p.CanvasWidth = w; p.CanvasHeight = h; p.CanvasPreset = "Custom"; return; }
        var edge = CanvasSizePicker.SelectedIndex switch { 2 => 1440, 3 => 2160, _ => 1080 };
        var scale = (double)edge / Math.Min(w, h);
        p.CanvasWidth = PresentationSettings.Even((int)(w * scale)); p.CanvasHeight = PresentationSettings.Even((int)(h * scale));
    }
    private sealed record GradientPreset(string Name, string A, string B);
    private void BuildGradientPresets()
    {
        GradientPreset[] presets = [new("Midnight", "#182435", "#46526D"), new("Aurora", "#294F54", "#AAA0C3"),
            new("Sunset", "#9C6475", "#E6BB9C"), new("Ocean", "#1C4359", "#78A5B2"), new("Purple Glow", "#343052", "#8D81B1"),
            new("Graphite", "#252A32", "#646E7E"), new("Warm Sand", "#A89B88", "#E9DFCB"), new("Arctic", "#91ABB8", "#DCE9EE"),
            new("Neon Blue", "#26365B", "#598CC0"), new("Rose", "#86596C", "#D6ACBC"), new("Emerald", "#21463F", "#70A28C")];
        foreach (var preset in presets)
        {
            var brush = new LinearGradientBrush { StartPoint = new(0,0), EndPoint = new(1,1) };
            brush.GradientStops.Add(new() { Color = ParseColor(preset.A), Offset = 0 }); brush.GradientStops.Add(new() { Color = ParseColor(preset.B), Offset = 1 });
            var swatch = new StackPanel { Width = 78, Spacing = 4, Tag = preset, Margin = new(0,0,0,4) };
            swatch.Children.Add(new Border { Height = 44, CornerRadius = new(8), Background = brush });
            swatch.Children.Add(new TextBlock { Text = preset.Name, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, preset.Name);
            GradientPresets.Items.Add(swatch);
        }
    }
    private static Windows.UI.Color ParseColor(string hex) => ColorHelper.FromArgb(255, Convert.ToByte(hex.Substring(1,2),16), Convert.ToByte(hex.Substring(3,2),16), Convert.ToByte(hex.Substring(5,2),16));
    private void OnGradientClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FrameworkElement { Tag: GradientPreset preset })
        { ViewModel.Presentation.BackgroundMode = "gradient"; ViewModel.Presentation.BackgroundColor = preset.A; ViewModel.Presentation.BackgroundColor2 = preset.B; }
    }
    private void OnColourChanged(ColorPicker sender, ColorChangedEventArgs e)
    { if (_ready && !_syncing) ViewModel.Presentation.BackgroundColor = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}"; }
    private async Task<string?> PickImageAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }
    private async void OnChooseBackgroundClick(object sender, RoutedEventArgs e)
    {
        var path = await PickImageAsync(); if (path is null || _closed) return;
        ViewModel.Presentation.BackgroundPath = path; ViewModel.Presentation.BackgroundMode = "image";
        BackgroundThumbnail.Source = new BitmapImage(new Uri(path));
    }
    private void OnRemoveBackgroundClick(object sender, RoutedEventArgs e)
    { ViewModel.Presentation.BackgroundPath = ""; ViewModel.Presentation.BackgroundMode = "solid"; BackgroundThumbnail.Source = null; }
    private async void OnChooseWatermarkClick(object sender, RoutedEventArgs e)
    {
        var path = await PickImageAsync(); if (path is null || _closed) return;
        ViewModel.Presentation.WatermarkPath = path; ViewModel.Presentation.WatermarkEnabled = true;
    }
    private void OnRemoveWatermarkClick(object sender, RoutedEventArgs e)
    { ViewModel.Presentation.WatermarkEnabled = false; ViewModel.Presentation.WatermarkPath = ""; }
    private void OnCornerPresetClick(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string radius }) ViewModel.Presentation.CornerRadius = double.Parse(radius); }
    private void OnShadowPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing || sender is not ComboBox box || box.SelectedIndex == 5) return;
        var p = ViewModel.Presentation;
        var index = box.SelectedIndex;
        p.Shadow = index > 0; p.ShadowOffsetX = 0;
        (p.ShadowBlur, p.ShadowOpacity, p.ShadowOffsetY) = index switch
        { 2 => (32, .35, 16), 3 => (48, .4, 28), 4 => (32, .6, 20), _ => (24, .28, 12) };
    }
}
