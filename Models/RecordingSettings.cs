namespace ScreenRecorderApp.Models;

public enum HardwareEncoder
{
    Auto,
    Nvenc,
    Amf,
    Qsv,
    SoftwareX264
}

public enum OutputContainer
{
    Mp4,
    Mkv
}

/// <summary>Target output quality. Scales the encoded video (via ffmpeg's scaler) independently of the native capture resolution.</summary>
public enum OutputResolution
{
    Native,
    P360,
    P480,
    P720,
    P900,
    P1080,
    P1440,
    P2160
}

/// <summary>Pairs an <see cref="OutputResolution"/> with a friendly label for display in a ComboBox.</summary>
public sealed record ResolutionOption(OutputResolution Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<ResolutionOption> All =
    [
        new(OutputResolution.Native, "Native (source resolution)"),
        new(OutputResolution.P360, "360p"),
        new(OutputResolution.P480, "480p"),
        new(OutputResolution.P720, "720p (HD)"),
        new(OutputResolution.P900, "900p"),
        new(OutputResolution.P1080, "1080p (Full HD)"),
        new(OutputResolution.P1440, "1440p (QHD)"),
        new(OutputResolution.P2160, "4K (2160p UHD)"),
    ];
}

/// <summary>
/// Which marker is drawn at the live cursor position. DXGI Desktop Duplication reports cursor
/// position/visibility but never composites the OS pointer bitmap into the captured frame by itself —
/// <see cref="SystemDefault"/> decodes and draws the real, current Windows cursor shape (whatever cursor
/// theme the user has set); the others draw a simple stylized marker instead.
/// </summary>
public enum CursorStyle
{
    SystemDefault,
    Arrow,
    CircleHighlight,
    Dot,
    Crosshair,
}

/// <summary>Pairs a <see cref="CursorStyle"/> with a friendly label for display in a ComboBox.</summary>
public sealed record CursorStyleOption(CursorStyle Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<CursorStyleOption> All =
    [
        new(CursorStyle.SystemDefault, "System default"),
        new(CursorStyle.Arrow, "Arrow"),
        new(CursorStyle.CircleHighlight, "Circle highlight"),
        new(CursorStyle.Dot, "Dot"),
        new(CursorStyle.Crosshair, "Crosshair"),
    ];
}

/// <summary>Pairs a cursor-following zoom factor with a friendly label for display in a ComboBox.</summary>
public sealed record ZoomLevelOption(double Factor, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<ZoomLevelOption> All =
    [
        new(1.5, "150%"),
        new(2.0, "200%"),
        new(3.0, "300%"),
    ];
}

/// <summary>All user-configurable options for a recording session.</summary>
public sealed class RecordingSettings
{
    public nint MonitorHandle { get; set; }
    public string MonitorFriendlyName { get; set; } = "Primary Display";

    public int CaptureWidth { get; set; } = 1920;
    public int CaptureHeight { get; set; } = 1080;

    public int Fps { get; set; } = 30;
    public int VideoBitrateKbps { get; set; } = 12000;

    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = false;
    public string? MicrophoneDeviceId { get; set; }

    public bool CaptureCursor { get; set; } = true;
    public CursorStyle CursorStyle { get; set; } = CursorStyle.SystemDefault;

    public bool MouseTrackingZoomEnabled { get; set; } = false;
    public double ZoomFactor { get; set; } = 2.0;
    public bool KeystrokeOverlayEnabled { get; set; } = false;

    public HardwareEncoder Encoder { get; set; } = HardwareEncoder.Auto;
    public OutputContainer Container { get; set; } = OutputContainer.Mp4;
    public OutputResolution Resolution { get; set; } = OutputResolution.Native;

    // Only takes effect when the resolved encoder is libx264 (Auto or SoftwareX264) — see
    // FFmpegEncoderService.BuildEncoderTuning. Trades meaningfully larger files for 4:4:4 chroma (no
    // color-bleed/blur around text edges), so it's opt-in rather than the default.
    public bool MaximizeTextClarity { get; set; } = false;

    public string OutputDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Cap-IT Recordings");

    public string BuildOutputFilePath()
    {
        Directory.CreateDirectory(OutputDirectory);
        var ext = Container == OutputContainer.Mp4 ? "mp4" : "mkv";
        var name = $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{ext}";
        return Path.Combine(OutputDirectory, name);
    }
}
