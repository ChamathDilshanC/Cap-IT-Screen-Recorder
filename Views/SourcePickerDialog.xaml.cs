using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views;

/// <summary>
/// The visual "choose what to record" picker: every connected display and every capturable window as a
/// tile with its own live thumbnail, so a source is chosen by looking at it rather than by reading a
/// window title out of a dropdown.
/// </summary>
/// <remarks>
/// Thumbnails are polled rather than streamed. <see cref="SourceThumbnailService"/> explains why GDI
/// stills beat standing up a real capture pipeline per tile; the interval here is the other half of
/// that tradeoff — slow enough that a dozen PrintWindow calls cost nothing noticeable, fast enough that
/// tiles visibly track what's on screen. The pass runs on a thread-pool thread (PrintWindow blocks on
/// the target app rendering itself, which a hung app can stall indefinitely) and only the final pixel
/// handoff comes back to the UI thread.
/// </remarks>
public sealed partial class SourcePickerDialog : ContentDialog
{
    private const int RefreshIntervalMs = 1200;

    /// <summary>Cap on how many windows get tiles. Every extra window is another PrintWindow per pass, and a picker with 60 tiles in it stops being a picker.</summary>
    private const int MaxWindowTiles = 24;

    private readonly MainViewModel _viewModel;
    private readonly SourceThumbnailService _thumbnails = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _refreshTimer;

    // Guards against a slow pass overlapping the next tick, and against publishing pixels into tiles
    // that a Rescan has already thrown away.
    private bool _refreshInFlight;
    private bool _closed;

    // The two GridViews hold one logical selection between them, so selecting in one has to clear the
    // other — which raises its SelectionChanged in turn. This breaks that loop.
    private bool _syncingSelection;

    public ObservableCollection<CaptureSourceItem> Screens { get; } = [];
    public ObservableCollection<CaptureSourceItem> WindowSources { get; } = [];

    /// <summary>Set when the user chose "Select and record" — the caller starts the recording once this dialog has actually closed.</summary>
    public bool StartRequested { get; private set; }

    /// <summary>The tile the user confirmed, or null if they cancelled.</summary>
    public CaptureSourceItem? ConfirmedSource { get; private set; }

    public SourcePickerDialog(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        _refreshTimer = _dispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
        _refreshTimer.Tick += (_, _) => RefreshThumbnails();

        Opened += OnOpened;
        Closed += OnClosed;
        // ContentDialogButtonClickEventArgs doesn't say which button was pressed, so the two buttons'
        // one difference — whether to start recording straight away — is carried by the handler itself.
        PrimaryButtonClick += (_, e) => Confirm(e, startRecording: true);
        SecondaryButtonClick += (_, e) => Confirm(e, startRecording: false);
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        Rescan();
        _refreshTimer.Start();
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _closed = true;
        _refreshTimer.Stop();
        _thumbnails.Dispose();
    }

    private void OnRescanClick(object sender, RoutedEventArgs e) => Rescan();

    /// <summary>Rebuilds both tile lists from a fresh enumeration, preselecting whatever the app is currently pointed at.</summary>
    private void Rescan()
    {
        var previousHandle = SelectedItem?.Handle ?? CurrentTargetHandle();

        Screens.Clear();
        foreach (var monitor in _viewModel.EnumerateMonitors()) Screens.Add(CaptureSourceItem.ForMonitor(monitor));

        WindowSources.Clear();
        foreach (var window in _viewModel.EnumerateWindows().Take(MaxWindowTiles))
        {
            WindowSources.Add(CaptureSourceItem.ForWindow(window));
        }

        NoWindowsText.Visibility = WindowSources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _syncingSelection = true;
        ScreensGrid.SelectedItem = Screens.FirstOrDefault(s => s.Handle == previousHandle);
        WindowsGrid.SelectedItem = WindowSources.FirstOrDefault(s => s.Handle == previousHandle);
        _syncingSelection = false;

        UpdateSelectionSummary();
        RefreshThumbnails();
    }

    /// <summary>The handle of the source the app is set to right now, so reopening the picker lands on it.</summary>
    private nint CurrentTargetHandle() => _viewModel.IsWindowCaptureMode
        ? _viewModel.SelectedWindow?.Handle ?? 0
        : _viewModel.SelectedMonitor?.Handle ?? 0;

    private CaptureSourceItem? SelectedItem =>
        ScreensGrid?.SelectedItem as CaptureSourceItem ?? WindowsGrid?.SelectedItem as CaptureSourceItem;

    private void OnScreenSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || ScreensGrid.SelectedItem is null) return;
        _syncingSelection = true;
        WindowsGrid.SelectedItem = null;
        _syncingSelection = false;
        UpdateSelectionSummary();
    }

    private void OnWindowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || WindowsGrid.SelectedItem is null) return;
        _syncingSelection = true;
        ScreensGrid.SelectedItem = null;
        _syncingSelection = false;
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var selected = SelectedItem;
        var hasSelection = selected is not null;

        IsPrimaryButtonEnabled = hasSelection;
        IsSecondaryButtonEnabled = hasSelection;
        SelectionSummary.Text = selected is null
            ? "Nothing selected yet — pick a tile above."
            : selected.Kind == CaptureTargetKind.Monitor
                ? $"Selected: {selected.Title} — the whole display, including anything drawn on top of it."
                : $"Selected: {selected.Title} — just this window, wherever it is on screen.";
    }

    private void Confirm(ContentDialogButtonClickEventArgs args, bool startRecording)
    {
        var selected = SelectedItem;
        if (selected is null)
        {
            args.Cancel = true;
            return;
        }

        ConfirmedSource = selected;
        // The caller does the actual starting, after this dialog has finished closing, so the record
        // path never runs behind a modal that is still on screen.
        StartRequested = startRecording;
        _viewModel.ApplyCaptureSource(selected.Monitor, selected.Window);
    }

    /// <summary>
    /// One capture pass over every tile, off the UI thread, publishing each result back as it lands so
    /// the grid fills in progressively instead of all at once at the end of the pass.
    /// </summary>
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

                _dispatcherQueue.TryEnqueue(() =>
                {
                    // The list can have been replaced by a Rescan while this pass was running, in which
                    // case this item's bitmap is no longer on screen and publishing into it is wasted
                    // (but harmless — it's still a valid bitmap, just unparented).
                    if (_closed) return;
                    item.PublishThumbnail();
                });
            }

            _dispatcherQueue.TryEnqueue(() => _refreshInFlight = false);
        });
    }
}
