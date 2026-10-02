using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Avalonia.Services;

namespace ScreenRecorderApp.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IRecordingWorkspaceAdapter _adapter;

    [ObservableProperty]
    private WorkspacePage _selectedPage = WorkspacePage.Review;

    [ObservableProperty]
    private ReviewViewModel _review;

    public MainViewModel(IRecordingWorkspaceAdapter adapter)
    {
        _adapter = adapter;
        _review = new ReviewViewModel(adapter);
    }

    [RelayCommand]
    private void SelectPage(WorkspacePage page) => SelectedPage = page;
}

public enum WorkspacePage
{
    Home,
    Capture,
    Tracking,
    Webcam,
    Annotations,
    Effects,
    Audio,
    Settings,
    Review,
    Export
}
