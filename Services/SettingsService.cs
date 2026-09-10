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

    // Last known-good copy, rotated on every successful save. This exists specifically because the file
    // has to survive an app update: the installer shuts the running app down (AppMutex/CloseApplications)
    // to overwrite Program Files, and a shutdown landing mid-write used to be able to leave a truncated
    // settings.json behind. Load() treats an unparseable file as "no settings at all" and hands back
    // defaults, and the next save then writes those defaults over everything — so a single badly-timed
    // write turned into every preference silently resetting. The backup is what makes that recoverable.
    private static readonly string BackupPath = SettingsPath + ".bak";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads the settings file, falling back to the <c>.bak</c> rotation if the main file is missing or
    /// unparseable, and only then to defaults. Never throws.
    /// </summary>
    /// <remarks>
    /// Preferences are stored under %LocalAppData%, entirely outside the installation directory, so an
    /// in-place update never touches them — the installer only overwrites <c>{app}</c> and does not run
    /// the uninstaller. The backup covers the remaining case: a write interrupted at exactly the wrong
    /// moment (the updater closing the app mid-save, a power loss) leaving a half-written file that would
    /// otherwise be indistinguishable from "this user has no settings".
    /// </remarks>
    public AppSettings Load()
    {
        return TryLoadFrom(SettingsPath) ?? TryLoadFrom(BackupPath) ?? new AppSettings();
    }

    private static AppSettings? TryLoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            // An empty or whitespace-only file is what a torn write most often leaves behind; treating it
            // as a miss (rather than letting the deserializer decide) keeps the fallback path explicit.
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch
        {
            // Corrupt/unreadable file, permissions issue, etc. — let the caller try the next candidate
            // rather than block startup over a preferences file.
            return null;
        }
    }

    /// <summary>
    /// Writes the settings file atomically, rotating the previous contents into <c>.bak</c>. Best effort
    /// — never throws.
    /// </summary>
    /// <remarks>
    /// The write goes to a temporary file that is flushed to disk before it replaces the real one, so a
    /// crash or a forced shutdown can only ever lose the *new* settings — it can't corrupt the existing
    /// ones. This matters most during an update, when the installer deliberately closes the app and any
    /// save still in flight is the one most likely to be interrupted.
    /// </remarks>
    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = SettingsPath + ".tmp";

            // Flushed explicitly: File.WriteAllText alone can return with the bytes still sitting in the
            // OS write cache, which is exactly the window a forced shutdown would tear.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(SettingsPath))
            {
                // Replace keeps the swap atomic and rotates the outgoing contents into the backup in the
                // same operation. ignoreMetadataErrors avoids failing over ACL/attribute copying on a
                // file whose contents are all that matter here.
                File.Replace(tempPath, SettingsPath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, SettingsPath);
            }
        }
        catch
        {
            // Best effort: losing a settings write is far better than crashing or blocking the UI over it.
        }
    }
}
