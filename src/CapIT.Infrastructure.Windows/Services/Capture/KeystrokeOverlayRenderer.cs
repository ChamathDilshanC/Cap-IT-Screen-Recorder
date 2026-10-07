using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Keeps a short rolling history of recent keystrokes (from <see cref="Tracking.GlobalKeyboardHook"/>)
/// and rasterizes them into a small BGRA "toast" bitmap for <see cref="VideoCaptureService"/> to blend
/// onto captured frames. The bitmap is only re-rendered when the visible text actually changes, not on
/// every frame — expiry (checked on every <see cref="TryGetOverlay"/> call) still drives per-frame removal
/// of stale entries even when nothing new was typed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeystrokeOverlayRenderer
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(2.5);
    private const int MaxEntries = 6;

    private readonly object _lock = new();
    private readonly List<(string Text, DateTime ExpiresAt)> _entries = [];

    private byte[]? _cachedBgra;
    private int _cachedWidth;
    private int _cachedHeight;
    private string _lastRenderedText = "";

    public void OnKeyPressed(string display)
    {
        lock (_lock)
        {
            _entries.Add((display, DateTime.UtcNow + HoldDuration));
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }
    }

    /// <summary>Returns the current overlay bitmap (BGRA, straight alpha) if there's anything to show right now.</summary>
    public bool TryGetOverlay(out byte[] bgra, out int width, out int height)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _entries.RemoveAll(e => e.ExpiresAt <= now);

            if (_entries.Count == 0)
            {
                bgra = [];
                width = 0;
                height = 0;
                return false;
            }

            var text = string.Join("   ", _entries.Select(e => e.Text));
            if (text != _lastRenderedText || _cachedBgra is null)
            {
                Render(text);
                _lastRenderedText = text;
            }

            bgra = _cachedBgra!;
            width = _cachedWidth;
            height = _cachedHeight;
            return true;
        }
    }

    private void Render(string text)
    {
        using var font = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Pixel);

        SizeF measured;
        using (var scratch = new Bitmap(1, 1))
        using (var scratchG = Graphics.FromImage(scratch))
        {
            measured = scratchG.MeasureString(text, font);
        }

        const int padX = 18, padY = 12;
        int w = Math.Max(1, (int)Math.Ceiling(measured.Width) + padX * 2);
        int h = Math.Max(1, (int)Math.Ceiling(measured.Height) + padY * 2);

        using var target = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(target))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var backdropPath = RoundedRect(new Rectangle(0, 0, w, h), 14);
            using var backdropBrush = new SolidBrush(Color.FromArgb(170, 18, 18, 22));
            g.FillPath(backdropBrush, backdropPath);

            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(text, font, textBrush, padX, padY);
        }

        _cachedBgra = BitmapToBgra(target, w, h);
        _cachedWidth = w;
        _cachedHeight = h;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static byte[] BitmapToBgra(Bitmap bitmap, int width, int height)
    {
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new byte[width * height * 4];
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    IntPtr.Add(data.Scan0, y * data.Stride), result, y * rowBytes, rowBytes);
            }
            return result;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
