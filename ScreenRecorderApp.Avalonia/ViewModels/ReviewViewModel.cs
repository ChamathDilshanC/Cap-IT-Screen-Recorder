using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Avalonia.Models;
using ScreenRecorderApp.Avalonia.Services;

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

    public ReviewViewModel(IRecordingWorkspaceAdapter adapter) => _adapter = adapter;

    [RelayCommand]
    private async Task LoadLatestAsync()
    {
        IsLoading = true;
        try
        {
            Recording = await _adapter.GetLatestRecordingAsync();
            StatusMessage = Recording is null ? "No recording is available." : "Latest recording loaded.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
