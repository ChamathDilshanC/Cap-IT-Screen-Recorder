using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.Services.Export;
using ScreenRecorderApp.Services.Library;

namespace ScreenRecorderApp.ViewModels;

public sealed record AspectOption(string Key, string Label, double IconWidth, double IconHeight)
{
    public static readonly IReadOnlyList<AspectOption> All =
    [
        new("Original", "Original", 26, 17),
        new("16:9", "16:9", 30, 17),
        new("9:16", "9:16", 14, 25),
        new("1:1", "1:1", 21, 21),
        new("4:5", "4:5", 18, 23),
        new("3:2", "3:2", 28, 19),
        new("4:3", "4:3", 26, 20),
        new("Custom", "Custom", 24, 18),
    ];
}

public sealed record GradientPreset(string Name, string ColorA, string ColorB);

public sealed record InspectorTab(string Key, string Title, string IconKey)
{
    public Avalonia.Media.Geometry? Icon => AppResources.Icon(IconKey);
}

public sealed record ChoiceOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A Review &amp; Export session: the document (<see cref="Document"/>, history + persistence), playback,
/// the selected text layer, workspace layout, background/frame/perspective helpers and the export
/// pipeline. The original recording is never modified; edits live in its metadata sidecar.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _exportCts;
    private bool _syncing;
    private bool _discarded;
    private string? _timelinePath;

    public EditorViewModel(string recordingPath, FilePickerService files)
    {
        Document = new ReviewViewModel(recordingPath);
        Text = new TextLayerEditor(Document);
        Playback = new VideoPlaybackController(() => Document.TrimStart, () => Document.TrimEnd);
        Files = files;
        _selectedTab = InspectorTabs[0];
        _selectedCanvasSize = CanvasSizes[0];
        Document.CompositionChanged += (_, _) => OnCompositionChanged();
        Document.PropertyChanged += OnDocumentPropertyChanged;
        Playback.MediaOpened += duration =>
        {
            // Probe failed but Media Foundation knows the length: adopt it, as the original editor did.
            if (Document.Probe?.Duration is null && duration > 0)
            {
                Document.Duration = duration;
                Document.TrimEnd = duration;
            }
        };
    }

    public ReviewViewModel Document { get; }
    public TextLayerEditor Text { get; }
    public VideoPlaybackController Playback { get; }
    public ToastService Toasts { get; } = new();
    public FilePickerService Files { get; }
    public DialogService? Dialogs { get; set; }

    /// <summary>Asks the window to close (after edits have been saved).</summary>
    public event Action? CloseRequested;

    public string FilePath => Document.FilePath;
    public string Title => Document.Title;

    // ---- Workspace ----------------------------------------------------------------------------------

    public IReadOnlyList<InspectorTab> InspectorTabs { get; } =
    [
        new("Canvas", "Canvas", "Icon.Ratio"),
        new("Background", "Background", "Icon.Image"),
        new("Video", "Video", "Icon.Video"),
        new("Style", "Corners & shadow", "Icon.Shadow"),
        new("Frame", "Frame", "Icon.Frame"),
        new("Watermark", "Watermark", "Icon.Stamp"),
        new("Text", "Text", "Icon.Tool.Text"),
        new("Export", "Export", "Icon.Export"),
    ];

    [ObservableProperty] private InspectorTab _selectedTab;
    [ObservableProperty] private bool _isInspectorVisible = true;
    [ObservableProperty] private bool _isLayersVisible = true;
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private string? _assetWarning;

    public bool IsTab(string key) => SelectedTab.Key == key;
    public bool ShowCanvasTab => IsTab("Canvas");
    public bool ShowBackgroundTab => IsTab("Background");
    public bool ShowVideoTab => IsTab("Video");
    public bool ShowStyleTab => IsTab("Style");
    public bool ShowFrameTab => IsTab("Frame");
    public bool ShowWatermarkTab => IsTab("Watermark");
    public bool ShowTextTab => IsTab("Text");
    public bool ShowExportTab => IsTab("Export");
    public bool HasAssetWarning => !string.IsNullOrEmpty(AssetWarning);

    partial void OnSelectedTabChanged(InspectorTab value)
    {
        foreach (var name in new[] { nameof(ShowCanvasTab), nameof(ShowBackgroundTab), nameof(ShowVideoTab), nameof(ShowStyleTab),
                     nameof(ShowFrameTab), nameof(ShowWatermarkTab), nameof(ShowTextTab), nameof(ShowExportTab) })
            OnPropertyChanged(name);
    }

    partial void OnAssetWarningChanged(string? value) => OnPropertyChanged(nameof(HasAssetWarning));

    public void SelectTab(string key) => SelectedTab = InspectorTabs.First(t => t.Key == key);

    [RelayCommand] private void ToggleInspector() => IsInspectorVisible = !IsInspectorVisible;
    [RelayCommand] private void ToggleLayers() => IsLayersVisible = !IsLayersVisible;
    [RelayCommand] private void DismissAssetWarning() => AssetWarning = null;
    [RelayCommand] private void ShowTextInspector() => SelectTab("Text");
    [RelayCommand] private void ShowVideoInspector() => SelectTab("Video");

    // ---- Loading ------------------------------------------------------------------------------------

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasLoadError;
    [ObservableProperty] private string _loadError = "";
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _timelineStrip;

    public async Task InitializeAsync()
    {
        try
        {
            await Document.InitializeAsync(_lifetime.Token);
            IsLoading = false;
            Text.Refresh();
            OnCompositionChanged();
            Playback.Open(FilePath);
            _ = LoadTimelineAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            IsLoading = false;
            HasLoadError = true;
            LastError = ex.ToString();
            LoadError = "This recording could not be opened. The original file has not been changed. Try opening it in another player.";
        }
    }

    private async Task LoadTimelineAsync()
    {
        try
        {
            var path = await TimelineThumbnailService.CreateAsync(FilePath, Document.Duration, _lifetime.Token);
            if (path is null) return;
            if (_lifetime.IsCancellationRequested) { TryDelete(path); return; }
            _timelinePath = path;
            await using var stream = File.OpenRead(path);
            TimelineStrip = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            // A thumbnail failure never prevents playback or export.
        }
    }

    /// <summary>Playback couldn't decode the file — explain it in plain words.</summary>
    public string PlaybackErrorText => Document.Probe?.IsUndecodableByWindows == true
        ? "Your original recording is safe. Windows can't preview this colour format, but you can open it in another player or export a compatible MP4 here."
        : "Windows couldn't play this recording. Your original file is still on disk — retry, open it in another player, or export with FFmpeg.";

    // ---- Canvas -------------------------------------------------------------------------------------

    public IReadOnlyList<AspectOption> AspectOptions { get; } = AspectOption.All;
    public IReadOnlyList<ChoiceOption> CanvasSizes { get; } =
        [new("Source", "Source / canvas"), new("1080", "1080p"), new("1440", "1440p"), new("2160", "2160p"), new("Custom", "Custom")];
    public IReadOnlyList<string> FitModes { get; } = ["Fit", "Fill", "Original", "Custom"];
    public IReadOnlyList<string> VideoPositions { get; } = ["Center", "Top", "Bottom", "Left", "Right", "Custom"];
    public IReadOnlyList<ChoiceOption> ShadowPresets { get; } =
        [new("None", "None"), new("Soft", "Soft"), new("Medium", "Medium"), new("Floating", "Floating"), new("Strong", "Strong"), new("Custom", "Custom")];
    public IReadOnlyList<string> FrameStyles { get; } = ["None", "Minimal", "Browser", "Studio", "Windows", "Device"];
    public IReadOnlyList<string> FrameThemes { get; } = ["Dark", "Light"];
    public IReadOnlyList<string> WatermarkPositions { get; } = ["Top Left", "Top Right", "Bottom Left", "Bottom Right", "Center"];
    public IReadOnlyList<string> ImageFits { get; } = ["Fit", "Fill", "Stretch"];
    public IReadOnlyList<string> ExportQualities { get; } = ["Best Quality", "Balanced", "Small File", "Social", "Source Quality"];
    public IReadOnlyList<string> ExportFrameRates { get; } = ["Source", "24", "30", "60"];
    public IReadOnlyList<string> ExportEncoders { get; } = ["Software (H.264)", "NVIDIA NVENC", "Intel Quick Sync", "AMD AMF"];
    public IReadOnlyList<ChoiceOption> BackgroundModes { get; } =
        [new("none", "None"), new("solid", "Colour"), new("gradient", "Gradient"), new("image", "Image")];

    public AspectOption? SelectedAspect
    {
        get => AspectOptions.FirstOrDefault(a => a.Key == Document.Presentation.CanvasPreset) ?? AspectOptions[0];
        set
        {
            if (value is null || _syncing || !Document.IsReady) return;
            var p = Document.Presentation;
            var size = p.ResolveCanvas(Document.SourceWidth, Document.SourceHeight);
            var custom = value.Key == "Custom";
            p.CanvasWidth = custom ? size.Width : 0;
            p.CanvasHeight = custom ? size.Height : 0;
            p.CanvasPreset = value.Key;
        }
    }

    public bool IsCustomCanvas => Document.Presentation.CanvasPreset == "Custom" || (Document.Presentation.CanvasWidth > 0 && Document.Presentation.CanvasHeight > 0);

    [ObservableProperty] private ChoiceOption _selectedCanvasSize = null!;

    partial void OnSelectedCanvasSizeChanged(ChoiceOption value)
    {
        if (value is null || _syncing || !Document.IsReady) return;
        var p = Document.Presentation;
        if (value.Key == "Source") { p.CanvasWidth = 0; p.CanvasHeight = 0; return; }
        var (w, h) = p.ResolveCanvas(Document.SourceWidth, Document.SourceHeight);
        if (value.Key == "Custom") { p.CanvasWidth = w; p.CanvasHeight = h; p.CanvasPreset = "Custom"; return; }
        var edge = value.Key switch { "1440" => 1440, "2160" => 2160, _ => 1080 };
        var scale = (double)edge / Math.Min(w, h);
        p.CanvasWidth = PresentationSettings.Even((int)(w * scale));
        p.CanvasHeight = PresentationSettings.Even((int)(h * scale));
    }

    public ChoiceOption? SelectedBackgroundMode
    {
        get => BackgroundModes.FirstOrDefault(m => m.Key == Document.Presentation.BackgroundMode) ?? BackgroundModes[2];
        set { if (value is not null && !_syncing) Document.Presentation.BackgroundMode = value.Key; }
    }

    public bool ShowGradientSettings => Document.Presentation.BackgroundMode == "gradient";
    public bool ShowColourSettings => Document.Presentation.BackgroundMode is "gradient" or "solid";
    public bool ShowImageSettings => Document.Presentation.BackgroundMode == "image";
    public bool ShowNoBackgroundNote => Document.Presentation.BackgroundMode == "none";

    public ChoiceOption? SelectedShadowPreset
    {
        get
        {
            var p = Document.Presentation;
            var index = !p.Shadow ? 0 : (p.ShadowBlur, p.ShadowOpacity, p.ShadowOffsetY, p.ShadowOffsetX) switch
            { (24, .28, 12, 0) => 1, (32, .35, 16, 0) => 2, (48, .4, 28, 0) => 3, (32, .6, 20, 0) => 4, _ => 5 };
            return ShadowPresets[index];
        }
        set
        {
            if (value is null || _syncing || value.Key == "Custom") return;
            var p = Document.Presentation;
            p.Shadow = value.Key != "None"; p.ShadowOffsetX = 0;
            (p.ShadowBlur, p.ShadowOpacity, p.ShadowOffsetY) = value.Key switch
            { "Medium" => (32, .35, 16), "Floating" => (48, .4, 28), "Strong" => (32, .6, 20), _ => (24, .28, 12) };
        }
    }

    [RelayCommand] private void ResetPadding() => Document.UpdatePresentation(p => p.Padding = 24);
    [RelayCommand] private void SetCornerRadius(string radius) => Document.Presentation.CornerRadius = double.Parse(radius, System.Globalization.CultureInfo.InvariantCulture);

    // ---- Background ---------------------------------------------------------------------------------

    public IReadOnlyList<BackgroundPreset> BackgroundPresets => Document.BackgroundPresets;
    public IReadOnlyList<PerspectivePreset> PerspectivePresets => Document.PerspectivePresets;

    public IReadOnlyList<GradientPreset> GradientPresets { get; } =
    [
        new("Midnight", "#182435", "#46526D"), new("Aurora", "#294F54", "#AAA0C3"), new("Sunset", "#9C6475", "#E6BB9C"),
        new("Ocean", "#1C4359", "#78A5B2"), new("Purple Glow", "#343052", "#8D81B1"), new("Graphite", "#252A32", "#646E7E"),
        new("Warm Sand", "#A89B88", "#E9DFCB"), new("Arctic", "#91ABB8", "#DCE9EE"), new("Neon Blue", "#26365B", "#598CC0"),
        new("Rose", "#86596C", "#D6ACBC"), new("Emerald", "#21463F", "#70A28C"),
    ];

    public ObservableCollection<string> RecentColours { get; } = ["#202838", "#DCE2E8", "#26374A", "#A89B88", "#86596C", "#21463F"];

    [RelayCommand]
    private void ApplyBackgroundPreset(BackgroundPreset preset)
    {
        var p = Document.Presentation;
        p.BackgroundPreset = preset.Key;
        p.BackgroundAnimationEnabled = preset.Animated;
        p.BackgroundMode = preset.Key == "None" ? "none" : preset.Pattern ? "pattern" : "gradient";
        p.BackgroundColor = preset.Color1; p.BackgroundColor2 = preset.Color2;
    }

    [RelayCommand]
    private void ApplyGradient(GradientPreset preset)
    {
        var p = Document.Presentation;
        p.BackgroundMode = "gradient"; p.BackgroundColor = preset.ColorA; p.BackgroundColor2 = preset.ColorB;
    }

    [RelayCommand]
    private void UseColour(string hex) => Document.Presentation.BackgroundColor = hex;

    /// <summary>Keeps the last six colours handy, most recent first.</summary>
    [RelayCommand]
    private void RememberColour()
    {
        var hex = Document.Presentation.BackgroundColor;
        RecentColours.Remove(hex); RecentColours.Insert(0, hex);
        while (RecentColours.Count > 6) RecentColours.RemoveAt(6);
    }

    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _backgroundThumbnail;
    private string? _thumbnailPath;

    [RelayCommand]
    private async Task ChooseBackgroundAsync()
    {
        var path = await Files.PickImageAsync("Choose a background image");
        if (path is null) return;
        Document.Presentation.BackgroundPath = path; Document.Presentation.BackgroundMode = "image";
    }

    [RelayCommand]
    private void RemoveBackground() { Document.Presentation.BackgroundPath = ""; Document.Presentation.BackgroundMode = "solid"; }

    [RelayCommand]
    private async Task ChooseWatermarkAsync()
    {
        var path = await Files.PickImageAsync("Choose a logo");
        if (path is null) return;
        Document.Presentation.WatermarkPath = path; Document.Presentation.WatermarkEnabled = true;
    }

    [RelayCommand]
    private void RemoveWatermark() { Document.Presentation.WatermarkEnabled = false; Document.Presentation.WatermarkPath = ""; }

    public string WatermarkFileName => string.IsNullOrWhiteSpace(Document.Presentation.WatermarkPath) ? "No logo chosen" : Path.GetFileName(Document.Presentation.WatermarkPath);

    // ---- Video placement ----------------------------------------------------------------------------

    [RelayCommand] private void ApplyPerspective(PerspectivePreset preset) => Document.ApplyPerspective(preset);
    [RelayCommand] private void ResetTransform() => Document.ResetTransform();

    [RelayCommand]
    private void AddZoomAtPlayhead()
    {
        if (!Document.IsReady || IsExporting) return;
        Document.AddZoom(Playback.Position);
        SelectTab("Video");
        IsInspectorVisible = true;
    }

    [RelayCommand]
    private void RemoveZoom(ZoomRegion region) => Document.ZoomRegions.Remove(region);

    [RelayCommand]
    private void SeekToZoom(ZoomRegion region) => Playback.Seek(region.StartSeconds);

    // ---- Composition change fan-out -----------------------------------------------------------------

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewViewModel.Presentation)) OnCompositionChanged();
        if (e.PropertyName == nameof(ReviewViewModel.IsReady))
        {
            ExportVideoCommand.NotifyCanExecuteChanged();
            ExportGifCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnCompositionChanged()
    {
        _syncing = true;
        try
        {
            OnPropertyChanged(nameof(SelectedAspect));
            OnPropertyChanged(nameof(IsCustomCanvas));
            OnPropertyChanged(nameof(SelectedBackgroundMode));
            OnPropertyChanged(nameof(ShowGradientSettings));
            OnPropertyChanged(nameof(ShowColourSettings));
            OnPropertyChanged(nameof(ShowImageSettings));
            OnPropertyChanged(nameof(ShowNoBackgroundNote));
            OnPropertyChanged(nameof(SelectedShadowPreset));
            OnPropertyChanged(nameof(WatermarkFileName));
            var p = Document.Presentation;
            SelectedCanvasSize = p.CanvasWidth <= 0 ? CanvasSizes[0] : p.CanvasPreset == "Custom" ? CanvasSizes[4] : SelectedCanvasSize ?? CanvasSizes[0];
        }
        finally
        {
            _syncing = false;
        }
        UpdateBackgroundThumbnail();
    }

    private void UpdateBackgroundThumbnail()
    {
        var path = Document.Presentation.BackgroundPath;
        if (_thumbnailPath == path) return;
        _thumbnailPath = path;
        try { BackgroundThumbnail = File.Exists(path) ? new Avalonia.Media.Imaging.Bitmap(path) : null; }
        catch { BackgroundThumbnail = null; }
    }

    // ---- Export -------------------------------------------------------------------------------------

    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _exportStage = "";
    [ObservableProperty] private string? _lastOutputPath;
    [ObservableProperty] private string? _lastError;

    public bool IsNotExporting => !IsExporting;
    public bool HasLastOutput => LastOutputPath is not null;
    public bool HasLastError => LastError is not null;

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotExporting));
        ExportVideoCommand.NotifyCanExecuteChanged();
        ExportGifCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastOutputPathChanged(string? value) => OnPropertyChanged(nameof(HasLastOutput));
    partial void OnLastErrorChanged(string? value) => OnPropertyChanged(nameof(HasLastError));

    private bool CanExport() => !IsExporting && Document.IsReady;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportVideoAsync() => ExportAsync(gif: false);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportGifAsync() => ExportAsync(gif: true);

    private async Task ExportAsync(bool gif)
    {
        if (IsExporting || !Document.IsReady) return;
        if (!double.IsFinite(Document.TrimStart) || !double.IsFinite(Document.TrimEnd) || Document.TrimEnd <= Document.TrimStart || Document.TrimEnd > Document.Duration)
        {
            Document.Status = "Choose a valid trim range before exporting.";
            Toasts.Warning("Check the trim range", "The end must be after the start.");
            return;
        }

        IsExporting = true;
        ExportProgress = 0;
        ExportStage = gif ? "Preparing GIF…" : "Preparing edited video…";
        try
        {
            var outputPath = MediaOutputPaths.BuildEditedPath(FilePath, gif);
            _exportCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            await Document.SaveAsync();
            Playback.Pause();
            var zooms = Document.ZoomRegions.Select(r => new ZoomRegion { StartSeconds = r.StartSeconds, EndSeconds = r.EndSeconds, CenterX = r.CenterX, CenterY = r.CenterY, Scale = r.Scale, Enabled = r.Enabled }).ToList();
            var options = new ExportSettings { Quality = Document.Export.Quality, Encoder = Document.Export.Encoder, FrameRate = Document.Export.FrameRate, IncludeAudio = Document.Export.IncludeAudio };
            var progress = new Progress<GifExportProgress>(p => { ExportStage = p.Stage; ExportProgress = p.PercentComplete; });
            await CompositionExportService.ExportAsync(FilePath, TimeSpan.FromSeconds(Document.TrimStart),
                TimeSpan.FromSeconds(Document.TrimEnd - Document.TrimStart), outputPath, gif, Document.Presentation.Clone(), zooms,
                options, progress, _exportCts.Token, Document.Metadata);
            LastOutputPath = outputPath;
            LastError = null;
            Document.Status = $"Exported · {Path.GetFileName(outputPath)}";
            Toasts.Success(gif ? "GIF exported" : "Video exported", Path.GetFileName(outputPath), "Show in folder", () => ShellIntegration.RevealFile(outputPath));
        }
        catch (OperationCanceledException)
        {
            Document.Status = "Export cancelled. Your original recording and edits are safe.";
            Toasts.Info("Export cancelled", "Your original recording and edits are safe.");
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Document.Status = "Export failed. Your original is safe.";
            if (Dialogs is not null)
                await Dialogs.ErrorAsync("Export failed",
                    "Your original recording is safe. Check that the output folder is writable, or choose the Software encoder on the Export tab and try again.",
                    ex.ToString(), AppResources.Icon("Icon.AlertCircle"));
        }
        finally
        {
            _exportCts?.Dispose(); _exportCts = null;
            IsExporting = false;
        }
    }

    [RelayCommand] private void CancelExport() => _exportCts?.Cancel();

    [RelayCommand]
    private void OpenOutputFolder() => ShellIntegration.OpenFolder(Path.GetDirectoryName(LastOutputPath ?? FilePath));

    [RelayCommand]
    private void OpenOriginal()
    {
        if (!ShellIntegration.OpenWithDefaultApp(FilePath)) Document.Status = "The original file could not be opened externally.";
    }

    /// <summary>Close the editor, keeping the original (edits are saved automatically).</summary>
    [RelayCommand]
    private void KeepOriginal() { if (!IsExporting) CloseRequested?.Invoke(); }

    [RelayCommand]
    private async Task DiscardAsync()
    {
        if (IsExporting || !Document.IsReady || Dialogs is null) return;
        var confirmed = await Dialogs.ConfirmAsync("Discard the original recording?",
            "This permanently deletes the original video and its edit settings. Exported copies are kept.",
            "Discard", DialogTone.Danger, AppResources.Icon("Icon.Trash"));
        if (!confirmed) return;
        try
        {
            _discarded = true;
            Playback.Release();
            await Document.CloseAsync(save: false);
            await Task.Run(() => RecordingLibrary.Delete(FilePath));
            CloseRequested?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.ToString();
            if (File.Exists(FilePath)) { _discarded = false; Document.ResumeEditing(); Playback.Open(FilePath); }
            Document.Status = "The recording could not be fully discarded. Check file access.";
            Toasts.Error("Couldn't discard the recording", "It may be open in another app.");
        }
    }

    // ---- Shutdown -----------------------------------------------------------------------------------

    public async Task ShutdownAsync()
    {
        _lifetime.Cancel();
        _exportCts?.Cancel();
        Playback.Dispose();
        await Document.CloseAsync(!_discarded);
        if (_timelinePath is not null) TryDelete(_timelinePath);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temp file; best effort */ }
    }

    public void Dispose()
    {
        _lifetime.Dispose();
        Playback.Dispose();
    }
}
