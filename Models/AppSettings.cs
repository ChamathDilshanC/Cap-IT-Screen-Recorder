namespace ScreenRecorderApp.Models;

/// <summary>
/// Durable user preferences, persisted across app restarts by <see cref="Services.SettingsService"/>.
/// Deliberately separate from <see cref="RecordingSettings"/>: that type carries a live
/// <c>MonitorHandle</c> (meaningless once the app restarts) and is built fresh for each recording
/// session, whereas this is the small set of primitive-friendly preferences worth remembering —
/// monitors and microphones are re-matched by name/id against whatever's actually connected at startup,
/// not by a stale handle.
/// </summary>
public sealed class AppSettings
{
    public CaptureTargetKind CaptureTargetKind { get; set; } = CaptureTargetKind.Monitor;
    public string? MonitorDeviceName { get; set; }

    // A saved HWND is meaningless across a restart, so the target window is re-matched by title +
    // process name against whatever's actually running — best-effort, falls back to Monitor mode if
    // nothing matches (same fallback style already used for a saved monitor that got unplugged).
    public string? TargetWindowTitle { get; set; }
    public string? TargetWindowProcessName { get; set; }

    public int Fps { get; set; } = 30;
    public double VideoBitrateKbps { get; set; } = 12000;
    public HardwareEncoder Encoder { get; set; } = HardwareEncoder.Auto;
    public OutputContainer Container { get; set; } = OutputContainer.Mp4;
    public OutputResolution Resolution { get; set; } = OutputResolution.Native;

    public bool CaptureCursor { get; set; } = true;
    public CursorStyle CursorStyle { get; set; } = CursorStyle.SystemDefault;

    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = false;
    public string? MicrophoneDeviceId { get; set; }

    // Studio Mic noise suppression (Phase 5). Only ever touches the microphone signal — see
    // FFmpegEncoderService.BuildArguments for how it stays isolated from system audio.
    public bool EnableMicNoiseSuppression { get; set; } = false;

    public bool MouseTrackingZoomEnabled { get; set; } = false;
    public double ZoomFactor { get; set; } = 2.0;
    public bool KeystrokeOverlayEnabled { get; set; } = false;

    public bool WebcamEnabled { get; set; } = false;
    public string? WebcamDeviceId { get; set; }

    public bool SpotlightEnabled { get; set; } = false;
    public double SpotlightRadius { get; set; } = 180;
    public bool ClickRipplesEnabled { get; set; } = false;

    // Live screen annotations (Phase 6). Driven directly by MainViewModel via AnnotationOverlayService
    // (using SelectedMonitor at record start), not through RecordingManager/RecordingSettings, so unlike
    // most other capture-affecting toggles this only needs to persist here, not in RecordingSettings too.
    public bool AnnotationsEnabled { get; set; } = false;
    public string AnnotationColorLabel { get; set; } = "Neon Green";
    public double AnnotationStrokeThickness { get; set; } = 6;

    public bool MaximizeTextClarity { get; set; } = false;

    public string OutputDirectory { get; set; } = new RecordingSettings().OutputDirectory;
}
