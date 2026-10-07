using ScreenRecorderApp.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace ScreenRecorderApp.Services.Library;

public enum RecordingFileKind { Recording, Edited, Gif }

/// <summary>One media file found in the output folder.</summary>
public sealed record RecordingFile(string Path, string FileName, DateTime CreatedAt, long SizeBytes, RecordingFileKind Kind);

/// <summary>Video details read from the Windows property system.</summary>
public sealed record RecordingDetails(TimeSpan? Duration, int Width, int Height);

/// <summary>A decoded thumbnail: tightly packed BGRA (premultiplied) pixels.</summary>
public sealed record ThumbnailPixels(byte[] Bgra, int Width, int Height);

/// <summary>
/// Finds recordings and edited exports under the configured output directory (the date-organised
/// <c>Recordings/</c> and <c>Edited/</c> folders from <see cref="MediaOutputPaths"/>, plus older recordings
/// saved directly in the root) and reads their details and thumbnails through the Windows shell — the
/// same cached thumbnails Explorer shows, so no FFmpeg decode is needed to browse the library.
/// </summary>
public static class RecordingLibrary
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv"];

    public static IReadOnlyList<RecordingFile> Enumerate(string outputRoot)
    {
        var results = new List<RecordingFile>();
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot)) return results;

        void Scan(string directory, SearchOption option, RecordingFileKind defaultKind)
        {
            if (!Directory.Exists(directory)) return;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*.*", option); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

            foreach (var path in files)
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var isGif = extension == ".gif";
                if (!isGif && !VideoExtensions.Contains(extension)) continue;
                if (path.Contains("Cap-IT Metadata", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length == 0) continue;
                    var kind = isGif ? RecordingFileKind.Gif : defaultKind;
                    results.Add(new RecordingFile(info.FullName, info.Name, info.CreationTime, info.Length, kind));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        Scan(Path.Combine(outputRoot, MediaOutputPaths.RecordingsFolderName), SearchOption.AllDirectories, RecordingFileKind.Recording);
        Scan(Path.Combine(outputRoot, MediaOutputPaths.EditedFolderName), SearchOption.AllDirectories, RecordingFileKind.Edited);
        Scan(outputRoot, SearchOption.TopDirectoryOnly, RecordingFileKind.Recording); // pre-organised recordings

        return results
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
    }

    public static async Task<RecordingDetails?> GetDetailsAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var video = await file.Properties.GetVideoPropertiesAsync();
            return new RecordingDetails(video.Duration > TimeSpan.Zero ? video.Duration : null, (int)video.Width, (int)video.Height);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ThumbnailPixels?> GetThumbnailAsync(string path, uint requestedSize = 320)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.VideosView, requestedSize, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null || thumbnail.Size == 0) return null;
            var decoder = await BitmapDecoder.CreateAsync(thumbnail);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return new ThumbnailPixels(pixels.DetachPixelData(), (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes a recording and the edit metadata stored beside it (current and legacy sidecar locations).</summary>
    public static void Delete(string path)
    {
        File.Delete(path);
        TryDelete(RecordingMetadata.GetPath(path));
        TryDelete(ZoomRegionStore.GetPath(path));
        TryDelete(path + ".metadata.json");
        TryDelete(path + ".zoom.json");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
