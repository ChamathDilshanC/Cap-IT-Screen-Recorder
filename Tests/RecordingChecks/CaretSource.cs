using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RecordingChecks;

/// <summary>Where the typing box sits, as fractions of the screen: far from where the harness parks the mouse so that "zoom follows the mouse" and "zoom follows the caret" give different crops.</summary>
internal static class CaretLayout
{
    public static Rectangle Box(int width, int height) => new((int)(width * 0.52), (int)(height * 0.52), (int)(width * 0.42), (int)(height * 0.30));
    public static Point MouseParking(int width, int height) => new((int)(width * 0.10), (int)(height * 0.14));
}

/// <summary>
/// The sharpness pattern as a backdrop with a real, native multi-line edit control on it. Keystrokes typed
/// into it move a genuine system caret (the thing GetGUIThreadInfo reports and Smart Tracking follows). The
/// form logs the caret's screen position whenever it moves, which is the reference the recording is judged by.
/// </summary>
internal sealed class CaretForm : Form
{
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hWnd, ref Point p);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int L, T, R, B;
    }

    private readonly Bitmap _backdrop;
    private readonly TextBox _box;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 30 };
    private Point _last = new(int.MinValue, 0);
    public volatile bool AbortRequested;

    public CaretForm(Rectangle bounds, string saveDir)
    {
        Text = "CapIT RecordingChecks caret target";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) AbortRequested = true; };

        _backdrop = PatternRenderer.Render(bounds.Width, bounds.Height, 1f);
        var rect = CaretLayout.Box(bounds.Width, bounds.Height);
        _box = new TextBox
        {
            Multiline = true, Bounds = rect, Font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel),
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Color.Black, ScrollBars = ScrollBars.None,
        };
        Controls.Add(_box);
        // The saved backdrop has the box area blanked so that registration never depends on typed text.
        Directory.CreateDirectory(saveDir);
        using (var save = new Bitmap(_backdrop))
        using (var g = Graphics.FromImage(save))
        {
            g.FillRectangle(Brushes.White, rect);
            save.Save(Path.Combine(saveDir, "pattern-1x.png"), ImageFormat.Png);
        }
        Shown += (_, _) => { Activate(); _box.Focus(); _poll.Start(); };
        _poll.Tick += (_, _) => LogCaret();
    }

    private void LogCaret()
    {
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(GetCurrentThreadId(), ref info) || info.hwndCaret == 0) return;
        var c = new Point((info.L + info.R) / 2, (info.T + info.B) / 2);
        if (!ClientToScreen(info.hwndCaret, ref c)) return;
        if (c == _last) return;
        _last = c;
        Console.Out.WriteLine($"CARET {c.X} {c.Y}");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.DrawImageUnscaled(_backdrop, 0, 0);
    }
}
