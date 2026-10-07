using Avalonia;
using Avalonia.Controls.Primitives;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// A titled group of related options: section heading (with optional description and header actions)
/// above a single card whose rows are separated by hairlines. Rows are usually <see cref="SettingsRow"/>s.
/// </summary>
public sealed class SettingsSection : HeaderedContentControl
{
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsSection, string?>(nameof(Description));

    public static readonly StyledProperty<object?> HeaderActionsProperty =
        AvaloniaProperty.Register<SettingsSection, object?>(nameof(HeaderActions));

    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object? HeaderActions { get => GetValue(HeaderActionsProperty); set => SetValue(HeaderActionsProperty, value); }
}
