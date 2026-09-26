using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using System.Drawing;

namespace ScreenRecorderApp.Views;

/// <summary>Review workspace host. Editing/persistence live in ReviewViewModel; pixels live in VideoPreviewCanvas.</summary>
public sealed partial class TrimExportWindow : Window
{
    public ReviewViewModel ViewModel { get; }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _playbackTimer;
    private MediaPlayer? _player;
    private bool _ready, _closed, _syncing, _fullScreen, _exporting;
    private bool _inspectorVisible = true;
    private string? _lastError, _lastOutput;
    private CancellationTokenSource? _exportCts;
    private Task? _shutdownTask;

    public TrimExportWindow(string filePath)
    {
        ViewModel = new(filePath);
        InitializeComponent();
        Root.DataContext = ViewModel;
        Title = "Review & Export — Cap-IT";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        SystemBackdrop = new MicaBackdrop();
        ApplyTheme(new SettingsService().Load().Theme);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new(Math.Min(1360, area.Width - 32), Math.Min(900, area.Height - 32)));
        var icon = Path.Combine(AppContext.BaseDirectory, "assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange && !_fullScreen && (AppWindow.Size.Width < 720 || AppWindow.Size.Height < 540))
                AppWindow.Resize(new(Math.Max(720, AppWindow.Size.Width), Math.Max(540, AppWindow.Size.Height)));
        };
        _playbackTimer = DispatcherQueue.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playbackTimer.Tick += (_, _) => UpdatePlayback();
        AppWindow.Closing += (_, _) => PrepareClose();
        Preview.AssetWarning += warning => { if (_closed) return; AssetWarningBar.Message = warning ?? ""; AssetWarningBar.IsOpen = warning is not null; };
        ViewModel.CompositionChanged += OnCompositionChanged;
        Timeline.SeekRequested += Seek;
        Timeline.TrimChanged += (start, end) => { ViewModel.TrimStart = start; ViewModel.TrimEnd = end; };
        Preview.TextPositionChanged += (x, y, completed) => ViewModel.SetTextOverlayPosition(x, y, completed);
        Root.Loaded += OnLoaded;
        Closed += OnClosed;
        BuildGradientPresets();
        RenderColourSwatches();
        foreach (var family in System.Drawing.FontFamily.Families.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            TextFontPicker.Items.Add(family.Name);
    }

    public void ApplyTheme(AppTheme theme)
    {
        Root.RequestedTheme = theme switch { AppTheme.Light => ElementTheme.Light, AppTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnLoaded;
        try
        {
            await ViewModel.InitializeAsync(_lifetime.Token);
            if (_closed) return;
            _ready = true; Scrubber.Maximum = ViewModel.Duration;
            OnCompositionChanged(this, EventArgs.Empty);
            SyncTextControls();
            OpenPlayer(); _playbackTimer.Start();
            _ = LoadTimelineAsync();
#if DEBUG
            _ = RunDiagnosticsAsync();
#endif
        }

        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_closed) return;
            _lastError = ex.ToString(); LoadingPanel.Visibility = Visibility.Collapsed;
            ShowPlaybackError("This recording could not be opened. The original file has not been changed. Try opening it externally.");
        }
    }
        private void SyncTextControls()
        {
            var text = ViewModel.SelectedTextOverlay;
            _syncing = true;
            TextLayerPicker.Items.Clear();
            var texts = ViewModel.Presentation.TextOverlays.Count > 0 ? ViewModel.Presentation.TextOverlays : [ViewModel.Presentation.TextOverlay];
            for (var i = 0; i < texts.Count; i++) TextLayerPicker.Items.Add($"Text {i + 1}");
            if (TextLayerPicker.Items.Count > 0) TextLayerPicker.SelectedIndex = Math.Clamp(ViewModel.SelectedTextOverlayIndex, 0, TextLayerPicker.Items.Count - 1);
            TextOverlayBox.Text = text.Text;
            TextFontPicker.SelectedItem = TextFontPicker.Items.Cast<string>().FirstOrDefault(x => x.Equals(text.FontFamily, StringComparison.OrdinalIgnoreCase)) ?? "Segoe UI";
            TextAnimationPicker.SelectedItem = TextAnimationPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == text.Animation);
            TextColourPicker.Color = ParseColor(text.Color);
            TextOpacitySlider.Value = text.Opacity;
            TextSizeBox.Value = text.FontSize; TextXBox.Value = text.X * 100; TextYBox.Value = text.Y * 100;
            TextHorizontalAlignmentPicker.SelectedItem = text.HorizontalAlignment;
            TextVerticalAlignmentPicker.SelectedItem = text.VerticalAlignment;
            TextBoldButton.IsChecked = text.Bold; TextItalicButton.IsChecked = text.Italic;
            _syncing = false;
        }
        private void OnTextLayerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _syncing || TextLayerPicker.SelectedIndex < 0) return;
            ViewModel.SelectedTextOverlayIndex = TextLayerPicker.SelectedIndex;
            Preview.SetActiveTextIndex(ViewModel.SelectedTextOverlayIndex);
            SyncTextControls();
        }
        private void OnTextOverlayChanged(object sender, TextChangedEventArgs e)
        { if (_ready && !_syncing) ViewModel.UpdateTextOverlay(t => t.Text = TextOverlayBox.Text); }
        private void OnTextFontChanged(object sender, SelectionChangedEventArgs e)
        { if (_ready && !_syncing && TextFontPicker.SelectedItem is string font) ViewModel.UpdateTextOverlay(t => t.FontFamily = font); }
        private void OnTextAnimationChanged(object sender, SelectionChangedEventArgs e)
        { if (_ready && !_syncing && TextAnimationPicker.SelectedItem is ComboBoxItem item && item.Tag is string animation) ViewModel.UpdateTextOverlay(t => t.Animation = animation); }
        private void OnTextAlignmentChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _syncing) return;
            ViewModel.UpdateTextOverlay(t =>
            {
                if (TextHorizontalAlignmentPicker.SelectedItem is string horizontal) t.HorizontalAlignment = horizontal;
                if (TextVerticalAlignmentPicker.SelectedItem is string vertical) t.VerticalAlignment = vertical;
            });
        }
        private void OnTextOverlayNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
        {
            if (!_ready || _syncing) return;
            ViewModel.UpdateTextOverlay(t => { t.FontSize = TextSizeBox.Value; t.X = TextXBox.Value / 100; t.Y = TextYBox.Value / 100; });
        }
        private void OnTextStyleClick(object sender, RoutedEventArgs e)
        { if (_ready && !_syncing) ViewModel.UpdateTextOverlay(t => { t.Bold = TextBoldButton.IsChecked == true; t.Italic = TextItalicButton.IsChecked == true; }); }
        private void OnClearTextOverlayClick(object sender, RoutedEventArgs e)
        { if (_ready) { ViewModel.UpdateTextOverlay(t => t.Text = ""); SyncTextControls(); } }
        private void OnAddTextOverlayClick(object sender, RoutedEventArgs e)
        { if (_ready) { ViewModel.AddTextOverlay(); Preview.SetActiveTextIndex(ViewModel.SelectedTextOverlayIndex); SyncTextControls(); } }
        private void OnRemoveTextOverlayClick(object sender, RoutedEventArgs e)
        { if (_ready) { ViewModel.RemoveSelectedTextOverlay(); Preview.SetActiveTextIndex(ViewModel.SelectedTextOverlayIndex); SyncTextControls(); } }
        private void OnTextColourChanged(ColorPicker sender, ColorChangedEventArgs e)
        { if (_ready && !_syncing) ViewModel.UpdateTextOverlay(t => t.Color = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}"); }
        private void OnTextOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
        { if (_ready && !_syncing) ViewModel.UpdateTextOverlay(t => t.Opacity = e.NewValue); }
    private void OpenPlayer()
    {
        ReleasePlayer();
        LoadingPanel.Visibility = Visibility.Visible; PlaybackErrorPanel.Visibility = Visibility.Collapsed;
        _player = new MediaPlayer { AutoPlay = false, Volume = .8 };
        _player.MediaOpened += OnMediaOpened; _player.MediaFailed += OnMediaFailed;
        Preview.Player.SetMediaPlayer(_player);
        _player.Source = MediaSource.CreateFromUri(new Uri(ViewModel.FilePath));
    }
    private void OnMediaOpened(MediaPlayer sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed || sender != _player) return;
        LoadingPanel.Visibility = Visibility.Collapsed;
        if (ViewModel.Probe?.Duration is null && sender.PlaybackSession.NaturalDuration.TotalSeconds > 0)
        { ViewModel.Duration = sender.PlaybackSession.NaturalDuration.TotalSeconds; ViewModel.TrimEnd = ViewModel.Duration; Scrubber.Maximum = ViewModel.Duration; }
        Seek(ViewModel.TrimStart);
    });
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed || sender != _player) return;
        _lastError = args.ErrorMessage;
        ShowPlaybackError(ViewModel.Probe?.IsUndecodableByWindows == true
            ? "The original recording is safe. Windows cannot preview this colour format. You can open it in another player or export a compatible MP4 here."
            : "Windows could not play this recording. Your original file is still on disk. Retry, open it externally, or try exporting with FFmpeg.");
    });
    private void ShowPlaybackError(string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed; PlaybackErrorPanel.Visibility = Visibility.Visible;
        PlaybackErrorDetail.Text = message;
    }
    private void OnCompositionChanged(object? sender, EventArgs e)
    {
        if (!_ready || _closed) return;
        _ = Preview.UpdateAsync(ViewModel.Presentation, ViewModel.SourceWidth, ViewModel.SourceHeight);
        Timeline.Update(ViewModel.Duration, ViewModel.TrimStart, ViewModel.TrimEnd, _player?.PlaybackSession.Position.TotalSeconds ?? 0, ViewModel.ZoomRegions);
        _syncing = true;
        AspectPicker.SelectedItem = AspectPicker.Items.Cast<GridViewItem>().FirstOrDefault(i => (string)i.Tag == ViewModel.Presentation.CanvasPreset);
        CustomCanvasFields.Visibility = ViewModel.Presentation.CanvasPreset == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        var p = ViewModel.Presentation;
        ShadowPresetPicker.SelectedIndex = !p.Shadow ? 0 : (p.ShadowBlur, p.ShadowOpacity, p.ShadowOffsetY, p.ShadowOffsetX) switch
        { (24, .28, 12, 0) => 1, (32, .35, 16, 0) => 2, (48, .4, 28, 0) => 3, (32, .6, 20, 0) => 4, _ => 5 };
        _syncing = false;
        RefreshBackgroundControls();
    }
    private void UpdatePlayback()
    {
        if (_closed || _player is null) return;
        var session = _player.PlaybackSession; var time = session.Position.TotalSeconds;
        if (session.PlaybackState == MediaPlaybackState.Playing && time >= ViewModel.TrimEnd)
        { _player.Pause(); Seek(ViewModel.TrimStart); time = ViewModel.TrimStart; }
        _syncing = true; Scrubber.Value = time; _syncing = false;
        CurrentTimeText.Text = TimeSpan.FromSeconds(Math.Max(0, time)).ToString(@"mm\:ss\.f");
        PlayIcon.Glyph = session.PlaybackState == MediaPlaybackState.Playing ? "\uE769" : "\uE768";
        Timeline.SetPosition(time); Preview.SetPosition(time, ViewModel.ZoomRegions);
    }
    private void Seek(double time)
    {
        if (_player is null || !_ready) return;
        _player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(time, 0, ViewModel.Duration));
        Preview.SetPosition(time, ViewModel.ZoomRegions);
    }
    private void OnSeekChanged(object sender, RangeBaseValueChangedEventArgs e) { if (!_syncing) Seek(e.NewValue); }
    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
        else { if (_player.PlaybackSession.Position.TotalSeconds >= ViewModel.TrimEnd || _player.PlaybackSession.Position.TotalSeconds < ViewModel.TrimStart) Seek(ViewModel.TrimStart); _player.Play(); }
    }
    private void OnFrameStepClick(object sender, RoutedEventArgs e) { _player?.Pause(); _player?.StepForwardOneFrame(); }
    private void OnMuteClick(object sender, RoutedEventArgs e) { if (_player is not null) _player.IsMuted = MuteButton.IsChecked == true; }
    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e) { if (_player is not null) _player.Volume = e.NewValue; }
    private void OnRetryClick(object sender, RoutedEventArgs e) => OpenPlayer();
    private void OnAddZoomClick(object sender, RoutedEventArgs e)
    {
        if (!_ready || _exporting) return;
        ViewModel.AddZoom(_player?.PlaybackSession.Position.TotalSeconds ?? ViewModel.TrimStart);
        InspectorTabs.SelectedIndex = 2; SetInspector(true);
    }
    private void OnRemoveZoomClick(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: ZoomRegion region }) ViewModel.ZoomRegions.Remove(region); }
    private void OnToggleInspectorClick(object sender, RoutedEventArgs e) => SetInspector(!_inspectorVisible);
    private void SetInspector(bool visible)
    {
        _inspectorVisible = visible; Inspector.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = new(visible ? 340 : 0);
    }
    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
    { if (e.NewSize.Width < 900 && _inspectorVisible) SetInspector(false); }
    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        _fullScreen = !_fullScreen;
        AppWindow.SetPresenter(_fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        SetInspector(!_fullScreen); TimelinePanel.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or NumberBox || _exporting) return;
        if (e.Key == VirtualKey.F11 || (e.Key == VirtualKey.Escape && _fullScreen)) { OnFullscreenClick(sender, e); e.Handled = true; }
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.Z) { ViewModel.UndoCommand.Execute(null); e.Handled = true; }
        if (ctrl && e.Key == VirtualKey.Y) { ViewModel.RedoCommand.Execute(null); e.Handled = true; }
        if (e.Key == VirtualKey.Space && e.OriginalSource is not Control) { OnPlayClick(sender, e); e.Handled = true; }
    }
    private async void OnKeepOriginalClick(object sender, RoutedEventArgs e)
    {
        if (_exporting) return;
        await RequestCloseAsync();
    }
    private async Task RequestCloseAsync()
    {
        await ShutdownAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            try { Close(); }
            catch (System.Runtime.InteropServices.COMException) { AppWindow.Destroy(); }
            if (ReferenceEquals((Application.Current as App)?.MainWindow, this)) Application.Current.Exit();
        });
    }
    private void PrepareClose()
    {
        if (_closed) return;
        _closed = true; _lifetime.Cancel(); _exportCts?.Cancel(); _playbackTimer.Stop();
        Preview.Dispose(); ReleasePlayer();
    }
    private async void OnClosed(object sender, WindowEventArgs e)
    {
        await ShutdownAsync();
        if (ReferenceEquals((Application.Current as App)?.MainWindow, this)) Application.Current.Exit();
    }
    private Task ShutdownAsync() => _shutdownTask ??= ShutdownCoreAsync();
    private async Task ShutdownCoreAsync()
    {
        PrepareClose();
        ViewModel.CompositionChanged -= OnCompositionChanged;
        await ViewModel.CloseAsync(!_discarded);
        _lifetime.Dispose();
        if (_timelinePath is not null) { try { File.Delete(_timelinePath); } catch { } }
    }
    private void ReleasePlayer()
    {
        if (_player is null) return;
        _player.MediaOpened -= OnMediaOpened; _player.MediaFailed -= OnMediaFailed;
        try { Preview.Player.SetMediaPlayer(null); } catch (Exception ex) when (ex is ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
        try { _player.Dispose(); } catch (ObjectDisposedException) { }
        _player = null;
    }
}
