using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace RecordingChecks;

/// <summary>
/// A static, full-screen sharpness target: small text in several sizes, white-on-dark and coloured text,
/// rotated edges, hairline grids and a ring. The same layout is rendered natively at 1x (what the screen —
/// and so the recorder — sees) and natively at 2x. The 2x render is the ground truth for what a 2x Smart
/// Tracking zoom of this 1x content would ideally look like, which is what the offline analysis scores the
/// recorded zoom against.
/// </summary>
internal static class PatternRenderer
{
    private static readonly string[] Words =
    {
        "Cap-IT", "Screen", "Recorder", "Smart", "Tracking", "zoom", "sharp", "text", "edges", "pixels",
        "Quality", "1080p", "H.264", "frame", "caret", "{ } [ ]", "0123456789", "illegal", "WMwm", "fi fl",
    };

    /// <summary>Renders the layout at <paramref name="scale"/> times the 1x size. Every word starts at a fixed grid position so that scale changes cannot accumulate positional drift along a line.</summary>
    public static Bitmap Render(int width1x, int height1x, float scale)
    {
        var bmp = new Bitmap((int)Math.Round(width1x * scale), (int)Math.Round(height1x * scale), PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.ScaleTransform(scale, scale);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Text blocks: sizes in pixels at 1x (what UI text on a 100 % display looks like: 12–16 px).
        float[] sizes = { 12f, 14f, 16f, 20f };
        int cellW = 120, y = 24;
        int rowIndex = 0;
        while (y < height1x - 40)
        {
            float size = sizes[rowIndex % sizes.Length];
            bool dark = rowIndex % 7 == 5;
            bool blue = rowIndex % 7 == 3;
            if (dark)
            {
                using var bg = new SolidBrush(Color.FromArgb(24, 26, 32));
                g.FillRectangle(bg, 0, y - 4, width1x, (int)(size * 1.6f) + 2);
            }
            using var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(dark ? Color.FromArgb(235, 238, 245) : blue ? Color.FromArgb(0, 80, 200) : Color.Black);
            for (int col = 0; col * cellW < width1x - cellW; col++)
            {
                // Pseudo-random but fixed word choice: a periodic layout would let the offline registration lock
                // onto a neighbouring repeat instead of the true crop.
                var rnd = new Random(rowIndex * 7919 + col * 104729 + 17);
                string w = Words[rnd.Next(Words.Length)] + (rnd.Next(3) == 0 ? rnd.Next(10, 99).ToString() : "");
                g.DrawString(w, font, brush, col * cellW + 10, y, StringFormat.GenericTypographic);
            }
            y += (int)(size * 1.6f) + 6;
            rowIndex++;
        }

        // Rotated black squares: slanted edges with exact vector ground truth.
        foreach (var (cx, cy, angle) in new[] { (700, 380, 5f), (900, 380, -7f), (1100, 380, 12f), (1300, 380, 3f), (300, 700, 5f), (1650, 800, 12f) })
        {
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            g.FillRectangle(Brushes.Black, -60, -60, 120, 120);
            g.Restore(state);
        }

        // Hairline grids: 1 px and 2 px lines, 8 px apart.
        using (var pen1 = new Pen(Color.Black, 1f))
        using (var pen2 = new Pen(Color.Black, 2f))
        {
            g.SmoothingMode = SmoothingMode.None;
            for (int i = 0; i < 12; i++) g.DrawLine(pen1, 560 + i * 8, 600, 560 + i * 8, 700);
            for (int i = 0; i < 12; i++) g.DrawLine(pen1, 560, 600 + i * 8, 660, 600 + i * 8);
            for (int i = 0; i < 8; i++) g.DrawLine(pen2, 1100 + i * 10, 640, 1100 + i * 10, 740);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawEllipse(pen2, 1250, 640, 120, 120);
            g.DrawEllipse(pen1, 1280, 670, 60, 60);
        }
        return bmp;
    }
}

internal sealed class PatternForm : Form
{
    private readonly Bitmap _shown;
    public volatile bool AbortRequested;

    public PatternForm(Rectangle bounds, string? saveDir)
    {
        Text = "CapIT RecordingChecks sharpness target";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.White;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) AbortRequested = true; };

        _shown = PatternRenderer.Render(bounds.Width, bounds.Height, 1f);
        if (saveDir is not null)
        {
            Directory.CreateDirectory(saveDir);
            _shown.Save(Path.Combine(saveDir, "pattern-1x.png"), ImageFormat.Png);
            foreach (var scale in new[] { 1.25f, 1.5f, 1.75f, 2f, 3f })
            {
                using var big = PatternRenderer.Render(bounds.Width, bounds.Height, scale);
                big.Save(Path.Combine(saveDir, $"pattern-{scale:0.#}x.png"), ImageFormat.Png);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImageUnscaled(_shown, 0, 0);
    }
}
