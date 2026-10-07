using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture;

namespace ScreenRecorderApp.ViewModels;

/// <summary>
/// The visual "choose what to record" picker: every display and capturable window as a tile with its own
/// live thumbnail, so a source is chosen by looking at it rather than by reading a window title.
/// </summary>
/// <remarks>
/// Thumbnails are polled rather than streamed (see <see cref="SourceThumbnailService"/>): slow enough that
/// a dozen PrintWindow calls cost nothing noticeable, fast enough that tiles track what's on screen. The
/// pass runs on a thread-pool thread and only the pixel handoff returns to the UI thread.
/// </remarks>
public sealed partial class SourcePickerViewModel : ViewModelBase, IDisposable
{
    private const int RefreshIntervalMs = 1200;

    /// <summary>Cap on window tiles — every extra window is another PrintWindow per pass.</summary>
    private const int MaxWindowTiles = 24;

    private readonly MainViewModel _main;
    private readonly Action<bool> _close;
    private readonly SourceThumbnailService _thumbnails = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly MonitorInfo? _initialMonitor;
    private readonly WindowInfo? _initialWindow;
    private bool _refreshInFlight;
    private bool _closed;
    private bool _confirmed;
    private bool _restoringSelection;

    public ObservableCollection<CaptureSourceItem> Screens { get; } = [];
    public ObservableCollection<CaptureSourceItem> WindowSources { get; } = [];

    /// <summary>0 = displays, 1 = windows.</summary>
    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private CaptureSourceItem? _selectedScreen;
    [ObservableProperty] private CaptureSourceItem? _selectedWindowSource;

    public bool ShowDisplays => FilterIndex == 0;
    public bool ShowWindows => FilterIndex == 1;
    public bool HasNoWindows => WindowSources.Count == 0;
    public CaptureSourceItem? SelectedItem => SelectedScreen ?? SelectedWindowSource;
    public bool HasSelection => SelectedItem is not null;

    public string SelectionSummary => SelectedItem switch
    {
        null => "Pick a display or window to preview it.",
        { Kind: CaptureTargetKind.Monitor } s => $"{s.Title} — the whole display, including anything drawn on top of it.",
        var s => $"{s.Title} — just this window, wherever it is on screen.",
    };

    /// <param name="close">Called with <c>true</c> when the user chose "Select and record".</param>
    public SourcePickerViewModel(MainViewModel main, Action<bool> close)
    {
        _main = main;
        _close = close;
        _initialMonitor = main.SelectedMonitor;
        _initialWindow = main.SelectedWindow;
        FilterIndex = main.IsWindowCaptureMode ? 1 : 0;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(RefreshIntervalMs), DispatcherPriority.Background, (_, _) => RefreshThumbnails());
        Rescan();
        _refreshTimer.Start();
    }

    partial void OnFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowDisplays));
        OnPropertyChanged(nameof(ShowWindows));
    }

    // The two lists hold one logical selection between them, so selecting in one clears the other.
    partial void OnSelectedScreenChanged(CaptureSourceItem? value)
    {
        if (value is null || _restoringSelection) { NotifySelection(); return; }
        SelectedWindowSource = null;
        _main.PreviewCaptureSource(value.Monitor, null);
        NotifySelection();
    }

    partial void OnSelectedWindowSourceChanged(CaptureSourceItem? value)
    {
        if (value is null || _restoringSelection) { NotifySelection(); return; }
        SelectedScreen = null;
        _main.PreviewCaptureSource(null, value.Window);
        NotifySelection();
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        SelectCommand.NotifyCanExecuteChanged();
        SelectAndRecordCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Rebuilds both tile lists from a fresh enumeration, preselecting the current target.</summary>
    [RelayCommand]
    private void Rescan()
    {
        var previousHandle = SelectedItem?.Handle ?? (_main.IsWindowCaptureMode ? _main.SelectedWindow?.Handle ?? 0 : _main.SelectedMonitor?.Handle ?? 0);

        Screens.Clear();
        foreach (var monitor in _main.EnumerateMonitors()) Screens.Add(CaptureSourceItem.ForMonitor(monitor));

        WindowSources.Clear();
        foreach (var window in _main.EnumerateWindows().Take(MaxWindowTiles)) WindowSources.Add(CaptureSourceItem.ForWindow(window));
        OnPropertyChanged(nameof(HasNoWindows));

        var screenMatch = Screens.FirstOrDefault(s => s.Handle == previousHandle);
        var windowMatch = WindowSources.FirstOrDefault(s => s.Handle == previousHandle);
        // Reselect without re-previewing the source that's already showing.
        _restoringSelection = true;
        try
        {
            SelectedScreen = screenMatch;
            SelectedWindowSource = screenMatch is null ? windowMatch : null;
        }
        finally { _restoringSelection = false; }
        NotifySelection();
        RefreshThumbnails();
    }

    private bool CanConfirm() => SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Select() => Confirm(startRecording: false);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void SelectAndRecord() => Confirm(startRecording: true);

    [RelayCommand]
    private void Cancel()
    {
        Dispose();
        _close(false);
    }

    private void Confirm(bool startRecording)
    {
        if (SelectedItem is not { } selected) return;
        _confirmed = true;
        _main.ApplyCaptureSource(selected.Monitor, selected.Window);
        Dispose();
        // The caller starts recording only after this overlay is gone, so its scrim never lands in the
        // opening frames of the recording.
        _close(startRecording);
    }

    /// <summary>One capture pass over every tile, off the UI thread, publishing each result as it lands.</summary>
    private void RefreshThumbnails()
    {
        if (_refreshInFlight || _closed) return;
        _refreshInFlight = true;

        var items = Screens.Concat(WindowSources).ToList();

        _ = Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (_closed) break;

                bool captured;
                try { captured = item.CaptureInto(_thumbnails); }
                catch { captured = false; } // a window can vanish mid-pass; the next rescan drops its tile
                if (!captured) continue;

                Dispatcher.UIThread.Post(() => { if (!_closed) item.PublishThumbnail(); }, DispatcherPriority.Background);
            }

            Dispatcher.UIThread.Post(() => _refreshInFlight = false);
        });
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _refreshTimer.Stop();
        _thumbnails.Dispose();
        // Cancelled: put the live preview back on the committed source.
        if (!_confirmed) _main.PreviewCaptureSource(_initialMonitor, _initialWindow);
    }
}
