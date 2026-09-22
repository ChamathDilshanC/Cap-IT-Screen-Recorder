using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.ViewModels;
using System.ComponentModel;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenRecorderApp.Views;

public sealed partial class MainWindow : Window
{
    // Below this, the fixed-width settings column starts squeezing the preview column into a sliver and
    // the settings panel runs out of vertical room, which is what makes scrollbars/overlap show up.
    // Accounts for the NavigationView pane (~220px) added alongside the original settings/preview split.
    private const int MinWindowWidth = 1100;
    private const int MinWindowHeight = 600;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Cap-IT Screen Recorder";
        RootFrame.Navigate(typeof(ShellPage));
        if (RootFrame.Content is ShellPage shell)
        {
            shell.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTheme(shell.ViewModel.SelectedAppTheme.Value);
        }
        Closed += OnClosed;

        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow is not null)
            {
                appWindow.Resize(new SizeInt32(1260, 720));
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                }

                appWindow.Changed += OnAppWindowChanged;

                var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
        }
        catch
        {
            // Sizing is a nicety; ignore if the platform APIs are unavailable.
        }
    }

    // Best-effort clean shutdown: flushes the pending settings save (a change made just before closing
    // would otherwise be lost to the ~400ms debounce in MainViewModel.QueueSaveSettings) and tears down
    // the live audio/preview capture threads so the process actually exits.
    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (RootFrame.Content is ShellPage shell)
        {
            shell.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            shell.ViewModel.Shutdown();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedAppTheme) &&
            sender is MainViewModel viewModel)
        {
            ApplyTheme(viewModel.SelectedAppTheme.Value);
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        RootFrame.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;

        var size = sender.Size;
        var clampedWidth = Math.Max(size.Width, MinWindowWidth);
        var clampedHeight = Math.Max(size.Height, MinWindowHeight);
        if (clampedWidth != size.Width || clampedHeight != size.Height)
        {
            sender.Resize(new SizeInt32(clampedWidth, clampedHeight));
        }
    }
}
