using System.Diagnostics;
using System.Text;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture.Interop;

namespace ScreenRecorderApp.Services.Capture;

/// <summary>Enumerates top-level windows that are actually reasonable to offer as an Application-Specific Capture target — the window-picker counterpart to <see cref="MonitorEnumerator"/>.</summary>
internal static class WindowEnumerator
{
    public static List<WindowInfo> GetWindows()
    {
        var windows = new List<WindowInfo>();
        var selfPid = (uint)Environment.ProcessId;

        bool EnumProc(nint hWnd, nint _)
        {
            if (!IsRecordableWindow(hWnd, selfPid, out var title, out var processName)) return true;

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
            });
            return true;
        }

        NativeMethods.EnumWindows(EnumProc, 0);
        return windows;
    }

    private static bool IsRecordableWindow(nint hWnd, uint selfPid, out string title, out string processName)
    {
        title = "";
        processName = "";

        if (!NativeMethods.IsWindowVisible(hWnd)) return false;
        if (NativeMethods.IsIconic(hWnd)) return false; // minimized — nothing useful to capture

        // Only true top-level windows, not child/owned windows a naive EnumWindows pass would still see.
        if (NativeMethods.GetAncestor(hWnd, NativeMethods.GaRoot) != hWnd) return false;

        // Tool windows (floating palettes, some overlays) aren't real app windows, unless explicitly
        // flagged as one via WS_EX_APPWINDOW.
        var exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GwlExStyle);
        var isToolWindow = (exStyle & NativeMethods.WsExToolWindow) != 0;
        var isAppWindow = (exStyle & NativeMethods.WsExAppWindow) != 0;
        if (isToolWindow && !isAppWindow) return false;

        // Cloaked windows report visible/not-minimized but aren't actually shown on screen (some UWP
        // windows, certain virtual-desktop cases) — capturing one would just be blank/stale.
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        var length = NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0) return false;
        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        title = sb.ToString();
        if (string.IsNullOrWhiteSpace(title)) return false;

        NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == selfPid) return false; // recording our own window is nonsensical here

        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch
        {
            // Process may have exited between EnumWindows and here; just fall back to no process name
            // rather than dropping an otherwise-valid window from the list.
            processName = "";
        }

        return true;
    }
}
