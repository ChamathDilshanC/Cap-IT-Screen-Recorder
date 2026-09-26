using ScreenRecorderApp.Models;
namespace ScreenRecorderApp.Services.Export;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Single coordinate contract for the live player, static layers and FFmpeg.</summary>
public sealed record CompositionLayout(int Width, int Height, PixelRect Video, PixelRect Frame, double Radius)
{
    public static CompositionLayout Create(PresentationSettings settings, int sourceWidth, int sourceHeight)
    {
        var p = settings.Clone(); p.Normalize();
        sourceWidth = Math.Max(2, sourceWidth); sourceHeight = Math.Max(2, sourceHeight);
        var (cw, ch) = p.ResolveCanvas(sourceWidth, sourceHeight);
        var pad = Math.Min(p.Padding, Math.Min(cw, ch) / 2 - 2);
        var framePad = p.DeviceFrameStyle == "None" ? 0 : (int)p.FramePadding + 2;
        var title = p.DeviceFrameStyle is "None" or "Device" || !p.FrameTitleBar ? 0 : 32;
        var aw = Math.Max(2, cw - 2 * (pad + framePad));
        var ah = Math.Max(2, ch - 2 * (pad + framePad) - title);
        var fit = p.FitMode == "Original" ? 1 : Math.Min((double)aw / sourceWidth, (double)ah / sourceHeight);
        var vw = Even(Math.Min(15360, sourceWidth * fit * p.VideoScale));
        var vh = Even(Math.Min(15360, sourceHeight * fit * p.VideoScale));
        if (p.FitMode == "Fill") { vw = Even(aw * p.VideoScale); vh = Even(ah * p.VideoScale); }
        var fw = vw + framePad * 2; var fh = vh + framePad * 2 + title;
        var x = p.VideoPosition switch { "Left" => pad, "Right" => cw - pad - fw, _ => (cw - fw) / 2 };
        var y = p.VideoPosition switch { "Top" => pad, "Bottom" => ch - pad - fh, _ => (ch - fh) / 2 };
        x += (int)p.VideoOffsetX; y += (int)p.VideoOffsetY;
        return new(cw, ch, new(x + framePad, y + framePad + title, vw, vh), new(x, y, fw, fh),
            Math.Min(p.CornerRadius, Math.Min(vw, vh) / 2d));
    }
    public static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
    public static PixelRect SourceCrop(int sw, int sh, PixelRect video, ZoomRegion? zoom)
    {
        var z = PresentationSettings.Safe(zoom?.Scale ?? 1, 1, 1, 4);
        var aspect = (double)video.Width / video.Height;
        var w = Math.Min(sw / z, sh / z * aspect);
        var h = w / aspect;
        var iw = Math.Clamp(Even(w), 2, sw); var ih = Math.Clamp(Even(h), 2, sh);
        var x = (int)Math.Round((sw - iw) * PresentationSettings.Safe(zoom?.CenterX ?? .5, .5, 0, 1));
        var y = (int)Math.Round((sh - ih) * PresentationSettings.Safe(zoom?.CenterY ?? .5, .5, 0, 1));
        return new(x, y, iw, ih);
    }
    // Later starting regions win; the editor and exporter share this precedence.
    public static ZoomRegion? ActiveZoom(IEnumerable<ZoomRegion> regions, double originalSeconds) =>
        regions.Where(r => r.Enabled && r.StartSeconds <= originalSeconds && r.EndSeconds > originalSeconds)
            .OrderBy(r => r.StartSeconds).LastOrDefault();
}
