using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

public sealed partial class HomePage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public HomePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // The single MainViewModel instance lives on ShellPage and is handed to every child page as a
        // navigation parameter, so Home and Tracking always observe/mutate the same recording state.
        ViewModel = (MainViewModel)e.Parameter;
        Bindings.Update();
        PreviewImage.Source = ViewModel.PreviewSource; // Image control is freshly created each navigation; needs the current value explicitly, not just future changes.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewSource))
        {
            PreviewImage.Source = ViewModel.PreviewSource;
        }
    }
}
