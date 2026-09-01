using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Phase 4 Step 1: settings/toggles for the cursor spotlight and click-ripple effects. The
/// global click hook (GlobalMouseHook.ClickAt) is real and wired at the VideoCaptureService level for
/// Step 2's rendering; VideoCaptureService doesn't composite either effect onto frames yet, so enabling
/// these toggles only persists the preference for now.</summary>
public sealed partial class EffectsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public EffectsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
    }
}
