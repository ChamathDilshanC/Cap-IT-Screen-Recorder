using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.Services.Capture;
using ScreenRecorderApp.Services.Encoding;
using ScreenRecorderApp.Services.Overlay;

namespace ScreenRecorderApp.ViewModels;

/// <summary>Display label for a <see cref="HardwareEncoder"/>.</summary>
public sealed record EncoderOption(HardwareEncoder Value, string Label, string Short)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<EncoderOption> All =
    [
        new(HardwareEncoder.Auto, "Automatic (best available)", "Automatic"),
        new(HardwareEncoder.Nvenc, "NVIDIA NVENC", "NVENC"),
        new(HardwareEncoder.Amf, "AMD AMF", "AMF"),
        new(HardwareEncoder.Qsv, "Intel Quick Sync", "Quick Sync"),
        new(HardwareEncoder.SoftwareX264, "Software (x264)", "Software"),
    ];
}

/// <summary>Display label for an <see cref="OutputContainer"/>.</summary>
public sealed record ContainerOption(OutputContainer Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<ContainerOption> All =
    [
        new(OutputContainer.Mp4, "MP4"),
        new(OutputContainer.Mkv, "MKV"),
    ];
}

/// <summary>
/// The single recording session shared by every page: capture target, quality, effects, audio,
/// annotations, presets, persistence, live preview and the record/pause/stop lifecycle. Pages bind to
/// this one instance so switching pages never resets settings or an active recording.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly RecordingManager _manager = new();
    private readonly SettingsService _settingsService = new();
    private readonly AnnotationOverlayService _annotations;
    private readonly MicLevelMonitorService _micLevelMonitor = new();
    private readonly SpeakerLevelMonitorService _speakerLevelMonitor = new();
    private readonly UpdateService _updateService = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly ToastService _toasts;
    private readonly IReviewWindowService _reviews;
    private readonly Action _requestExit;
    private bool _updateCheckInProgress;

    // Guards the load-and-apply pass in the constructor so setting ~15 properties from disk doesn't
    // immediately queue ~15 redundant saves of the values it just read.
    private bool _isLoadingSettings;
    private CancellationTokenSource? _saveDebounceCts;

    // The live audio monitors wrap NAudio WasapiCapture, which MUST be started/stopped off the UI thread
    // (see MicLevelMonitorService's remarks — doing it inline on the UI thread is what froze the whole
    // app when the mic device was changed). These coalesce the rapid changes made clicking through the
    // device combo / toggles into one background apply.
    private CancellationTokenSource? _micMonitorCts;
    private CancellationTokenSource? _speakerMonitorCts;

    // Same background-apply-with-debounce reasoning as the two monitors above, for pushing an audio
    // source change into a recording that's already running — see ApplyAudioSourcesLive.
    private CancellationTokenSource? _audioSourceCts;

    // Live-preview consumers currently on screen (Home, Capture, source picker). The capture service
    // only reads a preview frame back from the GPU when asked, so when nothing is showing the preview —
    // another page is open, or the window is minimised while recording — no readback happens at all.
    private int _previewConsumers;
    private bool _isAppVisible = true;

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public ObservableCollection<AudioDeviceOption> Microphones { get; } = [];
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public ObservableCollection<WebcamDeviceOption> Webcams { get; } = [];
    public ObservableCollection<RecordingPresetOption> Presets { get; } = [];

    public IReadOnlyList<CaptureTargetKindOption> CaptureTargetKindOptions { get; } = CaptureTargetKindOption.All;
    public IReadOnlyList<int> FpsOptions { get; } = [15, 24, 30, 60];
    public IReadOnlyList<EncoderOption> EncoderOptions { get; } = EncoderOption.All;
    public IReadOnlyList<ContainerOption> ContainerOptions { get; } = ContainerOption.All;
    public IReadOnlyList<ResolutionOption> ResolutionOptions { get; } = ResolutionOption.All;
    public IReadOnlyList<CursorStyleOption> CursorStyleOptions { get; } = CursorStyleOption.All;
    public IReadOnlyList<ZoomLevelOption> ZoomLevelOptions { get; } = ZoomLevelOption.All;
    public IReadOnlyList<WebcamTemplateOption> WebcamTemplateOptions { get; } = WebcamTemplateOption.All;
    public IReadOnlyList<AppThemeOption> AppThemeOptions { get; } = AppThemeOption.All;
    public IReadOnlyList<ClickSoundOption> ClickSoundOptions { get; } = ClickSoundOption.All;
    [ObservableProperty] private AppThemeOption _selectedAppTheme = AppThemeOption.All[0];

    [ObservableProperty] private CaptureTargetKindOption _selectedCaptureTargetKind = CaptureTargetKindOption.All[0];
    public bool IsWindowCaptureMode => SelectedCaptureTargetKind.Value == CaptureTargetKind.Window;
    public bool IsMonitorCaptureMode => !IsWindowCaptureMode;

    /// <summary>What the app will record right now, in one line.</summary>
    public string CurrentTargetSummary => IsWindowCaptureMode
        ? SelectedWindow is null ? "No window selected" : $"Window · {SelectedWindow.Title}"
        : SelectedMonitor is null ? "No display selected" : $"Display · {SelectedMonitor}";

    /// <summary>Short source name for compact surfaces (title bar, recording controller).</summary>
    public string CurrentTargetName => IsWindowCaptureMode
        ? SelectedWindow?.Title ?? "No window"
        : SelectedMonitor?.FriendlyName ?? "No display";

    public bool HasCaptureTarget => IsWindowCaptureMode ? SelectedWindow is not null : SelectedMonitor is not null;

    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private WindowInfo? _selectedWindow;
    [ObservableProperty] private AudioDeviceOption? _selectedMicrophone;

    [ObservableProperty] private bool _captureSystemAudio = true;
    [ObservableProperty] private bool _captureMicrophone;

    // Live microphone level meter, 0..1 on a dBFS scale, smoothed with a fast attack / slow release in
    // UpdateMicLevel() so it reads as a level rather than jittering with every 150 ms sample.
    [ObservableProperty] private double _micLevel;
    public bool ShowMicMeter => CaptureMicrophone;
    // 0.18 on the meter scale is about -49 dBFS — above a quiet room's noise floor, below any speech.
    public bool IsMicSignalPresent => MicLevel > 0.18;
    public bool IsMicMonitorAvailable => _micLevelMonitor.IsActive;
    // Distinguishes "the monitor couldn't open the device" (wrong/disconnected mic, or Windows' microphone
    // privacy switch is off) from "it opened fine but nobody's talking".
    public string MicStatusText => !_micLevelMonitor.IsActive ? "Mic unavailable" : IsMicSignalPresent ? "Mic active" : "No signal";

    [ObservableProperty] private double _speakerLevel;
    public bool ShowSpeakerMeter => CaptureSystemAudio;
    public bool IsSpeakerSignalPresent => SpeakerLevel > 0.18;
    public bool IsSpeakerMonitorAvailable => _speakerLevelMonitor.IsActive;
    public string SpeakerStatusText => !_speakerLevelMonitor.IsActive ? "Output unavailable" : IsSpeakerSignalPresent ? "Playing" : "Silent";

    // Studio Mic noise suppression (ffmpeg afftdn/highpass/adeclick on the mic leg only). Applied at
    // record start, so it doesn't touch the live preview.
    [ObservableProperty] private bool _enableMicNoiseSuppression;

    [ObservableProperty] private bool _captureCursor = true;
    [ObservableProperty] private CursorStyleOption _selectedCursorStyle = CursorStyleOption.All[0];

    [ObservableProperty] private bool _mouseTrackingZoomEnabled;
    [ObservableProperty] private ZoomLevelOption _selectedZoomLevel = ZoomLevelOption.All[0];
    [ObservableProperty] private bool _zoomOnClickOnly;
    [ObservableProperty] private bool _instantZoomOut;
    [ObservableProperty] private double _zoomAnimationSpeedPercent;
    [ObservableProperty] private bool _keystrokeOverlayEnabled;

    /// <summary>Segmented-control mirror of <see cref="ZoomOnClickOnly"/>: 0 = follow cursor and caret, 1 = clicks only.</summary>
    public int ZoomTriggerIndex
    {
        get => ZoomOnClickOnly ? 1 : 0;
        set => ZoomOnClickOnly = value == 1;
    }

    [ObservableProperty] private WebcamDeviceOption? _selectedWebcam;
    [ObservableProperty] private WebcamTemplateOption _selectedWebcamTemplate = WebcamTemplateOption.All[0];
    [ObservableProperty] private bool _webcamEnabled;
    [ObservableProperty] private double _webcamBrightness;
    [ObservableProperty] private double _webcamContrast = 1;
    [ObservableProperty] private double _webcamSaturation = 1;
    [ObservableProperty] private double _webcamWarmth;
    [ObservableProperty] private double _webcamSmoothing;
    [ObservableProperty] private WriteableBitmap? _webcamPreviewSource;
    [ObservableProperty] private long _webcamPreviewVersion;
    [ObservableProperty] private string _webcamPreviewStatus = "Turn on the webcam and select a camera to preview it";

    [ObservableProperty] private bool _spotlightEnabled;
    [ObservableProperty] private double _spotlightRadius = 180;
    [ObservableProperty] private bool _clickRipplesEnabled;
    [ObservableProperty] private bool _clickSoundsEnabled;
    [ObservableProperty] private ClickSoundOption _selectedClickSound = ClickSoundOption.All[0];
    [ObservableProperty] private double _clickSoundVolume = 75;

    // Live screen annotations. Driven directly by Start/StopRecordingAsync and SyncAnnotationOverlay
    // using SelectedMonitor, not through RecordingManager/RecordingSettings — persistence only.
    [ObservableProperty] private bool _annotationsEnabled;

    // Windows Graphics Capture for a specific window only captures that window's own surface, so the
    // overlay is invisible to a window-mode recording — gated off rather than silently no-op'd.
    public bool CanEnableAnnotations => IsMonitorCaptureMode;
    public bool CanToggleAnnotations => IsIdle && CanEnableAnnotations;

    // Pen colour/thickness/tool are deliberately live-updatable mid-recording — gated only by
    // AnnotationsEnabled, not IsIdle.
    public IReadOnlyList<AnnotationColorOption> AnnotationColorOptions { get; } = AnnotationColorOption.All;
    [ObservableProperty] private AnnotationColorOption _selectedAnnotationColor = AnnotationColorOption.All[0];
    [ObservableProperty] private double _annotationStrokeThickness = 6;

    // The on-screen toolbar is authoritative while drawing; changes there flow back here (guarded by
    // _syncingAnnotationFromToolbar) so this stays in step and persists.
    public IReadOnlyList<AnnotationToolOption> AnnotationToolOptions { get; } = AnnotationToolOption.All;
    public IReadOnlyList<AnnotationToolOption> QuickAnnotationTools { get; } = AnnotationToolOption.All
        .Where(t => t.Value is AnnotationTool.Select or AnnotationTool.Pen or AnnotationTool.Highlighter or AnnotationTool.Arrow
            or AnnotationTool.Line or AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Text
            or AnnotationTool.NumberedStep or AnnotationTool.Blur)
        .ToList();
    [ObservableProperty] private AnnotationToolOption _selectedAnnotationTool = AnnotationToolOption.All[0];
    public IReadOnlyList<AnnotationFadeOption> AnnotationFadeOptions { get; } = AnnotationFadeOption.All;
    [ObservableProperty] private AnnotationFadeOption _selectedAnnotationFade = AnnotationFadeOption.All[0];
    private bool _syncingAnnotationFromToolbar;

    [ObservableProperty] private int _fps = 30;
    [ObservableProperty] private double _videoBitrateKbps = 12000;
    [ObservableProperty] private HardwareEncoder _selectedEncoder = HardwareEncoder.Auto;
    [ObservableProperty] private OutputContainer _selectedContainer = OutputContainer.Mp4;
    [ObservableProperty] private ResolutionOption _selectedResolution = ResolutionOption.All[0];
    [ObservableProperty] private string _outputDirectory = new RecordingSettings().OutputDirectory;
    [ObservableProperty] private RecordingPresetOption? _selectedPreset;
    [ObservableProperty] private string _newPresetName = "";
    private bool _applyingPreset;
    public bool CanDeleteSelectedPreset => SelectedPreset is { IsBuiltIn: false };

    public EncoderOption SelectedEncoderOption
    {
        get => EncoderOptions.FirstOrDefault(o => o.Value == SelectedEncoder) ?? EncoderOptions[0];
        set { if (value is not null) SelectedEncoder = value.Value; }
    }

    public ContainerOption SelectedContainerOption
    {
        get => ContainerOptions.FirstOrDefault(o => o.Value == SelectedContainer) ?? ContainerOptions[0];
        set { if (value is not null) SelectedContainer = value.Value; }
    }

    // The yuv444p "Maximize text clarity" path only exists for libx264 (Auto or SoftwareX264) — see
    // FFmpegEncoderService.BuildEncoderTuning.
    [ObservableProperty] private bool _maximizeTextClarity;
    public bool CanMaximizeTextClarity => SelectedEncoder is HardwareEncoder.Auto or HardwareEncoder.SoftwareX264;
    public bool CanEditTextClarity => IsIdle && CanMaximizeTextClarity;

    [ObservableProperty] private RecordingState _state = RecordingState.Idle;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string? _lastOutputPath;
    [ObservableProperty] private WriteableBitmap? _previewSource;
    [ObservableProperty] private long _previewVersion;
    [ObservableProperty] private bool _isStarting;

    [ObservableProperty] private bool _ffmpegSetupRequired;
    [ObservableProperty] private bool _isDownloadingFFmpeg;
    [ObservableProperty] private double _ffmpegDownloadProgress;
    [ObservableProperty] private string _ffmpegSetupMessage = "";
    public bool ShowFFmpegSetup => FfmpegSetupRequired || IsDownloadingFFmpeg;
    public string FFmpegDownloadProgressText => $"{FfmpegDownloadProgress:0}%";

    private TaskCompletionSource<bool>? _ffmpegDecisionTcs;
    private CancellationTokenSource? _ffmpegDownloadCts;

    // Auto-update (GitHub Releases), checked at startup and every five minutes.
    private UpdateInfo? _pendingUpdate;
    private CancellationTokenSource? _updateDownloadCts;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBannerMessage = "";
    [ObservableProperty] private string _updateVersionText = "";
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _updateDownloadProgress;
    [ObservableProperty] private string _updateCheckStatus = "Cap-IT checks for updates automatically.";
    public bool IsNotDownloadingUpdate => !IsDownloadingUpdate;
    public string UpdateDownloadProgressText => $"{UpdateDownloadProgress:0}%";

    partial void OnIsDownloadingUpdateChanged(bool value) => OnPropertyChanged(nameof(IsNotDownloadingUpdate));
    partial void OnUpdateDownloadProgressChanged(double value) => OnPropertyChanged(nameof(UpdateDownloadProgressText));

    private byte[]? _previewBuffer;

    public bool HasPreview => PreviewSource is not null;
    public bool ShowPlaceholder => !HasPreview;
    public bool HasWebcamPreview => WebcamPreviewSource is not null;

    // UI snapshots run against the live desktop. Keep captured pages free of desktop contents.
    internal bool SuppressLivePreviewForSnapshot { get; set; }

    public string AppVersionText => $"Version {UpdateService.CurrentVersion.ToString(3)}";
    public string AppVersionShort => $"v{UpdateService.CurrentVersion.ToString(3)}";

    public string Greeting => DateTime.Now.Hour switch
    {
        < 5 => "Good evening",
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };

    /// <summary>
    /// Audio sources are freely editable when idle, and mid-recording too whenever the running audio
    /// pipeline can absorb the change — see RecordingManager.CanChangeAudioSourcesLive.
    /// </summary>
    public bool CanChangeAudioLive => IsIdle || _manager.CanChangeAudioSourcesLive;

    /// <summary>Shown only when the audio controls are locked because of how this recording was started.</summary>
    public bool ShowAudioLockedHint => IsBusy && !_manager.CanChangeAudioSourcesLive;

    public bool IsIdle => State == RecordingState.Idle;
    public bool IsBusy => !IsIdle;
    public bool IsRecording => State == RecordingState.Recording;
    public bool IsPaused => State == RecordingState.Paused;
    public bool CanEditMicNoiseSuppression => IsIdle && CaptureMicrophone;

    public string StateLabel => IsStarting ? "Starting" : State switch
    {
        RecordingState.Recording => IsScreenFrozen ? "Screen paused" : "Recording",
        RecordingState.Paused => "Paused",
        RecordingState.Stopping => "Finishing",
        _ => "Ready",
    };

    // "Pause Screen": freezes the recorded image only — audio and the elapsed timer keep running.
    // See RecordingManager.IsScreenFrozen.
    [ObservableProperty] private bool _isScreenFrozen;
    public string ScreenPauseButtonText => IsScreenFrozen ? "Resume screen" : "Freeze screen";

    partial void OnIsScreenFrozenChanged(bool value)
    {
        OnPropertyChanged(nameof(ScreenPauseButtonText));
        OnPropertyChanged(nameof(StateLabel));
        ToggleScreenPauseCommand.NotifyCanExecuteChanged();
    }

    public string PauseResumeButtonText => IsPaused ? "Resume" : "Pause";

    /// <summary>Raised on the UI thread when a recording has been saved.</summary>
    public event Action<string>? RecordingSaved;

    public MainViewModel(ToastService toasts, IReviewWindowService reviews, Action requestExit)
    {
        _toasts = toasts;
        _reviews = reviews;
        _requestExit = requestExit;

        // The overlay and toolbar are plain Win32 windows owned by the UI thread; Avalonia's Win32 message
        // loop pumps them exactly as WinUI's did.
        _annotations = new AnnotationOverlayService(SynchronizationContext.Current ?? new AvaloniaSynchronizationContext());
        _annotations.AttributesChanged += OnAnnotationAttributesChangedFromToolbar;
        _manager.WebcamFrameReady += OnWebcamFrameReady;

        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
        {
            UpdateElapsed();
            UpdateMicLevel();
            UpdateSpeakerLevel();
            UpdatePreview();
        });
        // Runs continuously (not just while recording) so the live preview updates as soon as a source is
        // selected, before the user ever presses Start Recording.
        _uiTimer.Start();
        _updateCheckTimer = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => _ = CheckForUpdatesAsync());
        _updateCheckTimer.Start();

        // Fires from VideoCaptureService's WGC Closed handler, on WGC's own thread.
        _manager.CaptureTargetLost += () => Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = "The captured window was closed.";
            _toasts.Warning("Captured window closed", "The recording was stopped because its window closed.");
            if (IsBusy) _ = StopRecordingAsync();
        });

        RefreshMonitors();
        RefreshMicrophones();
        RefreshWindows();
        LoadAndApplySettings();
        _ = InitializeWebcamAsync();

        // CommunityToolkit.Mvvm's generated setters skip On*Changed when the loaded value equals the field
        // default, so sync both monitors explicitly to whatever LoadAndApplySettings landed on.
        RestartMicMonitor();
        RestartSpeakerMonitor();

        _ = CheckForUpdatesAsync();

        // Posted rather than called inline: arming creates Win32 windows — not something to do partway
        // through building the shell that owns this view model.
        Dispatcher.UIThread.Post(SyncAnnotationOverlay, DispatcherPriority.Background);
    }

    /// <summary>A view that shows the live preview became visible / hidden.</summary>
    public void AttachPreviewConsumer() => _previewConsumers++;
    public void DetachPreviewConsumer() => _previewConsumers = Math.Max(0, _previewConsumers - 1);

    /// <summary>Main window minimised (false) or restored (true).</summary>
    public void SetAppVisible(bool visible) => _isAppVisible = visible;

    private async Task CheckForUpdatesAsync(bool userInitiated = false)
    {
        if (_updateCheckInProgress || IsDownloadingUpdate) return;
        _updateCheckInProgress = true;
        if (userInitiated) UpdateCheckStatus = "Checking for updates…";
        try
        {
            var info = await _updateService.CheckForUpdateAsync();
            if (info is null)
            {
                if (userInitiated) Dispatcher.UIThread.Post(() =>
                {
                    UpdateCheckStatus = $"You're up to date · checked {DateTime.Now:t}";
                    _toasts.Success("Cap-IT is up to date", $"You have the latest version ({AppVersionShort}).");
                });
                return;
            }

            // Don't re-announce the same release on every timer tick.
            if (!userInitiated && _pendingUpdate?.Version >= info.Version) return;

            _pendingUpdate = info;
            Dispatcher.UIThread.Post(() =>
            {
                var current = UpdateService.CurrentVersion.ToString(3);
                UpdateVersionText = info.VersionTag;
                UpdateBannerMessage = info.InstallerUrl is not null
                    ? $"Version {info.VersionTag} is ready (you have v{current}). It installs in place and restarts Cap-IT."
                    : $"Version {info.VersionTag} is available (you have v{current}). Open the release page to download it.";
                UpdateCheckStatus = $"Version {info.VersionTag} is available";
                UpdateAvailable = true;
            });
        }
        catch
        {
            // A network failure must not interrupt recording or show a misleading update message.
            if (userInitiated) Dispatcher.UIThread.Post(() => UpdateCheckStatus = "Couldn't reach GitHub. Check your connection and try again.");
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    [RelayCommand]
    private Task CheckForUpdatesNowAsync() => CheckForUpdatesAsync(userInitiated: true);

    /// <summary>(Re)applies the live mic level monitor on a background thread — off the UI thread is mandatory, see MicLevelMonitorService.</summary>
    private void RestartMicMonitor()
    {
        _micMonitorCts?.Cancel();
        var cts = _micMonitorCts = new CancellationTokenSource();
        var enabled = CaptureMicrophone;
        var deviceId = SelectedMicrophone?.Id;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(200, cts.Token); }
            catch (OperationCanceledException) { return; }

            if (enabled) _micLevelMonitor.Start(deviceId);
            else _micLevelMonitor.Stop();

            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(MicStatusText));
                OnPropertyChanged(nameof(IsMicMonitorAvailable));
            });
        });
    }

    /// <summary>Speaker/loopback equivalent of <see cref="RestartMicMonitor"/>.</summary>
    private void RestartSpeakerMonitor()
    {
        _speakerMonitorCts?.Cancel();
        var cts = _speakerMonitorCts = new CancellationTokenSource();
        var enabled = CaptureSystemAudio;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(200, cts.Token); }
            catch (OperationCanceledException) { return; }

            if (enabled) _speakerLevelMonitor.Start();
            else _speakerLevelMonitor.Stop();

            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(SpeakerStatusText));
                OnPropertyChanged(nameof(IsSpeakerMonitorAvailable));
            });
        });
    }

    /// <summary>Tears down monitors, preview, overlays and any recording so no capture thread keeps the process alive after the window closes.</summary>
    public void Shutdown()
    {
        FlushSettings();
        _uiTimer.Stop();
        _updateCheckTimer.Stop();
        _micMonitorCts?.Cancel();
        _speakerMonitorCts?.Cancel();
        _audioSourceCts?.Cancel();
        try { _micLevelMonitor.Dispose(); } catch { /* best effort */ }
        try { _speakerLevelMonitor.Dispose(); } catch { /* best effort */ }
        try { _annotations.Disarm(); } catch { /* best effort */ }
        try { _reviews.CloseAll(); } catch { /* best effort */ }
        try { _manager.Dispose(); } catch { /* best effort */ }
    }

    /// <summary>
    /// Webcam enumeration is WinRT-async, so it can't run inline with LoadAndApplySettings — this populates
    /// the list, then re-matches the saved device and applies the saved webcam/preset values.
    /// </summary>
    private async Task InitializeWebcamAsync()
    {
        await RefreshWebcamsAsync();

        _isLoadingSettings = true;
        try
        {
            var s = _settingsService.Load();
            SelectedAppTheme = AppThemeOptions.FirstOrDefault(t => t.Value == s.Theme) ?? AppThemeOptions[0];
            Presets.Clear();
            foreach (var preset in RecordingPreset.BuiltIn)
                Presets.Add(new RecordingPresetOption(preset.Clone(), true));
            foreach (var preset in s.CustomPresets ?? [])
                if (!string.IsNullOrWhiteSpace(preset.Name) && Presets.All(p => !p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase)))
                    Presets.Add(new RecordingPresetOption(preset, false));
            if (s.WebcamDeviceId is not null)
            {
                var match = Webcams.FirstOrDefault(w => w.Id == s.WebcamDeviceId);
                if (match is not null) SelectedWebcam = match;
            }
            WebcamEnabled = s.WebcamEnabled;
            SelectedWebcamTemplate = WebcamTemplateOptions.FirstOrDefault(t => t.Key == s.WebcamTemplate) ?? WebcamTemplateOptions[0];
            WebcamBrightness = s.WebcamBrightness;
            WebcamContrast = s.WebcamContrast;
            WebcamSaturation = s.WebcamSaturation;
            WebcamWarmth = s.WebcamWarmth;
            WebcamSmoothing = s.WebcamSmoothing;
        }
        finally
        {
            _isLoadingSettings = false;
        }
        OnPropertyChanged(nameof(WebcamStatusText));
    }

    /// <summary>Loads persisted preferences and re-matches saved devices against what was just enumerated.</summary>
    private void LoadAndApplySettings()
    {
        _isLoadingSettings = true;
        try
        {
            var s = _settingsService.Load();
            SelectedAppTheme = AppThemeOptions.FirstOrDefault(t => t.Value == s.Theme) ?? AppThemeOptions[0];

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

            // A saved HWND wouldn't survive a restart — re-match by title + process name, and only switch
            // into Window mode if that succeeds.
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
            SelectedEncoder = Enum.IsDefined(s.Encoder) ? s.Encoder : HardwareEncoder.Auto;
            SelectedContainer = Enum.IsDefined(s.Container) ? s.Container : OutputContainer.Mp4;
            SelectedResolution = ResolutionOptions.FirstOrDefault(r => r.Value == s.Resolution) ?? SelectedResolution;
            CaptureCursor = s.CaptureCursor;
            SelectedCursorStyle = CursorStyleOptions.FirstOrDefault(c => c.Value == s.CursorStyle) ?? SelectedCursorStyle;
            CaptureSystemAudio = s.CaptureSystemAudio;
            CaptureMicrophone = s.CaptureMicrophone;
            EnableMicNoiseSuppression = s.EnableMicNoiseSuppression;
            MouseTrackingZoomEnabled = s.MouseTrackingZoomEnabled;
            SelectedZoomLevel = ZoomLevelOptions.FirstOrDefault(z => z.Factor == s.ZoomFactor) ?? SelectedZoomLevel;
            ZoomOnClickOnly = s.ZoomOnClickOnly;
            InstantZoomOut = s.InstantZoomOut;
            ZoomAnimationSpeedPercent = Math.Clamp(s.ZoomAnimationSpeedPercent, 0, 50);
            KeystrokeOverlayEnabled = s.KeystrokeOverlayEnabled;
            SpotlightEnabled = s.SpotlightEnabled;
            SpotlightRadius = s.SpotlightRadius;
            ClickRipplesEnabled = s.ClickRipplesEnabled;
            ClickSoundsEnabled = s.ClickSoundsEnabled;
            SelectedClickSound = ClickSoundOptions.FirstOrDefault(x => x.Key == s.ClickSoundKey) ?? ClickSoundOptions[0];
            ClickSoundVolume = Math.Clamp(s.ClickSoundVolume * 100, 0, 100);
            AnnotationsEnabled = s.AnnotationsEnabled;
            SelectedAnnotationColor = AnnotationColorOptions.FirstOrDefault(c => c.Label == s.AnnotationColorLabel) ?? SelectedAnnotationColor;
            AnnotationStrokeThickness = s.AnnotationStrokeThickness > 0 ? s.AnnotationStrokeThickness : AnnotationStrokeThickness;
            SelectedAnnotationTool = AnnotationToolOptions.FirstOrDefault(t => t.Label == s.AnnotationToolLabel) ?? SelectedAnnotationTool;
            SelectedAnnotationFade = AnnotationFadeOptions.FirstOrDefault(t => t.Seconds == s.AnnotationFadeSeconds) ?? SelectedAnnotationFade;
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
        Theme = SelectedAppTheme.Value,
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
        Cursor = new CursorSettings { Enabled = CaptureCursor, Style = SelectedCursorStyle.Value },
        CaptureSystemAudio = CaptureSystemAudio,
        CaptureMicrophone = CaptureMicrophone,
        MicrophoneDeviceId = SelectedMicrophone?.Id,
        EnableMicNoiseSuppression = EnableMicNoiseSuppression,
        MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
        ZoomFactor = SelectedZoomLevel.Factor,
        ZoomOnClickOnly = ZoomOnClickOnly,
        InstantZoomOut = InstantZoomOut,
        ZoomAnimationSpeedPercent = ZoomAnimationSpeedPercent,
        KeystrokeOverlayEnabled = KeystrokeOverlayEnabled,
        WebcamEnabled = WebcamEnabled,
        WebcamDeviceId = SelectedWebcam?.Id,
        WebcamTemplate = SelectedWebcamTemplate.Key,
        WebcamBrightness = WebcamBrightness,
        WebcamContrast = WebcamContrast,
        WebcamSaturation = WebcamSaturation,
        WebcamWarmth = WebcamWarmth,
        WebcamSmoothing = WebcamSmoothing,
        SpotlightEnabled = SpotlightEnabled,
        SpotlightRadius = SpotlightRadius,
        ClickRipplesEnabled = ClickRipplesEnabled,
        ClickSoundsEnabled = ClickSoundsEnabled,
        ClickSoundKey = SelectedClickSound.Key,
        ClickSoundVolume = ClickSoundVolume / 100,
        AnnotationsEnabled = AnnotationsEnabled,
        AnnotationColorLabel = SelectedAnnotationColor.Label,
        AnnotationStrokeThickness = AnnotationStrokeThickness,
        AnnotationToolLabel = SelectedAnnotationTool.Label,
        AnnotationFadeSeconds = SelectedAnnotationFade.Seconds,
        MaximizeTextClarity = MaximizeTextClarity,
        OutputDirectory = OutputDirectory,
        CustomPresets = Presets.Where(p => !p.IsBuiltIn).Select(p => p.Preset.Clone()).ToList(),
    };

    [RelayCommand]
    private void ApplySelectedPreset()
    {
        if (SelectedPreset is null) return;
        var p = SelectedPreset.Preset;
        _applyingPreset = true;
        _isLoadingSettings = true;
        try
        {
            Fps = FpsOptions.Contains(p.Fps) ? p.Fps : Fps;
            SelectedResolution = ResolutionOptions.FirstOrDefault(x => x.Value == p.Resolution) ?? SelectedResolution;
            CaptureCursor = p.CaptureCursor;
            SelectedCursorStyle = CursorStyleOptions.FirstOrDefault(x => x.Value == p.CursorStyle) ?? SelectedCursorStyle;
            MouseTrackingZoomEnabled = p.MouseTrackingZoomEnabled;
            SelectedZoomLevel = ZoomLevelOptions.FirstOrDefault(x => x.Factor == p.ZoomFactor) ?? SelectedZoomLevel;
            ZoomOnClickOnly = p.ZoomOnClickOnly;
            InstantZoomOut = p.InstantZoomOut;
            ZoomAnimationSpeedPercent = Math.Clamp(p.ZoomAnimationSpeedPercent, 0, 50);
            KeystrokeOverlayEnabled = p.KeystrokeOverlayEnabled;
            WebcamEnabled = p.WebcamEnabled;
            CaptureSystemAudio = p.CaptureSystemAudio;
            CaptureMicrophone = p.CaptureMicrophone;
            EnableMicNoiseSuppression = p.EnableMicNoiseSuppression;
            AnnotationsEnabled = p.AnnotationsEnabled;
            SelectedAnnotationColor = AnnotationColorOptions.FirstOrDefault(x => x.Label == p.AnnotationColorLabel) ?? SelectedAnnotationColor;
            AnnotationStrokeThickness = p.AnnotationStrokeThickness > 0 ? p.AnnotationStrokeThickness : AnnotationStrokeThickness;
            SelectedAnnotationTool = AnnotationToolOptions.FirstOrDefault(x => x.Label == p.AnnotationToolLabel) ?? SelectedAnnotationTool;
            SelectedAnnotationFade = AnnotationFadeOptions.FirstOrDefault(x => x.Seconds == p.AnnotationFadeSeconds) ?? SelectedAnnotationFade;
            SpotlightEnabled = p.SpotlightEnabled;
            SpotlightRadius = p.SpotlightRadius;
            ClickRipplesEnabled = p.ClickRipplesEnabled;
            MaximizeTextClarity = p.MaximizeTextClarity;
        }
        finally
        {
            _isLoadingSettings = false;
            _applyingPreset = false;
        }
        // Setters above were suppressed from saving and from arming overlays; apply both now.
        SyncAnnotationOverlay();
        RestartPreviewIfIdle();
        FlushSettings();
        StatusMessage = $"Preset applied: {p.Name}";
        _toasts.Success("Preset applied", p.Name);
    }

    [RelayCommand]
    private void SaveCustomPreset()
    {
        var name = NewPresetName.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Presets.Any(p => p.IsBuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _toasts.Warning("Name already used", "Built-in presets cannot be overwritten. Choose another name.");
            return;
        }
        var existing = Presets.FirstOrDefault(p => !p.IsBuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var option = new RecordingPresetOption(BuildCurrentPreset(name), false);
        _applyingPreset = true;
        try
        {
            if (existing is null) Presets.Add(option);
            else Presets[Presets.IndexOf(existing)] = option;
            SelectedPreset = option;
        }
        finally { _applyingPreset = false; }
        NewPresetName = "";
        FlushSettings();
        _toasts.Success("Preset saved", name);
    }

    [RelayCommand]
    private void DeleteSelectedPreset()
    {
        if (SelectedPreset is not { IsBuiltIn: false } selected) return;
        Presets.Remove(selected);
        SelectedPreset = null;
        FlushSettings();
        _toasts.Info("Preset deleted", selected.Name);
    }

    private RecordingPreset BuildCurrentPreset(string name) => new()
    {
        Name = name, Fps = Fps, Resolution = SelectedResolution.Value, CaptureCursor = CaptureCursor,
        CursorStyle = SelectedCursorStyle.Value, MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
        ZoomFactor = SelectedZoomLevel.Factor, ZoomOnClickOnly = ZoomOnClickOnly,
        InstantZoomOut = InstantZoomOut, ZoomAnimationSpeedPercent = ZoomAnimationSpeedPercent,
        KeystrokeOverlayEnabled = KeystrokeOverlayEnabled, WebcamEnabled = WebcamEnabled,
        CaptureSystemAudio = CaptureSystemAudio, CaptureMicrophone = CaptureMicrophone,
        EnableMicNoiseSuppression = EnableMicNoiseSuppression, AnnotationsEnabled = AnnotationsEnabled,
        AnnotationColorLabel = SelectedAnnotationColor.Label, AnnotationStrokeThickness = AnnotationStrokeThickness,
        AnnotationToolLabel = SelectedAnnotationTool.Label, AnnotationFadeSeconds = SelectedAnnotationFade.Seconds,
        SpotlightEnabled = SpotlightEnabled, SpotlightRadius = SpotlightRadius,
        ClickRipplesEnabled = ClickRipplesEnabled, MaximizeTextClarity = MaximizeTextClarity
    };

    /// <summary>Debounced (~400 ms) save so a slider drag only hits disk once motion settles.</summary>
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

    /// <summary>Immediate save — on shutdown so the last pending change isn't lost.</summary>
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

    partial void OnIsStartingChanged(bool value) => OnPropertyChanged(nameof(StateLabel));

    partial void OnStateChanged(RecordingState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(PauseResumeButtonText));
        OnPropertyChanged(nameof(CanToggleAnnotations));
        OnPropertyChanged(nameof(CanChangeAudioLive));
        OnPropertyChanged(nameof(ShowAudioLockedHint));
        OnPropertyChanged(nameof(CanEditTextClarity));
        OnPropertyChanged(nameof(CanEditMicNoiseSuppression));
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();

        // Screen-freeze is cleared by the manager on stop / full-pause transitions.
        IsScreenFrozen = _manager.IsScreenFrozen;
        ToggleScreenPauseCommand.NotifyCanExecuteChanged();

        if (IsIdle) RestartPreviewIfIdle();
    }

    private void NotifyTargetChanged()
    {
        OnPropertyChanged(nameof(CurrentTargetSummary));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(HasCaptureTarget));
        StartRecordingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMonitorChanged(MonitorInfo? value)
    {
        NotifyTargetChanged();
        RestartPreviewIfIdle();
        SyncAnnotationOverlay(); // the overlay has to follow the display it's annotating
        QueueSaveSettings();
    }

    partial void OnSelectedAppThemeChanged(AppThemeOption value) => QueueSaveSettings();

    partial void OnSelectedWindowChanged(WindowInfo? value)
    {
        NotifyTargetChanged();
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedCaptureTargetKindChanged(CaptureTargetKindOption value)
    {
        OnPropertyChanged(nameof(IsWindowCaptureMode));
        OnPropertyChanged(nameof(IsMonitorCaptureMode));
        OnPropertyChanged(nameof(CanEnableAnnotations));
        OnPropertyChanged(nameof(CanToggleAnnotations));
        NotifyTargetChanged();
        RestartPreviewIfIdle();
        SyncAnnotationOverlay(); // window capture can't see the overlay at all — see CanEnableAnnotations
        QueueSaveSettings();
    }

    // Every effect below is composited per frame (or, for the webcam, lives on its own device lifecycle),
    // so all of them are pushed straight into the running capture instead of restarting it — they apply
    // live to the preview *and* to a recording in progress. RestartPreviewIfIdle only *starts* a preview
    // that isn't running yet: RecordingManager's Update* methods refresh the snapshot StartPreview dedups
    // against, so an already-running capture is left alone.
    partial void OnCaptureCursorChanged(bool value)
    {
        _manager.UpdateCursor(value, SelectedCursorStyle.Value);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedCursorStyleChanged(CursorStyleOption value)
    {
        _manager.UpdateCursor(CaptureCursor, value.Value);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnMouseTrackingZoomEnabledChanged(bool value)
    {
        _manager.UpdateZoom(value, SelectedZoomLevel.Factor, ZoomOnClickOnly, InstantZoomOut, ZoomAnimationSpeedPercent);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedZoomLevelChanged(ZoomLevelOption value)
    {
        _manager.UpdateZoom(MouseTrackingZoomEnabled, value.Factor, ZoomOnClickOnly, InstantZoomOut, ZoomAnimationSpeedPercent);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    /// <summary>Switching the zoom trigger mid-recording is safe; the camera eases between behaviours.</summary>
    partial void OnZoomOnClickOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(ZoomTriggerIndex));
        _manager.UpdateZoom(MouseTrackingZoomEnabled, SelectedZoomLevel.Factor, value, InstantZoomOut, ZoomAnimationSpeedPercent);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnInstantZoomOutChanged(bool value)
    {
        _manager.UpdateZoom(MouseTrackingZoomEnabled, SelectedZoomLevel.Factor, ZoomOnClickOnly, value, ZoomAnimationSpeedPercent);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnZoomAnimationSpeedPercentChanged(double value)
    {
        var clamped = Math.Clamp(value, 0, 50);
        if (Math.Abs(value - clamped) > .001) { ZoomAnimationSpeedPercent = clamped; return; }
        _manager.UpdateZoom(MouseTrackingZoomEnabled, SelectedZoomLevel.Factor, ZoomOnClickOnly, InstantZoomOut, clamped);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnKeystrokeOverlayEnabledChanged(bool value)
    {
        _manager.UpdateKeystrokeOverlay(value);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    public string WebcamStatusText => !WebcamEnabled ? "Off" : SelectedWebcam is null ? "No camera" : SelectedWebcam.Name;
    public bool HasWebcams => Webcams.Count > 0;

    partial void OnSelectedWebcamChanged(WebcamDeviceOption? value)
    {
        OnPropertyChanged(nameof(WebcamStatusText));
        UpdateWebcamAdjustments();
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnWebcamEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(WebcamStatusText));
        UpdateWebcamAdjustments();
        if (!value) WebcamPreviewSource = null;
        WebcamPreviewStatus = value ? "Waiting for camera frames…" : "Turn on the webcam and select a camera to preview it";
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedWebcamTemplateChanged(WebcamTemplateOption value)
    {
        UpdateWebcamAdjustments(value.Key);
        QueueSaveSettings();
    }

    partial void OnWebcamBrightnessChanged(double value) => UpdateWebcamAdjustments();
    partial void OnWebcamContrastChanged(double value) => UpdateWebcamAdjustments();
    partial void OnWebcamSaturationChanged(double value) => UpdateWebcamAdjustments();
    partial void OnWebcamWarmthChanged(double value) => UpdateWebcamAdjustments();
    partial void OnWebcamSmoothingChanged(double value) => UpdateWebcamAdjustments();

    [RelayCommand]
    private void ResetWebcamAdjustments()
    {
        WebcamBrightness = 0; WebcamContrast = 1; WebcamSaturation = 1; WebcamWarmth = 0; WebcamSmoothing = 0;
    }

    private void UpdateWebcamAdjustments(string? template = null)
    {
        _manager.UpdateWebcam(WebcamEnabled, SelectedWebcam?.Id, template ?? SelectedWebcamTemplate.Key,
            WebcamBrightness, WebcamContrast, WebcamSaturation, WebcamWarmth, WebcamSmoothing);
        QueueSaveSettings();
    }

    // Webcam frames arrive on the camera's own thread; only the newest pending frame is uploaded so a
    // slow UI tick never queues a backlog of 8 MB copies.
    private byte[]? _pendingWebcamFrame;
    private int _pendingWebcamWidth, _pendingWebcamHeight;
    private int _webcamUploadQueued;

    private void OnWebcamFrameReady(byte[] frame, int width, int height)
    {
        Volatile.Write(ref _pendingWebcamFrame, frame);
        _pendingWebcamWidth = width; _pendingWebcamHeight = height;
        if (Interlocked.Exchange(ref _webcamUploadQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _webcamUploadQueued, 0);
            var pending = Interlocked.Exchange(ref _pendingWebcamFrame, null);
            if (pending is null || !WebcamEnabled) return;
            var w = _pendingWebcamWidth; var h = _pendingWebcamHeight;
            if (WebcamPreviewSource is null || WebcamPreviewSource.PixelSize.Width != w || WebcamPreviewSource.PixelSize.Height != h)
                WebcamPreviewSource = CreateFrameBitmap(w, h);
            CopyIntoBitmap(WebcamPreviewSource, pending, w, h);
            WebcamPreviewVersion++;
            WebcamPreviewStatus = "";
        }, DispatcherPriority.Background);
    }

    partial void OnWebcamPreviewSourceChanged(WriteableBitmap? value) =>
        OnPropertyChanged(nameof(HasWebcamPreview));

    // Spotlight is a pure per-frame compositing parameter, pushed live so the radius can be tuned while
    // watching the result, even mid-recording.
    partial void OnSpotlightEnabledChanged(bool value)
    {
        _manager.UpdateSpotlight(value, SpotlightRadius);
        QueueSaveSettings();
    }

    partial void OnSpotlightRadiusChanged(double value)
    {
        _manager.UpdateSpotlight(SpotlightEnabled, value);
        QueueSaveSettings();
    }

    partial void OnClickRipplesEnabledChanged(bool value)
    {
        _manager.UpdateClickRipples(value);
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnClickSoundsEnabledChanged(bool value)
    {
        UpdateClickSound();
        QueueSaveSettings();
    }

    partial void OnSelectedClickSoundChanged(ClickSoundOption value)
    {
        UpdateClickSound();
        QueueSaveSettings();
    }

    partial void OnClickSoundVolumeChanged(double value)
    {
        UpdateClickSound();
        QueueSaveSettings();
    }

    private void UpdateClickSound() =>
        _manager.UpdateClickSound(ClickSoundsEnabled, SelectedClickSound.FileName, ClickSoundVolume / 100);

    partial void OnAnnotationsEnabledChanged(bool value)
    {
        SyncAnnotationOverlay();
        if (value && !_isLoadingSettings) StatusMessage = "Annotations ready — press Ctrl+Shift+D anywhere to start drawing.";
        QueueSaveSettings();
    }

    partial void OnSelectedAnnotationColorChanged(AnnotationColorOption value)
    {
        if (!_syncingAnnotationFromToolbar)
            _annotations.UpdateDrawingAttributes(SelectedAnnotationTool.Value, value.Value, AnnotationStrokeThickness);
        QueueSaveSettings();
    }

    partial void OnAnnotationStrokeThicknessChanged(double value)
    {
        if (!_syncingAnnotationFromToolbar)
            _annotations.UpdateDrawingAttributes(SelectedAnnotationTool.Value, SelectedAnnotationColor.Value, value);
        QueueSaveSettings();
    }

    partial void OnSelectedAnnotationToolChanged(AnnotationToolOption value)
    {
        if (!_syncingAnnotationFromToolbar)
            _annotations.UpdateDrawingAttributes(value.Value, SelectedAnnotationColor.Value, AnnotationStrokeThickness);
        QueueSaveSettings();
    }

    partial void OnSelectedAnnotationFadeChanged(AnnotationFadeOption value)
    {
        _annotations.UpdateFadeSeconds(value.Seconds);
        QueueSaveSettings();
    }

    /// <summary>A tool / colour / thickness was picked on the on-screen toolbar — mirror it here without pushing it straight back into the overlay.</summary>
    private void OnAnnotationAttributesChangedFromToolbar(AnnotationTool tool, System.Drawing.Color color, double thickness)
    {
        _syncingAnnotationFromToolbar = true;
        try
        {
            SelectedAnnotationTool = AnnotationToolOptions.FirstOrDefault(t => t.Value == tool) ?? SelectedAnnotationTool;
            var match = AnnotationColorOptions.FirstOrDefault(c =>
                c.Value.R == color.R && c.Value.G == color.G && c.Value.B == color.B);
            if (match is not null) SelectedAnnotationColor = match;
            AnnotationStrokeThickness = thickness;
        }
        finally
        {
            _syncingAnnotationFromToolbar = false;
        }
    }

    partial void OnSelectedEncoderChanged(HardwareEncoder value)
    {
        OnPropertyChanged(nameof(CanMaximizeTextClarity));
        OnPropertyChanged(nameof(CanEditTextClarity));
        OnPropertyChanged(nameof(SelectedEncoderOption));
        QueueSaveSettings();
    }

    partial void OnVideoBitrateKbpsChanged(double value) => QueueSaveSettings();

    partial void OnFfmpegSetupRequiredChanged(bool value) => OnPropertyChanged(nameof(ShowFFmpegSetup));
    partial void OnIsDownloadingFFmpegChanged(bool value) => OnPropertyChanged(nameof(ShowFFmpegSetup));
    partial void OnFfmpegDownloadProgressChanged(double value) => OnPropertyChanged(nameof(FFmpegDownloadProgressText));

    partial void OnSelectedMicrophoneChanged(AudioDeviceOption? value)
    {
        QueueSaveSettings();
        RestartMicMonitor();
        ApplyAudioSourcesLive();
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(IsMicMonitorAvailable));
    }

    /// <summary>
    /// Pushes the current audio-source selection into a running recording, off the UI thread and debounced.
    /// No-op when idle or when the pipeline can't take a source change — see RecordingManager.CanChangeAudioSourcesLive.
    /// </summary>
    private void ApplyAudioSourcesLive()
    {
        if (!_manager.CanChangeAudioSourcesLive) return;

        _audioSourceCts?.Cancel();
        var cts = _audioSourceCts = new CancellationTokenSource();
        var systemAudio = CaptureSystemAudio;
        var microphone = CaptureMicrophone;
        var micDeviceId = SelectedMicrophone?.Id;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(200, cts.Token); }
            catch (OperationCanceledException) { return; }

            try { _manager.UpdateAudioSources(systemAudio, microphone, micDeviceId); }
            catch { /* best effort: a device that won't open shouldn't take the recording down */ }
        });
    }

    partial void OnMicLevelChanged(double value)
    {
        OnPropertyChanged(nameof(IsMicSignalPresent));
        OnPropertyChanged(nameof(MicStatusText));
    }

    partial void OnFpsChanged(int value) => QueueSaveSettings();

    partial void OnSelectedContainerChanged(OutputContainer value)
    {
        OnPropertyChanged(nameof(SelectedContainerOption));
        QueueSaveSettings();
    }

    partial void OnSelectedResolutionChanged(ResolutionOption value) => QueueSaveSettings();

    partial void OnSelectedPresetChanged(RecordingPresetOption? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedPreset));
        if (value is not null && !_applyingPreset) ApplySelectedPreset();
    }

    partial void OnCaptureSystemAudioChanged(bool value)
    {
        QueueSaveSettings();
        OnPropertyChanged(nameof(ShowSpeakerMeter));
        RestartSpeakerMonitor();
        ApplyAudioSourcesLive();
        // IsActive can flip without SpeakerLevel changing, so nudge the dependent properties explicitly.
        OnPropertyChanged(nameof(SpeakerStatusText));
        OnPropertyChanged(nameof(IsSpeakerMonitorAvailable));
    }

    partial void OnSpeakerLevelChanged(double value)
    {
        OnPropertyChanged(nameof(IsSpeakerSignalPresent));
        OnPropertyChanged(nameof(SpeakerStatusText));
    }

    partial void OnCaptureMicrophoneChanged(bool value)
    {
        QueueSaveSettings();
        OnPropertyChanged(nameof(ShowMicMeter));
        OnPropertyChanged(nameof(CanEditMicNoiseSuppression));
        RestartMicMonitor();
        ApplyAudioSourcesLive();
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(IsMicMonitorAvailable));
    }

    partial void OnEnableMicNoiseSuppressionChanged(bool value) => QueueSaveSettings();

    partial void OnMaximizeTextClarityChanged(bool value) => QueueSaveSettings();

    partial void OnOutputDirectoryChanged(string value) => QueueSaveSettings();

    /// <summary>
    /// Arms the annotation overlay over the selected display whenever Annotations is on and a display is
    /// the capture target, and tears it down otherwise. Must run on the UI thread (creates Win32 windows).
    /// </summary>
    private void SyncAnnotationOverlay()
    {
        // Suppressed during the startup/preset load; the constructor posts one deliberate sync once the
        // shell is up, and ApplySelectedPreset syncs after applying.
        if (_isLoadingSettings) return;

        if (AnnotationsEnabled && IsMonitorCaptureMode && SelectedMonitor is { } monitor)
        {
            try { _annotations.Arm(monitor, SelectedAnnotationTool.Value, SelectedAnnotationColor.Value, AnnotationStrokeThickness, SelectedAnnotationFade.Seconds); }
            catch { /* best effort: annotations are an add-on, never worth failing a recording over */ }
        }
        else
        {
            _annotations.Disarm();
        }
    }

    /// <summary>(Re)starts the before-recording live preview on a background thread.</summary>
    private void RestartPreviewIfIdle()
    {
        var isWindowMode = IsWindowCaptureMode;
        if (!IsIdle || (isWindowMode ? SelectedWindow is null : SelectedMonitor is null)) return;

        var targetKind = SelectedCaptureTargetKind.Value;
        var monitor = isWindowMode ? null : SelectedMonitor;
        var window = isWindowMode ? SelectedWindow : null;
        StartPreview(targetKind, monitor, window);
    }

    /// <summary>Shows a picker candidate in the live preview without changing the committed recording target.</summary>
    public void PreviewCaptureSource(MonitorInfo? monitor, WindowInfo? window)
    {
        if (!IsIdle || (monitor is null && window is null)) return;
        StartPreview(window is null ? CaptureTargetKind.Monitor : CaptureTargetKind.Window, monitor, window);
    }

    private void StartPreview(CaptureTargetKind targetKind, MonitorInfo? monitor, WindowInfo? window)
    {
        var cursor = CaptureCursor;
        var cursorStyle = SelectedCursorStyle.Value;
        var zoomEnabled = MouseTrackingZoomEnabled;
        var zoomFactor = SelectedZoomLevel.Factor;
        var zoomClickOnly = ZoomOnClickOnly;
        var keystrokeOverlay = KeystrokeOverlayEnabled;
        var webcamEnabled = WebcamEnabled;
        var webcamDeviceId = SelectedWebcam?.Id;
        var webcamTemplate = SelectedWebcamTemplate.Key;
        var spotlightEnabled = SpotlightEnabled;
        var spotlightRadius = SpotlightRadius;
        var clickRipplesEnabled = ClickRipplesEnabled;
        var brightness = WebcamBrightness; var contrast = WebcamContrast; var saturation = WebcamSaturation;
        var warmth = WebcamWarmth; var smoothing = WebcamSmoothing;
        var instantZoomOut = InstantZoomOut; var zoomSpeed = ZoomAnimationSpeedPercent;
        _ = Task.Run(() =>
        {
            try
            {
                _manager.StartPreview(targetKind, monitor, window, cursor, cursorStyle, zoomEnabled, zoomFactor, keystrokeOverlay,
                    webcamEnabled, webcamDeviceId, spotlightEnabled, spotlightRadius, clickRipplesEnabled, zoomClickOnly, webcamTemplate,
                    brightness, contrast, saturation, warmth, smoothing, instantZoomOut, zoomSpeed);
            }
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

    /// <summary>Fresh enumeration for the visual source picker, which must not disturb the current selection.</summary>
    public IReadOnlyList<MonitorInfo> EnumerateMonitors() => _manager.GetMonitors();

    /// <inheritdoc cref="EnumerateMonitors"/>
    public IReadOnlyList<WindowInfo> EnumerateWindows() => _manager.GetWindows();

    /// <summary>Applies a choice made in the visual source picker. Exactly one of the two is non-null.</summary>
    public void ApplyCaptureSource(MonitorInfo? monitor, WindowInfo? window)
    {
        if (window is not null)
        {
            // The picker enumerates independently, so this window may not be in the list yet.
            var existing = Windows.FirstOrDefault(w => w.Handle == window.Handle);
            if (existing is null)
            {
                Windows.Add(window);
                existing = window;
            }
            SelectedWindow = existing;
            SelectedCaptureTargetKind = CaptureTargetKindOptions.First(k => k.Value == CaptureTargetKind.Window);
        }
        else if (monitor is not null)
        {
            var match = Monitors.FirstOrDefault(m => m.DeviceName == monitor.DeviceName);
            if (match is null)
            {
                RefreshMonitors(); // display connected since startup
                match = Monitors.FirstOrDefault(m => m.DeviceName == monitor.DeviceName);
            }
            if (match is not null) SelectedMonitor = match;
            SelectedCaptureTargetKind = CaptureTargetKindOptions.First(k => k.Value == CaptureTargetKind.Monitor);
        }
    }

    [RelayCommand]
    private void UseWholeDisplay() =>
        SelectedCaptureTargetKind = CaptureTargetKindOptions.First(k => k.Value == CaptureTargetKind.Monitor);

    [RelayCommand]
    private void UseSingleWindow() =>
        SelectedCaptureTargetKind = CaptureTargetKindOptions.First(k => k.Value == CaptureTargetKind.Window);

    [RelayCommand]
    private async Task RefreshWebcamsAsync()
    {
        var current = SelectedWebcam?.Id;
        var webcams = await WebcamDeviceEnumerator.GetWebcamsAsync();
        Webcams.Clear();
        foreach (var w in webcams) Webcams.Add(w);
        SelectedWebcam = Webcams.FirstOrDefault(w => w.Id == current) ?? Webcams.FirstOrDefault();
        OnPropertyChanged(nameof(HasWebcams));
    }

    /// <summary>
    /// Confirms ffmpeg is available before recording starts, offering to download it in place if it
    /// isn't. Returns false if the user declined or the download failed.
    /// </summary>
    private async Task<bool> EnsureFFmpegAvailableAsync()
    {
        if (FFmpegLocator.FindFFmpeg() is not null) return true;

        _ffmpegDecisionTcs = new TaskCompletionSource<bool>();
        FfmpegSetupMessage = "Cap-IT records through FFmpeg, which isn't installed yet. Download it now (about 90 MB)? This only happens once.";
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
            _toasts.Success("FFmpeg installed", "Recording is ready.");
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
            _toasts.Error("FFmpeg download failed", "Check your internet connection and try again.");
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

    [RelayCommand]
    private void DismissUpdate() => UpdateAvailable = false;

    [RelayCommand]
    private void ViewReleaseNotes()
    {
        try { UpdateService.OpenReleasesPage(_pendingUpdate?.ReleaseNotesUrl); }
        catch { /* browser refused to open — nothing more we can do */ }
    }

    [RelayCommand]
    private void CancelUpdateDownload() => _updateDownloadCts?.Cancel();

    /// <summary>Downloads the update installer and hands off to it; the app exits so the installer can replace it in place.</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_pendingUpdate is null) return;
        if (_pendingUpdate.InstallerUrl is null) { ViewReleaseNotes(); return; }

        IsDownloadingUpdate = true;
        _updateDownloadCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => UpdateDownloadProgress = p);
            var installerPath = await _updateService.DownloadInstallerAsync(_pendingUpdate, progress, _updateDownloadCts.Token);

            FlushSettings();
            _toasts.Success("Update downloaded", "Cap-IT will close and the installer will restart it.");
            _updateService.LaunchInstaller(installerPath, () => Dispatcher.UIThread.Post(_requestExit));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Update download cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            UpdateBannerMessage = "Couldn't download the update automatically — use “Release notes” to get it from GitHub.";
            _toasts.Error("Update failed", "Use Release notes to download it from GitHub.");
        }
        finally
        {
            IsDownloadingUpdate = false;
            UpdateDownloadProgress = 0;
            _updateDownloadCts?.Dispose();
            _updateDownloadCts = null;
        }
    }

    private bool CanStart() => IsIdle && !IsStarting && (IsWindowCaptureMode ? SelectedWindow is not null : SelectedMonitor is not null);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRecordingAsync()
    {
        var isWindowMode = IsWindowCaptureMode;
        if (isWindowMode ? SelectedWindow is null : SelectedMonitor is null) return;

        IsStarting = true;
        StartRecordingCommand.NotifyCanExecuteChanged();
        try
        {
            if (!await EnsureFFmpegAvailableAsync()) return;

            // Only the *active* target is carried into the session — VideoCaptureService.Prepare infers its
            // mode from whichever argument is non-null.
            var monitorTarget = isWindowMode ? null : SelectedMonitor;
            var windowTarget = isWindowMode ? SelectedWindow : null;

            var settings = new RecordingSettings
            {
                CaptureTargetKind = SelectedCaptureTargetKind.Value,
                MonitorHandle = monitorTarget?.Handle ?? 0,
                MonitorFriendlyName = monitorTarget?.FriendlyName ?? "",
                TargetWindowHandle = windowTarget?.Handle ?? 0,
                TargetWindowTitle = windowTarget?.Title,
                Fps = Fps,
                VideoBitrateKbps = (int)VideoBitrateKbps,
                CaptureSystemAudio = CaptureSystemAudio,
                CaptureMicrophone = CaptureMicrophone,
                MicrophoneDeviceId = SelectedMicrophone?.Id,
                EnableMicNoiseSuppression = EnableMicNoiseSuppression,
                CaptureCursor = CaptureCursor,
                CursorStyle = SelectedCursorStyle.Value,
                Cursor = new CursorSettings { Enabled = CaptureCursor, Style = SelectedCursorStyle.Value },
                MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
                ZoomFactor = SelectedZoomLevel.Factor,
                ZoomOnClickOnly = ZoomOnClickOnly,
                KeystrokeOverlayEnabled = KeystrokeOverlayEnabled,
                WebcamEnabled = WebcamEnabled,
                WebcamDeviceId = SelectedWebcam?.Id,
                WebcamTemplate = SelectedWebcamTemplate.Key,
                WebcamBrightness = WebcamBrightness,
                WebcamContrast = WebcamContrast,
                WebcamSaturation = WebcamSaturation,
                WebcamWarmth = WebcamWarmth,
                WebcamSmoothing = WebcamSmoothing,
                SpotlightEnabled = SpotlightEnabled,
                SpotlightRadius = SpotlightRadius,
                ClickRipplesEnabled = ClickRipplesEnabled,
                Encoder = SelectedEncoder,
                Container = SelectedContainer,
                Resolution = SelectedResolution.Value,
                OutputDirectory = OutputDirectory,
                MaximizeTextClarity = MaximizeTextClarity && CanMaximizeTextClarity,
            };

            StatusMessage = "Starting…";
            await _manager.StartAsync(settings, monitorTarget, windowTarget);
            State = _manager.State;

            // Idempotent — normally already armed from when Annotations was switched on.
            SyncAnnotationOverlay();

            var targetLabel = isWindowMode ? SelectedWindow!.Title : SelectedMonitor!.FriendlyName;
            StatusMessage = $"Recording {targetLabel} @ {Fps} FPS ({SelectedResolution.Label})";
        }
        catch (Exception ex)
        {
            State = RecordingState.Idle;
            StatusMessage = $"Failed to start: {ex.Message}";
            RecordingStartFailed?.Invoke(ex);
        }
        finally
        {
            IsStarting = false;
            StartRecordingCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Raised on the UI thread when a recording could not start, so the shell can explain it.</summary>
    public event Action<Exception>? RecordingStartFailed;

    private bool CanStop() => IsRecording || IsPaused;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopRecordingAsync()
    {
        StatusMessage = "Finalizing… (optimizing for playback)";
        var path = await _manager.StopAsync();
        LastOutputPath = path;
        State = _manager.State; // restarts the live preview since we're idle again
        StatusMessage = path is not null ? $"Saved: {path}" : "Recording stopped.";

        if (path is not null)
        {
            RecordingSaved?.Invoke(path);
            _toasts.Success("Recording saved", Path.GetFileName(path), "Show in folder", () => RevealInExplorer(path));
            ReviewRecording(path);
        }
        else
        {
            _toasts.Warning("Recording stopped", "Nothing was saved. Check the output folder and try again.");
        }
    }

    public void ReviewRecording(string path)
    {
        try
        {
            _reviews.Open(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open recording review window: {ex}");
            StatusMessage = $"Saved: {path} (review could not be opened)";
            _toasts.Error("Couldn't open the editor", "The recording is saved. Open it from Recordings.");
        }
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

    // Screen-freeze is meaningless while fully paused — enabled only while recording, or while frozen.
    private bool CanScreenPause() => IsRecording || IsScreenFrozen;

    [RelayCommand(CanExecute = nameof(CanScreenPause))]
    private void ToggleScreenPause()
    {
        if (IsScreenFrozen)
        {
            _manager.UnfreezeScreen();
            StatusMessage = "Recording…";
        }
        else
        {
            _manager.FreezeScreen();
            StatusMessage = "Screen paused — audio and timer still recording";
        }
        IsScreenFrozen = _manager.IsScreenFrozen;
    }

    [RelayCommand]
    private void ToggleAnnotations()
    {
        if (CanToggleAnnotations || AnnotationsEnabled) AnnotationsEnabled = !AnnotationsEnabled;
    }

    /// <summary>Same as Ctrl+Shift+D — for the floating controller's Draw button.</summary>
    [RelayCommand]
    private void ToggleDrawingMode()
    {
        if (_annotations.IsArmed) _annotations.ToggleDrawingMode();
        else if (CanToggleAnnotations) AnnotationsEnabled = true;
    }

    [RelayCommand]
    private void ToggleWebcam() => WebcamEnabled = !WebcamEnabled;

    [RelayCommand]
    private void ToggleMicrophone() { if (CanChangeAudioLive) CaptureMicrophone = !CaptureMicrophone; }

    [RelayCommand]
    private void ToggleSmartZoom() => MouseTrackingZoomEnabled = !MouseTrackingZoomEnabled;

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var folder = LastOutputPath is not null ? Path.GetDirectoryName(LastOutputPath) : OutputDirectory;
        if (string.IsNullOrEmpty(folder)) return;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private static void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch { /* Explorer unavailable */ }
    }

    private void UpdateElapsed()
    {
        var e = _manager.Elapsed;
        ElapsedText = e.ToString(e.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
    }

    // Meter floor in dBFS — mapping dB (how loudness is perceived) makes the meter visibly track a voice,
    // where linear RMS barely leaves the left edge for normal speech.
    private const double MeterFloorDb = -60.0;

    /// <summary>Converts a raw 0..1 RMS reading to a 0..1 meter position on a dBFS scale.</summary>
    private static double ToMeterScale(float rms)
    {
        if (rms <= 0.0000001f) return 0;
        var db = 20.0 * Math.Log10(rms);
        return Math.Clamp((db - MeterFloorDb) / -MeterFloorDb, 0, 1);
    }

    private void UpdateMicLevel()
    {
        if (!_micLevelMonitor.IsActive)
        {
            MicLevel = 0;
            return;
        }

        // Fast attack / slow release reads as a real VU meter instead of jittering with every sample.
        var raw = ToMeterScale(_micLevelMonitor.CurrentLevel);
        MicLevel = raw > MicLevel ? raw : MicLevel * 0.72;
    }

    private void UpdateSpeakerLevel()
    {
        if (!_speakerLevelMonitor.IsActive)
        {
            SpeakerLevel = 0;
            return;
        }

        var raw = ToMeterScale(_speakerLevelMonitor.CurrentLevel);
        SpeakerLevel = raw > SpeakerLevel ? raw : SpeakerLevel * 0.72;
    }

    /// <summary>
    /// Opaque BGRA bitmap for capture frames. Desktop-duplication frames carry an alpha byte that is
    /// usually 0 for otherwise-opaque content; an <see cref="AlphaFormat.Opaque"/> surface ignores it, so
    /// no per-pixel "force opaque" pass is needed before display.
    /// </summary>
    private static WriteableBitmap CreateFrameBitmap(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private static void CopyIntoBitmap(WriteableBitmap bitmap, byte[] source, int width, int height)
    {
        using var buffer = bitmap.Lock();
        var rowBytes = width * 4;
        if (buffer.RowBytes == rowBytes)
        {
            Marshal.Copy(source, 0, buffer.Address, Math.Min(source.Length, rowBytes * height));
            return;
        }
        for (var y = 0; y < height; y++)
            Marshal.Copy(source, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
    }

    private void UpdatePreview()
    {
        if (SuppressLivePreviewForSnapshot)
        {
            if (PreviewSource is not null) PreviewSource = null;
            return;
        }

        // Only read a frame back when something is actually showing it — each request costs the capture
        // thread a GPU readback and an 8 MB copy at 1080p.
        if (_previewConsumers == 0 || !_isAppVisible) return;

        int w = _manager.PreviewWidth;
        int h = _manager.PreviewHeight;
        if (w <= 0 || h <= 0) return;

        if (_previewBuffer is null || _previewBuffer.Length != w * h * 4)
        {
            _previewBuffer = new byte[w * h * 4];
        }

        if (!_manager.TryGetPreviewFrame(_previewBuffer)) return;

        if (PreviewSource is null || PreviewSource.PixelSize.Width != w || PreviewSource.PixelSize.Height != h)
        {
            PreviewSource = CreateFrameBitmap(w, h);
        }

        CopyIntoBitmap(PreviewSource, _previewBuffer, w, h);
        PreviewVersion++;
    }
}
