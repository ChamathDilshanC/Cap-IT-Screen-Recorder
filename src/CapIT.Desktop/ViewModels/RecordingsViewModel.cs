using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.Services.Library;

namespace ScreenRecorderApp.ViewModels;

/// <summary>A recording or export in the library.</summary>
public sealed partial class RecordingItem : ObservableObject
{
    public RecordingItem(RecordingFile file)
    {
        File = file;
        Title = System.IO.Path.GetFileNameWithoutExtension(file.FileName);
    }

    public RecordingFile File { get; }
    public string Path => File.Path;
    public string Title { get; }
    public string FileName => File.FileName;
    public string Extension => System.IO.Path.GetExtension(File.FileName).TrimStart('.').ToUpperInvariant();
    public bool IsEdited => File.Kind == RecordingFileKind.Edited;
    public bool IsGif => File.Kind == RecordingFileKind.Gif;
    public bool CanEdit => !IsGif;
    public string KindLabel => File.Kind switch { RecordingFileKind.Edited => "Edited", RecordingFileKind.Gif => "GIF", _ => "Original" };
    public string DateText => File.CreatedAt.ToString("MMM d, yyyy · h:mm tt", CultureInfo.CurrentCulture);
    public string TimeText => File.CreatedAt.ToString("h:mm tt", CultureInfo.CurrentCulture);
    public string SizeText => FormatSize(File.SizeBytes);

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private string _resolutionText = "";
    public bool HasThumbnail => Thumbnail is not null;
    public bool HasDuration => DurationText.Length > 0;
    public string MetaLine => string.Join("  ·  ", new[] { ResolutionText, SizeText }.Where(s => s.Length > 0));
    internal bool DetailsRequested;

    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));
    partial void OnDurationTextChanged(string value) => OnPropertyChanged(nameof(HasDuration));
    partial void OnResolutionTextChanged(string value) => OnPropertyChanged(nameof(MetaLine));

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB",
    };
}

/// <summary>Recordings captured on the same day.</summary>
public sealed class RecordingGroup(string header, IReadOnlyList<RecordingItem> items)
{
    public string Header { get; } = header;
    public IReadOnlyList<RecordingItem> Items { get; } = items;
    public string CountText => Items.Count == 1 ? "1 item" : $"{Items.Count} items";
}

/// <summary>
/// The recordings library: everything under the output folder, newest first, grouped by day. Details and
/// thumbnails load lazily in the background (a few at a time) so a large library opens instantly.
/// </summary>
public sealed partial class RecordingsViewModel : ViewModelBase
{
    private const int PageSize = 120;
    private readonly MainViewModel _main;
    private readonly ToastService _toasts;
    private readonly UiStateService _ui;
    private readonly SemaphoreSlim _thumbnailGate = new(3);
    private List<RecordingItem> _all = [];
    private int _visibleCount = PageSize;
    private CancellationTokenSource? _loadCts;

    public RecordingsViewModel(MainViewModel main, ToastService toasts, UiStateService ui)
    {
        _main = main;
        _toasts = toasts;
        _ui = ui;
        _isGridLayout = ui.Current.LibraryLayout != "list";
        _main.RecordingSaved += path => _ = RefreshAsync();
    }

    /// <summary>Set by the shell: asks before destructive actions.</summary>
    public DialogService? Dialogs { get; set; }

    public ObservableCollection<RecordingGroup> Groups { get; } = [];

    /// <summary>The newest few recordings, for the Home dashboard.</summary>
    public ObservableCollection<RecordingItem> Recent { get; } = [];
    public bool HasRecent => Recent.Count > 0;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isGridLayout = true;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _shownCount;

    public bool IsListLayout => !IsGridLayout;
    public bool IsEmpty => !IsLoading && TotalCount == 0;
    public bool HasNoMatches => !IsLoading && TotalCount > 0 && ShownCount == 0;
    public bool HasItems => ShownCount > 0;
    public bool CanShowMore => _all.Count(Matches) > _visibleCount;
    public string LibraryPath => _main.OutputDirectory;
    public string SummaryText => TotalCount == 1 ? "1 recording" : $"{TotalCount} recordings";

    partial void OnSearchTextChanged(string value) { _visibleCount = PageSize; Rebuild(); }
    partial void OnIsLoadingChanged(bool value) => NotifyState();
    partial void OnTotalCountChanged(int value) => NotifyState();
    partial void OnShownCountChanged(int value) => NotifyState();

    partial void OnIsGridLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListLayout));
        _ui.Current.LibraryLayout = value ? "grid" : "list";
        _ui.Save();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanShowMore));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        OnPropertyChanged(nameof(LibraryPath));
        try
        {
            var root = _main.OutputDirectory;
            var files = await Task.Run(() => RecordingLibrary.Enumerate(root), cts.Token);
            if (cts.IsCancellationRequested) return;
            // Keep already-loaded thumbnails for files that are still there.
            var existing = _all.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
            _all = files.Select(f => existing.TryGetValue(f.Path, out var item) && item.File.SizeBytes == f.SizeBytes ? item : new RecordingItem(f)).ToList();
            TotalCount = _all.Count;
            Rebuild();
            UpdateRecent();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!cts.IsCancellationRequested) IsLoading = false;
        }
    }

    [RelayCommand]
    private void ShowMore()
    {
        _visibleCount += PageSize;
        Rebuild();
    }

    [RelayCommand] private void UseGrid() => IsGridLayout = true;
    [RelayCommand] private void UseList() => IsGridLayout = false;

    private void UpdateRecent()
    {
        Recent.Clear();
        foreach (var item in _all.Take(4))
        {
            Recent.Add(item);
            _ = LoadDetailsAsync(item);
        }
        OnPropertyChanged(nameof(HasRecent));
    }

    private bool Matches(RecordingItem item) =>
        string.IsNullOrWhiteSpace(SearchText) || item.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        var visible = _all.Where(Matches).Take(_visibleCount).ToList();
        ShownCount = visible.Count;
        Groups.Clear();
        foreach (var group in visible.GroupBy(i => i.File.CreatedAt.Date))
            Groups.Add(new RecordingGroup(DayHeader(group.Key), group.ToList()));
        OnPropertyChanged(nameof(CanShowMore));
        foreach (var item in visible) _ = LoadDetailsAsync(item);
    }

    private static string DayHeader(DateTime day)
    {
        var today = DateTime.Today;
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        return day.Year == today.Year ? day.ToString("dddd, MMMM d", CultureInfo.CurrentCulture) : day.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
    }

    private async Task LoadDetailsAsync(RecordingItem item)
    {
        if (item.DetailsRequested) return;
        item.DetailsRequested = true;
        await _thumbnailGate.WaitAsync();
        try
        {
            var details = await RecordingLibrary.GetDetailsAsync(item.Path);
            var thumb = await RecordingLibrary.GetThumbnailAsync(item.Path);
            Dispatcher.UIThread.Post(() =>
            {
                if (details is not null)
                {
                    if (details.Duration is { } d) item.DurationText = Controls.Converters.FormatDuration(d.TotalSeconds);
                    if (details.Width > 0 && details.Height > 0) item.ResolutionText = $"{details.Width} × {details.Height}";
                }
                if (thumb is not null) item.Thumbnail = ToBitmap(thumb);
            }, DispatcherPriority.Background);
        }
        finally
        {
            _thumbnailGate.Release();
        }
    }

    private static WriteableBitmap ToBitmap(ThumbnailPixels pixels)
    {
        var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var rowBytes = pixels.Width * 4;
        for (var y = 0; y < pixels.Height; y++)
            Marshal.Copy(pixels.Bgra, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        return bitmap;
    }

    [RelayCommand]
    private void Open(RecordingItem item)
    {
        if (item.IsGif) { ShellIntegration.OpenWithDefaultApp(item.Path); return; }
        _main.ReviewRecording(item.Path);
    }

    [RelayCommand]
    private void Play(RecordingItem item)
    {
        if (!ShellIntegration.OpenWithDefaultApp(item.Path)) _toasts.Error("Couldn't open the file", "No app is associated with this file type.");
    }

    [RelayCommand]
    private void Reveal(RecordingItem item) => ShellIntegration.RevealFile(item.Path);

    [RelayCommand]
    private void OpenLibraryFolder() => ShellIntegration.OpenFolder(_main.OutputDirectory);

    [RelayCommand]
    private async Task DeleteAsync(RecordingItem item)
    {
        if (Dialogs is not null)
        {
            var confirmed = await Dialogs.ConfirmAsync(
                $"Delete “{item.Title}”?",
                "The file and its edit settings are permanently deleted. This can't be undone.",
                "Delete", DialogTone.Danger,
                AppResources.Icon("Icon.Trash"));
            if (!confirmed) return;
        }
        try
        {
            await Task.Run(() => RecordingLibrary.Delete(item.Path));
            _all.Remove(item);
            TotalCount = _all.Count;
            Rebuild();
            UpdateRecent();
            _toasts.Info("Recording deleted", item.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toasts.Error("Couldn't delete the recording", "It may be open in another app. Close it and try again.");
        }
    }
}
