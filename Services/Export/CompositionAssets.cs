using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Export;

public sealed record CompositionAssets(CompositionLayout Layout, byte[] Background, byte[] Overlay, byte[] Mask, byte[] Text, IReadOnlyList<byte[]> TextLetters, string? Warning);

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
        using var textLayer = new Bitmap(l.Width, l.Height, PixelFormat.Format32bppArgb);
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
        var texts = p.TextOverlays.Count > 0 ? p.TextOverlays : [p.TextOverlay];
        using (var g = Graphics.FromImage(textLayer))
        {
            Setup(g);
            foreach (var text in texts.Where(t => t.IsVisible && t.Animation != "BounceLetters"))
                DrawTextOverlay(g, text, l);
        }
        var textLetters = texts.Where(t => t.IsVisible && t.Animation == "BounceLetters")
            .SelectMany(text => RenderTextLetters(text, l)).ToArray();
        ct.ThrowIfCancellationRequested();
        return new(l, Png(background), Png(overlay), Png(mask), Png(textLayer), textLetters, warning);
    }

    private static IReadOnlyList<byte[]> RenderTextLetters(PresentationTextOverlay text, CompositionLayout layout)
    {
        if (!text.IsVisible) return Array.Empty<byte[]>();
        using var measureBitmap = new Bitmap(1, 1);
        using var measureGraphics = Graphics.FromImage(measureBitmap);
        using var font = CreateFont(text);
        var widths = text.Text.Select(letter => Math.Max(1f, measureGraphics.MeasureString(letter.ToString(), font).Width)).ToArray();
        var total = widths.Sum();
        var startX = (float)(AlignedOrigin(text.X * layout.Width, total, text.HorizontalAlignment));
        var result = new List<byte[]>();
        var cursor = startX;
        for (var index = 0; index < text.Text.Length; index++)
        {
            var letter = text.Text[index];
            var width = Math.Max(1f, measureGraphics.MeasureString(letter.ToString(), font).Width);
            using var bitmap = new Bitmap(layout.Width, layout.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            Setup(graphics);
            using var brush = new SolidBrush(Color.FromArgb((int)(text.Opacity * 255), Parse(text.Color)));
            var size = graphics.MeasureString(letter.ToString(), font);
            graphics.DrawString(letter.ToString(), font, brush, cursor,
                (float)AlignedOrigin(text.Y * layout.Height, size.Height, text.VerticalAlignment));
            result.Add(Png(bitmap));
            cursor += widths[index];
        }
        return result;
    }

    private static Font CreateFont(PresentationTextOverlay text)
    {
        var style = (text.Bold ? FontStyle.Bold : FontStyle.Regular) |
                    (text.Italic ? FontStyle.Italic : FontStyle.Regular);
        try { return new Font(text.FontFamily, (float)text.FontSize, style, GraphicsUnit.Pixel); }
        catch (ArgumentException) { return new Font("Segoe UI", (float)text.FontSize, style, GraphicsUnit.Pixel); }
    }

    private static void DrawTextOverlay(Graphics g, PresentationTextOverlay text, CompositionLayout layout)
    {
        if (!text.IsVisible) return;
        try
        {
            var style = (text.Bold ? FontStyle.Bold : FontStyle.Regular) |
                        (text.Italic ? FontStyle.Italic : FontStyle.Regular);
            using var font = new Font(text.FontFamily, (float)text.FontSize, style, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb((int)(text.Opacity * 255), Parse(text.Color)));
            var size = g.MeasureString(text.Text, font, layout.Width);
            using var format = new StringFormat
            {
                Alignment = ToStringAlignment(text.HorizontalAlignment),
                LineAlignment = ToStringAlignment(text.VerticalAlignment),
                FormatFlags = StringFormatFlags.LineLimit
            };
            var bounds = new RectangleF(
                (float)AlignedOrigin(text.X * layout.Width, size.Width, text.HorizontalAlignment),
                (float)AlignedOrigin(text.Y * layout.Height, size.Height, text.VerticalAlignment),
                size.Width, size.Height);
            g.DrawString(text.Text, font, brush,
                bounds, format);
        }
        catch (ArgumentException)
        {
            using var font = new Font("Segoe UI", (float)text.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb((int)(text.Opacity * 255), Parse(text.Color)));
            var size = g.MeasureString(text.Text, font, layout.Width);
            using var format = new StringFormat
            {
                Alignment = ToStringAlignment(text.HorizontalAlignment),
                LineAlignment = ToStringAlignment(text.VerticalAlignment),
                FormatFlags = StringFormatFlags.LineLimit
            };
            g.DrawString(text.Text, font, brush,
                new RectangleF(
                    (float)AlignedOrigin(text.X * layout.Width, size.Width, text.HorizontalAlignment),
                    (float)AlignedOrigin(text.Y * layout.Height, size.Height, text.VerticalAlignment),
                    size.Width, size.Height),
                format);
        }
    }

    private static double AlignedOrigin(double anchor, double size, string alignment) =>
        alignment switch
        {
            "Left" or "Top" => anchor,
            "Right" or "Bottom" => anchor - size,
            _ => anchor - size / 2
        };

    private static StringAlignment ToStringAlignment(string alignment) =>
        alignment switch
        {
            "Left" or "Top" => StringAlignment.Near,
            "Right" or "Bottom" => StringAlignment.Far,
            _ => StringAlignment.Center
        };

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
        if (p.DeviceFrameStyle == "Browser")
        {
            DrawBrowserChrome(g, new RectangleF(f.X, f.Y, f.Width, f.Height), light, p.FrameControls);
            return;
        }
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
    }

    private static void DrawBrowserChrome(Graphics g, RectangleF frame, bool light, bool controls)
    {
        var chrome = light ? Color.FromArgb(238, 239, 242) : Color.FromArgb(31, 34, 42);
        var toolbar = light ? Color.FromArgb(248, 249, 250) : Color.FromArgb(24, 27, 34);
        using var chromeBrush = new SolidBrush(chrome);
        using var toolbarBrush = new SolidBrush(toolbar);
        g.FillRectangle(chromeBrush, frame.X, frame.Y, frame.Width, 32);
        g.FillRectangle(toolbarBrush, frame.X, frame.Y + 32, frame.Width, 34);

        if (controls)
        {
            var y = frame.Y + 16;
            var colors = new[] { Color.FromArgb(255, 255, 95, 86), Color.FromArgb(255, 255, 189, 46), Color.FromArgb(255, 39, 201, 63) };
            for (var i = 0; i < colors.Length; i++)
            {
                using var dot = new SolidBrush(colors[i]);
                g.FillEllipse(dot, frame.X + 14 + i * 16, y - 5, 10, 10);
            }
        }

        using var muted = new SolidBrush(light ? Color.FromArgb(95, 99, 108) : Color.FromArgb(164, 170, 182));
        using var line = new Pen(muted, 1.4f);
        var navY = frame.Y + 49;
        var navLeft = frame.X + 18;
        g.DrawLine(line, navLeft + 7, navY - 5, navLeft, navY);
        g.DrawLine(line, navLeft, navY, navLeft + 7, navY + 5);
        g.DrawLine(line, navLeft + 21, navY - 5, navLeft + 28, navY);
        g.DrawLine(line, navLeft + 28, navY, navLeft + 21, navY + 5);

        var address = new RectangleF(frame.X + 92, frame.Y + 39, Math.Max(80, frame.Width - 184), 20);
        using var addressBrush = new SolidBrush(light ? Color.FromArgb(224, 226, 230) : Color.FromArgb(49, 53, 63));
        using var addressPath = Rounded(address, 6);
        g.FillPath(addressBrush, addressPath);
        using var addressText = new SolidBrush(light ? Color.FromArgb(75, 79, 88) : Color.FromArgb(196, 201, 211));
        using var font = new Font("Segoe UI", Math.Max(8, frame.Height / 90f), FontStyle.Regular, GraphicsUnit.Pixel);
        var label = "example.com";
        g.DrawString(label, font, addressText, address.X + 10, address.Y + 4);

        var right = frame.Right - 22;
        g.DrawEllipse(line, right - 42, navY - 5, 10, 10);
        g.DrawLine(line, right - 37, navY, right - 32, navY);
        g.DrawLine(line, right - 32, navY, right - 34, navY - 3);
        g.DrawLine(line, right - 32, navY, right - 32, navY + 3);
        g.DrawEllipse(line, right - 14, navY - 5, 10, 10);
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
