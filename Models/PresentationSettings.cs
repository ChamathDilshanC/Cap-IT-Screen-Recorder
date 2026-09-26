using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ScreenRecorderApp.Models;

/// <summary>Non-destructive editing values. All distances are output canvas pixels.</summary>
public sealed partial class PresentationSettings : ObservableObject
{
    [ObservableProperty] private string _canvasPreset = "Original";
    [ObservableProperty] private int _canvasWidth = 0;
    [ObservableProperty] private int _canvasHeight = 0;
    [ObservableProperty] private string _backgroundMode = "gradient";
    [ObservableProperty] private string _backgroundPath = "";
    [ObservableProperty] private string _backgroundColor = "#202838";
    [ObservableProperty] private string _backgroundColor2 = "#716B91";
    [ObservableProperty] private double _gradientAngle = 45;
    [ObservableProperty] private string _backgroundImageFit = "Fill";
    [ObservableProperty] private double _backgroundBlur = 0;
    [ObservableProperty] private double _backgroundDim = 0;
    [ObservableProperty] private int _padding = 64;
    [ObservableProperty] private double _videoScale = .92;
    [ObservableProperty] private string _fitMode = "Fit";
    [ObservableProperty] private double _videoOffsetX = 0;
    [ObservableProperty] private double _videoOffsetY = 0;
    [ObservableProperty] private string _videoPosition = "Center";
    [ObservableProperty] private double _cornerRadius = 20;
    [ObservableProperty] private bool _shadow = true;
    [ObservableProperty] private double _shadowBlur = 24;
    [ObservableProperty] private double _shadowOpacity = .28;
    [ObservableProperty] private double _shadowOffsetX = 0;
    [ObservableProperty] private double _shadowOffsetY = 12;
    [ObservableProperty] private bool _borderEnabled = false;
    [ObservableProperty] private string _borderColor = "#FFFFFF";
    [ObservableProperty] private double _borderWidth = 1;
    [ObservableProperty] private double _borderOpacity = .5;
    [ObservableProperty] private string _deviceFrameStyle = "None";
    [ObservableProperty] private string _frameTheme = "Dark";
    [ObservableProperty] private double _framePadding = 6;
    [ObservableProperty] private bool _frameTitleBar = true;
    [ObservableProperty] private bool _frameControls = true;
    [ObservableProperty] private bool _watermarkEnabled = false;
    [ObservableProperty] private string _watermarkPath = "";
    [ObservableProperty] private double _watermarkOpacity = .75;
    [ObservableProperty] private double _watermarkScale = .14;
    [ObservableProperty] private string _watermarkPosition = "Bottom Right";
    [ObservableProperty] private double _watermarkMargin = 24;
    [ObservableProperty] private double _watermarkOffsetX = 0;
    [ObservableProperty] private double _watermarkOffsetY = 0;
    // Retained schema-1 property. Normalize migrates it to the neutral minimal frame.
    public bool DeviceFrame { get; set; }
    [JsonIgnore] public (int Width, int Height) CanvasSize => ResolveCanvas(1920, 1080);
    public (int Width, int Height) ResolveCanvas(int sourceWidth, int sourceHeight)
    {
        if (CanvasWidth >= 64 && CanvasHeight >= 64) return (Even(CanvasWidth), Even(CanvasHeight));
        return CanvasPreset switch
        {
            "16:9" => (1920, 1080), "9:16" => (1080, 1920), "1:1" => (1080, 1080),
            "4:5" => (1080, 1350), "3:2" => (1620, 1080), "4:3" => (1440, 1080),
            _ => (Even(sourceWidth > 0 ? sourceWidth : 1920), Even(sourceHeight > 0 ? sourceHeight : 1080))
        };
    }
    public static int Even(int value) => Math.Clamp(value / 2 * 2, 64, 7680);
    public static double Safe(double value, double fallback, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    public static string Color(string? value, string fallback) =>
        value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value.ToUpperInvariant() : fallback;
    private static string Option(string? value, string fallback, params string[] allowed) =>
        allowed.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    public void Normalize()
    {
        CanvasPreset = Option(CanvasPreset, "Original", "Original", "16:9", "9:16", "1:1", "4:5", "3:2", "4:3", "Custom");
        CanvasWidth = CanvasWidth >= 64 ? Even(CanvasWidth) : 0;
        CanvasHeight = CanvasHeight >= 64 ? Even(CanvasHeight) : 0;
        BackgroundMode = Option(BackgroundMode, "gradient", "solid", "gradient", "image", "none");
        BackgroundPath ??= ""; WatermarkPath ??= "";
        BackgroundColor = Color(BackgroundColor, "#202838"); BackgroundColor2 = Color(BackgroundColor2, "#716B91");
        GradientAngle = Safe(GradientAngle, 45, 0, 360);
        BackgroundImageFit = Option(BackgroundImageFit, "Fill", "Fit", "Fill", "Stretch");
        BackgroundBlur = Safe(BackgroundBlur, 0, 0, 64); BackgroundDim = Safe(BackgroundDim, 0, 0, 1);
        Padding = Math.Clamp(Padding, 0, 200); VideoScale = Safe(VideoScale, .92, .4, 1.2);
        FitMode = Option(FitMode, "Fit", "Fit", "Fill", "Original", "Custom");
        VideoPosition = Option(VideoPosition, "Center", "Center", "Top", "Bottom", "Left", "Right", "Custom");
        VideoOffsetX = Safe(VideoOffsetX, 0, -7680, 7680); VideoOffsetY = Safe(VideoOffsetY, 0, -7680, 7680);
        CornerRadius = Safe(CornerRadius, 20, 0, 64);
        ShadowBlur = Safe(ShadowBlur, 24, 0, 100); ShadowOpacity = Safe(ShadowOpacity, .28, 0, 1);
        ShadowOffsetX = Safe(ShadowOffsetX, 0, -200, 200); ShadowOffsetY = Safe(ShadowOffsetY, 12, -200, 200);
        BorderColor = Color(BorderColor, "#FFFFFF"); BorderWidth = Safe(BorderWidth, 1, 0, 12);
        BorderOpacity = Safe(BorderOpacity, .5, 0, 1);
        if (DeviceFrame && DeviceFrameStyle == "None") DeviceFrameStyle = "Minimal";
        DeviceFrame = false;
        DeviceFrameStyle = Option(DeviceFrameStyle, "None", "None", "Minimal", "Browser", "Studio", "Windows", "Device");
        FrameTheme = Option(FrameTheme, "Dark", "Dark", "Light"); FramePadding = Safe(FramePadding, 6, 0, 24);
        WatermarkOpacity = Safe(WatermarkOpacity, .75, 0, 1); WatermarkScale = Safe(WatermarkScale, .14, .03, .5);
        WatermarkPosition = Option(WatermarkPosition, "Bottom Right", "Top Left", "Top Right", "Bottom Left", "Bottom Right", "Center");
        WatermarkMargin = Safe(WatermarkMargin, 24, 0, 200);
        WatermarkOffsetX = Safe(WatermarkOffsetX, 0, -7680, 7680); WatermarkOffsetY = Safe(WatermarkOffsetY, 0, -7680, 7680);
    }
    // Do not copy ObservableObject subscribers into a history/export snapshot.
    public PresentationSettings Clone() => JsonSerializer.Deserialize<PresentationSettings>(JsonSerializer.Serialize(this))!;
}
