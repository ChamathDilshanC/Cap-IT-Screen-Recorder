using Avalonia;
using Avalonia.Media;

namespace ScreenRecorderApp.Services;

/// <summary>Typed access to application resources from view models (icons for dialogs, navigation).</summary>
public static class AppResources
{
    public static Geometry? Icon(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value as Geometry : null;
}
