namespace ScreenRecorderApp.Models;

public sealed record AppThemeOption(AppTheme Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<AppThemeOption> All =
    [
        new(AppTheme.System, "System default"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark"),
    ];
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Pairs a <see cref="CaptureTargetKind"/> with a friendly label for display in a ComboBox.</summary>
public sealed record CaptureTargetKindOption(CaptureTargetKind Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<CaptureTargetKindOption> All =
    [
        new(CaptureTargetKind.Monitor, "Entire display"),
        new(CaptureTargetKind.Window, "Specific window"),
    ];
}

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
        new(1.25, "125%"),
        new(1.5, "150%"),
        new(1.75, "175%"),
        new(2.0, "200%"),
        new(3.0, "300%"),
    ];
}

/// <summary>Visual styles for the webcam picture-in-picture overlay.</summary>
public sealed record WebcamTemplateOption(string Key, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<WebcamTemplateOption> All =
    [
        new("circle", "Circle"),
        new("rounded", "Rounded"),
        new("square", "Square"),
        new("landscape", "Landscape"),
        new("neon", "Neon ring"),
    ];
}

/// <summary>Pairs an <see cref="AnnotationTool"/> with a friendly label, for the Annotations tab and toolbar-state sync.</summary>
public sealed record AnnotationToolOption(AnnotationTool Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<AnnotationToolOption> All =
    [
        new(AnnotationTool.Select, "Select / move"),
        new(AnnotationTool.Pen, "Pen"),
        new(AnnotationTool.Highlighter, "Highlighter"),
        new(AnnotationTool.Marker, "Marker"),
        new(AnnotationTool.Line, "Line"),
        new(AnnotationTool.Arrow, "Arrow"),
        new(AnnotationTool.DoubleArrow, "Double arrow"),
        new(AnnotationTool.Rectangle, "Rectangle"),
        new(AnnotationTool.Square, "Square"),
        new(AnnotationTool.Ellipse, "Ellipse"),
        new(AnnotationTool.Circle, "Circle"),
        new(AnnotationTool.RoundedRectangle, "Rounded rectangle"),
        new(AnnotationTool.Triangle, "Triangle"),
        new(AnnotationTool.Diamond, "Diamond"),
        new(AnnotationTool.FlowchartProcess, "Process"),
        new(AnnotationTool.FlowchartDecision, "Decision"),
        new(AnnotationTool.StartEnd, "Start / end"),
        new(AnnotationTool.Database, "Database"),
        new(AnnotationTool.Document, "Document"),
        new(AnnotationTool.User, "User"),
        new(AnnotationTool.Server, "Server"),
        new(AnnotationTool.Monitor, "Monitor"),
        new(AnnotationTool.Mobile, "Mobile"),
        new(AnnotationTool.Folder, "Folder"),
        new(AnnotationTool.Callout, "Callout"),
        new(AnnotationTool.Cloud, "Cloud"),
        new(AnnotationTool.SpeechBubble, "Speech bubble"),
        new(AnnotationTool.ThoughtBubble, "Thought bubble"),
        new(AnnotationTool.Label, "Label"),
        new(AnnotationTool.Tag, "Tag"),
        new(AnnotationTool.PointerCallout, "Pointer callout"),
        new(AnnotationTool.CurvedLine, "Curved line"),
        new(AnnotationTool.ElbowLine, "Elbow line"),
        new(AnnotationTool.DashedLine, "Dashed line"),
        new(AnnotationTool.Bezier, "Bezier"),
        new(AnnotationTool.Measurement, "Measurement"),
        new(AnnotationTool.Blur, "Blur"),
        new(AnnotationTool.Pixelate, "Pixelate"),
        new(AnnotationTool.Magnifier, "Magnifier"),
        new(AnnotationTool.SpotlightRectangle, "Spotlight rectangle"),
        new(AnnotationTool.Underline, "Underline"),
        new(AnnotationTool.StrikeThrough, "Strike-through"),
        new(AnnotationTool.Check, "Check"),
        new(AnnotationTool.Cross, "X"),
        new(AnnotationTool.Warning, "Warning"),
        new(AnnotationTool.Info, "Info"),
        new(AnnotationTool.Question, "Question"),
        new(AnnotationTool.Cursor, "Cursor"),
        new(AnnotationTool.Click, "Click"),
        new(AnnotationTool.KeyboardBadge, "Keyboard badge"),
        new(AnnotationTool.CodeFrame, "Code frame"),
        new(AnnotationTool.TerminalFrame, "Terminal frame"),
        new(AnnotationTool.BrowserFrame, "Browser frame"),
        new(AnnotationTool.Braces, "Braces"),
        new(AnnotationTool.Crosshair, "Crosshair"),
        new(AnnotationTool.NumberedStep, "Numbered step"),
        new(AnnotationTool.Spotlight, "Spotlight"),
        new(AnnotationTool.Text, "Text"),
    ];
}

public sealed record AnnotationFadeOption(int Seconds, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<AnnotationFadeOption> All =
    [
        new(0, "Persistent"),
        new(3, "Fade after 3 seconds"),
        new(5, "Fade after 5 seconds"),
        new(10, "Fade after 10 seconds"),
    ];
}

/// <summary>Pairs a preset pen color for annotation drawing with a friendly label for display in a ComboBox. Presets rather than a full color picker, since a tutorial annotation needs to read clearly against arbitrary desktop content, not match a brand palette.</summary>
public sealed record AnnotationColorOption(Windows.UI.Color Value, string Label)
{
    public override string ToString() => Label;

    public Microsoft.UI.Xaml.Media.SolidColorBrush Brush { get; } = new(Value);

    public static readonly IReadOnlyList<AnnotationColorOption> All =
    [
        new(Windows.UI.Color.FromArgb(255, 57, 255, 20), "Neon Green"),
        new(Windows.UI.Color.FromArgb(255, 255, 32, 32), "Bright Red"),
        new(Windows.UI.Color.FromArgb(255, 255, 221, 0), "Yellow"),
        new(Windows.UI.Color.FromArgb(255, 255, 0, 220), "Magenta"),
        new(Windows.UI.Color.FromArgb(255, 0, 220, 255), "Cyan"),
        new(Windows.UI.Color.FromArgb(255, 255, 255, 255), "White"),
    ];
}

/// <summary>All user-configurable options for a recording session.</summary>
public sealed class RecordingSettings
{
    public CaptureTargetKind CaptureTargetKind { get; set; } = CaptureTargetKind.Monitor;

    public nint MonitorHandle { get; set; }
    public string MonitorFriendlyName { get; set; } = "Primary Display";

    public nint TargetWindowHandle { get; set; }
    public string? TargetWindowTitle { get; set; }

    public int CaptureWidth { get; set; } = 1920;
    public int CaptureHeight { get; set; } = 1080;

    public int Fps { get; set; } = 30;
    public int VideoBitrateKbps { get; set; } = 12000;

    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = false;
    public string? MicrophoneDeviceId { get; set; }

    // Studio Mic noise suppression (Phase 5): highpass + afftdn (+ adeclick) applied to the mic signal
    // only. When both system audio and the mic are being captured, FFmpegEncoderService keeps them on
    // separate pipes/inputs so the filter never touches system audio — see BuildArguments there.
    public bool EnableMicNoiseSuppression { get; set; } = false;

    public bool CaptureCursor { get; set; } = true;
    public CursorStyle CursorStyle { get; set; } = CursorStyle.SystemDefault;
    public CursorSettings Cursor { get; set; } = new();

    public bool MouseTrackingZoomEnabled { get; set; } = false;
    public double ZoomFactor { get; set; } = 2.0;
    // Restricts the smart zoom's trigger to mouse clicks: with this on, moving the mouse or typing no
    // longer holds the camera in, so it sits at 1x until you actually click something and then eases back
    // out a couple of seconds later. Separate from MouseTrackingZoomEnabled rather than a third state of
    // it, because it is a modifier on how the same feature is triggered, not a different feature.
    public bool ZoomOnClickOnly { get; set; } = false;
    public bool KeystrokeOverlayEnabled { get; set; } = false;

    // Circular webcam PiP overlay (Phase 3). WebcamDeviceId is the WinRT DeviceInformation.Id string —
    // opaque but stable for a given physical device, matched by exact equality at load, same convention
    // MicrophoneDeviceId already uses (unlike MonitorDeviceName/window title+process, no friendlier
    // re-match key exists or is needed here).
    public bool WebcamEnabled { get; set; } = false;
    public string? WebcamDeviceId { get; set; }
    public string WebcamTemplate { get; set; } = "circle";
    public double WebcamBrightness { get; set; }
    public double WebcamContrast { get; set; } = 1;
    public double WebcamSaturation { get; set; } = 1;
    public double WebcamWarmth { get; set; }
    public double WebcamSmoothing { get; set; }

    // Advanced cursor effects (Phase 4). SpotlightRadius is canvas pixels (same space _cursorX/_cursorY
    // already live in) — a Slider-bound raw value rather than an options list, since "how big" is a
    // continuous preference, not a handful of named presets like ZoomLevelOption.
    public bool SpotlightEnabled { get; set; } = false;
    public double SpotlightRadius { get; set; } = 180;
    public bool ClickRipplesEnabled { get; set; } = false;

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
