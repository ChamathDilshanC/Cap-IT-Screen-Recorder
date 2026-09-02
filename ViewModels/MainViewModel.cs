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
using ScreenRecorderApp.Services.Overlay;
using ScreenRecorderApp.Views;

namespace ScreenRecorderApp.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly RecordingManager _manager = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly AnnotationOverlayService _annotations;
    private readonly MicLevelMonitorService _micLevelMonitor = new();
    private readonly SpeakerLevelMonitorService _speakerLevelMonitor = new();
    private readonly UpdateService _updateService = new();
    private readonly DispatcherQueueTimer _uiTimer;

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

    /// <summary>What the app will record right now, in one line — shown next to the source picker button so the current target is readable without opening a dropdown.</summary>
    public string CurrentTargetSummary => IsWindowCaptureMode
        ? SelectedWindow is null ? "No window selected" : $"Window · {SelectedWindow.Title}"
        : SelectedMonitor is null ? "No display selected" : $"Display · {SelectedMonitor}";

    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private WindowInfo? _selectedWindow;
    [ObservableProperty] private AudioDeviceOption? _selectedMicrophone;

    [ObservableProperty] private bool _captureSystemAudio = true;
    [ObservableProperty] private bool _captureMicrophone;

    // Live microphone level meter, shown on the Home tab so the user can confirm the mic is actually
    // picking up sound before (not just during) recording — see MicLevelMonitorService. 0..1, smoothed
    // with a fast attack / slow release (like a real VU meter) in UpdateMicLevel() below rather than
    // shown raw, since the raw per-150ms-tick RMS reads as jittery rather than a level.
    [ObservableProperty] private double _micLevel;
    public bool ShowMicMeter => CaptureMicrophone;
    // Threshold sits on the dBFS meter scale (see ToMeterScale), not on raw RMS: 0.18 is about -49 dBFS,
    // comfortably above a quiet room's noise floor but well below any actual speech, so the label tracks
    // "someone is talking" rather than "the room exists".
    public bool IsMicSignalPresent => MicLevel > 0.18;
    /// <summary>Whether the level monitor could open the device at all — drives the meter's muted "unavailable" look.</summary>
    public bool IsMicMonitorAvailable => _micLevelMonitor.IsActive;
    // Distinguishes "the monitor couldn't even open the device" (wrong/disconnected mic, or Windows'
    // system-wide "Let desktop apps access your microphone" privacy toggle is off — WASAPI capture can
    // silently deliver empty buffers rather than failing outright in that case) from "it opened fine but
    // nobody's talking right now" — otherwise both look identical as a permanently-empty meter with no
    // way to tell which one you're looking at.
    public string MicStatusText => !_micLevelMonitor.IsActive ? "Mic unavailable" : IsMicSignalPresent ? "Mic active" : "No signal";
    // global:: needed because this class already has a member named "Windows" (the window-picker list),
    // which would otherwise shadow the Windows namespace here.
    public Microsoft.UI.Xaml.Media.SolidColorBrush MicMeterBrush => new(!_micLevelMonitor.IsActive ? global::Windows.UI.Color.FromArgb(255, 130, 130, 60) : MicLevel switch
    {
        > 0.85 => global::Windows.UI.Color.FromArgb(255, 232, 17, 35),  // near clipping
        > 0.03 => global::Windows.UI.Color.FromArgb(255, 16, 185, 90),  // picking up sound
        _ => global::Windows.UI.Color.FromArgb(255, 90, 90, 100),       // silence
    });

    // Live system/speaker output level meter — mirrors the mic meter above exactly, on the render
    // (playback) device instead of the capture device. See SpeakerLevelMonitorService.
    [ObservableProperty] private double _speakerLevel;
    public bool ShowSpeakerMeter => CaptureSystemAudio;
    public bool IsSpeakerSignalPresent => SpeakerLevel > 0.18;
    public bool IsSpeakerMonitorAvailable => _speakerLevelMonitor.IsActive;
    public string SpeakerStatusText => !_speakerLevelMonitor.IsActive ? "Speaker unavailable" : IsSpeakerSignalPresent ? "Playing" : "Silent";
    public Microsoft.UI.Xaml.Media.SolidColorBrush SpeakerMeterBrush => new(!_speakerLevelMonitor.IsActive ? global::Windows.UI.Color.FromArgb(255, 130, 130, 60) : SpeakerLevel switch
    {
        > 0.85 => global::Windows.UI.Color.FromArgb(255, 232, 17, 35),
        > 0.03 => global::Windows.UI.Color.FromArgb(255, 16, 185, 90),
        _ => global::Windows.UI.Color.FromArgb(255, 90, 90, 100),
    });

    // Studio Mic noise suppression (Phase 5 — ffmpeg afftdn/highpass/adeclick on the mic leg only).
    // Settings-only like CaptureMicrophone/CaptureSystemAudio above: it changes what RecordingManager
    // hands to ffmpeg at record start, not the live preview, so no RestartPreviewIfIdle().
    [ObservableProperty] private bool _enableMicNoiseSuppression;

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

    // Advanced cursor effects (Phase 4 Step 1 — hooks/settings/UI only; VideoCaptureService doesn't
    // render the spotlight or ripples onto frames yet, so these don't touch RestartPreviewIfIdle either,
    // same reasoning as the webcam properties above at their equivalent stage).
    [ObservableProperty] private bool _spotlightEnabled;
    [ObservableProperty] private double _spotlightRadius = 180;
    public string SpotlightRadiusLabel => $"{SpotlightRadius:0}px";
    [ObservableProperty] private bool _clickRipplesEnabled;

    // Live screen annotations (Phase 6 Step 1 — overlay window + click-through toggle + global hotkey
    // only; no InkCanvas/drawing surface yet, that's Step 2). Settings-only like WebcamEnabled:
    // AnnotationOverlayService is driven directly by Start/StopRecordingAsync using SelectedMonitor, not
    // through RecordingManager/RecordingSettings, so no RestartPreviewIfIdle() and no RecordingSettings
    // mapping is needed, just persistence.
    [ObservableProperty] private bool _annotationsEnabled;

    // Windows Graphics Capture for a specific window only captures that window's own surface, not other
    // windows layered on top of it — so the overlay is invisible to a window-mode recording no matter
    // what. Gated off entirely rather than silently no-op'd, so the UI is honest about the limitation.
    public bool CanEnableAnnotations => IsMonitorCaptureMode;

    // Combines the capture-mode gate above with the usual "can't change settings mid-recording" rule
    // every other toggle already follows — a single bindable property since x:Bind can't AND two
    // properties together without a converter.
    public bool CanToggleAnnotations => IsIdle && CanEnableAnnotations;

    // Pen color/thickness (Phase 6 Step 2). Unlike every other recording setting, these are deliberately
    // live-updatable mid-recording (see On*Changed below) — a presenter switching color between arrows
    // shouldn't have to stop recording to do it — so their IsEnabled in XAML is gated only by
    // AnnotationsEnabled, not IsIdle.
    public IReadOnlyList<AnnotationColorOption> AnnotationColorOptions { get; } = AnnotationColorOption.All;
    [ObservableProperty] private AnnotationColorOption _selectedAnnotationColor = AnnotationColorOption.All[0];
    [ObservableProperty] private double _annotationStrokeThickness = 6;
    public string AnnotationStrokeThicknessLabel => $"{AnnotationStrokeThickness:0}px";

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

    // Auto-update (GitHub Releases). The check runs once, in the background, at startup; the banner only
    // appears if a strictly-newer release exists. See UpdateService.
    private UpdateInfo? _pendingUpdate;
    private CancellationTokenSource? _updateDownloadCts;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBannerMessage = "";
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _updateDownloadProgress;
    public bool IsNotDownloadingUpdate => !IsDownloadingUpdate;
    public string UpdateDownloadProgressText => $"{UpdateDownloadProgress:0}%";

    partial void OnIsDownloadingUpdateChanged(bool value) => OnPropertyChanged(nameof(IsNotDownloadingUpdate));
    partial void OnUpdateDownloadProgressChanged(double value) => OnPropertyChanged(nameof(UpdateDownloadProgressText));

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
        _annotations = new AnnotationOverlayService(_dispatcherQueue);
        _uiTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(150);
        _uiTimer.Tick += (_, _) =>
        {
            UpdateElapsed();
            UpdateMicLevel();
            UpdateSpeakerLevel();
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

        // Not folded into OnCaptureMicrophoneChanged/OnCaptureSystemAudioChanged: CommunityToolkit.Mvvm's
        // generated property setters skip the On*Changed call entirely when the incoming value equals
        // the field-initializer default — which for CaptureSystemAudio (defaults to true both as a field
        // initializer and in AppSettings) means the common "always been on, nothing to load" case would
        // silently never start the speaker monitor at all. Explicitly syncing both monitors to whatever
        // LoadAndApplySettings actually landed on, unconditionally, sidesteps that no-op entirely.
        RestartMicMonitor();
        RestartSpeakerMonitor();

        _ = CheckForUpdatesAsync();

        // Posted rather than called inline: LoadAndApplySettings can turn AnnotationsEnabled on, and
        // arming creates a second WinUI Window — not something to do partway through building the one
        // that owns this view model. By the time this runs the shell is up and it's an ordinary call.
        _dispatcherQueue.TryEnqueue(SyncAnnotationOverlay);
    }

    /// <summary>One-shot background update check on startup. Silent unless a newer GitHub release exists.</summary>
    private async Task CheckForUpdatesAsync()
    {
        UpdateInfo? info;
        try { info = await _updateService.CheckForUpdateAsync(); }
        catch { return; }
        if (info is null) return;

        _pendingUpdate = info;
        _dispatcherQueue.TryEnqueue(() =>
        {
            var current = UpdateService.CurrentVersion.ToString(3);
            UpdateBannerMessage = info.InstallerUrl is not null
                ? $"Version {info.VersionTag} is available (you have v{current}). It installs to your current location and restarts the app."
                : $"Version {info.VersionTag} is available (you have v{current}). Open the releases page to download it.";
            UpdateAvailable = true;
        });
    }

    /// <summary>(Re)applies the live mic level monitor to the current CaptureMicrophone / SelectedMicrophone
    /// state on a background thread — off the UI thread is mandatory, see MicLevelMonitorService's
    /// remarks. A short debounce coalesces the burst of changes a user makes clicking through the device
    /// combo so only the final selection actually opens a capture.</summary>
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

            _dispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(MicStatusText));
                OnPropertyChanged(nameof(MicMeterBrush));
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

            _dispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(SpeakerStatusText));
                OnPropertyChanged(nameof(SpeakerMeterBrush));
                OnPropertyChanged(nameof(IsSpeakerMonitorAvailable));
            });
        });
    }

    /// <summary>Called from MainWindow.OnClosed. Tears down the live monitors, preview and any recording
    /// so no foreground WASAPI/WGC capture thread keeps ScreenRecorderApp.exe alive after the window is
    /// gone — a lingering process was what the next launch couldn't get past ("won't reopen").</summary>
    public void Shutdown()
    {
        FlushSettings();
        _uiTimer.Stop();
        _micMonitorCts?.Cancel();
        _speakerMonitorCts?.Cancel();
        try { _micLevelMonitor.Dispose(); } catch { /* best effort */ }
        try { _speakerLevelMonitor.Dispose(); } catch { /* best effort */ }
        try { _annotations.Disarm(); } catch { /* best effort */ }
        try { _manager.Dispose(); } catch { /* best effort */ }
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
            EnableMicNoiseSuppression = s.EnableMicNoiseSuppression;
            MouseTrackingZoomEnabled = s.MouseTrackingZoomEnabled;
            SelectedZoomLevel = ZoomLevelOptions.FirstOrDefault(z => z.Factor == s.ZoomFactor) ?? SelectedZoomLevel;
            KeystrokeOverlayEnabled = s.KeystrokeOverlayEnabled;
            SpotlightEnabled = s.SpotlightEnabled;
            SpotlightRadius = s.SpotlightRadius;
            ClickRipplesEnabled = s.ClickRipplesEnabled;
            AnnotationsEnabled = s.AnnotationsEnabled;
            SelectedAnnotationColor = AnnotationColorOptions.FirstOrDefault(c => c.Label == s.AnnotationColorLabel) ?? SelectedAnnotationColor;
            AnnotationStrokeThickness = s.AnnotationStrokeThickness > 0 ? s.AnnotationStrokeThickness : AnnotationStrokeThickness;
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
        EnableMicNoiseSuppression = EnableMicNoiseSuppression,
        MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
        ZoomFactor = SelectedZoomLevel.Factor,
        KeystrokeOverlayEnabled = KeystrokeOverlayEnabled,
        WebcamEnabled = WebcamEnabled,
        WebcamDeviceId = SelectedWebcam?.Id,
        SpotlightEnabled = SpotlightEnabled,
        SpotlightRadius = SpotlightRadius,
        ClickRipplesEnabled = ClickRipplesEnabled,
        AnnotationsEnabled = AnnotationsEnabled,
        AnnotationColorLabel = SelectedAnnotationColor.Label,
        AnnotationStrokeThickness = AnnotationStrokeThickness,
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
        OnPropertyChanged(nameof(CanToggleAnnotations));
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();

        if (IsIdle) RestartPreviewIfIdle();
    }

    partial void OnSelectedMonitorChanged(MonitorInfo? value)
    {
        OnPropertyChanged(nameof(CurrentTargetSummary));
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartPreviewIfIdle();
        SyncAnnotationOverlay(); // the overlay has to follow the display it's annotating
        QueueSaveSettings();
    }

    partial void OnSelectedWindowChanged(WindowInfo? value)
    {
        OnPropertyChanged(nameof(CurrentTargetSummary));
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnSelectedCaptureTargetKindChanged(CaptureTargetKindOption value)
    {
        OnPropertyChanged(nameof(IsWindowCaptureMode));
        OnPropertyChanged(nameof(IsMonitorCaptureMode));
        OnPropertyChanged(nameof(CurrentTargetSummary));
        OnPropertyChanged(nameof(CanEnableAnnotations));
        OnPropertyChanged(nameof(CanToggleAnnotations));
        StartRecordingCommand.NotifyCanExecuteChanged();
        RestartPreviewIfIdle();
        SyncAnnotationOverlay(); // window capture can't see the overlay at all — see CanEnableAnnotations
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

    partial void OnSelectedWebcamChanged(WebcamDeviceOption? value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnWebcamEnabledChanged(bool value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    // Both of these are pushed straight into the running capture rather than restarting it. The
    // spotlight is a pure per-frame compositing parameter (no device, no hook), so a restart was never
    // needed — and for the radius it was actively wrong: it used to be a Prepare()-time-only value with
    // no restart wired up at all, so dragging the slider saved a new number that nothing on screen ever
    // reflected. Going live also means the radius can be adjusted mid-recording, while you can actually
    // see the result, instead of being frozen for the whole session.
    partial void OnSpotlightEnabledChanged(bool value)
    {
        _manager.UpdateSpotlight(value, SpotlightRadius);
        QueueSaveSettings();
    }

    partial void OnSpotlightRadiusChanged(double value)
    {
        OnPropertyChanged(nameof(SpotlightRadiusLabel));
        _manager.UpdateSpotlight(SpotlightEnabled, value);
        QueueSaveSettings();
    }

    partial void OnClickRipplesEnabledChanged(bool value)
    {
        RestartPreviewIfIdle();
        QueueSaveSettings();
    }

    partial void OnAnnotationsEnabledChanged(bool value)
    {
        SyncAnnotationOverlay();
        if (value) StatusMessage = "Annotations ready — press Ctrl+Shift+D anywhere to start drawing.";
        QueueSaveSettings();
    }

    partial void OnSelectedAnnotationColorChanged(AnnotationColorOption value)
    {
        _annotations.UpdateDrawingAttributes(value.Value, AnnotationStrokeThickness);
        QueueSaveSettings();
    }

    partial void OnAnnotationStrokeThicknessChanged(double value)
    {
        OnPropertyChanged(nameof(AnnotationStrokeThicknessLabel));
        _annotations.UpdateDrawingAttributes(SelectedAnnotationColor.Value, value);
        QueueSaveSettings();
    }

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

    partial void OnSelectedMicrophoneChanged(AudioDeviceOption? value)
    {
        QueueSaveSettings();
        RestartMicMonitor();
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(MicMeterBrush));
        OnPropertyChanged(nameof(IsMicMonitorAvailable));
    }

    partial void OnMicLevelChanged(double value)
    {
        OnPropertyChanged(nameof(IsMicSignalPresent));
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(MicMeterBrush));
        OnPropertyChanged(nameof(IsMicMonitorAvailable));
    }

    partial void OnFpsChanged(int value) => QueueSaveSettings();

    partial void OnSelectedContainerChanged(OutputContainer value) => QueueSaveSettings();

    partial void OnSelectedResolutionChanged(ResolutionOption value) => QueueSaveSettings();

    partial void OnCaptureSystemAudioChanged(bool value)
    {
        QueueSaveSettings();
        OnPropertyChanged(nameof(ShowSpeakerMeter));
        RestartSpeakerMonitor();
        // IsActive can flip without SpeakerLevel itself changing (e.g. it's already 0 and Start() just
        // failed, or it's still 0 right after a successful Start() before the first callback arrives) —
        // OnSpeakerLevelChanged wouldn't fire in either case, so these need an explicit nudge here too.
        OnPropertyChanged(nameof(SpeakerStatusText));
        OnPropertyChanged(nameof(SpeakerMeterBrush));
        OnPropertyChanged(nameof(IsSpeakerMonitorAvailable));
    }

    partial void OnSpeakerLevelChanged(double value)
    {
        OnPropertyChanged(nameof(IsSpeakerSignalPresent));
        OnPropertyChanged(nameof(SpeakerStatusText));
        OnPropertyChanged(nameof(SpeakerMeterBrush));
        OnPropertyChanged(nameof(IsSpeakerMonitorAvailable));
    }

    partial void OnCaptureMicrophoneChanged(bool value)
    {
        QueueSaveSettings();
        OnPropertyChanged(nameof(ShowMicMeter));
        RestartMicMonitor();
        // See the matching comment in OnCaptureSystemAudioChanged — IsActive can change independently of
        // MicLevel, so OnMicLevelChanged alone can't be relied on to refresh these.
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(MicMeterBrush));
        OnPropertyChanged(nameof(IsMicMonitorAvailable));
    }

    partial void OnEnableMicNoiseSuppressionChanged(bool value) => QueueSaveSettings();

    partial void OnMaximizeTextClarityChanged(bool value) => QueueSaveSettings();

    partial void OnOutputDirectoryChanged(string value) => QueueSaveSettings();

    /// <summary>
    /// Brings the annotation overlay in line with the current settings — armed (and positioned over the
    /// selected display) whenever Annotations is on and a display is the capture target, torn down
    /// otherwise. Called on every input to that decision rather than only at record start, which is what
    /// the overlay used to be tied to: with it armed only while recording, switching Annotations on and
    /// pressing Ctrl+Shift+D did nothing at all, because neither the overlay nor its hotkey hook existed
    /// yet. Must run on the UI thread — it creates/destroys a WinUI Window.
    /// </summary>
    private void SyncAnnotationOverlay()
    {
        // Suppressed during the startup load. This is reached from the SelectedMonitor / capture-kind /
        // AnnotationsEnabled setters, all of which fire while LoadAndApplySettings runs — which is
        // inside this view model's constructor, itself inside MainWindow's. Creating a second WinUI
        // Window from there is asking for trouble; the constructor posts one deliberate sync via the
        // DispatcherQueue once the shell is actually up, and that is the only startup arming there is.
        if (_isLoadingSettings) return;

        if (AnnotationsEnabled && IsMonitorCaptureMode && SelectedMonitor is { } monitor)
        {
            try { _annotations.Arm(monitor, SelectedAnnotationColor.Value, AnnotationStrokeThickness); }
            catch { /* best effort: annotations are an add-on, never worth failing a recording over */ }
        }
        else
        {
            _annotations.Disarm();
        }
    }

    /// <summary>(Re)starts the before-recording live preview on a background thread. Safe to call any
    /// time a relevant setting changes; it's a no-op unless idle and a capture target is selected.</summary>
    private void RestartPreviewIfIdle()
    {
        var isWindowMode = IsWindowCaptureMode;
        if (!IsIdle || (isWindowMode ? SelectedWindow is null : SelectedMonitor is null)) return;

        var targetKind = SelectedCaptureTargetKind.Value;
        var monitor = isWindowMode ? null : SelectedMonitor;
        var window = isWindowMode ? SelectedWindow : null;
        var cursor = CaptureCursor;
        var cursorStyle = SelectedCursorStyle.Value;
        var zoomEnabled = MouseTrackingZoomEnabled;
        var zoomFactor = SelectedZoomLevel.Factor;
        var keystrokeOverlay = KeystrokeOverlayEnabled;
        var webcamEnabled = WebcamEnabled;
        var webcamDeviceId = SelectedWebcam?.Id;
        var spotlightEnabled = SpotlightEnabled;
        var spotlightRadius = SpotlightRadius;
        var clickRipplesEnabled = ClickRipplesEnabled;
        _ = Task.Run(() =>
        {
            try
            {
                _manager.StartPreview(targetKind, monitor, window, cursor, cursorStyle, zoomEnabled, zoomFactor, keystrokeOverlay,
                    webcamEnabled, webcamDeviceId, spotlightEnabled, spotlightRadius, clickRipplesEnabled);
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

    /// <summary>Fresh enumeration for the visual source picker, which builds its own tile list and must
    /// not disturb the current selection the way <see cref="RefreshMonitorsCommand"/> does.</summary>
    public IReadOnlyList<MonitorInfo> EnumerateMonitors() => _manager.GetMonitors();

    /// <inheritdoc cref="EnumerateMonitors"/>
    public IReadOnlyList<WindowInfo> EnumerateWindows() => _manager.GetWindows();

    /// <summary>
    /// Applies a choice made in the visual source picker. Exactly one of the two is non-null — the same
    /// single-target rule the whole capture path now enforces (see RecordingManager.StartAsync).
    /// </summary>
    public void ApplyCaptureSource(MonitorInfo? monitor, WindowInfo? window)
    {
        if (window is not null)
        {
            // The picker enumerates independently, so this window may not be in the dropdown's list yet
            // (opened since the last refresh). Adding it keeps the dropdown showing the real selection
            // instead of falling back to a blank ComboBox.
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

    /// <summary>Downloads the update installer and hands off to it. On success the app exits so the
    /// installer can overwrite it in place; the installer relaunches it afterwards.</summary>
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
            _updateService.LaunchInstaller(installerPath, () =>
                _dispatcherQueue.TryEnqueue(() => Microsoft.UI.Xaml.Application.Current.Exit()));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Update download cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            UpdateBannerMessage = "Couldn't download the update automatically — use “Release notes” to get it from GitHub.";
        }
        finally
        {
            IsDownloadingUpdate = false;
            UpdateDownloadProgress = 0;
            _updateDownloadCts?.Dispose();
            _updateDownloadCts = null;
        }
    }

    private bool CanStart() => IsIdle && (IsWindowCaptureMode ? SelectedWindow is not null : SelectedMonitor is not null);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRecordingAsync()
    {
        var isWindowMode = IsWindowCaptureMode;
        if (isWindowMode ? SelectedWindow is null : SelectedMonitor is null) return;

        if (!await EnsureFFmpegAvailableAsync()) return;

        // Only the *active* target is carried into the session. Both used to be filled in
        // unconditionally, and since a window is essentially always selected (RefreshWindows auto-picks
        // the first one), VideoCaptureService.Prepare — which infers its mode from whichever argument is
        // non-null — took the window branch even in "Entire display" mode, so a display recording
        // silently captured some arbitrary window instead. The live preview never showed it because
        // RestartPreviewIfIdle already nulled out the inactive one.
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
            MouseTrackingZoomEnabled = MouseTrackingZoomEnabled,
            ZoomFactor = SelectedZoomLevel.Factor,
            KeystrokeOverlayEnabled = KeystrokeOverlayEnabled,
            WebcamEnabled = WebcamEnabled,
            WebcamDeviceId = SelectedWebcam?.Id,
            SpotlightEnabled = SpotlightEnabled,
            SpotlightRadius = SpotlightRadius,
            ClickRipplesEnabled = ClickRipplesEnabled,
            Encoder = SelectedEncoder,
            Container = SelectedContainer,
            Resolution = SelectedResolution.Value,
            OutputDirectory = OutputDirectory,
            MaximizeTextClarity = MaximizeTextClarity && CanMaximizeTextClarity,
        };

        try
        {
            StatusMessage = "Starting…";
            await _manager.StartAsync(settings, monitorTarget, windowTarget);
            State = _manager.State;

            // Idempotent — the overlay is normally already armed from when Annotations was switched on.
            // This just guarantees it before the recording that will capture it actually starts.
            SyncAnnotationOverlay();

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

        // Phase 7: offer trim/GIF-export/discard right after a successful recording, instead of just
        // silently saving. A separate Window (not modal to MainWindow) — see TrimExportWindow's remarks
        // for why — so the user can keep using the app (e.g. start another recording) while reviewing.
        if (path is not null)
        {
            new TrimExportWindow(path).Activate();
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

    // Meter floor in dBFS. Linear RMS is a poor thing to drive a meter with — normal speech sits around
    // 0.02-0.1 RMS, so a linear bar barely leaves the left edge and reads as "nothing is happening" even
    // while audio is clearly being captured. Mapping dB (which is how loudness is actually perceived)
    // across this range is what makes the meter visibly track the voice.
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

        // Fast attack (snap straight to a louder reading) / slow release (decay gradually otherwise)
        // reads as a real VU meter instead of jittering with every 150ms sample.
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
