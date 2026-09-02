using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Studio Mic noise suppression (Phase 5) — ffmpeg afftdn/highpass/adeclick applied to the microphone leg only.</summary>
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
