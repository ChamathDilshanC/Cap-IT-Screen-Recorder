using System.Runtime.InteropServices;

namespace ScreenRecorderApp.Services.Capture.Interop;

internal static class NativeMethods
{
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref Rect lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public const uint MonitorInfoFPrimary = 0x1;

    // --- Window enumeration (for Application-Specific Capture's window picker) ---

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hWnd, uint gaFlags);

    public const uint GaRoot = 2;

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(nint hWnd, int nIndex);

    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    /// <summary>
    /// DWMWA_CLOAKED: nonzero when a window is "cloaked" — reported visible/not-minimized by every other
    /// API, but not actually shown on screen (e.g. some UWP windows kept alive off-screen, certain virtual
    /// desktop cases). A well-known gotcha for window enumeration; skipping cloaked windows keeps the
    /// picker from listing windows that would just capture as blank/stale.
    /// </summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    public const int DwmwaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point lpPoint);
}
