using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// One option inside a <see cref="SettingsSection"/>: optional icon tile, title and description on the
/// left, the control (<see cref="ContentControl.Content"/>) right-aligned and vertically centred, and an
/// optional full-width <see cref="Details"/> area underneath for previews or secondary controls.
/// </summary>
public sealed class SettingsRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SettingsRow, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<object?> DetailsProperty =
        AvaloniaProperty.Register<SettingsRow, object?>(nameof(Details));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Details { get => GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
}
