using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
        Bindings.Update();
    }
}
