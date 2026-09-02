using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

public sealed partial class CapturePage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public CapturePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
        Bindings.Update();
    }

    /// <summary>Opens the same visual source picker the Home tab offers. Unlike Home's, this one never
    /// starts the recording itself — you're on the settings tab, mid-setup, not ready to roll.</summary>
    private async void OnChooseSourceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var dialog = new SourcePickerDialog(ViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
