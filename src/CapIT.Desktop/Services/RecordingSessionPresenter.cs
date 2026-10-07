using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Platform;
using ScreenRecorderApp.ViewModels;
using ScreenRecorderApp.Views;

namespace ScreenRecorderApp.Services;

/// <summary>
/// Presentation side of a recording session: shows the compact floating controller (excluded from
/// capture) while recording, and optionally tucks the main window away and brings it back afterwards.
/// </summary>
public sealed class RecordingSessionPresenter(MainViewModel main, ShellViewModel shell)
{
    private Window? _mainWindow;
    private RecordingControllerWindow? _controller;
    private bool _minimizedForRecording;

    public void Attach(Window mainWindow)
    {
        _mainWindow = mainWindow;
        main.PropertyChanged += OnMainPropertyChanged;
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsBusy)) return;
        if (main.IsBusy) OnRecordingStarted();
        else OnRecordingStopped();
    }

    private void OnRecordingStarted()
    {
        if (shell.ShowRecordingController && _controller is null)
        {
            _controller = new RecordingControllerWindow { DataContext = main };
            _controller.ShowActivated = false;
            _controller.Opened += (_, _) => PositionController();
            _controller.ShowRestore += RestoreMainWindow;
            _controller.Show();
        }

        if (shell.HideWhileRecording && _mainWindow is { WindowState: not WindowState.Minimized })
        {
            _minimizedForRecording = true;
            _mainWindow.WindowState = WindowState.Minimized;
        }
    }

    private void OnRecordingStopped()
    {
        _controller?.Close();
        _controller = null;
        if (_minimizedForRecording) RestoreMainWindow();
        _minimizedForRecording = false;
    }

    private void RestoreMainWindow()
    {
        if (_mainWindow is null) return;
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>Top-centre of the display being recorded (or the one showing the recorded window).</summary>
    private void PositionController()
    {
        if (_controller is null) return;
        if (_controller.TryGetPlatformHandle()?.Handle is { } hwnd) WindowInterop.ExcludeFromCapture(hwnd);

        (int X, int Y, int Width, int Height)? area = main.IsWindowCaptureMode
            ? WindowInterop.GetWorkAreaForWindow(main.SelectedWindow?.Handle ?? 0)
            : main.SelectedMonitor is { } m ? WindowInterop.GetWorkArea(m.Handle) : null;
        if (area is not { } a)
        {
            var screen = _controller.Screens.Primary;
            if (screen is null) return;
            a = (screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
        }

        var scale = _controller.DesktopScaling;
        var width = (int)Math.Ceiling(_controller.Bounds.Width * scale);
        _controller.Position = new PixelPoint(a.X + (a.Width - width) / 2, a.Y + (int)(16 * scale));
    }
}
