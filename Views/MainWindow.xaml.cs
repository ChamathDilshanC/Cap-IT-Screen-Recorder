using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenRecorderApp.Views;

public sealed partial class MainWindow : Window
{
    // Below this, the fixed-width settings column starts squeezing the preview column into a sliver and
    // the settings panel runs out of vertical room, which is what makes scrollbars/overlap show up.
    private const int MinWindowWidth = 900;
    private const int MinWindowHeight = 600;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Cap-IT Screen Recorder";
        RootFrame.Navigate(typeof(MainPage));

        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow is not null)
            {
                appWindow.Resize(new SizeInt32(1040, 700));
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
