namespace ScreenRecorderApp.Models;

/// <summary>
/// Cursor presentation settings retained with a recording. The capture pipeline currently rasterizes
/// the cursor into each frame, so the advanced values are also preserved as export metadata for future
/// re-rendering and for editors that understand the Cap-IT manifest.
/// </summary>
public sealed class CursorSettings
{
    public bool Enabled { get; set; } = true;
    public CursorStyle Style { get; set; } = CursorStyle.SystemDefault;
    public double Size { get; set; } = 1;
    public double Smoothing { get; set; } = 0.5;
    public bool HideWhenIdle { get; set; }
    public bool ClickEmphasis { get; set; }
    public bool Trail { get; set; }

    public CursorSettings Clone() => new()
    {
        Enabled = Enabled, Style = Style, Size = Size, Smoothing = Smoothing,
        HideWhenIdle = HideWhenIdle, ClickEmphasis = ClickEmphasis, Trail = Trail
    };
}
