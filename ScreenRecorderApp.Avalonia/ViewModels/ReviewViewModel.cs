using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Avalonia.Models;
using ScreenRecorderApp.Avalonia.Services;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Avalonia.ViewModels;

public partial class ReviewViewModel : ObservableObject
{
    private readonly IRecordingWorkspaceAdapter _adapter;

    [ObservableProperty]
    private RecordingSummary? _recording;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage = "Ready for a recording.";

    [ObservableProperty]
    private PresentationSettings _presentation = new();

    public IReadOnlyList<string> CanvasPresets { get; } = ["Original", "16:9", "9:16", "1:1", "4:5", "Custom"];
    public IReadOnlyList<string> FitModes { get; } = ["Fit", "Fill", "Original", "Custom"];

    public ReviewViewModel(IRecordingWorkspaceAdapter adapter) => _adapter = adapter;

    [RelayCommand]
    private async Task LoadLatestAsync()
    {
        IsLoading = true;
        try
        {
            Recording = await _adapter.GetLatestRecordingAsync();
            if (Recording is not null)
                StatusMessage = $"Loaded {Recording.Title} · {Recording.Width}×{Recording.Height}";
            if (Recording is null) StatusMessage = "No recording is available.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ResetPadding() => Presentation.Padding = 24;

    [RelayCommand]
    private void ResetTransform()
    {
        Presentation.RotationX = 0;
        Presentation.RotationY = 0;
        Presentation.RotationZ = 0;
        Presentation.VideoScale = .92;
        Presentation.VideoOffsetX = 0;
        Presentation.VideoOffsetY = 0;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Recording is null) { StatusMessage = "Load a recording before exporting."; return; }
        var destination = Path.Combine(Path.GetDirectoryName(Recording.SourcePath!)!, Recording.Title + "-export.mp4");
        IsLoading = true;
        try
        {
            await _adapter.ExportAsync(Recording, destination);
            StatusMessage = $"Exported {Path.GetFileName(destination)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
