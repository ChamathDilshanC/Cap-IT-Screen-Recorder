using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.ViewModels;
using ScreenRecorderApp.Views;

namespace ScreenRecorderApp;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            if (Program.ReviewPath is { } reviewPath)
            {
                // Standalone editor (ScreenRecorderApp.exe --review <file>).
                ApplyTheme(new SettingsService().Load().Theme);
                var review = new ReviewWindow(reviewPath);
                desktop.MainWindow = review;
#if DEBUG
                if (Diagnostics.UiSnapshotHarness.IsRequested(out var editorSnapshots))
                    review.Opened += (_, _) => _ = Diagnostics.UiSnapshotHarness.RunEditorAsync(review, editorSnapshots, () => desktop.Shutdown());
                else if (Diagnostics.SmokeHarness.EditorDirectory is { } editorSmoke)
                    review.Opened += (_, _) => _ = Diagnostics.SmokeHarness.RunEditorAsync(review, editorSmoke, () => desktop.Shutdown());
#endif
            }
            else
            {
                Services = ConfigureServices(desktop);
                var window = Services.GetRequiredService<MainWindow>();
                var shell = Services.GetRequiredService<ShellViewModel>();
                window.DataContext = shell;
                Services.GetRequiredService<RecordingSessionPresenter>().Attach(window);

                ApplyTheme(shell.Main.SelectedAppTheme.Value);
                shell.Main.PropertyChanged += OnMainPropertyChanged;
                desktop.MainWindow = window;
#if DEBUG
                if (Diagnostics.UiSnapshotHarness.IsRequested(out var snapshotDir))
                    window.Opened += (_, _) => _ = Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_EXTRAS") == "1"
                        ? Diagnostics.UiSnapshotHarness.RunExtrasAsync(window, shell, snapshotDir, () => desktop.Shutdown())
                        : Diagnostics.UiSnapshotHarness.RunAsync(window, shell, snapshotDir, () => desktop.Shutdown());
                else if (Diagnostics.SmokeHarness.RecordDirectory is { } recordSmoke)
                    window.Opened += (_, _) => _ = Diagnostics.SmokeHarness.RunRecordingAsync(shell, recordSmoke, () => desktop.Shutdown());
#endif
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => { var ui = new UiStateService(); ui.Load(); return ui; });
        services.AddSingleton<ToastService>();
        services.AddSingleton<ReviewWindowService>();
        services.AddSingleton<IReviewWindowService>(sp => sp.GetRequiredService<ReviewWindowService>());
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<ToastService>(),
            sp.GetRequiredService<IReviewWindowService>(),
            () => desktop.Shutdown()));
        services.AddSingleton<MainWindow>();
        services.AddSingleton(sp => new DialogService(sp.GetRequiredService<MainWindow>().Dialogs));
        services.AddSingleton(sp => new FilePickerService(() => sp.GetRequiredService<MainWindow>()));
        services.AddSingleton<RecordingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<RecordingSessionPresenter>();
        return services.BuildServiceProvider();
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedAppTheme) && sender is MainViewModel main)
            ApplyTheme(main.SelectedAppTheme.Value);
    }

    public void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
