using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Stub page — Master Plan item 2 (circular webcam PiP overlay with AI background blur). No
/// backend logic yet; wired into the shared MainViewModel now so a later phase is a pure addition.</summary>
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
