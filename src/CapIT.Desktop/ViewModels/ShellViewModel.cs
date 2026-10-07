using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Services;

namespace ScreenRecorderApp.ViewModels;

/// <summary>
/// Application shell state: sidebar navigation, collapsed state, the source picker overlay, shell-level
/// commands (open recording, settings) and presentation preferences. Recording state lives in
/// <see cref="Main"/>, shared by every page.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly UiStateService _ui;
    private readonly FilePickerService _files;

    public ShellViewModel(MainViewModel main, RecordingsViewModel recordings, ToastService toasts, DialogService dialogs,
        FilePickerService files, UiStateService ui)
    {
        Main = main;
        Recordings = recordings;
        Toasts = toasts;
        Dialogs = dialogs;
        _files = files;
        _ui = ui;
        recordings.Dialogs = dialogs;
        _isSidebarCollapsed = ui.Current.SidebarCollapsed;

        CaptureItems =
        [
            new(PageKey.Home, "Home", Icon("Icon.Home")),
            new(PageKey.Capture, "Capture", Icon("Icon.Monitor")),
            new(PageKey.Tracking, "Smart Tracking", Icon("Icon.Focus")),
            new(PageKey.Webcam, "Webcam", Icon("Icon.Video")),
            new(PageKey.Annotations, "Annotations", Icon("Icon.Pen")),
            new(PageKey.Effects, "Effects", Icon("Icon.Sparkles")),
            new(PageKey.Audio, "Audio", Icon("Icon.Volume")),
        ];
        MediaItems = [new(PageKey.Recordings, "Recordings", Icon("Icon.Film"))];
        SystemItems = [new(PageKey.Settings, "Settings", Icon("Icon.Settings"), "Ctrl+,")];
        _currentPage = CaptureItems[0];
        _currentPage.IsSelected = true;

        main.PropertyChanged += OnMainPropertyChanged;
        main.RecordingStartFailed += ex => _ = Dialogs.ErrorAsync("Recording couldn't start",
            "Cap-IT couldn't start capturing the selected source. Check that it's still available, then try again — or choose a different encoder on the Capture page.",
            ex.ToString(), Icon("Icon.AlertCircle"));
    }

    public MainViewModel Main { get; }
    public RecordingsViewModel Recordings { get; }
    public ToastService Toasts { get; }
    public DialogService Dialogs { get; }

    public IReadOnlyList<NavigationItem> CaptureItems { get; }
    public IReadOnlyList<NavigationItem> MediaItems { get; }
    public IReadOnlyList<NavigationItem> SystemItems { get; }
    private IEnumerable<NavigationItem> AllItems => CaptureItems.Concat(MediaItems).Concat(SystemItems);

    [ObservableProperty] private NavigationItem _currentPage;
    [ObservableProperty] private bool _isSidebarCollapsed;
    [ObservableProperty] private SourcePickerViewModel? _sourcePicker;

    public bool IsSidebarExpanded => !IsSidebarCollapsed;
    public bool IsSourcePickerOpen => SourcePicker is not null;

    public bool HideWhileRecording
    {
        get => _ui.Current.HideWhileRecording;
        set { if (_ui.Current.HideWhileRecording == value) return; _ui.Current.HideWhileRecording = value; _ui.Save(); OnPropertyChanged(); }
    }

    public bool ShowRecordingController
    {
        get => _ui.Current.ShowRecordingController;
        set { if (_ui.Current.ShowRecordingController == value) return; _ui.Current.ShowRecordingController = value; _ui.Save(); OnPropertyChanged(); }
    }

    /// <summary>Settings secondary navigation: 0 General · 1 Recording · 2 Storage · 3 Shortcuts · 4 Updates · 5 About.</summary>
    [ObservableProperty] private int _settingsSectionIndex;

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSidebarExpanded));
        _ui.Current.SidebarCollapsed = value;
        _ui.Save();
    }

    partial void OnSourcePickerChanged(SourcePickerViewModel? value) => OnPropertyChanged(nameof(IsSourcePickerOpen));

    partial void OnCurrentPageChanged(NavigationItem value)
    {
        foreach (var item in AllItems) item.IsSelected = ReferenceEquals(item, value);
        if (value.Key == PageKey.Recordings) _ = Recordings.RefreshAsync();
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.OutputDirectory) && CurrentPage.Key == PageKey.Recordings)
            _ = Recordings.RefreshAsync();
    }

    [RelayCommand]
    private void Navigate(NavigationItem item) => CurrentPage = item;

    public void NavigateTo(PageKey key) => CurrentPage = AllItems.First(i => i.Key == key);

    [RelayCommand] private void GoHome() => NavigateTo(PageKey.Home);
    [RelayCommand] private void GoCapture() => NavigateTo(PageKey.Capture);
    [RelayCommand] private void GoAudio() => NavigateTo(PageKey.Audio);
    [RelayCommand] private void GoWebcam() => NavigateTo(PageKey.Webcam);
    [RelayCommand] private void GoRecordings() => NavigateTo(PageKey.Recordings);
    [RelayCommand] private void GoSettings() => NavigateTo(PageKey.Settings);

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void OpenSourcePicker()
    {
        if (!Main.IsIdle || SourcePicker is not null) return;
        SourcePicker = new SourcePickerViewModel(Main, startRecording =>
        {
            SourcePicker = null;
            if (!startRecording) return;
            // Let the overlay leave the screen before capture begins, so the scrim never lands in the
            // first frames of the recording.
            DispatcherTimer.RunOnce(() =>
            {
                if (Main.StartRecordingCommand.CanExecute(null)) Main.StartRecordingCommand.Execute(null);
            }, TimeSpan.FromMilliseconds(180));
        });
    }

    [RelayCommand]
    private async Task OpenRecordingAsync()
    {
        var path = await _files.PickRecordingAsync();
        if (path is not null) Main.ReviewRecording(path);
    }

    [RelayCommand]
    private async Task ChooseOutputFolderAsync()
    {
        var folder = await _files.PickFolderAsync("Choose where recordings are saved", Main.OutputDirectory);
        if (folder is null) return;
        Main.OutputDirectory = folder;
        Toasts.Success("Output folder updated", folder);
    }

    [RelayCommand]
    private void OpenOutputFolder() => ShellIntegration.OpenFolder(Main.OutputDirectory);

    [RelayCommand]
    private void OpenProjectPage() => ShellIntegration.OpenWithDefaultApp("https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder");

    [RelayCommand]
    private void ReportIssue() => ShellIntegration.OpenWithDefaultApp("https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/issues");

    private static Geometry? Icon(string key) => AppResources.Icon(key);
}
