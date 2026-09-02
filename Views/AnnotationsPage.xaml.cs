using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>Live screen annotations (Phase 6) — enable toggle, hotkey reference, monitor-capture-only
/// gate, and pen color/thickness (live-updatable mid-recording). The actual overlay window/hook/drawing
/// surface live in AnnotationOverlayService and AnnotationOverlayWindow, armed from MainViewModel at
/// record start/stop.</summary>
public sealed partial class AnnotationsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public AnnotationsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
    }
}
