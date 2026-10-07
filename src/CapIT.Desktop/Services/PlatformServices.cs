using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ScreenRecorderApp.Services;

/// <summary>Opens and tracks Review &amp; Export editor windows (one per recording).</summary>
public interface IReviewWindowService
{
    void Open(string recordingPath);
    void CloseAll();

    /// <summary>Closes every editor after it has saved its edits.</summary>
    Task CloseAllAsync();
}

/// <summary>File and folder pickers through Avalonia's storage provider (the native Windows dialogs).</summary>
public sealed class FilePickerService(Func<TopLevel?> topLevel)
{
    private static readonly FilePickerFileType Videos = new("Recordings") { Patterns = ["*.mp4", "*.mkv"] };
    private static readonly FilePickerFileType Images = new("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg"] };

    public Task<string?> PickRecordingAsync() => PickFileAsync("Open a recording", Videos);
    public Task<string?> PickImageAsync(string title = "Choose an image") => PickFileAsync(title, Images);

    public async Task<string?> PickFolderAsync(string title, string? startIn = null)
    {
        if (topLevel()?.StorageProvider is not { CanPickFolder: true } storage) return null;
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startIn) && Directory.Exists(startIn))
            start = await storage.TryGetFolderFromPathAsync(startIn);
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickFileAsync(string title, FilePickerFileType type)
    {
        if (topLevel()?.StorageProvider is not { CanOpen: true } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [type] });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}

/// <summary>Explorer / default-app / clipboard operations. Keeps process launching out of views and view models.</summary>
public static class ShellIntegration
{
    public static bool OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static bool RevealFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static bool OpenWithDefaultApp(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> CopyTextAsync(TopLevel? topLevel, string text)
    {
        if (topLevel?.Clipboard is not { } clipboard) return false;
        try { await clipboard.SetTextAsync(text); return true; }
        catch { return false; }
    }
}
