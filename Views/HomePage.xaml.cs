using System.ComponentModel;
using Microsoft.UI.Xaml;
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

    /// <summary>
    /// Opens the visual source picker. The recording, if the user asked for one, is started here rather
    /// than inside the dialog: ShowAsync only returns once the dialog has fully closed, and starting a
    /// recording behind a modal that is still on screen would capture the modal's own dimming layer in
    /// the opening frames.
    /// </summary>
    private async void OnChooseSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SourcePickerDialog(ViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();

        if (dialog.StartRequested && ViewModel.StartRecordingCommand.CanExecute(null))
        {
            await ViewModel.StartRecordingCommand.ExecuteAsync(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewSource))
        {
            PreviewImage.Source = ViewModel.PreviewSource;
        }
    }
}
