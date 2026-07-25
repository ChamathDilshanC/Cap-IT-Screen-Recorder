using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>A small pre-rendered BGRA overlay blitted onto captured frames at the live cursor position.</summary>
public readonly record struct CursorIconBitmap(byte[] Bgra, int Width, int Height, int HotspotX, int HotspotY);

/// <summary>
/// Renders a handful of self-contained cursor overlay styles (no external image assets, generated once
/// and cached) — these replace the real OS pointer with a simple, deliberately stylized marker, since
/// DXGI Desktop Duplication only reports cursor position/visibility and never composites the actual OS
/// pointer bitmap into the captured frame.
/// </summary>
public static class CursorIcons
{
    private static readonly Dictionary<CursorStyle, CursorIconBitmap> Cache = new();

    public static CursorIconBitmap Get(CursorStyle style)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(style, out var cached)) return cached;

            var built = style switch
            {
                CursorStyle.Arrow => BuildArrow(),
                CursorStyle.CircleHighlight => BuildCircleHighlight(),
                CursorStyle.Dot => BuildDot(),
                CursorStyle.Crosshair => BuildCrosshair(),
                _ => BuildArrow(),
            };
            Cache[style] = built;
            return built;
        }
    }

    private static CursorIconBitmap BuildArrow()
    {
        const int w = 20, h = 28;
        var bgra = new byte[w * h * 4];

        // A concave 7-point silhouette (tip, down the left edge, in to a notch, out to the tail flag,
        // back to the notch, out to the body's right corner) — the same basic outline shape as a
        // standard arrow pointer, not just a plain triangle.
        (float X, float Y)[] outer =
        [
            (1, 1), (1, 22), (6, 17), (9, 26), (12, 24), (8, 15), (15, 15),
        ];
        var inner = ScaleTowardCentroid(outer, 0.78f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!PointInPolygon(x + 0.5f, y + 0.5f, outer)) continue;

                int i = (y * w + x) * 4;
                if (PointInPolygon(x + 0.5f, y + 0.5f, inner))
                {
                    bgra[i + 0] = 255; bgra[i + 1] = 255; bgra[i + 2] = 255; bgra[i + 3] = 255;
                }
                else
                {
                    bgra[i + 0] = 0; bgra[i + 1] = 0; bgra[i + 2] = 0; bgra[i + 3] = 255;
                }
            }
        }

        return new CursorIconBitmap(bgra, w, h, 1, 1);
    }

    private static (float X, float Y)[] ScaleTowardCentroid((float X, float Y)[] poly, float factor)
    {
        float cx = poly.Average(p => p.X), cy = poly.Average(p => p.Y);
        var scaled = new (float X, float Y)[poly.Length];
        for (int i = 0; i < poly.Length; i++)
        {
            scaled[i] = (cx + (poly[i].X - cx) * factor, cy + (poly[i].Y - cy) * factor);
        }
        return scaled;
    }

    private static bool PointInPolygon(float px, float py, (float X, float Y)[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            float xi = poly[i].X, yi = poly[i].Y;
            float xj = poly[j].X, yj = poly[j].Y;
            bool crosses = yi > py != yj > py && px < (xj - xi) * (py - yi) / (yj - yi) + xi;
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private static CursorIconBitmap BuildCircleHighlight()
    {
        const int size = 44;
        const float c = size / 2f, outerR = 17f, innerR = 12f;
        var bgra = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > outerR || dist < innerR) continue;

                // Soft 1.5px feather on both edges of the ring so it doesn't look jagged.
                float edge = MathF.Min(outerR - dist, dist - innerR);
                byte alpha = (byte)(200 * Math.Clamp(edge / 1.5f, 0f, 1f));

                int i = (y * size + x) * 4;
                bgra[i + 0] = 0; bgra[i + 1] = 200; bgra[i + 2] = 255; // BGR amber/gold
                bgra[i + 3] = alpha;
            }
        }

        return new CursorIconBitmap(bgra, size, size, size / 2, size / 2);
    }

    private static CursorIconBitmap BuildDot()
    {
        const int size = 20;
        const float c = size / 2f, r = 6f;
        var bgra = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > r) continue;

                byte alpha = (byte)(230 * Math.Clamp((r - dist) / 1.2f, 0f, 1f));
                int i = (y * size + x) * 4;
                bgra[i + 0] = 0; bgra[i + 1] = 60; bgra[i + 2] = 255; // BGR red-orange
                bgra[i + 3] = alpha;
            }
        }

        return new CursorIconBitmap(bgra, size, size, size / 2, size / 2);
    }

    private static CursorIconBitmap BuildCrosshair()
    {
        const int size = 32;
        const float c = size / 2f, halfLen = 13f, gap = 4f, thickness = 1.4f;
        var bgra = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                bool onHorizontal = MathF.Abs(dy) <= thickness && MathF.Abs(dx) >= gap && MathF.Abs(dx) <= halfLen;
                bool onVertical = MathF.Abs(dx) <= thickness && MathF.Abs(dy) >= gap && MathF.Abs(dy) <= halfLen;
                if (!onHorizontal && !onVertical) continue;

                int i = (y * size + x) * 4;
                bgra[i + 0] = 0; bgra[i + 1] = 0; bgra[i + 2] = 255; // solid red
                bgra[i + 3] = 235;
            }
        }

        return new CursorIconBitmap(bgra, size, size, size / 2, size / 2);
    }
}
