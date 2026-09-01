using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Circular webcam PiP overlay: device selection here, actual capture/masking/compositing in
/// WebcamCaptureService and VideoCaptureService.ApplyWebcamOverlay.</summary>
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
