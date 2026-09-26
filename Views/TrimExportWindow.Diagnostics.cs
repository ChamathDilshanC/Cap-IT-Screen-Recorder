#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenRecorderApp.Models;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace ScreenRecorderApp.Views;

public sealed partial class TrimExportWindow
{
    // Opt-in local smoke harness. No diagnostic controls or behavior in release builds.
    private async Task RunDiagnosticsAsync()
    {
        var directory = Environment.GetEnvironmentVariable("CAPIT_REVIEW_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var log = new List<string>();
        void Assert(bool pass, string name) { log.Add((pass ? "PASS " : "FAIL ") + name); }
        try
        {
            await Task.Delay(2200);
            Assert(_player is not null && PlaybackErrorPanel.Visibility == Visibility.Collapsed, "Media opened without failure");
            Assert(_player?.PlaybackSession.NaturalVideoWidth > 0, "Native video dimensions");
            var compatibility = Environment.GetEnvironmentVariable("CAPIT_REVIEW_COMPATIBILITY");
            if (compatibility == "legacy")
            {
                Assert(ViewModel.Presentation.CanvasPreset == "4:5" && ViewModel.Presentation.VideoScale == .82 && !ViewModel.Presentation.Shadow, "Legacy style restored");
                Assert(ViewModel.TrimStart == .5 && ViewModel.TrimEnd == 2.5, "Legacy trim restored");
                Assert(ViewModel.ZoomRegions.Count == 1 && ViewModel.ZoomRegions[0].StartSeconds == 1, "Original zoom times restored");
                return;
            }
            if (compatibility == "missing")
            {
                Assert(ViewModel.Presentation.CanvasPreset == "Original" && ViewModel.Presentation.VideoScale == .92, "Missing metadata uses meaningful defaults");
                Assert(ViewModel.SourceWidth == 320 && ViewModel.SourceHeight == 180 && ViewModel.Duration > 3.9, "Missing metadata probes the recording");
                return;
            }
            _player?.Play(); await Task.Delay(600);
            Assert(_player?.PlaybackSession.Position.TotalSeconds > .1, "Playback advances");
            _player?.Pause(); Seek(Math.Min(1, ViewModel.Duration / 2)); await Task.Delay(300);
            Assert(Math.Abs((_player?.PlaybackSession.Position.TotalSeconds ?? -1) - Math.Min(1, ViewModel.Duration / 2)) < .12, "Pause and seek");
            var frameTime = _player?.PlaybackSession.Position.TotalSeconds ?? 0;
            _player?.StepForwardOneFrame(); await Task.Delay(200);
            Assert(_player?.PlaybackSession.Position.TotalSeconds > frameTime, "Frame step");
            MuteButton.IsChecked = true; OnMuteClick(this, new RoutedEventArgs());
            Assert(_player?.IsMuted == true, "Mute control");
            BackgroundModes.SelectedValue = "solid";
            Assert(ViewModel.Presentation.BackgroundMode == "solid", "Background mode binding");
            Assert(GradientSettingsPanel.Visibility == Visibility.Collapsed, "Only relevant background controls shown");
            BackgroundModes.SelectedValue = "gradient";
            var scale = FindControls<Slider>(VideoSection).First(s => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(s) == "Video scale");
            scale.Value = .75; await Task.Delay(80);
            Assert(Math.Abs(ViewModel.Presentation.VideoScale - .75) < .001, "Two-way scale binding");
            var padding = FindControls<Slider>(CanvasSection).First(); padding.Value = 80;
            await Task.Delay(80); Assert(ViewModel.Presentation.Padding == 80, "Two-way padding binding");
            for (var i = 0; i < 9; i++)
            {
                InspectorTabs.SelectedIndex = i;
                Assert(InspectorSections.Children.OfType<FrameworkElement>().Count(c => c.Visibility == Visibility.Visible) == 1, "Inspector section " + i);
                await Task.Delay(50);
                foreach (var combo in FindControls<ComboBox>(InspectorSections).Where(c => c.Items.Count > 0))
                    Assert(combo.SelectedIndex >= 0, "Nonblank selector: " + (combo.Header ?? combo.Name));
            }
            ViewModel.Presentation.VideoOffsetX = double.NaN;
            ViewModel.TrimEnd = double.NaN;
            Assert(ViewModel.Presentation.VideoOffsetX == 0 && ViewModel.TrimEnd == ViewModel.Duration, "Empty numeric fields recover safely");
            var before = ViewModel.Presentation.CornerRadius;
            var after = before == 64 ? 20 : 64;
            ViewModel.Presentation.CornerRadius = after;
            ViewModel.UndoCommand.Execute(null);
            Assert(ViewModel.Presentation.CornerRadius == before, "Undo restores styling");
            ViewModel.RedoCommand.Execute(null);
            Assert(ViewModel.Presentation.CornerRadius == after, "Redo restores styling");
            ViewModel.PresetName = "Smoke custom";
            await ViewModel.SavePresetCommand.ExecuteAsync(null);
            Assert(ViewModel.SelectedPreset is { Name: "Smoke custom", IsBuiltIn: false }, "Save custom preset");
            ViewModel.PresetName = "Renamed smoke custom";
            await ViewModel.RenamePresetCommand.ExecuteAsync(null);
            Assert(ViewModel.SelectedPreset?.Name == "Renamed smoke custom", "Rename custom preset");
            await ViewModel.DeletePresetCommand.ExecuteAsync(null);
            Assert(ViewModel.Presets.All(p => p.Name != "Renamed smoke custom"), "Delete custom preset");
            var builtInCount = ViewModel.Presets.Count;
            await ViewModel.DeletePresetCommand.ExecuteAsync(null);
            Assert(ViewModel.Presets.Count == builtInCount, "Built-in presets protected");
            ViewModel.Status = "Original recording preserved · edits save automatically";
            ViewModel.Presentation.CanvasPreset = "9:16";
            await Task.Delay(800);
            Assert(AspectPicker.SelectedItem is GridViewItem { Tag: "9:16" }, "Aspect selection restored");
            ViewModel.Presentation.CanvasPreset = "Original";
            ViewModel.Presentation.Padding = 24; ViewModel.Presentation.VideoScale = .92;
            InspectorTabs.SelectedIndex = 0;
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.System })
            {
                ApplyTheme(theme); await Task.Delay(500);
                Assert(Preview.ActualWidth > 100 && Preview.ActualHeight > 60, theme + " preview has usable size");
                await SaveLayoutImageAsync(Path.Combine(directory, "layout-" + theme + ".png"));
            }
            foreach (var size in new[] { (1366, 768), (1100, 700), (900, 650) })
            {
                AppWindow.Resize(new(size.Item1, size.Item2)); await Task.Delay(400);
                Assert(Preview.ActualWidth > 100 && Timeline.ActualWidth > 250, $"Responsive layout {size}");
                await SaveLayoutImageAsync(Path.Combine(directory, $"layout-{size.Item1}.png"));
            }
            OnFullscreenClick(this, new RoutedEventArgs()); await Task.Delay(300);
            Assert(_fullScreen && TimelinePanel.Visibility == Visibility.Collapsed, "Full screen preview");
            OnFullscreenClick(this, new RoutedEventArgs());
            await ViewModel.SaveAsync();
            Assert(RecordingMetadata.Load(ViewModel.FilePath)?.Presentation.Padding == 24, "Editor autosave roundtrip");
        }
        catch (Exception ex) { log.Add("FAIL " + ex); }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "ui-smoke.txt"), log);
            await RequestCloseAsync();
        }
    }
    private static IEnumerable<T> FindControls<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T control) yield return control;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in FindControls<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private async Task SaveLayoutImageAsync(string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(Root);
        if (bitmap.PixelWidth == 0) return;
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var file = await StorageFile.GetFileFromPathAsync(await CreateImageFileAsync(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    private static async Task<string> CreateImageFileAsync(string path) { await File.WriteAllBytesAsync(path, []); return path; }
}
#endif
