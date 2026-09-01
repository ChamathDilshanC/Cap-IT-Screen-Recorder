using Microsoft.UI.Xaml.Controls;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

public sealed partial class ShellPage : Page
{
    // Owned here (not per-page) so Home and Tracking both observe/mutate one shared recording session —
    // switching tabs must never reset in-progress settings or an active recording.
    public MainViewModel ViewModel { get; } = new();

    public ShellPage()
    {
        InitializeComponent();
    }

    private void OnNavigationViewLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(HomePage), ViewModel);
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item) return;

        var pageType = item.Tag switch
        {
            "Tracking" => typeof(TrackingPage),
            "Webcam" => typeof(WebcamPage),
            "Annotations" => typeof(AnnotationsPage),
            "Effects" => typeof(EffectsPage),
            "Audio" => typeof(AudioPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, ViewModel);
        }
    }
}
