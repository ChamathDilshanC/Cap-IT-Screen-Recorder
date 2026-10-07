namespace ScreenRecorderApp.Services.Encoding;

public static class FFmpegLocator
{
    /// <summary>
    /// Locates ffmpeg.exe: first next to the app (an "ffmpeg" subfolder, or the app folder itself),
    /// then falls back to the system PATH. Returns null if it cannot be found anywhere.
    /// </summary>
    public static string? FindFFmpeg()
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(full)) return full;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }

        return null;
    }
}
