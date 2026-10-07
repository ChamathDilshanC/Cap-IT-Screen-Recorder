using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;

namespace ScreenRecorderApp;

internal static class Program
{
    // Named system mutex used for two things:
    //  1. Single instance — a second launch focuses the running window and exits instead of starting a
    //     rival process that would contend for the window and audio devices.
    //  2. The Inno Setup installer's AppMutex — with this exact name in the .iss, an in-place update can
    //     detect the running app, close it, install over the same folder, and relaunch it.
    // Must stay byte-for-byte identical to AppMutex in Installer\CapITScreenRecorder.iss.
    private const string SingleInstanceMutexName = "CapITScreenRecorderSingleInstanceMutex";
    private static Mutex? _singleInstanceMutex;

    /// <summary>Set when launched as <c>ScreenRecorderApp.exe --review &lt;file&gt;</c>: a standalone editor.</summary>
    public static string? ReviewPath { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("AppDomain UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { LogCrash("UnobservedTaskException", e.Exception); e.SetObserved(); };

        if (args.Length >= 2 && args[0] == "--review" && File.Exists(args[1]))
            ReviewPath = Path.GetFullPath(args[1]);

        // A standalone review owns no capture devices and may coexist with the recording instance.
        if (ReviewPath is null && !TryAcquireSingleInstance())
        {
            FocusExistingInstance();
            return 0;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash("Fatal", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            return createdNew;
        }
        catch
        {
            // If the mutex can't be created for some reason, don't block startup over it.
            return true;
        }
    }

    /// <summary>
    /// Raises the running instance's main window. Process.MainWindowHandle can't be trusted for this: it
    /// is often the annotation overlay (a visible, untitled layered window), so the window is found by
    /// enumerating the other process's visible, titled top-level windows instead.
    /// </summary>
    private static void FocusExistingInstance()
    {
        try
        {
            var self = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(self.ProcessName))
            {
                if (other.Id == self.Id) continue;
                var target = nint.Zero;
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid != (uint)other.Id || !IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0) return true;
                    target = hwnd;
                    return false;
                }, nint.Zero);
                if (target == nint.Zero) continue;
                ShowWindow(target, SW_RESTORE);
                SetForegroundWindow(target);
                break;
            }
        }
        catch
        {
            // Best effort — the running instance stays where it is.
        }
    }

    public static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {source}\n{ex}\n\n");
        }
        catch
        {
            // If even the crash log can't be written there is nothing more to do.
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hwnd);
}
