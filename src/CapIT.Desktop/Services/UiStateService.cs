using System.Text.Json;

namespace ScreenRecorderApp.Services;

/// <summary>Presentation-only preferences: window placement, sidebar state, library layout.</summary>
public sealed class UiState
{
    public double? Width { get; set; }
    public double? Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool IsMaximized { get; set; }
    public bool SidebarCollapsed { get; set; }

    /// <summary>Minimise the main window while recording (the floating controller stays available).</summary>
    public bool HideWhileRecording { get; set; }

    /// <summary>Show the floating recording controller while recording.</summary>
    public bool ShowRecordingController { get; set; } = true;

    public string LibraryLayout { get; set; } = "grid";
}

/// <summary>
/// Persists <see cref="UiState"/> beside the app's settings.json, in its own file so UI preferences can
/// never disturb (or reset) recording settings. Load never throws; a bad file means defaults.
/// </summary>
public sealed class UiStateService
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cap-IT Screen Recorder", "ui-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UiState Current { get; private set; } = new();

    public UiState Load()
    {
        try
        {
            if (File.Exists(StatePath))
                Current = JsonSerializer.Deserialize<UiState>(File.ReadAllText(StatePath), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Current = new();
        }
        return Current;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, StatePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Window placement is a convenience; never fail the app over it.
        }
    }
}
