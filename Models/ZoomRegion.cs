using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ScreenRecorderApp.Services;

namespace ScreenRecorderApp.Models;

/// <summary>A retained, post-recording camera move. Coordinates are normalized (0..1).</summary>
public sealed class ZoomRegion : INotifyPropertyChanged
{
    private double _startSeconds;
    private double _endSeconds = 2;
    private double _centerX = .5;
    private double _centerY = .5;
    private double _scale = 1.5;
    private bool _enabled = true;

    public double StartSeconds { get => _startSeconds; set => Set(ref _startSeconds, value); }
    public double EndSeconds { get => _endSeconds; set => Set(ref _endSeconds, value); }
    public double CenterX { get => _centerX; set => Set(ref _centerX, value); }
    public double CenterY { get => _centerY; set => Set(ref _centerY, value); }
    public double Scale { get => _scale; set => Set(ref _scale, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Persists review edits beside the recording, so reopening the review retains camera moves.</summary>
public static class ZoomRegionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static string GetPath(string recordingPath) =>
        RecordingDataPaths.FileInDirectory(recordingPath, ".zoom.json");

    private static string LegacyPath(string recordingPath) => recordingPath + ".zoom.json";

    public static List<ZoomRegion> Load(string recordingPath)
    {
        try
        {
            var path = GetPath(recordingPath);
            if (!File.Exists(path)) path = LegacyPath(recordingPath);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<ZoomRegion>>(File.ReadAllText(path), Options) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void Save(string recordingPath, IEnumerable<ZoomRegion> regions)
    {
        try
        {
            var path = GetPath(recordingPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(regions, Options));
        }
        catch { /* sidecar retention must never block recording/export */ }
    }
}
