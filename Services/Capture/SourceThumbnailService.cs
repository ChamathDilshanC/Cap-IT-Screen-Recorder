using System.Runtime.InteropServices;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture.Interop;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Grabs small BGRA thumbnails of displays and individual windows, for the visual "choose what to
/// record" picker. Polled on a timer while that picker is open, which is what makes its tiles live
/// rather than static icons.
/// </summary>
/// <remarks>
/// Deliberately GDI, not the DXGI/WGC engine <see cref="VideoCaptureService"/> uses. Standing up a
/// duplication output or a WGC frame pool per tile — a dozen of them at once, torn down again when the
/// picker closes — would be far heavier than the once-a-second still each tile actually needs, and
/// DXGI only allows one duplication of a given output at a time anyway, so it would fight with the live
/// preview already running behind the dialog.
///
/// Windows are captured with PrintWindow(PW_RENDERFULLCONTENT) rather than blitted off the screen,
/// specifically because the interesting case for a window picker is the window you *can't* see — one
/// buried behind others. PrintWindow asks the window to render itself, so a fully occluded window still
/// produces a correct thumbnail; a screen blit would show whatever is covering it. The screen blit is
/// kept only as a fallback for the handful of windows that refuse to render themselves.
///
/// Everything is scaled by GDI directly into one reused destination DIB, so no full-resolution managed
/// bitmap is ever allocated — a refresh pass over a dozen sources allocates nothing on the GC heap
/// beyond the caller's own destination buffers.
/// </remarks>
public sealed class SourceThumbnailService : IDisposable
{
    public const int ThumbWidth = 320;
    public const int ThumbHeight = 180;
    public const int ThumbByteSize = ThumbWidth * ThumbHeight * 4;

    /// <summary>Letterbox/background color behind a thumbnail that doesn't match the tile's aspect ratio.</summary>
    private const int BackgroundColorRef = 0x00141014; // COLORREF is 0x00BBGGRR

    // Serializes captures against Dispose. The picker runs its capture pass on a thread-pool thread and
    // disposes this from the UI thread the instant the dialog closes — without this, a Dispose landing
    // mid-capture would free the DIB while CopyThumbnailTo is still reading its bits, which is an
    // access violation rather than a caught exception. Captures are also serialized against each other,
    // which costs nothing: the picker only ever runs one pass at a time.
    private readonly object _gate = new();

    private nint _thumbDc;
    private nint _thumbBitmap;
    private nint _thumbBits;
    private nint _previousThumbBitmap;
    private nint _backgroundBrush;
    private bool _disposed;

    /// <summary>Captures <paramref name="monitor"/> scaled into <paramref name="destination"/> (BGRA, <see cref="ThumbByteSize"/> bytes). Returns false if the capture failed.</summary>
    public bool TryCaptureMonitor(MonitorInfo monitor, byte[] destination)
    {
        lock (_gate) return CaptureMonitorLocked(monitor, destination);
    }

    private bool CaptureMonitorLocked(MonitorInfo monitor, byte[] destination)
    {
        if (_disposed) return false;
        if (monitor.Width <= 0 || monitor.Height <= 0) return false;

        var screenDc = Gdi.GetDC(nint.Zero);
        if (screenDc == nint.Zero) return false;

        try
        {
            if (!EnsureThumbnailSurface()) return false;
            ClearThumbnailSurface();

            var (dx, dy, dw, dh) = Fit(monitor.Width, monitor.Height);
            Gdi.SetStretchBltMode(_thumbDc, Gdi.Halftone);
            Gdi.SetBrushOrgEx(_thumbDc, 0, 0, nint.Zero);

            // CAPTUREBLT so layered/transparent windows (and anything else DWM composites separately)
            // are included, rather than showing up as holes in the thumbnail.
            if (!Gdi.StretchBlt(_thumbDc, dx, dy, dw, dh, screenDc, monitor.X, monitor.Y, monitor.Width, monitor.Height,
                    Gdi.SrcCopy | Gdi.CaptureBlt))
            {
                return false;
            }

            return CopyThumbnailTo(destination);
        }
        finally
        {
            Gdi.ReleaseDC(nint.Zero, screenDc);
        }
    }

    /// <summary>Captures the window <paramref name="hwnd"/> scaled into <paramref name="destination"/> (BGRA, <see cref="ThumbByteSize"/> bytes). Returns false if the window is gone, has no area, or couldn't be rendered.</summary>
    public bool TryCaptureWindow(nint hwnd, byte[] destination)
    {
        lock (_gate) return CaptureWindowLocked(hwnd, destination);
    }

    private bool CaptureWindowLocked(nint hwnd, byte[] destination)
    {
        if (_disposed) return false;
        if (!NativeMethods.IsWindow(hwnd)) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return false;

        var screenDc = Gdi.GetDC(nint.Zero);
        if (screenDc == nint.Zero) return false;

        nint windowDc = nint.Zero, windowBitmap = nint.Zero, previousWindowBitmap = nint.Zero;
        try
        {
            if (!EnsureThumbnailSurface()) return false;

            windowDc = Gdi.CreateCompatibleDC(screenDc);
            if (windowDc == nint.Zero) return false;

            windowBitmap = CreateTopDownDib(windowDc, w, h, out _);
            if (windowBitmap == nint.Zero) return false;
            previousWindowBitmap = Gdi.SelectObject(windowDc, windowBitmap);

            // PW_RENDERFULLCONTENT is what makes this work for DirectComposition-based windows (browsers,
            // Electron, most modern apps) — without it they render as an empty frame.
            var rendered = Gdi.PrintWindow(hwnd, windowDc, Gdi.PwRenderFullContent);
            if (!rendered)
            {
                // Fallback for windows that won't render on demand: take whatever is on screen at their
                // rect. Wrong if something is covering them, but a stale-looking tile beats a blank one.
                rendered = Gdi.BitBlt(windowDc, 0, 0, w, h, screenDc, rect.Left, rect.Top, Gdi.SrcCopy | Gdi.CaptureBlt);
            }
            if (!rendered) return false;

            ClearThumbnailSurface();
            var (dx, dy, dw, dh) = Fit(w, h);
            Gdi.SetStretchBltMode(_thumbDc, Gdi.Halftone);
            Gdi.SetBrushOrgEx(_thumbDc, 0, 0, nint.Zero);
            if (!Gdi.StretchBlt(_thumbDc, dx, dy, dw, dh, windowDc, 0, 0, w, h, Gdi.SrcCopy)) return false;

            return CopyThumbnailTo(destination);
        }
        finally
        {
            if (windowDc != nint.Zero)
            {
                if (previousWindowBitmap != nint.Zero) Gdi.SelectObject(windowDc, previousWindowBitmap);
                Gdi.DeleteDC(windowDc);
            }
            if (windowBitmap != nint.Zero) Gdi.DeleteObject(windowBitmap);
            Gdi.ReleaseDC(nint.Zero, screenDc);
        }
    }

    /// <summary>Aspect-preserving fit of a source of the given size into the thumbnail, centered.</summary>
    private static (int X, int Y, int Width, int Height) Fit(int sourceWidth, int sourceHeight)
    {
        var scale = Math.Min(ThumbWidth / (double)sourceWidth, ThumbHeight / (double)sourceHeight);
        var w = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var h = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return ((ThumbWidth - w) / 2, (ThumbHeight - h) / 2, w, h);
    }

    private bool EnsureThumbnailSurface()
    {
        if (_disposed) return false;
        if (_thumbDc != nint.Zero) return true;

        var screenDc = Gdi.GetDC(nint.Zero);
        if (screenDc == nint.Zero) return false;
        try
        {
            _thumbDc = Gdi.CreateCompatibleDC(screenDc);
            if (_thumbDc == nint.Zero) return false;

            _thumbBitmap = CreateTopDownDib(_thumbDc, ThumbWidth, ThumbHeight, out _thumbBits);
            if (_thumbBitmap == nint.Zero)
            {
                Gdi.DeleteDC(_thumbDc);
                _thumbDc = nint.Zero;
                return false;
            }

            _previousThumbBitmap = Gdi.SelectObject(_thumbDc, _thumbBitmap);
            _backgroundBrush = Gdi.CreateSolidBrush(BackgroundColorRef);
            return true;
        }
        finally
        {
            Gdi.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private void ClearThumbnailSurface()
    {
        if (_backgroundBrush == nint.Zero) return;
        var full = new NativeMethods.Rect { Left = 0, Top = 0, Right = ThumbWidth, Bottom = ThumbHeight };
        Gdi.FillRect(_thumbDc, ref full, _backgroundBrush);
    }

    private bool CopyThumbnailTo(byte[] destination)
    {
        if (destination.Length < ThumbByteSize || _thumbBits == nint.Zero) return false;

        // GDI drawing is batched per thread; without a flush the DIB bits can still be un-written when
        // they're read straight out of memory like this.
        Gdi.GdiFlush();
        Marshal.Copy(_thumbBits, destination, 0, ThumbByteSize);

        // GDI leaves the alpha byte of a 32bpp DIB as whatever it happened to be (usually 0) — the same
        // quirk MainViewModel.UpdatePreview already compensates for on captured frames. WriteableBitmap
        // treats its source as premultiplied, so alpha 0 would render the whole tile invisible.
        for (int i = 3; i < ThumbByteSize; i += 4) destination[i] = 255;
        return true;
    }

    /// <summary>Creates a 32bpp top-down (negative height) DIB section, so its rows are in the same order a WriteableBitmap expects.</summary>
    private static nint CreateTopDownDib(nint dc, int width, int height, out nint bits)
    {
        var header = new Gdi.BitmapInfoHeader
        {
            biSize = Marshal.SizeOf<Gdi.BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Gdi.BiRgb,
        };
        return Gdi.CreateDIBSection(dc, ref header, Gdi.DibRgbColors, out bits, nint.Zero, 0);
    }

    public void Dispose()
    {
        lock (_gate) DisposeLocked();
    }

    private void DisposeLocked()
    {
        if (_disposed) return;
        _disposed = true;

        if (_thumbDc != nint.Zero)
        {
            if (_previousThumbBitmap != nint.Zero) Gdi.SelectObject(_thumbDc, _previousThumbBitmap);
            Gdi.DeleteDC(_thumbDc);
            _thumbDc = nint.Zero;
        }
        if (_thumbBitmap != nint.Zero)
        {
            Gdi.DeleteObject(_thumbBitmap);
            _thumbBitmap = nint.Zero;
        }
        if (_backgroundBrush != nint.Zero)
        {
            Gdi.DeleteObject(_backgroundBrush);
            _backgroundBrush = nint.Zero;
        }
        _thumbBits = nint.Zero;
    }

    /// <summary>The GDI surface kept private to this service — none of it is useful to the DXGI/WGC capture paths in <see cref="Interop.NativeMethods"/>.</summary>
    private static class Gdi
    {
        public const int SrcCopy = 0x00CC0020;
        public const int CaptureBlt = 0x40000000;
        public const int Halftone = 4;
        public const int BiRgb = 0;
        public const uint DibRgbColors = 0;
        public const uint PwRenderFullContent = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("user32.dll")]
        public static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(nint hWnd, nint hDC);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern int FillRect(nint hDC, ref NativeMethods.Rect lprc, nint hbr);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleDC(nint hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(nint hdc);

        [DllImport("gdi32.dll")]
        public static extern nint SelectObject(nint hdc, nint h);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(nint ho);

        [DllImport("gdi32.dll")]
        public static extern nint CreateSolidBrush(int color);

        [DllImport("gdi32.dll")]
        public static extern nint CreateDIBSection(nint hdc, ref BitmapInfoHeader pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, int rop);

        [DllImport("gdi32.dll")]
        public static extern bool StretchBlt(nint hdcDest, int xDest, int yDest, int wDest, int hDest,
            nint hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

        [DllImport("gdi32.dll")]
        public static extern int SetStretchBltMode(nint hdc, int mode);

        [DllImport("gdi32.dll")]
        public static extern bool SetBrushOrgEx(nint hdc, int x, int y, nint lppt);

        [DllImport("gdi32.dll")]
        public static extern bool GdiFlush();
    }
}
