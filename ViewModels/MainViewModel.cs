using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.Services.Capture;
using ScreenRecorderApp.Services.Encoding;

namespace ScreenRecorderApp.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly RecordingManager _manager = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _uiTimer;

    // Guards the load-and-apply pass in the constructor so setting ~15 properties from disk doesn't
    // immediately queue ~15 redundant saves of the values it just read.
    private bool _isLoadingSettings;
    private CancellationTokenSource? _saveDebounceCts;

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public ObservableCollection<AudioDeviceOption> Microphones { get; } = [];
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public ObservableCollection<WebcamDeviceOption> Webcams { get; } = [];

    public IReadOnlyList<CaptureTargetKindOption> CaptureTargetKindOptions { get; } = CaptureTargetKindOption.All;
    public IReadOnlyList<int> FpsOptions { get; } = [15, 24, 30, 60];
    public IReadOnlyList<HardwareEncoder> EncoderOptions { get; } = Enum.GetValues<HardwareEncoder>();
    public IReadOnlyList<OutputContainer> ContainerOptions { get; } = Enum.GetValues<OutputContainer>();
    public IReadOnlyList<ResolutionOption> ResolutionOptions { get; } = ResolutionOption.All;
    public IReadOnlyList<CursorStyleOption> CursorStyleOptions { get; } = CursorStyleOption.All;
    public IReadOnlyList<ZoomLevelOption> ZoomLevelOptions { get; } = ZoomLevelOption.All;

    [ObservableProperty] private CaptureTargetKindOption _selectedCaptureTargetKind = CaptureTargetKindOption.All[0];
    public bool IsWindowCaptureMode => SelectedCaptureTargetKind.Value == CaptureTargetKind.Window;
    public bool IsMonitorCaptureMode => !IsWindowCaptureMode;

    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private WindowInfo? _selectedWindow;
    [ObservableProperty] private AudioDeviceOption? _selectedMicrophone;

    [ObservableProperty] private bool _captureSystemAudio = true;
    [ObservableProperty] private bool _captureMicrophone;
    [ObservableProperty] private bool _captureCursor = true;
    [ObservableProperty] private CursorStyleOption _selectedCursorStyle = CursorStyleOption.All[0];

    [ObservableProperty] private bool _mouseTrackingZoomEnabled;
    [ObservableProperty] private ZoomLevelOption _selectedZoomLevel = ZoomLevelOption.All[0];
    [ObservableProperty] private bool _keystrokeOverlayEnabled;

    // Circular webcam PiP overlay (Phase 3 Step 1 — device selection/persistence only; VideoCaptureService
    // doesn't composite it onto frames yet, so these don't touch RestartPreviewIfIdle like the capture
    // target/cursor properties above do).
    [ObservableProperty] private WebcamDeviceOption? _selectedWebcam;
    [ObservableProperty] private bool _webcamEnabled;

    [ObservableProperty] private int _fps = 30;
    [ObservableProperty] private double _videoBitrateKbps = 12000;
    public string BitrateLabel => $"Video bitrate: {VideoBitrateKbps:0} kbps";
    [ObservableProperty] private HardwareEncoder _selectedEncoder = HardwareEncoder.Auto;
    [ObservableProperty] private OutputContainer _selectedContainer = OutputContainer.Mp4;
    [ObservableProperty] private ResolutionOption _selectedResolution = ResolutionOption.All[0];
    [ObservableProperty] private string _outputDirectory = new RecordingSettings().OutputDirectory;

    // The yuv444p "Maximize text clarity" path only exists for libx264 (Auto or SoftwareX264) — see
    // FFmpegEncoderService.BuildEncoderTuning.
    [ObservableProperty] private bool _maximizeTextClarity;
    public bool CanMaximizeTextClarity => SelectedEncoder is HardwareEncoder.Auto or HardwareEncoder.SoftwareX264;

    [ObservableProperty] private RecordingState _state = RecordingState.Idle;
    [ObservableProperty] private string _elapsedText = "00:00:00";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string? _lastOutputPath;
    [ObservableProperty] private WriteableBitmap? _previewSource;

    [ObservableProperty] private bool _ffmpegSetupRequired;
    [ObservableProperty] private bool _isDownloadingFFmpeg;
    [ObservableProperty] private double _ffmpegDownloadProgress;
    [ObservableProperty] private string _ffmpegSetupMessage = "";
    public bool ShowFFmpegSetupBanner => FfmpegSetupRequired || IsDownloadingFFmpeg;
    public string FFmpegDownloadProgressText => $"{FfmpegDownloadProgress:0}%";

    private TaskCompletionSource<bool>? _ffmpegDecisionTcs;
    private CancellationTokenSource? _ffmpegDownloadCts;

    private byte[]? _previewBuffer;

    public bool HasPreview => PreviewSource is not null;
    public bool ShowPlaceholder => !HasPreview;

    public bool IsIdle => State == RecordingState.Idle;
    public bool IsBusy => !IsIdle;
    public bool IsRecording => State == RecordingState.Recording;
    public bool IsPaused => State == RecordingState.Paused;
    public string PauseResumeButtonText => IsPaused ? "Resume" : "Pause";
    public string PauseResumeGlyph => IsPaused ? "" : "";

    public MainViewModel()
    {
        _uiTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(150);
        _uiTimer.Tick += (_, _) =>
        {
            UpdateElapsed();
            UpdatePreview();
        };
        // Runs continuously (not just while recording) so the live preview can update as soon as a
        // monitor is selected, before the user ever presses Start Recording.
        _uiTimer.Start();

        // Fires from VideoCaptureService's WGC Closed handler, which runs on WGC's own thread, not the
        // UI thread — everything below touches [ObservableProperty]-backed state XAML bindings expect
        // updates for on the UI thread, so it has to be marshaled via the DispatcherQueue captured above.
        _manager.CaptureTargetLost += () => _dispatcherQueue.TryEnqueue(() =>
        {
            StatusMessage = "The captured window was closed.";
            if (IsBusy) _ = StopRecordingAsync();
        });

        RefreshMonitors();
        RefreshMicrophones();
        RefreshWindows();
        LoadAndApplySettings();
        _ = InitializeWebcamAsync();
    }

    /// <summary>
    /// Webcam enumeration is WinRT-async (DeviceInformation.FindAllAsync) unlike every other Refresh*
    /// method here, so it can't run synchronously inline with LoadAndApplySettings in the constructor —
    /// this populates the list, then re-matches the saved device (by exact id, same convention
    /// MicrophoneDeviceId already uses) once it's available, and applies the saved enabled toggle.
    /// Guarded the same way LoadAndApplySettings is, so applying these two values doesn't immediately
    /// queue a save right back.
    /// </summary>
    private async Task InitializeWebcamAsync()
    {
        await RefreshWebcamsAsync();

        _isLoadingSettings = true;
        try
        {
            var s = _settingsService.Load();
            if (s.WebcamDeviceId is not null)
            {
                var match = Webcams.FirstOrDefault(w => w.Id == s.WebcamDeviceId);
                if (match is not null) SelectedWebcam = match;
            }
            WebcamEnabled = s.WebcamEnabled;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>Loads persisted preferences and applies them, matching the saved monitor/microphone by
    /// name/id against what was just enumerated (falling back to the existing default-selection already
    /// picked by RefreshMonitors/RefreshMicrophones if nothing matches — e.g. a saved monitor got
    /// unplugged). Guarded so applying these ~15 values doesn't immediately queue ~15 saves right back.</summary>
    private void LoadAndApplySettings()
    {
        _isLoadingSettings = true;
        try
        {
            var s = _settingsService.Load();

            if (s.MonitorDeviceName is not null)
            {
                var match = Monitors.FirstOrDefault(m => m.DeviceName == s.MonitorDeviceName);
                if (match is not null) SelectedMonitor = match;
            }
            if (s.MicrophoneDeviceId is not null)
            {
                var match = Microphones.FirstOrDefault(m => m.Id == s.MicrophoneDeviceId);
                if (match is not null) SelectedMicrophone = match;
            }

            // A saved HWND wouldn't survive a restart anyway — re-match by title + process name against
            // whatever's actually running right now, and only switch into Window mode if that succeeds;
            // otherwise stay in (the already-selected default) Monitor mode rather than land on a mode
            // with nothing selected.
            if (s.CaptureTargetKind == CaptureTargetKind.Window && s.TargetWindowTitle is not null)
            {
                var match = Windows.FirstOrDefault(w => w.Title == s.TargetWindowTitle && w.ProcessName == s.TargetWindowProcessName)
                             ?? Windows.FirstOrDefault(w => w.Title == s.TargetWindowTitle);
                if (match is not null)
                {
                    SelectedWindow = match;
                    SelectedCaptureTargetKind = CaptureTargetKindOptions.First(k => k.Value == CaptureTargetKind.Window);
                }
            }

            Fps = FpsOptions.Contains(s.Fps) ? s.Fps : Fps;
            VideoBitrateKbps = s.VideoBitrateKbps;
            SelectedEncoder = s.Encoder;
            SelectedContainer = s.Container;
            SelectedResolution = ResolutionOptions.FirstOrDefault(r => r.Value == s.Resolution) ?? SelectedResolution;
            CaptureCursor = s.CaptureCursor;
            SelectedCursorStyle = CursorStyleOptions.FirstOrDefault(c => c.Value == s.CursorStyle) ?? SelectedCursorStyle;
            CaptureSystemAudio = s.CaptureSystemAudio;
            CaptureMicrophone = s.CaptureMicrophone;
            MouseTrackingZoomEnabled = s.MouseTrackingZoomEnabled;
            SelectedZoomLevel = ZoomLevelOptions.FirstOrDefault(z => z.Factor == s.ZoomFactor) ?? SelectedZoomLevel;
            KeystrokeOverlayEnabled = s.KeystrokeOverlayEnabled;
            MaximizeTextClarity = s.MaximizeTextClarity;
            if (!string.IsNullOrWhiteSpace(s.OutputDirectory)) OutputDirectory = s.OutputDirectory;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private AppSettings BuildAppSettings() => new()
    {
        CaptureTargetKind = SelectedCaptureTargetKind.Value,
        MonitorDeviceName = SelectedMonitor?.DeviceName,
        TargetWindowTitle = SelectedWindow?.Title,
        TargetWindowProcessName = SelectedWindow?.ProcessName,
        Fps = Fps,
        VideoBitrateKbps = VideoBitrateKbps,
        Encoder = SelectedEncoder,
        Container = SelectedContainer,
        Resolution = SelectedResolution.Value,
        CaptureCursor = CaptureCursor,
        CursorStyle = SelectedCursorStyle.Value,
        CaptureSystemAudio = CaptureSystemAudio,
        CaptureMicrophone = CaptureMicrophone,
        MicrophoneDeviceId = SelectedMicrophone?.Id,
        MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
        ZoomFactor = SelectedZoomLevel.Factor,
        KeystrokeOverlayEnabled = KeystrokeOverlayEnabled,
        WebcamEnabled = WebcamEnabled,
        WebcamDeviceId = SelectedWebcam?.Id,
        MaximizeTextClarity = MaximizeTextClarity,
        OutputDirectory = OutputDirectory,
    };

    /// <summary>Debounced (~400ms) save — called from every setting's On&lt;Prop&gt;Changed partial so a
    /// slider drag doesn't hit disk on every tick, only once motion settles.</summary>
    private void QueueSaveSettings()
    {
        if (_isLoadingSettings) return;

        _saveDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;
        var snapshot = BuildAppSettings();

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(400, cts.Token); }
            catch (OperationCanceledException) { return; }
            if (cts.Token.IsCancellationRequested) return;
            _settingsService.Save(snapshot);
        });
    }

    /// <summary>Immediate, non-debounced save — call on app shutdown so the last pending change isn't lost.</summary>
    public void FlushSettings()
    {
        _saveDebounceCts?.Cancel();
        _settingsService.Save(BuildAppSettings());
    }

    partial void OnPreviewSourceChanged(WriteableBitmap? value)
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    partial void OnStateChanged(RecordingState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseResumeButtonText));
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();

        if (IsIdle) RestartPreviewIfIdle();
    }

    partial void OnSelectedMonitorChanged(MonitorInfo? value)
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedWindowChanged(WindowInfo? value)
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedCaptureTargetKindChanged(CaptureTargetKindOption value)
    {
        OnPropertyChanged(nameof(IsWindowCaptureMode));
        OnPropertyChanged(nameof(IsMonitorCaptureMode));
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnCaptureCursorChanged(bool value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedCursorStyleChanged(CursorStyleOption value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnMouseTrackingZoomEnabledChanged(bool value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedZoomLevelChanged(ZoomLevelOption value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnKeystrokeOverlayEnabledChanged(bool value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    // No RestartPreviewIfIdle() here yet — VideoCaptureService doesn't composite the webcam onto frames
    // until Phase 3 Step 2, so there's nothing for the live preview to reflect from these two yet.
    partial void OnSelectedWebcamChanged(WebcamDeviceOption? value) => QueueSaveSettings();
    partial void OnWebcamEnabledChanged(bool value) => QueueSaveSettings();

    partial void OnSelectedEncoderChanged(HardwareEncoder value)
    {
        OnPropertyChanged(nameof(CanMaximizeTextClarity));
        QueueSaveSettings();
    }

    partial void OnVideoBitrateKbpsChanged(double value)
    {
        OnPropertyChanged(nameof(BitrateLabel));
        QueueSaveSettings();
    }

    partial void OnFfmpegSetupRequiredChanged(bool value) => OnPropertyChanged(nameof(ShowFFmpegSetupBanner));

    partial void OnIsDownloadingFFmpegChanged(bool value) => OnPropertyChanged(nameof(ShowFFmpegSetupBanner));

    partial void OnFfmpegDownloadProgressChanged(double value) => OnPropertyChanged(nameof(FFmpegDownloadProgressText));

    partial void OnSelectedMicrophoneChanged(AudioDeviceOption? value) => QueueSaveSettings();

    partial void OnFpsChanged(int value) => QueueSaveSettings();

    partial void OnSelectedContainerChanged(OutputContainer value) => QueueSaveSettings();

    partial void OnSelectedResolutionChanged(ResolutionOption value) => QueueSaveSettings();

    partial void OnCaptureSystemAudioChanged(bool value) => QueueSaveSettings();

    partial void OnCaptureMicrophoneChanged(bool value) => QueueSaveSettings();

    partial void OnMaximizeTextClarityChanged(bool value) => QueueSaveSettings();

    partial void OnOutputDirectoryChanged(string value) => QueueSaveSettings();

    /// <summary>(Re)starts the before-recording live preview on a background thread. Safe to call any
    /// time a relevant setting changes; it's a no-op unless idle and a capture target is selected.</summary>
    private void RestartPreviewIfIdle()
    {
        var isWindowMode = IsWindowCaptureMode;
        if (!IsIdle || (isWindowMode ? SelectedWindow is null : SelectedMonitor is null)) return;

        var monitor = isWindowMode ? null : SelectedMonitor;
        var window = isWindowMode ? SelectedWindow : null;
        var cursor = CaptureCursor;
        var cursorStyle = SelectedCursorStyle.Value;
        var zoomEnabled = MouseTrackingZoomEnabled;
        var zoomFactor = SelectedZoomLevel.Factor;
        var keystrokeOverlay = KeystrokeOverlayEnabled;
        _ = Task.Run(() =>
        {
            try { _manager.StartPreview(monitor, window, cursor, cursorStyle, zoomEnabled, zoomFactor, keystrokeOverlay); }
            catch { /* best effort: live preview is a convenience, not required to record */ }
        });
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        var current = SelectedMonitor?.DeviceName;
        Monitors.Clear();
        foreach (var m in _manager.GetMonitors()) Monitors.Add(m);
        SelectedMonitor = Monitors.FirstOrDefault(m => m.DeviceName == current)
                           ?? Monitors.FirstOrDefault(m => m.IsPrimary)
                           ?? Monitors.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshMicrophones()
    {
        var current = SelectedMicrophone?.Id;
        Microphones.Clear();
        foreach (var mic in _manager.GetMicrophones()) Microphones.Add(mic);
        SelectedMicrophone = Microphones.FirstOrDefault(m => m.Id == current) ?? Microphones.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        var current = SelectedWindow?.Handle;
        Windows.Clear();
        foreach (var w in _manager.GetWindows()) Windows.Add(w);
        SelectedWindow = Windows.FirstOrDefault(w => w.Handle == current) ?? Windows.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RefreshWebcamsAsync()
    {
        var current = SelectedWebcam?.Id;
        var webcams = await WebcamDeviceEnumerator.GetWebcamsAsync();
        Webcams.Clear();
        foreach (var w in webcams) Webcams.Add(w);
        SelectedWebcam = Webcams.FirstOrDefault(w => w.Id == current) ?? Webcams.FirstOrDefault();
    }

    /// <summary>
    /// Confirms ffmpeg is available before recording starts, offering to download it in place (via
    /// <see cref="FFmpegDownloader"/>) if it isn't. Returns false if the user declined or the download
    /// failed, in which case the caller should abort starting.
    /// </summary>
    private async Task<bool> EnsureFFmpegAvailableAsync()
    {
        if (FFmpegLocator.FindFFmpeg() is not null) return true;

        _ffmpegDecisionTcs = new TaskCompletionSource<bool>();
        FfmpegSetupMessage = "ffmpeg wasn't found. Download it now (~90 MB) to enable recording?";
        FfmpegSetupRequired = true;
        var wantsDownload = await _ffmpegDecisionTcs.Task;
        FfmpegSetupRequired = false;
        if (!wantsDownload) return false;

        IsDownloadingFFmpeg = true;
        _ffmpegDownloadCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => FfmpegDownloadProgress = p);
            await FFmpegDownloader.DownloadAndInstallAsync(progress, _ffmpegDownloadCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "ffmpeg download cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"ffmpeg download failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsDownloadingFFmpeg = false;
            FfmpegDownloadProgress = 0;
            _ffmpegDownloadCts?.Dispose();
            _ffmpegDownloadCts = null;
        }
    }

    [RelayCommand]
    private void ConfirmDownloadFFmpeg() => _ffmpegDecisionTcs?.TrySetResult(true);

    [RelayCommand]
    private void CancelFFmpegSetup() => _ffmpegDecisionTcs?.TrySetResult(false);

    [RelayCommand]
    private void CancelFFmpegDownload() => _ffmpegDownloadCts?.Cancel();

    private bool CanStart() => IsIdle && (IsWindowCaptureMode ? SelectedWindow is not null : SelectedMonitor is not null);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRecordingAsync()
    {
        var isWindowMode = IsWindowCaptureMode;
        if (isWindowMode ? SelectedWindow is null : SelectedMonitor is null) return;

        if (!await EnsureFFmpegAvailableAsync()) return;

        var settings = new RecordingSettings
        {
            CaptureTargetKind = SelectedCaptureTargetKind.Value,
            MonitorHandle = SelectedMonitor?.Handle ?? 0,
            MonitorFriendlyName = SelectedMonitor?.FriendlyName ?? "",
            TargetWindowHandle = SelectedWindow?.Handle ?? 0,
            TargetWindowTitle = SelectedWindow?.Title,
            Fps = Fps,
            VideoBitrateKbps = (int)VideoBitrateKbps,
            CaptureSystemAudio = CaptureSystemAudio,
            CaptureMicrophone = CaptureMicrophone,
            MicrophoneDeviceId = SelectedMicrophone?.Id,
            CaptureCursor = CaptureCursor,
            CursorStyle = SelectedCursorStyle.Value,
            MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
            ZoomFactor = SelectedZoomLevel.Factor,
            KeystrokeOverlayEnabled = KeystrokeOverlayEnabled,
            Encoder = SelectedEncoder,
            Container = SelectedContainer,
            Resolution = SelectedResolution.Value,
            OutputDirectory = OutputDirectory,
            MaximizeTextClarity = MaximizeTextClarity && CanMaximizeTextClarity,
        };

        try
        {
            StatusMessage = "Starting…";
            await _manager.StartAsync(settings, SelectedMonitor, SelectedWindow);
            State = _manager.State;
            var targetLabel = isWindowMode ? SelectedWindow!.Title : SelectedMonitor!.FriendlyName;
            StatusMessage = $"Recording {targetLabel} @ {Fps} FPS ({SelectedResolution.Label})";
        }
        catch (Exception ex)
        {
            State = RecordingState.Idle;
            StatusMessage = $"Failed to start: {ex.Message}";
        }
    }

    private bool CanStop() => IsRecording || IsPaused;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopRecordingAsync()
    {
        StatusMessage = "Finalizing…";
        var path = await _manager.StopAsync();
        LastOutputPath = path;
        State = _manager.State; // triggers OnStateChanged, which restarts the live preview since we're idle again
        StatusMessage = path is not null ? $"Saved: {path}" : "Recording stopped.";
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void PauseResume()
    {
        if (IsRecording)
        {
            _manager.Pause();
            StatusMessage = "Paused";
        }
        else if (IsPaused)
        {
            _manager.Resume();
            StatusMessage = "Recording…";
        }
        State = _manager.State;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var folder = LastOutputPath is not null ? Path.GetDirectoryName(LastOutputPath) : OutputDirectory;
        if (string.IsNullOrEmpty(folder)) return;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void UpdateElapsed()
    {
        var e = _manager.Elapsed;
        ElapsedText = e.ToString(e.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
    }

    private void UpdatePreview()
    {
        // No state guard: video capture (and therefore a frame to show) may be active before recording
        // even starts (preview mode, kicked off by RestartPreviewIfIdle), not just while recording/paused.
        int w = _manager.PreviewWidth;
        int h = _manager.PreviewHeight;
        if (w <= 0 || h <= 0) return;

        if (_previewBuffer is null || _previewBuffer.Length != w * h * 4)
        {
            _previewBuffer = new byte[w * h * 4];
        }

        if (!_manager.TryGetPreviewFrame(_previewBuffer)) return;

        // Desktop-duplication frames carry a BGRA alpha channel that's usually 0 for otherwise-opaque
        // desktop content (it's not meaningful — ffmpeg's rawvideo->yuv420p path ignores it entirely).
        // WriteableBitmap treats source pixels as premultiplied alpha though, so alpha=0 would render as
        // fully transparent/black; force it opaque for display purposes only.
        for (int i = 3; i < _previewBuffer.Length; i += 4)
        {
            _previewBuffer[i] = 255;
        }

        if (PreviewSource is null || PreviewSource.PixelWidth != w || PreviewSource.PixelHeight != h)
        {
            PreviewSource = new WriteableBitmap(w, h);
        }

        using (var stream = PreviewSource.PixelBuffer.AsStream())
        {
            stream.Write(_previewBuffer, 0, _previewBuffer.Length);
        }
        PreviewSource.Invalidate();
    }
}
