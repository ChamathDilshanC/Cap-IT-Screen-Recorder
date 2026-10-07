using Avalonia;
using Avalonia.Controls.Primitives;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Small status pill (dot + label). Tone is chosen with a style class: <c>success</c>, <c>warning</c>,
/// <c>error</c>, <c>info</c>, <c>accent</c>, <c>recording</c> (pulsing dot) or none for neutral.
/// </summary>
public sealed class StatusBadge : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(Text));

    public static readonly StyledProperty<bool> ShowDotProperty =
        AvaloniaProperty.Register<StatusBadge, bool>(nameof(ShowDot), true);

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool ShowDot { get => GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }
}
