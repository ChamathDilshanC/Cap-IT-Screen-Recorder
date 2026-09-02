using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenRecorderApp.Services.Export;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ScreenRecorderApp.Views;

/// <summary>
/// Shown after a successful recording (see MainViewModel.StopRecordingAsync) so the user can preview the
/// clip, trim it, and either keep the full MP4, export a trimmed GIF, or discard the recording outright.
/// </summary>
/// <remarks>
/// A plain top-level Window rather than a ContentDialog (both are confirmed available in this Windows
/// App SDK version, so the choice is purely about fit): GIF export can take real wall-clock time and
/// shouldn't force-block the whole app modally the way a ContentDialog would; the trim UI (video preview
/// + dual-thumb range + progress) needs more room than a dialog is meant to host; and "Discard" is
/// naturally just closing this window after deleting the file, with no dialog-result plumbing needed.
/// Follows the same second-Window interop shape already established by AnnotationOverlayWindow.
///
/// Step 1 built preview playback, the trim range, and Save/Discard, with GifExportService's real 2-pass
/// palettegen/paletteuse argument-building logic ready but unused. Step 2 (this revision) wires
/// <see cref="OnExportGifClick"/> up to actually run <see cref="GifExportService.ExportAsync"/> in the
/// background, with live progress reported into <c>ExportProgress</c>.
/// </remarks>
public sealed partial class TrimExportWindow : Window
{
    private readonly string _filePath;

    // Canceled if the window is closed while an export is still running, so a stray ffmpeg process never
    // outlives the window that started it.
    private CancellationTokenSource? _exportCts;

    public TrimExportWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        FilePathText.Text = filePath;

        // MediaPlayerElement.MediaPlayer is read-only — it auto-creates a default MediaPlayer instance on
        // first access rather than needing one assigned.
        Player.MediaPlayer.MediaOpened += OnMediaOpened;
        Player.Source = MediaSource.CreateFromUri(new Uri(filePath));

        Closed += (_, _) =>
        {
            _exportCts?.Cancel();
            Player.MediaPlayer?.Dispose();
        };
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        var duration = sender.PlaybackSession.NaturalDuration;
        var totalSeconds = duration.TotalSeconds > 0 ? duration.TotalSeconds : 1;

        // MediaOpened fires on MediaPlayer's own thread, not the UI thread — every property touched
        // below is bound to XAML, so it has to be marshaled back, same DispatcherQueue.TryEnqueue pattern
        // MainViewModel already uses for VideoCaptureService's off-thread CaptureTargetLost event.
        DispatcherQueue.TryEnqueue(() =>
        {
            TrimRange.Maximum = totalSeconds;
            TrimRange.RangeStart = 0;
            TrimRange.RangeEnd = totalSeconds;
            UpdateTrimLabels();
        });
    }

    private void TrimRange_ValueChanged(object sender, RangeChangedEventArgs e) => UpdateTrimLabels();

    private void UpdateTrimLabels()
    {
        var start = TimeSpan.FromSeconds(TrimRange.RangeStart);
        var end = TimeSpan.FromSeconds(TrimRange.RangeEnd);
        TrimStartText.Text = start.ToString(@"mm\:ss\.f");
        TrimEndText.Text = end.ToString(@"mm\:ss\.f");
        TrimStatusText.Text = $"Selected: {(end - start).TotalSeconds:0.0}s";
    }

    private async void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Discard this recording?",
            Content = "The recorded file will be permanently deleted. This can't be undone.",
            PrimaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        Player.MediaPlayer?.Dispose();
        try { File.Delete(_filePath); } catch { /* best effort — file may already be gone or briefly locked by the player */ }
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Close();

    private async void OnExportGifClick(object sender, RoutedEventArgs e)
    {
        var start = TimeSpan.FromSeconds(TrimRange.RangeStart);
        var duration = TimeSpan.FromSeconds(TrimRange.RangeEnd - TrimRange.RangeStart);
        if (duration <= TimeSpan.Zero) return;

        var outputGifPath = Path.ChangeExtension(_filePath, ".gif");

        _exportCts = new CancellationTokenSource();
        SetExportingState(true);
        ExportStageText.Text = "Generating palette…";
        ExportProgress.Value = 0;

        // GifExportService.ExportAsync reports progress from a Process.ErrorDataReceived callback — a
        // thread-pool thread, never the UI thread — so every callback explicitly re-enters via
        // DispatcherQueue.TryEnqueue, the same pattern OnMediaOpened above already uses. This doesn't
        // lean on Progress<T>'s own ambient-SynchronizationContext marshaling at all, deliberately: it's
        // correct regardless of whether WinUI installs one, and it matches how every other cross-thread
        // callback in this codebase already gets back to the UI thread.
        var progress = new Progress<GifExportProgress>(p => DispatcherQueue.TryEnqueue(() =>
        {
            ExportStageText.Text = p.Stage;
            ExportProgress.Value = p.PercentComplete;
        }));

        try
        {
            await GifExportService.ExportAsync(_filePath, start, duration, outputGifPath, progress, _exportCts.Token);
            ExportStageText.Text = "Done!";
            TrimStatusText.Text = $"Saved: {outputGifPath}";
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-export — nothing to report to anymore.
        }
        catch (Exception ex)
        {
            ExportStageText.Text = "Export failed.";
            TrimStatusText.Text = ex.Message;
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
            SetExportingState(false);
        }
    }

    /// <summary>Locks down every control that could change trim/file state mid-export (per the brief) — Save is included too, since letting the window close mid-export would abandon a running ffmpeg process with nothing left to report progress to.</summary>
    private void SetExportingState(bool exporting)
    {
        ExportProgressPanel.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        TrimRange.IsEnabled = !exporting;
        ExportGifButton.IsEnabled = !exporting;
        DiscardButton.IsEnabled = !exporting;
        SaveButton.IsEnabled = !exporting;
    }
}
