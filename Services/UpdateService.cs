using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenRecorderApp.Services;

/// <summary>What <see cref="UpdateService.CheckForUpdateAsync"/> found: a release newer than the running
/// build. <see cref="InstallerUrl"/> is null when the release has no <c>*Setup*.exe</c> asset attached —
/// in that case the UI falls back to just opening <see cref="ReleaseNotesUrl"/>.</summary>
public sealed record UpdateInfo(
    Version Version,
    string VersionTag,
    string ReleaseNotesUrl,
    string? InstallerUrl,
    string? InstallerName);

/// <summary>
/// Checks GitHub Releases for a newer build and, when the user opts in, downloads that release's Inno
/// Setup installer and hands off to it. The installer replaces the app in place — same folder — because
/// its <c>AppId</c> is stable and Inno reuses the previous install location; it also closes and relaunches
/// the running app itself (<c>AppMutex</c> + <c>CloseApplications</c> + a postinstall <c>[Run]</c> entry),
/// so all this class has to do is start it and get out of the way.
/// </summary>
public sealed class UpdateService
{
    // Public GitHub REST endpoint for the most recent non-draft release. No auth needed for public repos
    // (rate limit is 60 req/h per IP, which a once-per-launch check never comes close to).
    private const string LatestReleaseApi =
        "https://api.github.com/repos/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/latest";

    private const string ReleasesPage =
        "https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub's API returns 403 to requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cap-IT-Screen-Recorder-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The running build's version, normalized to major.minor.patch (revision dropped so it
    /// compares cleanly against a 3-part semver tag). Driven by &lt;Version&gt; in the csproj.</summary>
    public static Version CurrentVersion => Normalize(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Queries GitHub for the latest release. Returns an <see cref="UpdateInfo"/> only when that release
    /// is strictly newer than <see cref="CurrentVersion"/>; returns null otherwise — including on any
    /// network / parse error, since a failed background update check must stay silent.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            if (release is null || release.Draft || release.Prerelease) return null;
            if (!TryParseTag(release.TagName, out var latest)) return null;
            if (latest <= CurrentVersion) return null;

            var installer = release.Assets?.FirstOrDefault(a =>
                a.Name is not null &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

            var notesUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPage : release.HtmlUrl!;

            return new UpdateInfo(latest, release.TagName!.Trim(), notesUrl,
                installer?.DownloadUrl, installer?.Name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the release installer to <c>%TEMP%</c>, reporting progress as 0-100. Returns the local
    /// path. Throws on cancellation or any download error (the caller surfaces those to the user).
    /// </summary>
    public async Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(update.InstallerUrl))
            throw new InvalidOperationException("This release has no installer attached — open the releases page to download it.");

        var fileName = string.IsNullOrEmpty(update.InstallerName)
            ? $"CapIT-Screen-Recorder-Setup-{update.VersionTag}.exe"
            : update.InstallerName!;
        var destination = Path.Combine(Path.GetTempPath(), fileName);

        using var response = await Http
            .GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            if (totalBytes > 0) progress.Report(Math.Min(100, received * 100.0 / totalBytes));
        }

        progress.Report(100);
        return destination;
    }

    /// <summary>
    /// Launches the downloaded installer silently and invokes <paramref name="requestExit"/> so the app
    /// can close. The installer waits on the app's mutex, closes anything still holding it, installs over
    /// the existing location, then relaunches the app via its postinstall [Run] entry.
    /// </summary>
    public void LaunchInstaller(string installerPath, Action requestExit)
    {
        var startInfo = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            // /SILENT       — progress window only, no wizard pages
            // /CLOSEAPPLICATIONS + /FORCECLOSEAPPLICATIONS — shut the running app if our own exit lags
            // No /DIR       — Inno reuses the previous install folder (UsePreviousAppDir), so an update
            //                 lands exactly where the user originally installed it.
            Arguments = "/SILENT /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NOCANCEL /SP-",
        };

        Process.Start(startInfo);
        requestExit();
    }

    /// <summary>Opens the releases page in the default browser — the fallback path when there's no
    /// installer asset or the automatic download failed.</summary>
    public static void OpenReleasesPage(string? url = null) =>
        Process.Start(new ProcessStartInfo(url ?? ReleasesPage) { UseShellExecute = true });

    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        // Tags are conventionally "v2.1.0"; tolerate a missing "v" and any "-beta" / "+meta" suffix.
        var trimmed = tag.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+', ' ']);
        if (cut > 0) trimmed = trimmed[..cut];

        if (!Version.TryParse(trimmed, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
    }
}
