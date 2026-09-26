using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Encoding;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

namespace ScreenRecorderApp.ViewModels;

/// <summary>Editor document, history and persistence; playback and pickers belong to the view.</summary>
public sealed partial class ReviewViewModel : ObservableObject
{
    public string FilePath { get; }
    public string Title => Path.GetFileNameWithoutExtension(FilePath);
    public RecordingMetadata Metadata { get; private set; } = new();
    public MediaProbeResult? Probe { get; private set; }
    [ObservableProperty] private PresentationSettings _presentation = new();
    [ObservableProperty] private double _duration = 1;
    [ObservableProperty] private double _trimStart;
    [ObservableProperty] private double _trimEnd = 1;
    [ObservableProperty] private int _sourceWidth = 1920;
    [ObservableProperty] private int _sourceHeight = 1080;
    [ObservableProperty] private string _status = "Loading recording…";
    [ObservableProperty] private string _presetName = "";
    [ObservableProperty] private PresentationPreset? _selectedPreset;
    [ObservableProperty] private bool _isReady;
    public ExportSettings Export { get; } = new();
    public ObservableCollection<ZoomRegion> ZoomRegions { get; } = [];
    public ObservableCollection<PresentationPreset> Presets { get; } = new(PresentationPreset.BuiltIns);
    public event EventHandler? CompositionChanged;
    public string RecordingInfo => $@"{TimeSpan.FromSeconds(Duration):mm\:ss}  ·  {SourceWidth} × {SourceHeight}  ·  {Probe?.FrameRate:0.##} fps";
    public string ScaleLabel => $"{Presentation.VideoScale:P0}";
    public string CanvasLabel { get { var (w,h) = Presentation.ResolveCanvas(SourceWidth, SourceHeight); return $"{w} × {h}"; } }
    public string TrimLabel => $"{Math.Max(0, TrimEnd - TrimStart):0.0}s selected";
    public void UpdateTextOverlay(Action<PresentationTextOverlay> update)
    {
        update(Presentation.TextOverlay);
        Presentation.TextOverlay.Normalize();
        Changed();
    }
    public void SetTextOverlayPosition(double x, double y, bool notify = true)
    {
        Presentation.TextOverlay.X = Math.Clamp(x, 0, 1);
        Presentation.TextOverlay.Y = Math.Clamp(y, 0, 1);
        if (notify) Changed();
    }
    private readonly DispatcherQueueTimer _commitTimer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly List<string> _history = [];
    private int _historyIndex;
    private bool _restoring = true;
    private bool _disposed;
    private bool _normalizing;
    private static string PresetPath
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("CAPIT_REVIEW_SMOKE_DIR") is { Length: > 0 } testFolder)
                return Path.Combine(testFolder, "test-presets.json");
#endif
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cap-IT Screen Recorder", "presentation-presets.json");
        }
    }
    private sealed record Snapshot(PresentationSettings Presentation, double TrimStart, double TrimEnd, List<ZoomRegion> ZoomRegions);

    public ReviewViewModel(string path)
    {
        FilePath = path;
        _commitTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _commitTimer.Interval = TimeSpan.FromMilliseconds(450); _commitTimer.IsRepeating = false;
        _commitTimer.Tick += async (_, _) => { Commit(); await SaveAsync(); };
        Presentation.PropertyChanged += OnPresentationChanged;
        ZoomRegions.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null) foreach (ZoomRegion r in args.OldItems) r.PropertyChanged -= OnRegionChanged;
            if (args.NewItems is not null) foreach (ZoomRegion r in args.NewItems) r.PropertyChanged += OnRegionChanged;
            Changed();
        };
        SelectedPreset = Presets[0];
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        var metadataTask = Task.Run(() => RecordingMetadata.Load(FilePath) ?? new(), ct);
        var probeTask = MediaProbe.ProbeAsync(FilePath, ct);
        Metadata = await metadataTask; Probe = await probeTask;
        ct.ThrowIfCancellationRequested();
        SourceWidth = Probe.Width > 0 ? Probe.Width : Metadata.CaptureWidth > 0 ? Metadata.CaptureWidth : 1920;
        SourceHeight = Probe.Height > 0 ? Probe.Height : Metadata.CaptureHeight > 0 ? Metadata.CaptureHeight : 1080;
        Duration = Math.Max(.05, Probe.Duration?.TotalSeconds ?? Metadata.DurationSeconds ?? 1);
        Presentation = Metadata.Presentation;
        Presentation.Normalize();
        TrimStart = PresentationSettings.Safe(Metadata.TrimStartSeconds, 0, 0, Math.Max(0, Duration - .05));
        TrimEnd = PresentationSettings.Safe(Metadata.TrimEndSeconds ?? Duration, Duration, TrimStart + .01, Duration);
        foreach (var r in await Task.Run(() => ZoomRegionStore.Load(FilePath), ct))
        {
            if (r is null || !double.IsFinite(r.StartSeconds) || !double.IsFinite(r.EndSeconds)) continue;
            r.StartSeconds = Math.Clamp(r.StartSeconds, 0, Duration);
            r.EndSeconds = Math.Clamp(r.EndSeconds, 0, Duration);
            r.CenterX = PresentationSettings.Safe(r.CenterX, .5, 0, 1); r.CenterY = PresentationSettings.Safe(r.CenterY, .5, 0, 1);
            r.Scale = PresentationSettings.Safe(r.Scale, 1.5, 1, 4);
            if (r.EndSeconds > r.StartSeconds) ZoomRegions.Add(r);
        }
        try
        {
            if (File.Exists(PresetPath))
                foreach (var preset in JsonSerializer.Deserialize<List<PresentationPreset>>(await File.ReadAllTextAsync(PresetPath, ct)) ?? [])
                    if (preset?.Settings is not null && !string.IsNullOrWhiteSpace(preset.Name))
                    { preset.Settings.Normalize(); Presets.Add(preset with { IsBuiltIn = false }); }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { Status = "Custom presets could not be loaded."; }
        _restoring = false; IsReady = true; _history.Add(Serialize());
        OnPropertyChanged(nameof(RecordingInfo)); NotifyComposition();
        Status = "Original recording preserved · edits save automatically";
    }

    partial void OnPresentationChanging(PresentationSettings value) => Presentation.PropertyChanged -= OnPresentationChanged;
    partial void OnPresentationChanged(PresentationSettings value) { value.PropertyChanged += OnPresentationChanged; Changed(); }
    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_normalizing) return;
        _normalizing = true;
        try { Presentation.Normalize(); } finally { _normalizing = false; }
        Changed();
    }
    private void OnRegionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_normalizing || sender is not ZoomRegion r) return;
        _normalizing = true;
        try
        {
            r.StartSeconds = PresentationSettings.Safe(r.StartSeconds, 0, 0, Math.Max(0, Duration - .01));
            r.EndSeconds = PresentationSettings.Safe(r.EndSeconds, Duration, r.StartSeconds + .01, Duration);
            r.CenterX = PresentationSettings.Safe(r.CenterX, .5, 0, 1); r.CenterY = PresentationSettings.Safe(r.CenterY, .5, 0, 1);
            r.Scale = PresentationSettings.Safe(r.Scale, 1.5, 1, 4);
        }
        finally { _normalizing = false; }
        Changed();
    }
    partial void OnTrimStartChanged(double value)
    {
        if (!_restoring && !_normalizing)
        { _normalizing = true; TrimStart = PresentationSettings.Safe(value, 0, 0, Math.Max(0, TrimEnd - .01)); _normalizing = false; }
        OnPropertyChanged(nameof(TrimLabel)); if (!_normalizing) Changed();
    }
    partial void OnTrimEndChanged(double value)
    {
        if (!_restoring && !_normalizing)
        { _normalizing = true; TrimEnd = PresentationSettings.Safe(value, Duration, Math.Min(Duration, TrimStart + .01), Duration); _normalizing = false; }
        OnPropertyChanged(nameof(TrimLabel)); if (!_normalizing) Changed();
    }
    private void Changed()
    {
        if (_restoring || _disposed) return;
        NotifyComposition(); _commitTimer.Stop(); _commitTimer.Start();
        UndoCommand.NotifyCanExecuteChanged();
    }
    private void NotifyComposition()
    {
        OnPropertyChanged(nameof(ScaleLabel)); OnPropertyChanged(nameof(CanvasLabel));
        CompositionChanged?.Invoke(this, EventArgs.Empty);
    }
    private string Serialize() => JsonSerializer.Serialize(new Snapshot(Presentation, TrimStart, TrimEnd, ZoomRegions.ToList()));
    private void Commit()
    {
        if (_restoring || _history.Count == 0) return;
        var snapshot = Serialize();
        if (_history[_historyIndex] == snapshot) return;
        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(snapshot);
        if (_history.Count > 100) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
        UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();
    }
    private bool CanUndo() => _historyIndex > 0 || (_history.Count > 0 && Serialize() != _history[_historyIndex]);
    private bool CanRedo() => _historyIndex < _history.Count - 1;
    [RelayCommand(CanExecute = nameof(CanUndo))] private void Undo() { Commit(); if (_historyIndex > 0) Restore(--_historyIndex); }
    [RelayCommand(CanExecute = nameof(CanRedo))] private void Redo() { if (CanRedo()) Restore(++_historyIndex); }
    private void Restore(int index)
    {
        _restoring = true; _commitTimer.Stop();
        var state = JsonSerializer.Deserialize<Snapshot>(_history[index])!;
        Presentation = state.Presentation; TrimStart = state.TrimStart; TrimEnd = state.TrimEnd;
        foreach (var r in ZoomRegions) r.PropertyChanged -= OnRegionChanged;
        ZoomRegions.Clear(); foreach (var r in state.ZoomRegions) ZoomRegions.Add(r);
        _restoring = false; NotifyComposition();
        UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();
        _ = SaveAsync();
    }
    [RelayCommand] private void ResetStyling() { Commit(); Presentation = new(); Commit(); }
    [RelayCommand] private void ApplyPreset() { if (SelectedPreset is null) return; Commit(); Presentation = SelectedPreset.Settings.Clone(); Commit(); }
    [RelayCommand] private void RemoveZoom(ZoomRegion region) => ZoomRegions.Remove(region);
    public void AddZoom(double time) => ZoomRegions.Add(new() { StartSeconds = Math.Clamp(time, 0, Math.Max(0, Duration - .05)), EndSeconds = Math.Min(Duration, time + 2) });

    [RelayCommand] private async Task SavePresetAsync()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) { Status = "Enter a name for your preset."; return; }
        if (Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Status = "That preset name already exists. Choose another name."; return; }
        var preset = new PresentationPreset(name, Presentation.Clone()); Presets.Add(preset); SelectedPreset = preset;
        await SavePresetsAsync();
    }
    [RelayCommand] private async Task RenamePresetAsync()
    {
        if (SelectedPreset is not { IsBuiltIn: false } selected) { Status = "Built-in presets cannot be renamed."; return; }
        var name = PresetName.Trim();
        if (name.Length == 0 || Presets.Any(p => p != selected && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Status = "Use a unique preset name."; return; }
        var i = Presets.IndexOf(selected); Presets[i] = selected with { Name = name }; SelectedPreset = Presets[i]; await SavePresetsAsync();
    }
    [RelayCommand] private async Task DeletePresetAsync()
    {
        if (SelectedPreset is not { IsBuiltIn: false } selected) { Status = "Built-in presets cannot be deleted."; return; }
        Presets.Remove(selected); SelectedPreset = Presets[0]; await SavePresetsAsync();
    }
    private async Task SavePresetsAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PresetPath)!);
            var temp = PresetPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(Presets.Where(p => !p.IsBuiltIn)));
            File.Move(temp, PresetPath, true); Status = "Custom presets saved";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Could not save presets. Check folder access."; }
    }
    public async Task SaveAsync()
    {
        if (!IsReady) return;
        // Snapshot before awaiting: UI mutations cannot race a background serialization.
        var metadata = JsonSerializer.Deserialize<RecordingMetadata>(JsonSerializer.Serialize(Metadata))!;
        metadata.SchemaVersion = 2; metadata.Presentation = Presentation.Clone(); metadata.Presentation.Normalize();
        metadata.TrimStartSeconds = TrimStart; metadata.TrimEndSeconds = TrimEnd;
        var zoomJson = JsonSerializer.Serialize(ZoomRegions);
        await _saveLock.WaitAsync();
        try
        {
            await metadata.SaveAsync(FilePath);
            var path = ZoomRegionStore.GetPath(FilePath); var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, zoomJson); File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Edits could not be saved. Check access to the recording folder."; }
        finally { _saveLock.Release(); }
    }
    public async Task CloseAsync(bool save = true)
    {
        _disposed = true; _commitTimer.Stop();
        if (save) await SaveAsync();
        else { await _saveLock.WaitAsync(); _saveLock.Release(); }
    }
    public void ResumeEditing() => _disposed = false;
}
