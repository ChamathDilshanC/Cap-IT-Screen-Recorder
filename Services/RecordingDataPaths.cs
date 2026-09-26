namespace ScreenRecorderApp.Services;

/// <summary>Centralizes files generated for a recording so media folders stay uncluttered.</summary>
public static class RecordingDataPaths
{
    public static string DirectoryFor(string recordingPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(recordingPath))
            ?? Environment.CurrentDirectory;
        return Path.Combine(directory, "Cap-IT Metadata");
    }

    public static string FileInDirectory(string recordingPath, string suffix) =>
        Path.Combine(DirectoryFor(recordingPath), Path.GetFileName(recordingPath) + suffix);
}
