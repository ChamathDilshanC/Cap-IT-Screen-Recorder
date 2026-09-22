using System.Text.Json.Serialization;

namespace ScreenRecorderApp.Models;

/// <summary>Non-destructive presentation settings applied after recording.</summary>
public sealed class PresentationSettings
{
    public string CanvasPreset { get; set; } = "16:9";
    public string BackgroundMode { get; set; } = "image";
    public string BackgroundPath { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = "#15151A";
    public string BackgroundColor2 { get; set; } = "#5F4BDB";
    public int Padding { get; set; } = 0;
    public double VideoScale { get; set; } = .82;
    public double CornerRadius { get; set; } = 18;
    public bool Shadow { get; set; } = true;
    public bool DeviceFrame { get; set; }
    public bool WatermarkEnabled { get; set; }
    public string WatermarkPath { get; set; } = string.Empty;
    public double WatermarkOpacity { get; set; } = .75;
    public double WatermarkScale { get; set; } = .14;

    [JsonIgnore]
    public (int Width, int Height) CanvasSize => CanvasPreset switch
    {
        "9:16" => (1080, 1920),
        "1:1" => (1080, 1080),
        "4:5" => (1080, 1350),
        _ => (1920, 1080)
    };

    public PresentationSettings Clone() => (PresentationSettings)MemberwiseClone();
}
