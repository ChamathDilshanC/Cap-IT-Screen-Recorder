using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewSource))
        {
            PreviewImage.Source = ViewModel.PreviewSource;
        }
    }
}
