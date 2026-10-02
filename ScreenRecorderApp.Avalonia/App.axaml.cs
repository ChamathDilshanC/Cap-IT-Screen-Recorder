using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScreenRecorderApp.Avalonia.Services;
using ScreenRecorderApp.Avalonia.ViewModels;
using ScreenRecorderApp.Avalonia.Views;

namespace ScreenRecorderApp.Avalonia;

public partial class App : Application
{
    public static IRecordingWorkspaceAdapter WorkspaceAdapter { get; } = new ExistingRecordingWorkspaceAdapter();
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(WorkspaceAdapter)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
