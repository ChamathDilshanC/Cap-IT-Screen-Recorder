using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture;

namespace ScreenRecorderApp.ViewModels;

/// <summary>
/// One tile in the visual capture-source picker: a display or a window, with a live thumbnail that the
/// picker refreshes on a timer while it's open.
/// </summary>
/// <remarks>
/// Wraps the original <see cref="MonitorInfo"/>/<see cref="WindowInfo"/> rather than copying fields, so
/// applying a choice hands the exact object back to the view model — no re-matching by name or handle.
/// </remarks>
public sealed partial class CaptureSourceItem : ObservableObject
{
    private readonly byte[] _pixels = new byte[SourceThumbnailService.ThumbByteSize];

    private CaptureSourceItem(CaptureTargetKind kind, MonitorInfo? monitor, WindowInfo? window, string title, string subtitle)
    {
        Kind = kind;
        Monitor = monitor;
        Window = window;
        Title = title;
        Subtitle = subtitle;
        // GDI thumbnails carry no meaningful alpha; an opaque surface ignores it.
        Thumbnail = new WriteableBitmap(new PixelSize(SourceThumbnailService.ThumbWidth, SourceThumbnailService.ThumbHeight),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    }

    public static CaptureSourceItem ForMonitor(MonitorInfo monitor) => new(
        CaptureTargetKind.Monitor, monitor, null,
        monitor.FriendlyName,
        $"{monitor.Width} × {monitor.Height}{(monitor.IsPrimary ? "  ·  Primary" : string.Empty)}");

    public static CaptureSourceItem ForWindow(WindowInfo window) => new(
        CaptureTargetKind.Window, null, window,
        window.Title,
        string.IsNullOrWhiteSpace(window.ProcessName) ? "Application window" : window.ProcessName);

    public CaptureTargetKind Kind { get; }
    public MonitorInfo? Monitor { get; }
    public WindowInfo? Window { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsDisplay => Kind == CaptureTargetKind.Monitor;

    /// <summary>One bitmap per tile for the lifetime of the picker; refreshes rewrite its pixels in place.</summary>
    public WriteableBitmap Thumbnail { get; }

    [ObservableProperty] private long _thumbnailVersion;

    /// <summary>Identity of the underlying source: an HMONITOR or an HWND.</summary>
    public nint Handle => Monitor?.Handle ?? Window?.Handle ?? 0;

    /// <summary>False until a thumbnail has been captured — the tile shows a placeholder until then.</summary>
    [ObservableProperty] private bool _hasThumbnail;

    /// <summary>Captures a fresh thumbnail. Call from a background thread: window capture can block on the target app.</summary>
    public bool CaptureInto(SourceThumbnailService thumbnails) => Kind == CaptureTargetKind.Monitor
        ? thumbnails.TryCaptureMonitor(Monitor!, _pixels)
        : thumbnails.TryCaptureWindow(Window!.Handle, _pixels);

    /// <summary>Pushes the pixels captured by the last <see cref="CaptureInto"/> into <see cref="Thumbnail"/>. UI thread.</summary>
    public void PublishThumbnail()
    {
        using (var buffer = Thumbnail.Lock())
        {
            var rowBytes = SourceThumbnailService.ThumbWidth * 4;
            if (buffer.RowBytes == rowBytes)
                Marshal.Copy(_pixels, 0, buffer.Address, _pixels.Length);
            else
                for (var y = 0; y < SourceThumbnailService.ThumbHeight; y++)
                    Marshal.Copy(_pixels, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        }
        HasThumbnail = true;
        ThumbnailVersion++;
    }
}
