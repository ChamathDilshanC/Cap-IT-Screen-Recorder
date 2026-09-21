using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ScreenRecorderApp.Services.Encoding;
using ScreenRecorderApp.Services.Export;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Media.Core;
using Windows.Media.Playback;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
    private string _backgroundPath = Path.Combine(AppContext.BaseDirectory, "assets", "Logo-CapIT.png");
    private readonly string _dotPatternPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapIT", "dot-pattern.png");
    private CanvasPreset _canvas = new("1920 × 1080 (Landscape)", 1920, 1080);
    private bool _hasCustomStyle;

    // What ffmpeg could read about the file. Null until the probe finishes; OnMediaFailed can fire first,
    // which is what _pendingPlaybackError defers.
    private MediaProbeResult? _probe;
    private bool _pendingPlaybackError;

    // Canceled if the window is closed while an export is still running, so a stray ffmpeg process never
    // outlives the window that started it.
    private CancellationTokenSource? _exportCts;

    public TrimExportWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        FilePathText.Text = filePath;

        // Without this a WinUI Window falls back to the literal string "WinUI Desktop" in the title bar
        // and the taskbar, which is what this window was shipping as.
        Title = "Review & Export — Cap-IT Screen Recorder";
        CanvasPresetComboBox.ItemsSource = new[]
        {
            new CanvasPreset("1920 × 1080 (Landscape)", 1920, 1080),
            new CanvasPreset("1080 × 1080 (Square)", 1080, 1080),
            new CanvasPreset("1080 × 1920 (Portrait)", 1080, 1920)
        };
        CanvasPresetComboBox.SelectedIndex = 0;
        BackgroundPresetComboBox.ItemsSource = new[]
        {
            new BackgroundPreset("Cap-IT logo", Path.Combine(AppContext.BaseDirectory, "assets", "Logo-CapIT.png")),
            new BackgroundPreset("App icon", Path.Combine(AppContext.BaseDirectory, "assets", "AppIcon.ico")),
            new BackgroundPreset("Dot pattern", _dotPatternPath)
        };
        BackgroundPresetComboBox.SelectedIndex = 0;
        UpdatePreviewStyle();
        _ = EnsureDotPatternAsync();

        // The MediaPlayer is created and owned explicitly here rather than read back off the element.
        // MediaPlayerElement.MediaPlayer is null until a Source has been assigned — it only auto-creates
        // one as a side effect of that assignment — so subscribing to it first threw a
        // NullReferenceException straight out of this constructor, and since that runs from
        // MainViewModel.StopRecordingAsync, the trim/GIF-export window silently never appeared after any
        // recording. Building the player up front and handing it over with SetMediaPlayer makes the
        // ordering explicit instead of load-bearing.
        var player = new MediaPlayer();
        player.MediaOpened += OnMediaOpened;
        player.MediaFailed += OnMediaFailed;
        player.Source = MediaSource.CreateFromUri(new Uri(filePath));
        Player.SetMediaPlayer(player);

        Closed += (_, _) =>
        {
            _exportCts?.Cancel();
            TeardownPlayer(); // no-op if a button handler already tore it down
        };

        _ = InitializeTrimRangeAsync();
    }

    /// <summary>
    /// Establishes the trim range from the file's real duration, probed with ffmpeg rather than taken
    /// from the media player. See <see cref="MediaProbe"/>: the app records fragmented MP4, whose
    /// duration Media Foundation reports as zero, which used to leave this slider pinned at 0 and
    /// Trim/GIF Export unusable. <see cref="OnMediaOpened"/> stays as the fallback for anything the
    /// probe can't read.
    /// </summary>
    private async Task InitializeTrimRangeAsync()
    {
        // Kept for OnMediaFailed, which needs the pixel format to tell an unplayable file apart from a
        // perfectly good one Windows merely can't decode — and which may well fire before this returns.
        _probe = await MediaProbe.ProbeAsync(_filePath);

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_pendingPlaybackError) ShowPlaybackError();
            if (_probe.Duration is { TotalSeconds: > 0 } value) ApplyDuration(value.TotalSeconds);
        });
    }

    /// <summary>
    /// Playback failed. The element's own error text ("Video could not be decoded") is the same whether
    /// the file is truncated or merely encoded in something Media Foundation doesn't implement, so it is
    /// replaced with a message that says which — and, when the recording is fine, says so plainly rather
    /// than letting the user assume they just lost a take.
    /// </summary>
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        // Fires on MediaPlayer's own thread, and can beat the ffmpeg probe that decides *which* message
        // to show — in that case the flag defers it to InitializeTrimRangeAsync's continuation.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_probe is null)
            {
                _pendingPlaybackError = true;
                return;
            }

            ShowPlaybackError();
        });
    }

    private void ShowPlaybackError()
    {
        _pendingPlaybackError = false;
        Player.Visibility = Visibility.Collapsed;
        PlaybackErrorPanel.Visibility = Visibility.Visible;

        if (_probe?.IsUndecodableByWindows == true)
        {
            PlaybackErrorTitle.Text = "Your recording is fine — Windows just can't preview it";
            PlaybackErrorDetail.Text =
                "It was recorded with 4:4:4 color, which Windows' built-in video decoder doesn't support, " +
                "so it won't play here, in Movies & TV, or in Photos. VLC and most editors open it normally, " +
                "and Export GIF below still works.\n\n" +
                "Recordings made from version 2.6.2 onward play everywhere — turn off \"Maximize text " +
                "clarity\" on the Capture tab if this one was made with it on.";
        }
        else
        {
            PlaybackErrorTitle.Text = "This recording couldn't be previewed";
            PlaybackErrorDetail.Text =
                "Windows couldn't decode the video for playback. The file is still on disk and Export GIF " +
                "may still work — it decodes with FFmpeg rather than the Windows player.";
        }
    }

    private void ApplyDuration(double totalSeconds)
    {
        TrimRange.Maximum = totalSeconds;
        TrimRange.RangeStart = 0;
        TrimRange.RangeEnd = totalSeconds;
        UpdateTrimLabels();
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        var duration = sender.PlaybackSession.NaturalDuration;
        var totalSeconds = duration.TotalSeconds > 0 ? duration.TotalSeconds : 1;

        // MediaOpened fires on MediaPlayer's own thread, not the UI thread — every property touched
        // below is bound to XAML, so it has to be marshaled back, same DispatcherQueue.TryEnqueue pattern
        // MainViewModel already uses for VideoCaptureService's off-thread CaptureTargetLost event.
        // Only used when the ffmpeg probe couldn't supply a duration — it already ran by now and, for
        // this app's own fragmented-MP4 output, is the only one of the two that reports a real value.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (TrimRange.Maximum > 1) return;
            ApplyDuration(totalSeconds);
        });
    }

    private void TrimRange_ValueChanged(object sender, RangeChangedEventArgs e) => UpdateTrimLabels();

    private void BackgroundPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackgroundPresetComboBox.SelectedItem is BackgroundPreset preset)
        {
            _backgroundPath = preset.Path;
            _hasCustomStyle = true;
            UpdatePreviewStyle();
        }
    }

    private void CanvasPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CanvasPresetComboBox.SelectedItem is CanvasPreset preset)
        {
            _canvas = preset;
            _hasCustomStyle = true;
            UpdatePreviewLayout();
        }
    }

    private async void OnChooseBackgroundClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        _backgroundPath = file.Path;
        _hasCustomStyle = true;
        BackgroundPresetComboBox.SelectedItem = null;
        PreviewBackground.Source = new BitmapImage(new Uri(file.Path));
    }

    private void VideoScale_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _hasCustomStyle = true;
        UpdatePreviewStyle();
    }

    private void CornerRadius_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _hasCustomStyle = true;
        UpdatePreviewStyle();
    }

    private void UpdatePreviewStyle()
    {
        if (!File.Exists(_backgroundPath)) return;
        PreviewBackground.Source = new BitmapImage(new Uri(_backgroundPath));
        UpdatePreviewLayout();
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewLayout();

    private void UpdatePreviewLayout()
    {
        if (PreviewCanvas is null || PreviewVideoFrame is null || _canvas.Height <= 0) return;
        var availableWidth = Math.Max(80, PreviewCanvas.ActualWidth - 48);
        var availableHeight = Math.Max(80, PreviewCanvas.ActualHeight - 32);
        var aspect = (double)_canvas.Width / _canvas.Height;
        var width = Math.Min(availableWidth, availableHeight * aspect);
        var height = width / aspect;
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * aspect;
        }
        PreviewVideoFrame.Width = width;
        PreviewVideoFrame.Height = height;
        var scale = VideoScaleSlider?.Value ?? .82;
        PreviewVideoFrame.CornerRadius = new CornerRadius(
            (CornerRadiusSlider?.Value ?? 18) * Math.Min(width / _canvas.Width, height / _canvas.Height));
        PreviewVideoFrame.RenderTransform = new CompositeTransform
        {
            ScaleX = scale,
            ScaleY = scale
        };
    }

    private async Task EnsureDotPatternAsync()
    {
        try
        {
            if (!File.Exists(_dotPatternPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dotPatternPath)!);
                using var stream = await FileRandomAccessStream.OpenAsync(
                    _dotPatternPath, FileAccessMode.ReadWrite, StorageOpenOptions.None,
                    FileOpenDisposition.OpenAlways);
                const uint width = 128;
                var pixels = new byte[width * width * 4];
                for (var y = 0; y < width; y++)
                for (var x = 0; x < width; x++)
                {
                    var dot = Math.Abs((x % 32) - 16) <= 2 && Math.Abs((y % 32) - 16) <= 2;
                    var index = ((y * width) + x) * 4;
                    pixels[index] = dot ? (byte)95 : (byte)24;
                    pixels[index + 1] = dot ? (byte)75 : (byte)24;
                    pixels[index + 2] = dot ? (byte)220 : (byte)24;
                    pixels[index + 3] = 255;
                }
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    width, width, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (BackgroundPresetComboBox.SelectedItem is BackgroundPreset preset &&
                    preset.Path == _dotPatternPath)
                    UpdatePreviewStyle();
            });
        }
        catch
        {
            // Optional generated preset; built-in and custom image presets remain available.
        }
    }

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

        TeardownPlayer(); // must release the file before deleting it
        try { File.Delete(_filePath); } catch { /* best effort — file may already be gone or briefly locked by the player */ }
        CloseSafely();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!_hasCustomStyle && TrimRange.RangeStart <= 0.001 &&
            Math.Abs(TrimRange.RangeEnd - TrimRange.Maximum) < 0.001)
        {
            CloseSafely();
            return;
        }

        var outputPath = Path.Combine(Path.GetDirectoryName(_filePath)!,
            $"{Path.GetFileNameWithoutExtension(_filePath)}_edited.mp4");
        _exportCts = new CancellationTokenSource();
        SetExportingState(true);
        ExportStageText.Text = "Exporting MP4…";
        ExportProgress.Value = 0;
        try
        {
            var progress = new Progress<GifExportProgress>(p =>
            {
                ExportStageText.Text = p.Stage;
                ExportProgress.Value = p.PercentComplete;
            });
            await Mp4ExportService.ExportAsync(_filePath, TimeSpan.FromSeconds(TrimRange.RangeStart),
                TimeSpan.FromSeconds(TrimRange.RangeEnd - TrimRange.RangeStart), outputPath, _backgroundPath,
                _canvas.Width, _canvas.Height, VideoScaleSlider.Value, CornerRadiusSlider.Value,
                progress, _exportCts.Token);
            TrimStatusText.Text = $"Saved: {outputPath}";
            CloseSafely();
        }
        catch (OperationCanceledException) { }
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

    /// <summary>
    /// Closes the window from a button handler without taking the process down with it.
    /// </summary>
    /// <remarks>
    /// Calling <see cref="Window.Close"/> directly inside a click handler on that same window throws
    /// <c>COMException 0x80004004</c> (E_ABORT) — the close re-enters while the event is still being
    /// dispatched. Nothing catches it there, so it surfaced as an unhandled XAML exception and killed
    /// the whole app: pressing "Keep MP4" (or confirming "Discard") after a recording closed Cap-IT
    /// outright. Posting the close back through the dispatcher lets the event finish unwinding first,
    /// and the try/catch keeps a late close on an already-closing window from being fatal either.
    /// </remarks>
    private void CloseSafely()
    {
        // Order matters: the playback session has to be torn down before the close, or the close is
        // what aborts. Detaching via SetMediaPlayer(null) first means the element no longer references
        // a player that is mid-shutdown.
        TeardownPlayer();

        DispatcherQueue.TryEnqueue(() =>
        {
            // AppWindow.Destroy() closes the window at the windowing layer instead of going through
            // XAML's Window.Close(), which is the call that raises E_ABORT here. Close() stays as a
            // fallback for the unlikely case AppWindow isn't available.
            try
            {
                AppWindow.Destroy();
                return;
            }
            catch { /* fall through to Close() below */ }

            try { Close(); }
            catch { /* already closing/closed — nothing left to do */ }
        });
    }

    /// <summary>Detaches and disposes the media player. Safe to call more than once.</summary>
    private void TeardownPlayer()
    {
        try
        {
            var player = Player.MediaPlayer;
            Player.SetMediaPlayer(null);
            player?.Dispose();
        }
        catch { /* best effort — the window is going away regardless */ }
    }

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
        CanvasPresetComboBox.IsEnabled = !exporting;
        BackgroundPresetComboBox.IsEnabled = !exporting;
        VideoScaleSlider.IsEnabled = !exporting;
        CornerRadiusSlider.IsEnabled = !exporting;
        ExportGifButton.IsEnabled = !exporting;
        DiscardButton.IsEnabled = !exporting;
        SaveButton.IsEnabled = !exporting;
    }

    private sealed record CanvasPreset(string Name, int Width, int Height)
    {
        public override string ToString() => Name;
    }

    private sealed record BackgroundPreset(string Name, string Path)
    {
        public override string ToString() => Name;
    }
}
