using Microsoft.UI.Xaml;
using ScreenRecorderApp.Services.Export;
using ScreenRecorderApp.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace ScreenRecorderApp.Views;

public sealed partial class TrimExportWindow
{
    private bool _discarded;
    private async void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        if (_exporting || !_ready) return;
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Discard the original recording?",
            Content = "This permanently deletes the original video and its edit metadata. Exported copies are kept.",
            PrimaryButtonText = "Discard", CloseButtonText = "Cancel", DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        try
        {
            _discarded = true; _player?.Pause(); ReleasePlayer();
            await ViewModel.CloseAsync(false);
            await Task.Run(() =>
            {
                File.Delete(ViewModel.FilePath);
                File.Delete(RecordingMetadata.GetPath(ViewModel.FilePath)); File.Delete(ZoomRegionStore.GetPath(ViewModel.FilePath));
            });
            await RequestCloseAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lastError = ex.ToString();
            if (File.Exists(ViewModel.FilePath)) { _discarded = false; ViewModel.ResumeEditing(); OpenPlayer(); }
            ViewModel.Status = "The recording could not be fully discarded. Check file access."; CopyErrorButton.Visibility = Visibility.Visible;
        }
    }
    private async void OnExportClick(object sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void OnExportGifClick(object sender, RoutedEventArgs e) => await ExportAsync(true);
    private async Task ExportAsync(bool gif)
    {
        if (_exporting || !_ready) return;
        if (!double.IsFinite(ViewModel.TrimStart) || !double.IsFinite(ViewModel.TrimEnd) || ViewModel.TrimEnd <= ViewModel.TrimStart || ViewModel.TrimEnd > ViewModel.Duration)
        { ViewModel.Status = "Choose a valid trim range before exporting."; return; }
        _exporting = true; SetExporting(true);
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = ViewModel.Title + "_edited", SuggestedStartLocation = PickerLocationId.VideosLibrary };
            picker.FileTypeChoices.Add(gif ? "Animated GIF" : "MP4 video", new List<string> { gif ? ".gif" : ".mp4" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null || _closed) return;
            _exportCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            await ViewModel.SaveAsync(); _player?.Pause();
            var zooms = ViewModel.ZoomRegions.Select(r => new ZoomRegion { StartSeconds = r.StartSeconds, EndSeconds = r.EndSeconds, CenterX = r.CenterX, CenterY = r.CenterY, Scale = r.Scale, Enabled = r.Enabled }).ToList();
            var options = new ExportSettings { Quality = ViewModel.Export.Quality, Encoder = ViewModel.Export.Encoder, FrameRate = ViewModel.Export.FrameRate, IncludeAudio = ViewModel.Export.IncludeAudio };
            var progress = new Progress<GifExportProgress>(p => DispatcherQueue.TryEnqueue(() =>
            { if (_closed) return; ExportStageText.Text = p.Stage; ExportProgress.Value = p.PercentComplete; }));
            await CompositionExportService.ExportAsync(ViewModel.FilePath, TimeSpan.FromSeconds(ViewModel.TrimStart),
                TimeSpan.FromSeconds(ViewModel.TrimEnd - ViewModel.TrimStart), file.Path, gif, ViewModel.Presentation.Clone(), zooms,
                options, progress, _exportCts.Token, ViewModel.Metadata);
            _lastOutput = file.Path;
            if (!_closed) { ViewModel.Status = $"Exported · {Path.GetFileName(file.Path)}"; OpenOutputButton.Visibility = Visibility.Visible; CopyErrorButton.Visibility = Visibility.Collapsed; }
        }
        catch (OperationCanceledException) { if (!_closed) ViewModel.Status = "Export cancelled. Your original recording and edits are safe."; }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            if (!_closed)
            {
                ViewModel.Status = "Export failed. Your original is safe. Check the output folder or choose Software, then export again.";
                CopyErrorButton.Visibility = Visibility.Visible; OpenOutputButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _exportCts?.Dispose(); _exportCts = null; _exporting = false;
            if (!_closed) SetExporting(false);
        }
    }
    private void SetExporting(bool busy)
    {
        SetControlsEnabled(Inspector, !busy); SetControlsEnabled(TimelinePanel, !busy); SetControlsEnabled(EditCommands, !busy);
        ExportProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ExportProgress.Value = 0; ExportStageText.Text = "Choose an export destination…";
    }
    private static void SetControlsEnabled(DependencyObject parent, bool enabled)
    {
        if (parent is Microsoft.UI.Xaml.Controls.Control control) control.IsEnabled = enabled;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            SetControlsEnabled(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i), enabled);
    }
    private void OnCancelExportClick(object sender, RoutedEventArgs e) => _exportCts?.Cancel();
    private void OnCopyErrorClick(object sender, RoutedEventArgs e)
    { var data = new DataPackage(); data.SetText(_lastError ?? "No technical details available."); Clipboard.SetContent(data); }
    private async void OnOpenOriginalClick(object sender, RoutedEventArgs e)
    { try { await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(ViewModel.FilePath)); } catch { ViewModel.Status = "The original file could not be opened externally."; } }
    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    { try { await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(_lastOutput ?? ViewModel.FilePath)!); } catch { ViewModel.Status = "The output folder could not be opened."; } }
    private string? _timelinePath;
    private async Task LoadTimelineAsync()
    {
        try
        {
            var path = await TimelineThumbnailService.CreateAsync(ViewModel.FilePath, ViewModel.Duration, _lifetime.Token);
            if (path is null) return;
            if (_closed) { try { File.Delete(path); } catch { } return; }
            _timelinePath = path; Timeline.SetThumbnails(path);
        }
        catch (OperationCanceledException) { }
        catch { /* A thumbnail failure never prevents playback or export. */ }
    }
}
