using Avalonia;
using Avalonia.Media;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Attached properties read by the Cap-IT control themes, so markup can say
/// <c>&lt;Button ui:Ui.Icon="{StaticResource Icon.Refresh}" Content="Refresh" /&gt;</c> instead of
/// hand-building an icon + label stack for every button.
/// </summary>
public static class Ui
{
    /// <summary>Leading icon geometry for buttons, toggle buttons and navigation items.</summary>
    public static readonly AttachedProperty<Geometry?> IconProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, Geometry?>("Icon", typeof(Ui));

    /// <summary>Draws <see cref="IconProperty"/> filled instead of stroked.</summary>
    public static readonly AttachedProperty<bool> IconFilledProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, bool>("IconFilled", typeof(Ui));

    public static Geometry? GetIcon(AvaloniaObject element) => element.GetValue(IconProperty);
    public static void SetIcon(AvaloniaObject element, Geometry? value) => element.SetValue(IconProperty, value);
    public static bool GetIconFilled(AvaloniaObject element) => element.GetValue(IconFilledProperty);
    public static void SetIconFilled(AvaloniaObject element, bool value) => element.SetValue(IconFilledProperty, value);
}
