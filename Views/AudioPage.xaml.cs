using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Stub page — Master Plan item 6 (AI mic noise suppression via ffmpeg afftdn/arnndn). No
/// backend logic yet; wired into the shared MainViewModel now so a later phase is a pure addition.</summary>
public sealed partial class AudioPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public AudioPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
    }
}
