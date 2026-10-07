using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Displays a bitmap whose pixels are rewritten in place (live capture preview, webcam, editor video).
/// <see cref="Image"/> cannot know a <see cref="WriteableBitmap"/> changed; bumping <see cref="Version"/>
/// tells this control to redraw — render only, no measure/arrange — so a 30 fps preview never triggers
/// layout passes.
/// </summary>
public sealed class FrameView : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<FrameView, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<long> VersionProperty =
        AvaloniaProperty.Register<FrameView, long>(nameof(Version));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<FrameView, Stretch>(nameof(Stretch), Stretch.Uniform);

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<FrameView, CornerRadius>(nameof(CornerRadius));

    /// <summary>Optional normalized source crop (0..1) — used for zoom regions in the editor.</summary>
    public static readonly StyledProperty<Rect> SourceCropProperty =
        AvaloniaProperty.Register<FrameView, Rect>(nameof(SourceCrop), new Rect(0, 0, 1, 1));

    static FrameView()
    {
        AffectsRender<FrameView>(SourceProperty, VersionProperty, StretchProperty, CornerRadiusProperty, SourceCropProperty);
        AffectsMeasure<FrameView>(SourceProperty, StretchProperty);
    }

    public FrameView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.MediumQuality);
    }

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public long Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Rect SourceCrop { get => GetValue(SourceCropProperty); set => SetValue(SourceCropProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Source is null) return default;
        var size = Source.Size;
        if (double.IsInfinity(availableSize.Width) && double.IsInfinity(availableSize.Height)) return size;
        return Stretch.CalculateSize(availableSize, size);
    }

    public override void Render(DrawingContext context)
    {
        var source = Source;
        if (source is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var pixel = source.Size;
        var crop = SourceCrop;
        var sourceRect = new Rect(pixel.Width * crop.X, pixel.Height * crop.Y, pixel.Width * crop.Width, pixel.Height * crop.Height);
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0) return;

        var viewport = new Rect(Bounds.Size);
        var scale = Stretch.CalculateScaling(viewport.Size, sourceRect.Size);
        var destSize = sourceRect.Size * scale;
        var dest = new Rect((viewport.Width - destSize.Width) / 2, (viewport.Height - destSize.Height) / 2, destSize.Width, destSize.Height);

        // UniformToFill: trim the source instead of drawing outside the control.
        if (Stretch == Stretch.UniformToFill)
        {
            var visible = dest.Intersect(viewport);
            var sx = sourceRect.Width / dest.Width; var sy = sourceRect.Height / dest.Height;
            sourceRect = new Rect(sourceRect.X + (visible.X - dest.X) * sx, sourceRect.Y + (visible.Y - dest.Y) * sy, visible.Width * sx, visible.Height * sy);
            dest = visible;
        }

        if (CornerRadius != default)
        {
            using (context.PushClip(new RoundedRect(dest, CornerRadius)))
                context.DrawImage(source, sourceRect, dest);
        }
        else
        {
            context.DrawImage(source, sourceRect, dest);
        }
    }
}
