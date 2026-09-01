using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as a plain JSON file under %LocalAppData%. Deliberately not
/// <c>ApplicationData.Current.LocalSettings</c>: this app is unpackaged (<c>WindowsPackageType=None</c>
/// in the csproj), and that API throws without a package identity unless specially bootstrapped — a
/// plain file is the reliable, idiomatic choice here, in the same spirit as <c>OutputDirectory</c>'s
/// default already pointing at a plain <see cref="Environment.SpecialFolder"/> path.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cap-IT Screen Recorder", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads the settings file if it exists and is valid; returns defaults otherwise. Never throws.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable file, permissions issue, etc. — fall back to defaults rather than
            // block startup over a preferences file.
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings file, creating its folder if needed. Best effort — never throws.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best effort: losing a settings write is far better than crashing or blocking the UI over it.
        }
    }
}
