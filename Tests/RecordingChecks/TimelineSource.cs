using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RecordingChecks;

/// <summary>
/// The wall-clock code painted into the middle of the test source, and the decoder that reads it back out
/// of a (possibly zoomed, scaled, compressed) video frame.
/// </summary>
/// <remarks>
/// Layout along one scanline, in units of <see cref="Unit"/> source pixels:
/// <c>[marker 2][gap 1][bit0 2][bit1 2]...[bitN-1 2][gap 1][marker 2]</c> — white markers on black, each
/// bit a pair of cells, white-then-black for 1 and black-then-white for 0. Because every bit has exactly
/// one white half and the whole thing is bracketed by two markers, a decoder needs no knowledge of the
/// zoom factor: it finds the outer white edges, divides the span by (2N+6), and samples cell centres. That
/// is what makes the code survive Smart Tracking's 1x..3x push-in, which is the whole point of the test.
/// The payload is milliseconds on a monotonic clock since the source started, so a decoded frame says
/// exactly which instant of the "screen action" it shows.
/// </remarks>
internal static class TimelineCode
{
    public const int Bits = 24;
    public const int Unit = 12;
    public const int StripHeight = 400;
    public static int SpanUnits => 2 * Bits + 6;

    public static long? DecodeRow(byte[] frame, int rowStart, int w)
    {
        var row = new ReadOnlySpan<byte>(frame, rowStart, w);
        int left = -1, right = -1;
        for (int x = 0; x < w; x++) if (row[x] >= 128) { left = x; break; }
        if (left < 0) return null;
        for (int x = w - 1; x >= 0; x--) if (row[x] >= 128) { right = x; break; }
        double span = right - left + 1;
        double u = span / SpanUnits;
        if (u < 3) return null;

        bool White(double x) { int i = (int)Math.Round(x); return i >= 0 && i < w && frame[rowStart + i] >= 128; }

        // Marker and gap sanity: rejects a row that merely contains some other bright content.
        if (!White(left + u) || White(left + 2.5 * u)) return null;
        if (!White(right - u) || White(right - 2.5 * u)) return null;

        long value = 0;
        for (int i = 0; i < Bits; i++)
        {
            bool first = White(left + (3 + 2 * i + 0.5) * u);
            bool second = White(left + (3 + 2 * i + 1.5) * u);
            if (first == second) return null;
            value = (value << 1) | (first ? 1L : 0L);
        }
        return value;
    }
}

/// <summary>
/// Full-screen, borderless, topmost window that repaints the timeline code plus a sweeping bar as fast as
/// it can, so every display refresh really is a new frame.
/// </summary>
/// <remarks>
/// Paints straight into a DIB section and blits it to the window with a single <c>BitBlt</c> from a
/// dedicated thread. GDI+ (<c>DrawImage</c>) and WM_PAINT with automatic double-buffering both turned out
/// to manage only a handful of 1080p updates a second, which would have made the source — not the
/// recorder — the reason frames repeat. The source also keeps its own paint statistics so a report can say
/// how many distinct frames existed to be recorded at all.
/// </remarks>
internal sealed unsafe class TimelineForm : Form
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Thread? _painter;
    private volatile bool _running = true;
    public volatile bool AbortRequested;

    public long ElapsedMs => _clock.ElapsedMilliseconds;
    public long Paints;
    public double MaxPaintGapMs;
    private long _lastPaintTicks;

    private nint _memDc, _dib, _bits;

    public const string WindowTitle = "CapIT RecordingChecks timeline source";

    public TimelineForm(Rectangle bounds)
    {
        Text = WindowTitle;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && Environment.GetEnvironmentVariable("RC_IGNORE_ESC") != "1") AbortRequested = true; };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        int w = Width, h = Height;
        var windowDc = GetDC(Handle);
        var bmi = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        _memDc = CreateCompatibleDC(windowDc);
        _dib = CreateDIBSection(windowDc, ref bmi, 0, out _bits, 0, 0);
        SelectObject(_memDc, _dib);
        ReleaseDC(Handle, windowDc);

        var handle = Handle;
        _painter = new Thread(() =>
        {
            timeBeginPeriod(1);
            var dc = GetDC(handle);
            while (_running)
            {
                PaintOnce(dc, w, h);
                Thread.Sleep(1);
            }
            ReleaseDC(handle, dc);
            timeEndPeriod(1);
        }) { IsBackground = true, Name = "timeline-painter" };
        _painter.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _running = false;
        _painter?.Join(1000);
        base.OnFormClosing(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    private void PaintOnce(nint windowDc, int w, int h)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastPaintTicks != 0) MaxPaintGapMs = Math.Max(MaxPaintGapMs, (now - _lastPaintTicks) * 1000.0 / Stopwatch.Frequency);
        _lastPaintTicks = now;
        Paints++;

        long ms = _clock.ElapsedMilliseconds & ((1L << TimelineCode.Bits) - 1);
        var px = (uint*)_bits;
        new Span<uint>(px, w * h).Clear();

        void Fill(int x, int y, int rw, int rh)
        {
            for (int row = y; row < y + rh; row++)
                new Span<uint>(px + (long)row * w + x, rw).Fill(0xFFFFFFFF);
        }

        int u = TimelineCode.Unit;
        int spanPx = TimelineCode.SpanUnits * u;
        int x0 = (w - spanPx) / 2;
        int y0 = (h - TimelineCode.StripHeight) / 2;
        int sh = TimelineCode.StripHeight;

        Fill(x0, y0, 2 * u, sh);
        Fill(x0 + spanPx - 2 * u, y0, 2 * u, sh);
        for (int i = 0; i < TimelineCode.Bits; i++)
        {
            bool one = ((ms >> (TimelineCode.Bits - 1 - i)) & 1) == 1;
            int cx = x0 + (3 + 2 * i) * u;
            Fill(one ? cx : cx + u, y0, u, sh);
        }

        // Full-width motion above and below the strip: a bar sweeping the screen every 1.2s. It gives the
        // zoomed picture something to show besides the code.
        double phase = (ms % 1200) / 1200.0;
        int barX = (int)(phase * (w - 60));
        Fill(barX, 40, 60, 120);
        Fill(w - 60 - barX, h - 160, 60, 120);

        BitBlt(windowDc, 0, 0, w, h, _memDc, 0, 0, 0x00CC0020 /* SRCCOPY */);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint p);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint p);
}
