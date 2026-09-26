using CommunityToolkit.Mvvm.ComponentModel;
namespace ScreenRecorderApp.Models;

public sealed partial class ExportSettings : ObservableObject
{
    [ObservableProperty] private string _quality = "Balanced";
    [ObservableProperty] private string _encoder = "Software (H.264)";
    [ObservableProperty] private string _frameRate = "Source";
    [ObservableProperty] private bool _includeAudio = true;
    public int Crf => Quality switch { "Best Quality" => 16, "Small File" => 28, "Social" => 23, "Source Quality" => 18, _ => 20 };
}
