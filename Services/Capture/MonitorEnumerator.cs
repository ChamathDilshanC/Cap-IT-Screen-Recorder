using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Capture.Interop;

namespace ScreenRecorderApp.Services.Capture;

internal static class MonitorEnumerator
{
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        bool EnumProc(nint hMonitor, nint hdcMonitor, ref NativeMethods.Rect rect, nint dwData)
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
            };

            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                var isPrimary = (info.dwFlags & NativeMethods.MonitorInfoFPrimary) != 0;

                // szDevice looks like "\\.\DISPLAY1" — show a plain "Display 1" instead of that raw
                // device path in the UI.
                var friendlyName = $"Display {monitors.Count + 1}";
                var lastDigits = new string(info.szDevice.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                if (lastDigits.Length > 0) friendlyName = $"Display {lastDigits}";

                monitors.Add(new MonitorInfo
                {
                    Handle = hMonitor,
                    DeviceName = info.szDevice,
                    FriendlyName = friendlyName,
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top,
                    IsPrimary = isPrimary
                });
            }

            return true;
        }

        NativeMethods.EnumDisplayMonitors(0, 0, EnumProc, 0);

        return monitors;
    }
}
