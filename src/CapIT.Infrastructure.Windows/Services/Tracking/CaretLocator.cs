using System.Runtime.InteropServices;

namespace ScreenRecorderApp.Services.Tracking;

/// <summary>
/// Locates the text caret of whatever control currently has keyboard focus in the foreground
/// application, via the same GetGUIThreadInfo caret-rect technique Magnifier's "follow text cursor"
/// mode and on-screen keyboards use. Lets the smart zoom steer toward where the user is actually
/// typing instead of wherever the mouse happens to be sitting.
/// </summary>
internal static class CaretLocator
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    /// <summary>
    /// Returns the current text caret's center point in virtual-screen coordinates (the same space
    /// desktop window rectangles live in). Returns false if there's no active caret right now — e.g.
    /// keyboard focus is on a non-text control, or the foreground app doesn't expose one.
    /// </summary>
    public static bool TryGetCaretScreenPosition(out int x, out int y)
    {
        x = 0;
        y = 0;

        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero) return false;

        var threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref info)) return false;
        if (info.hwndCaret == nint.Zero) return false;

        // rcCaret is in the caret owner window's client coordinates; ClientToScreen on its center
        // converts to virtual-screen coordinates, matching everything else this feature works in.
        var center = new POINT
        {
            X = (info.rcCaret.Left + info.rcCaret.Right) / 2,
            Y = (info.rcCaret.Top + info.rcCaret.Bottom) / 2,
        };
        if (!ClientToScreen(info.hwndCaret, ref center)) return false;

        x = center.X;
        y = center.Y;
        return true;
    }
}
