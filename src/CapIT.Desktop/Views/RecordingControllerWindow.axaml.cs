using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ScreenRecorderApp.Views;

/// <summary>Compact floating controls shown while recording. Excluded from capture by <see cref="Services.RecordingSessionPresenter"/>.</summary>
public partial class RecordingControllerWindow : Window
{
    public RecordingControllerWindow() => InitializeComponent();

    /// <summary>The user asked to bring the main window back.</summary>
    public event Action? ShowRestore;

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnShowAppClick(object? sender, RoutedEventArgs e) => ShowRestore?.Invoke();
}
