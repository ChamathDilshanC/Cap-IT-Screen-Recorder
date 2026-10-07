using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScreenRecorderApp.Controls;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.ViewModels;
using ScreenRecorderApp.Views.Chrome;
using ScreenRecorderApp.Views.Pages;

namespace ScreenRecorderApp.Views;

/// <summary>
/// Application shell window: custom chrome (drag region, double-click maximise, caption buttons), window
/// placement persistence, page hosting and shell keyboard handling. Everything else is view-model driven.
/// </summary>
public partial class MainWindow : Window
{
    private readonly UiStateService? _ui;

    public MainWindow() : this(null) { }

    public MainWindow(UiStateService? ui)
    {
        _ui = ui;
        InitializeComponent();
        PageHost.ContentTemplate = new FuncDataTemplate<NavigationItem?>((item, _) => item is null ? new Panel() : CreatePage(item.Key), supportsRecycling: false);
        RestorePlacement();
        WindowChrome.Attach(this, TitleBar, Root);
        PropertyChanged += OnWindowPropertyChanged;
        Closing += OnClosing;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public DialogHost Dialogs => DialogHostControl;

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    // Pages bind to the shell, not to the NavigationItem the page host would otherwise hand them.
    private Control CreatePage(PageKey key)
    {
        var page = CreateView(key);
        page.DataContext = DataContext;
        return page;
    }

    private static Control CreateView(PageKey key) => key switch
    {
        PageKey.Capture => new CapturePage(),
        PageKey.Tracking => new TrackingPage(),
        PageKey.Webcam => new WebcamPage(),
        PageKey.Annotations => new AnnotationsPage(),
        PageKey.Effects => new EffectsPage(),
        PageKey.Audio => new AudioPage(),
        PageKey.Recordings => new RecordingsPage(),
        PageKey.Settings => new SettingsPage(),
        _ => new HomePage(),
    };

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty) Shell?.Main.SetAppVisible(WindowState != WindowState.Minimized);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Shell?.SourcePicker is { } picker && !Dialogs.IsOpen)
        {
            picker.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---- Placement ----------------------------------------------------------------------------------

    private void RestorePlacement()
    {
        var state = _ui?.Current;
        if (state is null) return;
        if (state.Width is > 0 and var w && state.Height is > 0 and var h)
        {
            Width = Math.Max(MinWidth, w);
            Height = Math.Max(MinHeight, h);
        }
        if (state.X is { } x && state.Y is { } y)
        {
            // Never reopen off-screen: the saved title bar must land on a connected monitor.
            var titleBar = new PixelRect(x + 40, y, 200, 40);
            if (Screens.All.Any(s => s.WorkingArea.Intersects(titleBar)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(x, y);
            }
        }
        if (state.IsMaximized) Opened += (_, _) => WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        if (_ui is null) return;
        var state = _ui.Current;
        state.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            state.Width = Width; state.Height = Height;
            state.X = Position.X; state.Y = Position.Y;
        }
        _ui.Save();
    }

    private bool _closeConfirmed;

    /// <summary>
    /// Quitting is orderly: an active recording is stopped and saved (after asking), open editors save
    /// their edits, then capture threads and devices are released so the process really exits.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || Shell is not { } shell) return;
        e.Cancel = true;

        if (shell.Main.IsBusy)
        {
            var stop = await shell.Dialogs.ConfirmAsync("Stop recording and quit?",
                "A recording is in progress. Cap-IT will stop it and save the video before closing.",
                "Stop & quit", DialogTone.Warning, AppResources.Icon("Icon.Fill.Stop"));
            if (!stop) return;
            await shell.Main.StopRecordingCommand.ExecuteAsync(null);
        }

        SavePlacement();
        if (App.Services?.GetService(typeof(IReviewWindowService)) is IReviewWindowService reviews) await reviews.CloseAllAsync();
        shell.Main.Shutdown();
        _closeConfirmed = true;
        Close();
    }
}
