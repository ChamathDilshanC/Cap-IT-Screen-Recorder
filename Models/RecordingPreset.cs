namespace ScreenRecorderApp.Models;

/// <summary>A named snapshot of recording options. Device selections are intentionally not included;
/// presets change how a recording is made without disconnecting the user's selected devices.</summary>
public sealed class RecordingPreset
{
    public string Name { get; set; } = "";
    public int Fps { get; set; } = 30;
    public OutputResolution Resolution { get; set; } = OutputResolution.Native;
    public bool CaptureCursor { get; set; } = true;
    public CursorStyle CursorStyle { get; set; } = CursorStyle.SystemDefault;
    public bool MouseTrackingZoomEnabled { get; set; }
    public double ZoomFactor { get; set; } = 2;
    public bool ZoomOnClickOnly { get; set; }
    public bool InstantZoomOut { get; set; }
    public double ZoomAnimationSpeedPercent { get; set; }
    public bool KeystrokeOverlayEnabled { get; set; }
    public bool WebcamEnabled { get; set; }
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; }
    public bool EnableMicNoiseSuppression { get; set; }
    public bool AnnotationsEnabled { get; set; }
    public string AnnotationColorLabel { get; set; } = "Neon Green";
    public double AnnotationStrokeThickness { get; set; } = 6;
    public string AnnotationToolLabel { get; set; } = "Pen";
    public int AnnotationFadeSeconds { get; set; }
    public bool SpotlightEnabled { get; set; }
    public double SpotlightRadius { get; set; } = 180;
    public bool ClickRipplesEnabled { get; set; }

    public RecordingPreset Clone() => (RecordingPreset)MemberwiseClone();

    public static IReadOnlyList<RecordingPreset> BuiltIn { get; } =
    [
        new() { Name = "Tutorial", Fps = 30, Resolution = OutputResolution.P1080, CaptureCursor = true, MouseTrackingZoomEnabled = true, ZoomFactor = 2, WebcamEnabled = false, CaptureSystemAudio = true, CaptureMicrophone = true, AnnotationsEnabled = true },
        new() { Name = "Coding", Fps = 60, Resolution = OutputResolution.P1080, CaptureCursor = true, MouseTrackingZoomEnabled = true, ZoomFactor = 1.5, ZoomOnClickOnly = true, CaptureSystemAudio = true, CaptureMicrophone = true, MaximizeTextClarity = true },
        new() { Name = "Presentation", Fps = 30, Resolution = OutputResolution.P1080, CaptureCursor = true, CursorStyle = CursorStyle.CircleHighlight, WebcamEnabled = true, CaptureSystemAudio = true, CaptureMicrophone = true, AnnotationsEnabled = true, SpotlightEnabled = true },
        new() { Name = "Gaming", Fps = 60, Resolution = OutputResolution.Native, CaptureCursor = false, CaptureSystemAudio = true, CaptureMicrophone = true, WebcamEnabled = true },
        new() { Name = "Bug Report", Fps = 30, Resolution = OutputResolution.P1080, CaptureCursor = true, MouseTrackingZoomEnabled = false, CaptureSystemAudio = true, CaptureMicrophone = true, AnnotationsEnabled = true, KeystrokeOverlayEnabled = true },
        new() { Name = "Vertical Reel", Fps = 60, Resolution = OutputResolution.P1080, CaptureCursor = true, MouseTrackingZoomEnabled = true, ZoomFactor = 2, WebcamEnabled = true, CaptureSystemAudio = true, CaptureMicrophone = true },
    ];

    // Kept here rather than in the UI so preset defaults remain discoverable and testable.
    public bool MaximizeTextClarity { get; set; }
}

public sealed class RecordingPresetOption
{
    public RecordingPreset Preset { get; }
    public bool IsBuiltIn { get; }
    public string Name => Preset.Name;
    public RecordingPresetOption(RecordingPreset preset, bool isBuiltIn)
    {
        Preset = preset;
        IsBuiltIn = isBuiltIn;
    }
    public override string ToString() => Name;
}
