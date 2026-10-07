namespace ScreenRecorderApp.Services;

/// <summary>Builds predictable, date-organized destinations for source recordings and edited exports.</summary>
public static class MediaOutputPaths
{
    public const string RecordingsFolderName = "Recordings";
    public const string EditedFolderName = "Edited";

    public static string BuildRecordingPath(string outputRoot, string extension, DateTime? now = null)
    {
        var timestamp = now ?? DateTime.Now;
        var directory = CreateDateDirectory(outputRoot, RecordingsFolderName, timestamp);
        return Path.Combine(directory, $"Recording_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}.{extension.TrimStart('.')}");
    }

    public static string BuildEditedPath(string sourcePath, bool gif, DateTime? now = null)
    {
        var timestamp = now ?? DateTime.Now;
        var directory = CreateDateDirectory(FindOutputRoot(sourcePath), EditedFolderName, timestamp);
        var sourceName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        return Path.Combine(directory, $"{sourceName}_edited_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}.{(gif ? "gif" : "mp4")}");
    }

    private static string CreateDateDirectory(string root, string category, DateTime timestamp)
    {
        var directory = Path.Combine(Path.GetFullPath(root), category, timestamp.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindOutputRoot(string sourcePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!);
        while (directory.Parent is not null)
        {
            if (directory.Name.Equals(RecordingsFolderName, StringComparison.OrdinalIgnoreCase))
                return directory.Parent.FullName;
            directory = directory.Parent;
        }

        return Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Recording" : sanitized;
    }
}
