using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.ViewModels;
using ScreenRecorderApp.Views.Chrome;

namespace ScreenRecorderApp.Views;

/// <summary>
/// Review &amp; Export window. Editing state lives in <see cref="EditorViewModel"/>; this class connects the
/// composition preview to it and owns window-only behaviour (shortcuts, full screen, responsive panels,
/// an orderly save before closing).
/// </summary>
public partial class ReviewWindow : Window
{
    private readonly EditorViewModel? _editor;
    private bool _shutdownComplete;
    private bool _layersBeforeFullscreen = true, _inspectorBeforeFullscreen = true;
    private bool _autoLayout = true;   // until the user shows/hides layers themselves
    private bool _applyingLayout;

    public ReviewWindow() : this("") { }

    public ReviewWindow(string filePath)
    {
        FilePath = filePath;
        InitializeComponent();
        WindowChrome.Attach(this, TitleBar, Root);
        if (string.IsNullOrEmpty(filePath)) return;

        _editor = new EditorViewModel(filePath, new FilePickerService(() => this))
        {
            Dialogs = new DialogService(DialogHostControl),
        };
        DataContext = _editor;

        _editor.Document.CompositionChanged += (_, _) => RefreshPreview();
        _editor.Document.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ReviewViewModel.Presentation)) RefreshPreview(); };
        _editor.Playback.PositionChanged += seconds => Preview.SetPosition(seconds, _editor.Document.ZoomRegions);
        _editor.Text.LayerSelected += index => Preview.SetActiveTextIndex(index);
        _editor.CloseRequested += () => Dispatcher.UIThread.Post(Close);
        Preview.TextPositionChanged += (x, y, completed) => _editor.Document.SetTextOverlayPosition(x, y, completed);
        Preview.AssetWarning += warning => _editor.AssetWarning = warning;
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.IsLayersVisible) && !_applyingLayout) _autoLayout = false;
        };

        FitToWorkArea();
        Opened += async (_, _) => await _editor.InitializeAsync();
        Closing += OnClosing;
        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public string FilePath { get; }

    private void RefreshPreview()
    {
        if (_editor is null || !_editor.Document.IsReady) return;
        _ = Preview.UpdateAsync(_editor.Document.Presentation, _editor.Document.SourceWidth, _editor.Document.SourceHeight);
        Preview.SetPosition(_editor.Playback.Position, _editor.Document.ZoomRegions);
    }

    private void FitToWorkArea()
    {
        if (Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Width = Math.Min(Width, area.Width - 32);
        Height = Math.Min(Height, area.Height - 32);
    }

    /// <summary>Narrow windows give the canvas priority: layers fold away first, then the inspector.</summary>
    private void ApplyResponsiveLayout(double width)
    {
        if (_editor is null || _editor.IsFullscreen) return;
        _applyingLayout = true;
        try
        {
            if (_autoLayout) _editor.IsLayersVisible = width >= 1240;
            else if (width < 1000 && _editor.IsInspectorVisible) _editor.IsLayersVisible = false;
        }
        finally { _applyingLayout = false; }
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_editor is null) return;
        _applyingLayout = true;
        try { ApplyFullscreen(!_editor.IsFullscreen); }
        finally { _applyingLayout = false; }
    }

    private void ApplyFullscreen(bool enter)
    {
        if (_editor is null) return;
        if (enter)
        {
            _layersBeforeFullscreen = _editor.IsLayersVisible;
            _inspectorBeforeFullscreen = _editor.IsInspectorVisible;
            _editor.IsFullscreen = true;
            _editor.IsLayersVisible = false;
            _editor.IsInspectorVisible = false;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            _editor.IsFullscreen = false;
            WindowState = WindowState.Normal;
            _editor.IsLayersVisible = _layersBeforeFullscreen;
            _editor.IsInspectorVisible = _inspectorBeforeFullscreen;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null || DialogHostControl.IsOpen) return;
        var inText = e.Source is TextBox || (e.Source as Visual)?.FindAncestorOfType<NumericUpDown>() is not null;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (e.Key == Key.F11 || (e.Key == Key.Escape && _editor.IsFullscreen)) { ToggleFullscreen(); e.Handled = true; return; }
        if (_editor.IsExporting) return;
        if (ctrl && e.Key == Key.E) { _editor.ExportVideoCommand.Execute(null); e.Handled = true; return; }
        if (inText) return;
        if (ctrl && e.Key == Key.Z) { _editor.Document.UndoCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { _editor.Document.RedoCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Space && e.Source is not Button) { _editor.Playback.TogglePlay(); e.Handled = true; }
    }

    private async void OnCopyErrorClick(object? sender, RoutedEventArgs e)
    {
        if (_editor?.LastError is { } error && await ShellIntegration.CopyTextAsync(this, error))
            _editor.Toasts.Info("Copied", "Technical details are on the clipboard.");
    }

    private void OnColourCommitted(object? sender, RoutedEventArgs e) => _editor?.RememberColourCommand.Execute(null);

    /// <summary>Edits are saved (and the media released) before the window actually closes.</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_editor is null || _shutdownComplete) return;
        e.Cancel = true;
        await CloseAndSaveAsync();
    }

    /// <summary>Saves edits, releases the media file and closes. Safe to call more than once.</summary>
    public async Task CloseAndSaveAsync()
    {
        if (_editor is not null && !_shutdownComplete)
        {
            _shutdownComplete = true;
            Preview.Release();
            await _editor.ShutdownAsync();
        }
        Close();
    }
}
