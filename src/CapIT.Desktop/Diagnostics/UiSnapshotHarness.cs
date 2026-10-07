#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ScreenRecorderApp.ViewModels;
using ScreenRecorderApp.Views;

namespace ScreenRecorderApp.Diagnostics;

/// <summary>
/// Opt-in layout review harness (Debug builds only). With CAPIT_UI_SNAPSHOT_DIR set, the app renders every
/// page in both themes at the given logical sizes to PNG files, then exits. CAPIT_UI_SNAPSHOT_PAGES can
/// restrict it to a comma-separated list of page keys; CAPIT_UI_SNAPSHOT_SIZES to "1280x720,1920x1080".
/// </summary>
internal static class UiSnapshotHarness
{
    public static bool IsRequested(out string directory)
    {
        directory = Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_DIR") ?? "";
        return directory.Length > 0;
    }

    /// <summary>Standalone editor (--review): every inspector tab in both themes.</summary>
    public static async Task RunEditorAsync(Window window, string directory, Action exit)
    {
        Directory.CreateDirectory(directory);
        var themes = (Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_THEMES") ?? "Dark,Light")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sizes = (Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_SIZES") ?? "1280x720")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split('x')).Select(p => (W: double.Parse(p[0]), H: double.Parse(p[1]))).ToList();
        try
        {
            await Task.Delay(4000); // probe, metadata, first frame, timeline thumbnails
            if (window.DataContext is not EditorViewModel editor) return;
            foreach (var theme in themes)
            {
                Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                foreach (var (w, h) in sizes)
                {
                    window.Width = w; window.Height = h;
                    foreach (var tab in editor.InspectorTabs)
                    {
                        editor.SelectedTab = tab;
                        await Task.Delay(600);
                        Save(window, Path.Combine(directory, $"editor-{theme.ToLowerInvariant()}-{w}x{h}-{tab.Key}.png"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "snapshot-error.txt"), ex.ToString());
        }
        finally
        {
            exit();
        }
    }

    /// <summary>Transient and alternate states: collapsed sidebar, minimum size, overlays, toasts, settings sections.</summary>
    public static async Task RunExtrasAsync(Window window, ShellViewModel shell, string directory, Action exit)
    {
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Delay(1500);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            window.Width = 1024; window.Height = 640;
            shell.NavigateTo(PageKey.Home);
            await Task.Delay(700);
            Save(window, Path.Combine(directory, "extra-min-1024x640-home.png"));
            shell.NavigateTo(PageKey.Capture);
            await Task.Delay(700);
            Save(window, Path.Combine(directory, "extra-min-1024x640-capture.png"));

            window.Width = 1280; window.Height = 720;
            shell.IsSidebarCollapsed = true;
            shell.NavigateTo(PageKey.Effects);
            await Task.Delay(700);
            Save(window, Path.Combine(directory, "extra-collapsed-sidebar.png"));
            shell.IsSidebarCollapsed = false;

            shell.NavigateTo(PageKey.Home);
            shell.OpenSourcePickerCommand.Execute(null);
            await Task.Delay(2600); // thumbnails
            Save(window, Path.Combine(directory, "extra-source-picker.png"));
            shell.SourcePicker?.CancelCommand.Execute(null);
            await Task.Delay(400);

            shell.Toasts.Success("Recording saved", "Recording_2026-10-07_20-41-02-118.mp4", "Show in folder", () => { });
            shell.Toasts.Warning("Camera unavailable", "Cap-IT couldn't access the selected camera. Check whether another app is using it.");
            var dialog = shell.Dialogs.ConfirmAsync("Delete “Recording_2026-10-03”?",
                "The file and its edit settings are permanently deleted. This can't be undone.", "Delete",
                Services.DialogTone.Danger, Services.AppResources.Icon("Icon.Trash"));
            await Task.Delay(800);
            Save(window, Path.Combine(directory, "extra-dialog-toasts.png"));
            ((MainWindow)window).Dialogs.CurrentRequest?.Cancel();
            await dialog;

            shell.NavigateTo(PageKey.Settings);
            for (var i = 1; i <= 5; i++)
            {
                shell.SettingsSectionIndex = i;
                await Task.Delay(500);
                Save(window, Path.Combine(directory, $"extra-settings-{i}.png"));
            }
            shell.SettingsSectionIndex = 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "snapshot-error.txt"), ex.ToString());
        }
        finally
        {
            exit();
        }
    }

    private static void Save(Window window, string path)
    {
        var root = (Control)window.Content!;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height), new Vector(96, 96));
        bitmap.Render(root);
        bitmap.Save(path);
    }

    public static async Task RunAsync(Window window, ShellViewModel shell, string directory, Action exit)
    {
        Directory.CreateDirectory(directory);
        shell.Main.SuppressLivePreviewForSnapshot = true;
        var pages = (Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_PAGES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Enum.Parse<PageKey>(p, ignoreCase: true)).ToList();
        if (pages.Count == 0) pages = Enum.GetValues<PageKey>().ToList();
        var sizes = (Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_SIZES") ?? "1280x720,1920x1080")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split('x')).Select(p => (W: double.Parse(p[0]), H: double.Parse(p[1]))).ToList();
        var themes = (Environment.GetEnvironmentVariable("CAPIT_UI_SNAPSHOT_THEMES") ?? "Dark,Light")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            await Task.Delay(1500); // let devices enumerate and the first preview frame arrive
            foreach (var theme in themes)
            {
                Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                foreach (var (w, h) in sizes)
                {
                    window.WindowState = WindowState.Normal;
                    window.Width = w; window.Height = h;
                    foreach (var page in pages)
                    {
                        shell.NavigateTo(page);
                        await Task.Delay(700);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        var root = (Control)window.Content!;
                        var size = new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height);
                        File.AppendAllText(Path.Combine(directory, "sizes.txt"),
                            $"{theme} {w}x{h} {page}: window={window.Width}x{window.Height} client={window.ClientSize} root={root.Bounds} scaling={window.RenderScaling} screen={window.Screens.ScreenFromWindow(window)?.WorkingArea}" + Environment.NewLine);
                        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                        bitmap.Render(root);
                        bitmap.Save(Path.Combine(directory, $"{theme.ToLowerInvariant()}-{w}x{h}-{page}.png"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "snapshot-error.txt"), ex.ToString());
        }
        finally
        {
            exit();
        }
    }
}
#endif
