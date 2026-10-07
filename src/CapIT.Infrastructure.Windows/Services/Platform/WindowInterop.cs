using System.Runtime.InteropServices;

namespace ScreenRecorderApp.Services.Platform;

/// <summary>Win32 window operations the presentation layer needs but no UI framework exposes.</summary>
public static class WindowInterop
{
    private const uint WDA_NONE = 0x0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>
    /// Keeps a window out of every screen capture — Desktop Duplication, Windows Graphics Capture and
    /// screenshots — so app chrome like the recording controller never appears in the recording.
    /// Windows 10 2004+; returns false (window stays capturable) on anything older.
    /// </summary>
    public static bool ExcludeFromCapture(nint hwnd, bool exclude = true) =>
        hwnd != 0 && SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);

    /// <summary>Work area (virtual-screen pixels) of the monitor showing <paramref name="hwnd"/>.</summary>
    public static (int X, int Y, int Width, int Height)? GetWorkAreaForWindow(nint hwnd)
    {
        if (hwnd == 0) return null;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return GetWorkArea(monitor);
    }

    /// <summary>Work area (virtual-screen pixels) of an HMONITOR.</summary>
    public static (int X, int Y, int Width, int Height)? GetWorkArea(nint monitor)
    {
        if (monitor == 0) return null;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        var r = info.rcWork;
        return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}
