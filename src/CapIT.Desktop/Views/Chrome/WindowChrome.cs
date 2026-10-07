using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ScreenRecorderApp.Views.Chrome;

/// <summary>
/// Custom-chrome behaviour shared by Cap-IT windows: the title bar drags the window (Windows Snap still
/// works — it's a real move loop), double-click toggles maximise, and a maximised frameless window is
/// inset by <see cref="Window.OffScreenMargin"/> so no content is lost past the monitor edge.
/// </summary>
public static class WindowChrome
{
    public static void Attach(Window window, Control titleBar, Control root)
    {
        titleBar.PointerPressed += (_, e) =>
        {
            if (IsFromButton(e) || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
            window.BeginMoveDrag(e);
        };
        titleBar.DoubleTapped += (_, e) =>
        {
            if (!IsFromButton(e)) ToggleMaximize(window);
        };
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
                root.Margin = window.WindowState == WindowState.Maximized ? window.OffScreenMargin : default;
        };
    }

    public static void ToggleMaximize(Window? window)
    {
        if (window is null) return;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private static bool IsFromButton(RoutedEventArgs e) =>
        e.Source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null;
}
