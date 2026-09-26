using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Export;

public sealed record CompositionAssets(CompositionLayout Layout, byte[] Background, byte[] Overlay, byte[] Mask, string? Warning);

/// <summary>Renders static artwork once per edit, never per video frame. Preview and export use identical pixels.</summary>
public static class CompositionAssetRenderer
{
    public static CompositionAssets Render(PresentationSettings settings, int sourceWidth, int sourceHeight, CancellationToken ct = default)
    {
        var p = settings.Clone(); p.Normalize();
        var l = CompositionLayout.Create(p, sourceWidth, sourceHeight);
        string? warning = null;
        using var background = new Bitmap(l.Width, l.Height, PixelFormat.Format32bppArgb);
        using var overlay = new Bitmap(l.Width, l.Height, PixelFormat.Format32bppArgb);
        using var mask = new Bitmap(l.Video.Width, l.Video.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(background))
        {
            Setup(g); g.Clear(Parse(p.BackgroundColor));
            if (p.BackgroundMode == "none") g.Clear(Color.Black); // MP4 has no alpha channel.
            if (p.BackgroundMode == "gradient")
            {
                using var brush = new LinearGradientBrush(new Rectangle(0, 0, l.Width, l.Height), Parse(p.BackgroundColor),
                    Parse(p.BackgroundColor2), (float)p.GradientAngle);
                g.FillRectangle(brush, 0, 0, l.Width, l.Height);
            }
            if (p.BackgroundMode == "image")
            {
                try
                {
                    using var img = Image.FromFile(p.BackgroundPath);
                    DrawImage(g, img, l.Width, l.Height, p.BackgroundImageFit);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
                { warning = "Background image unavailable. The canvas colour is used in preview and export. Choose a replacement image."; }
            }
        }
        ct.ThrowIfCancellationRequested();
        if (p.BackgroundMode == "image" && p.BackgroundBlur > 0) Blur(background, (int)p.BackgroundBlur, ct);
        using (var g = Graphics.FromImage(background))
        {
            Setup(g);
            if (p.BackgroundDim > 0 && p.BackgroundMode == "image")
            {
                using var dim = new SolidBrush(Color.FromArgb((int)(p.BackgroundDim * 255), Color.Black));
                g.FillRectangle(dim, 0, 0, l.Width, l.Height);
            }
        }
        if (p.Shadow && p.ShadowOpacity > 0)
        {
            using var shadow = new Bitmap(l.Width, l.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(shadow))
            {
                Setup(g);
                var r = l.Frame;
                using var path = Rounded(new RectangleF(r.X + (float)p.ShadowOffsetX, r.Y + (float)p.ShadowOffsetY, r.Width, r.Height), l.Radius);
                using var brush = new SolidBrush(Color.FromArgb((int)(p.ShadowOpacity * 255), Color.Black));
                g.FillPath(brush, path);
            }
            Blur(shadow, (int)p.ShadowBlur, ct);
            using var target = Graphics.FromImage(background);
            target.DrawImageUnscaled(shadow, 0, 0);
        }
        ct.ThrowIfCancellationRequested();
        using (var g = Graphics.FromImage(background))
        {
            Setup(g);
            if (p.DeviceFrameStyle != "None") DrawFrame(g, p, l);
        }
        using (var g = Graphics.FromImage(mask))
        {
            Setup(g); g.Clear(Color.Black);
            using var path = Rounded(new RectangleF(0, 0, l.Video.Width, l.Video.Height), l.Radius);
            g.FillPath(Brushes.White, path);
        }
        using (var g = Graphics.FromImage(overlay))
        {
            Setup(g);
            if (p.BorderEnabled && p.BorderWidth > 0)
            {
                var v = l.Video; var inset = (float)p.BorderWidth / 2;
                using var path = Rounded(new RectangleF(v.X + inset, v.Y + inset, Math.Max(1, v.Width - 2 * inset), Math.Max(1, v.Height - 2 * inset)), Math.Max(0, l.Radius - inset));
                using var pen = new Pen(Color.FromArgb((int)(p.BorderOpacity * 255), Parse(p.BorderColor)), (float)p.BorderWidth);
                g.DrawPath(pen, path);
            }
            if (p.WatermarkEnabled)
            {
                try
                {
                    using var img = Image.FromFile(p.WatermarkPath);
                    var w = Math.Max(1, (int)(l.Width * p.WatermarkScale));
                    var h = Math.Max(1, (int)((double)w * img.Height / img.Width));
                    var x = p.WatermarkPosition.Contains("Left") ? p.WatermarkMargin : p.WatermarkPosition.Contains("Right") ? l.Width - w - p.WatermarkMargin : (l.Width - w) / 2d;
                    var y = p.WatermarkPosition.Contains("Top") ? p.WatermarkMargin : p.WatermarkPosition.Contains("Bottom") ? l.Height - h - p.WatermarkMargin : (l.Height - h) / 2d;
                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)p.WatermarkOpacity });
                    g.DrawImage(img, new Rectangle((int)(x + p.WatermarkOffsetX), (int)(y + p.WatermarkOffsetY), w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attributes);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
                { warning = "Watermark image unavailable. It is omitted from preview and export. Choose a replacement image."; }
            }
        }
        ct.ThrowIfCancellationRequested();
        return new(l, Png(background), Png(overlay), Png(mask), warning);
    }

    private static void DrawFrame(Graphics g, PresentationSettings p, CompositionLayout l)
    {
        var f = l.Frame; var light = p.FrameTheme == "Light";
        var fill = light ? Color.FromArgb(240, 241, 245) : Color.FromArgb(36, 39, 47);
        using var brush = new SolidBrush(fill);
        using var outline = new Pen(light ? Color.FromArgb(175, 180, 190) : Color.FromArgb(80, 85, 100), 1);
        using var path = Rounded(new RectangleF(f.X, f.Y, f.Width, f.Height), l.Radius + p.FramePadding);
        g.FillPath(brush, path); g.DrawPath(outline, path);
        if (!p.FrameTitleBar || p.DeviceFrameStyle == "Device") return;
        using var ink = new SolidBrush(light ? Color.FromArgb(100, 104, 118) : Color.FromArgb(180, 187, 202));
        var yy = f.Y + 16;
        if (p.FrameControls)
        {
            if (p.DeviceFrameStyle == "Windows")
            {
                using var pen = new Pen(ink, 1.2f);
                g.DrawLine(pen, f.X + f.Width - 74, yy, f.X + f.Width - 66, yy);
                g.DrawRectangle(pen, f.X + f.Width - 49, yy - 4, 8, 8);
                g.DrawLine(pen, f.X + f.Width - 24, yy - 4, f.X + f.Width - 16, yy + 4);
                g.DrawLine(pen, f.X + f.Width - 24, yy + 4, f.X + f.Width - 16, yy - 4);
            }
            else if (p.DeviceFrameStyle == "Minimal") g.FillEllipse(ink, f.X + f.Width - 24, yy - 3, 6, 6);
            else for (var i = 0; i < 3; i++) g.FillEllipse(ink, f.X + 14 + i * 16, yy - 4, 8, 8);
        }
        if (p.DeviceFrameStyle == "Browser" && f.Width > 180)
        {
            using var address = Rounded(new RectangleF(f.X + 80, f.Y + 8, Math.Max(20, f.Width - 160), 17), 5);
            using var muted = new SolidBrush(light ? Color.White : Color.FromArgb(54, 59, 70));
            g.FillPath(muted, address);
        }
    }

    private static void DrawImage(Graphics g, Image image, int w, int h, string fit)
    {
        if (fit == "Stretch") { g.DrawImage(image, 0, 0, w, h); return; }
        var scale = fit == "Fit" ? Math.Min((double)w / image.Width, (double)h / image.Height) : Math.Max((double)w / image.Width, (double)h / image.Height);
        var iw = (float)(image.Width * scale); var ih = (float)(image.Height * scale);
        g.DrawImage(image, (w - iw) / 2, (h - ih) / 2, iw, ih);
    }
    private static void Setup(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }
    private static Color Parse(string hex) => ColorTranslator.FromHtml(hex);
    private static byte[] Png(Bitmap bitmap) { using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
    private static GraphicsPath Rounded(RectangleF r, double radius)
    {
        var path = new GraphicsPath();
        var d = (float)Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    // Three linear-time box passes approximate a Gaussian, including alpha. Identical in both consumers.
    private static void Blur(Bitmap bitmap, int radius, CancellationToken ct)
    {
        if (radius < 1) return;
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var w = bitmap.Width; var h = bitmap.Height;
            var a = new byte[data.Stride * h]; var b = new byte[a.Length];
            Marshal.Copy(data.Scan0, a, 0, a.Length);
            var r = Math.Max(1, radius / 2); var n = r * 2 + 1;
            for (var pass = 0; pass < 3; pass++)
            {
                ct.ThrowIfCancellationRequested();
                for (var y = 0; y < h; y++) for (var c = 0; c < 4; c++)
                {
                    var sum = 0; var row = y * data.Stride;
                    for (var k = -r; k <= r; k++) sum += a[row + Math.Clamp(k, 0, w - 1) * 4 + c];
                    for (var x = 0; x < w; x++)
                    {
                        b[row + x * 4 + c] = (byte)(sum / n);
                        sum += a[row + Math.Clamp(x + r + 1, 0, w - 1) * 4 + c] - a[row + Math.Clamp(x - r, 0, w - 1) * 4 + c];
                    }
                }
                for (var x = 0; x < w; x++) for (var c = 0; c < 4; c++)
                {
                    var sum = 0; var col = x * 4 + c;
                    for (var k = -r; k <= r; k++) sum += b[Math.Clamp(k, 0, h - 1) * data.Stride + col];
                    for (var y = 0; y < h; y++)
                    {
                        a[y * data.Stride + col] = (byte)(sum / n);
                        sum += b[Math.Clamp(y + r + 1, 0, h - 1) * data.Stride + col] - b[Math.Clamp(y - r, 0, h - 1) * data.Stride + col];
                    }
                }
            }
            Marshal.Copy(a, 0, data.Scan0, a.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
