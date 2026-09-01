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

    public bool MouseTrackingZoomEnabled { get; set; } = false;
    public double ZoomFactor { get; set; } = 2.0;
    public bool KeystrokeOverlayEnabled { get; set; } = false;

    public bool WebcamEnabled { get; set; } = false;
    public string? WebcamDeviceId { get; set; }

    public bool MaximizeTextClarity { get; set; } = false;

    public string OutputDirectory { get; set; } = new RecordingSettings().OutputDirectory;
}
