using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenRecorderApp.ViewModels;

public enum PageKey { Home, Capture, Tracking, Webcam, Annotations, Effects, Audio, Recordings, Settings }

/// <summary>A sidebar destination.</summary>
public sealed partial class NavigationItem(PageKey key, string title, Geometry? icon, string? shortcut = null) : ObservableObject
{
    public PageKey Key { get; } = key;
    public string Title { get; } = title;
    public Geometry? Icon { get; } = icon;
    public string ToolTip { get; } = shortcut is null ? title : $"{title}  ·  {shortcut}";

    [ObservableProperty] private bool _isSelected;
}
