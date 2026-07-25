using Microsoft.UI.Xaml;
using ScreenRecorderApp.Views;

namespace ScreenRecorderApp;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
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
}
