using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using ScreenRecorderApp.Views;

namespace ScreenRecorderApp;

public partial class App : Application
{
    // Named system mutex used for two things:
    //  1. Single instance — a second launch focuses the running window and exits instead of starting a
    //     rival process (which is what left the app seemingly unable to "reopen": a stuck/lingering
    //     copy was still holding the window and audio devices).
    //  2. The Inno Setup installer's AppMutex — with this exact name in the .iss, an in-place update can
    //     detect the running app, close it, install over the same folder, and relaunch it.
    // Must stay byte-for-byte identical to AppMutex in Installer\CapITScreenRecorder.iss.
    private const string SingleInstanceMutexName = "CapITScreenRecorderSingleInstanceMutex";
    private static Mutex? _singleInstanceMutex;

    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TryAcquireSingleInstance())
        {
            FocusExistingInstance();
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            return createdNew;
        }
        catch
        {
            // If the mutex can't be created for some reason, don't block startup over it.
            return true;
        }
    }

    private static void FocusExistingInstance()
    {
        try
        {
            var self = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(self.ProcessName))
            {
                if (other.Id == self.Id) continue;

                var handle = other.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    break;
                }
            }
        }
        catch
        {
            // Best effort — the running instance stays where it is; we just won't have raised it.
        }
    }

    private void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("XAML UnhandledException", e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash("AppDomain UnhandledException", e.ExceptionObject as Exception);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {source}\n{ex}\n\n");
        }
        catch
        {
            // Best effort — if we can't even write the crash log, there's nothing more to do.
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
