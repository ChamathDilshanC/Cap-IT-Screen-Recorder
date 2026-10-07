using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenRecorderApp.Services;

namespace ScreenRecorderApp.Models;

/// <summary>Sidecar metadata for a completed recording and its retained cursor presentation settings.</summary>
public sealed class RecordingMetadata
{
    public int SchemaVersion { get; set; } = 2;
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public double? DurationSeconds { get; set; }
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public int Fps { get; set; }
    public CursorSettings Cursor { get; set; } = new();
    public PresentationSettings Presentation { get; set; } = new();
    public double TrimStartSeconds { get; set; }
    public double? TrimEndSeconds { get; set; }
    public bool PresentationBaked { get; set; }

    [JsonIgnore]
    public string CursorSummary => Cursor.Enabled
        ? $"{Cursor.Style}, {Cursor.Size:0.##}×, smoothing {Cursor.Smoothing:0.##}"
        : "Hidden";

    public static string GetPath(string recordingPath) =>
        RecordingDataPaths.FileInDirectory(recordingPath, ".metadata.json");

    private static string LegacyPath(string recordingPath) => recordingPath + ".metadata.json";

    public static RecordingMetadata? Load(string recordingPath)
    {
        try
        {
            var path = GetPath(recordingPath);
            if (!File.Exists(path)) path = LegacyPath(recordingPath);
            if (!File.Exists(path)) return null;
            var metadata = JsonSerializer.Deserialize<RecordingMetadata>(File.ReadAllText(path));
            if (metadata is not null) metadata.Cursor ??= new CursorSettings();
            if (metadata is not null) metadata.Presentation ??= new PresentationSettings();
            metadata?.Presentation.Normalize();
            return metadata;
        }
        catch { return null; }
    }

    public void Save(string recordingPath)
    {
        var path = GetPath(recordingPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }

    public async Task SaveAsync(string recordingPath)
    {
        var path = GetPath(recordingPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
