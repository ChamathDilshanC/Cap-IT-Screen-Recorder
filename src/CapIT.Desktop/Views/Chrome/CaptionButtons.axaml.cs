using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ScreenRecorderApp.Views.Chrome;

/// <summary>Minimise / maximise-restore / close for frameless windows. Tracks the window state for the icon.</summary>
public partial class CaptionButtons : UserControl
{
    private Window? _window;

    public CaptionButtons() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null) return;
        _window.PropertyChanged += OnWindowPropertyChanged;
        UpdateIcon();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null) _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) UpdateIcon();
    }

    private void UpdateIcon()
    {
        var maximized = _window?.WindowState == WindowState.Maximized;
        MaximizeIcon.Data = this.FindResource(maximized ? "Icon.CaptionRestore" : "Icon.CaptionMaximize") as Geometry;
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore down" : "Maximize");
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) { if (_window is not null) _window.WindowState = WindowState.Minimized; }
    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => WindowChrome.ToggleMaximize(_window);
    private void OnCloseClick(object? sender, RoutedEventArgs e) => _window?.Close();
}
