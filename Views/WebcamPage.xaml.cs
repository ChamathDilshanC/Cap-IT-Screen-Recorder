using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Phase 3 Step 1: device selection/persistence UI for the circular webcam PiP overlay. Actual
/// frame capture and compositing onto the recording (Phase 3 Step 2) isn't wired up yet — VideoCaptureService
/// doesn't touch the webcam at all so far, so enabling the toggle here only persists the preference.</summary>
public sealed partial class WebcamPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public WebcamPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
    }
}
