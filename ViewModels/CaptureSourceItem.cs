using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture;

namespace ScreenRecorderApp.ViewModels;

/// <summary>
/// One tile in the visual capture-source picker: a display or a window, with a live thumbnail that the
/// picker refreshes on a timer while it's open.
/// </summary>
/// <remarks>
/// Wraps either a <see cref="MonitorInfo"/> or a <see cref="WindowInfo"/> rather than duplicating their
/// fields, so applying a choice hands the original object straight back to the view model — no
/// re-matching by name or handle, and no chance of the tile and the thing it records drifting apart.
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
        Thumbnail = new WriteableBitmap(SourceThumbnailService.ThumbWidth, SourceThumbnailService.ThumbHeight);
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

    // Internal, not public: these are only ever read in C# (to hand the choice back to MainViewModel),
    // never bound to. Public would pull MonitorInfo/WindowInfo into the generated XamlTypeInfo as
    // activatable types, and neither has a parameterless constructor — they're all `required init`
    // members — so the generated activator wouldn't compile.
    internal MonitorInfo? Monitor { get; }
    internal WindowInfo? Window { get; }
    public string Title { get; }
    public string Subtitle { get; }

    /// <summary>The tile image. One bitmap per item for the lifetime of the picker — refreshes rewrite its pixels in place rather than replacing it, so the Image control never re-binds mid-animation.</summary>
    public WriteableBitmap Thumbnail { get; }

    /// <summary>Identity of the underlying source: an HMONITOR or an HWND. Used to preselect the tile matching the current capture target.</summary>
    public nint Handle => Monitor?.Handle ?? Window?.Handle ?? 0;

    /// <summary>False until a thumbnail has actually been captured — the tile shows a placeholder until then, rather than a frame of uninitialized black.</summary>
    [ObservableProperty] private bool _hasThumbnail;

    /// <summary>Inverse of <see cref="HasThumbnail"/>, for the tile's loading spinner. A property rather than a value converter, since x:Bind already maps bool to Visibility on its own.</summary>
    public bool IsLoadingThumbnail => !HasThumbnail;

    partial void OnHasThumbnailChanged(bool value) => OnPropertyChanged(nameof(IsLoadingThumbnail));

    /// <summary>Captures a fresh thumbnail. Call from a background thread: window capture can block briefly on the target app rendering itself.</summary>
    public bool CaptureInto(SourceThumbnailService thumbnails) => Kind == CaptureTargetKind.Monitor
        ? thumbnails.TryCaptureMonitor(Monitor!, _pixels)
        : thumbnails.TryCaptureWindow(Window!.Handle, _pixels);

    /// <summary>Pushes the pixels captured by the last <see cref="CaptureInto"/> into <see cref="Thumbnail"/>. Must run on the UI thread.</summary>
    public void PublishThumbnail()
    {
        using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(Thumbnail.PixelBuffer))
        {
            stream.Write(_pixels, 0, _pixels.Length);
        }
        Thumbnail.Invalidate();
        HasThumbnail = true;
    }
}
