#if DEBUG
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services.Encoding;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Diagnostics;

/// <summary>
/// Opt-in end-to-end checks for Debug builds, driven through the same view models the UI uses:
///   CAPIT_RECORD_SMOKE_DIR — record ~5 s of the primary display (with pause/resume) into that folder,
///                            then probe the result. Leaves the editor that opens afterwards closed.
///   CAPIT_EDITOR_SMOKE_DIR — in a --review window: play, trim, add a zoom region and a text layer,
///                            undo/redo, export MP4 and GIF, and verify the files and saved metadata.
/// Results are written to results.txt as PASS/FAIL lines.
/// </summary>
internal static class SmokeHarness
{
    public static string? RecordDirectory => Environment.GetEnvironmentVariable("CAPIT_RECORD_SMOKE_DIR") is { Length: > 0 } d ? d : null;
    public static string? EditorDirectory => Environment.GetEnvironmentVariable("CAPIT_EDITOR_SMOKE_DIR") is { Length: > 0 } d ? d : null;

    public static async Task RunRecordingAsync(ShellViewModel shell, string directory, Action exit)
    {
        var log = new List<string>();
        void Check(bool pass, string name) => log.Add((pass ? "PASS " : "FAIL ") + name);
        var main = shell.Main;
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(2500);
            main.UseWholeDisplayCommand.Execute(null);
            main.OutputDirectory = Path.Combine(directory, "out");
            main.CaptureMicrophone = false; // keep the smoke recording free of room audio
            Check(main.HasCaptureTarget, "A display is selected");
            Check(main.StartRecordingCommand.CanExecute(null), "Start is enabled");

            await main.StartRecordingCommand.ExecuteAsync(null);
            Check(main.IsRecording, "Recording started");
            await Task.Delay(800);
            var windows = (Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Windows;
            Check(windows.Any(w => w is Views.RecordingControllerWindow { IsVisible: true }), "Floating controller shown");
            await Task.Delay(1700);
            main.PauseResumeCommand.Execute(null);
            Check(main.IsPaused, "Paused");
            await Task.Delay(800);
            main.PauseResumeCommand.Execute(null);
            Check(main.IsRecording, "Resumed");
            await Task.Delay(2000);
            Check(main.ElapsedText != "00:00", $"Timer advanced ({main.ElapsedText})");
            await main.StopRecordingCommand.ExecuteAsync(null);
            Check(main.IsIdle, "Stopped and idle");
            Check(!windows.Any(w => w is Views.RecordingControllerWindow), "Controller closed");

            var path = main.LastOutputPath;
            Check(path is not null && File.Exists(path), $"Recording saved ({path})");
            if (path is not null && File.Exists(path))
            {
                Check(new FileInfo(path).Length > 10_000, $"Recording has data ({new FileInfo(path).Length} bytes)");
                var probe = await MediaProbe.ProbeAsync(path, CancellationToken.None);
                Check(probe.Width > 0 && probe.Height > 0, $"Video stream {probe.Width}×{probe.Height}");
                Check(probe.Duration?.TotalSeconds > 3, $"Duration {probe.Duration?.TotalSeconds:0.0}s");
                Check(path.Contains(Path.Combine("out", MediaOutputPathsRecordings)), "Saved under Recordings/<date>");
            }
            await Task.Delay(2500); // the review window opens after a recording
            Check(windows.Any(w => w is Views.ReviewWindow), "Review & Export opened after stopping");
            foreach (var w in windows.OfType<Views.ReviewWindow>().ToList()) w.Close();
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            log.Add("FAIL exception: " + ex);
        }
        finally
        {
            File.WriteAllLines(Path.Combine(directory, "results.txt"), log);
            exit();
        }
    }

    private const string MediaOutputPathsRecordings = Services.MediaOutputPaths.RecordingsFolderName;

    public static async Task RunEditorAsync(Window window, string directory, Action exit)
    {
        var log = new List<string>();
        void Check(bool pass, string name) => log.Add((pass ? "PASS " : "FAIL ") + name);
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(3500);
            if (window.DataContext is not EditorViewModel editor) { log.Add("FAIL no editor"); return; }
            var doc = editor.Document;
            Check(doc.IsReady, "Document loaded");
            Check(!editor.Playback.HasError && editor.Playback.Frame is not null, "First frame decoded");
            Check(doc.Duration > 1, $"Duration {doc.Duration:0.0}s");
            Check(editor.ExportVideoCommand.CanExecute(null), "Export enabled once loaded");

            var frames = editor.Playback.FrameVersion;
            editor.Playback.TogglePlay();
            await Task.Delay(1200);
            Check(editor.Playback.IsPlaying, "Playing");
            Check(editor.Playback.Position > .3, $"Playhead advanced ({editor.Playback.Position:0.00}s)");
            Check(editor.Playback.FrameVersion - frames > 10, $"Frames presented ({editor.Playback.FrameVersion - frames})");
            editor.Playback.TogglePlay();
            Check(!editor.Playback.IsPlaying, "Paused");

            editor.Playback.Seek(.5);
            doc.TrimStart = .5; doc.TrimEnd = 2.5;
            Check(Math.Abs(doc.TrimStart - .5) < .01 && Math.Abs(doc.TrimEnd - 2.5) < .01, "Trim applied");
            editor.AddZoomAtPlayheadCommand.Execute(null);
            Check(doc.ZoomRegions.Count >= 1, "Zoom region added at playhead");
            editor.Text.Text = "Cap-IT smoke";
            Check(doc.SelectedTextOverlay.Text == "Cap-IT smoke", "Text layer edited through the inspector model");
            doc.Presentation.CanvasPreset = "1:1";
            await Task.Delay(700); // debounce commit
            var radius = doc.Presentation.CornerRadius;
            doc.Presentation.CornerRadius = 40;
            await Task.Delay(700);
            doc.UndoCommand.Execute(null);
            Check(Math.Abs(doc.Presentation.CornerRadius - radius) < .01, "Undo restores the previous value");
            doc.RedoCommand.Execute(null);
            Check(Math.Abs(doc.Presentation.CornerRadius - 40) < .01, "Redo re-applies it");

            await editor.ExportVideoCommand.ExecuteAsync(null);
            log.Add($"INFO status: {doc.Status} | error: {editor.LastError}");
            Check(editor.LastOutputPath is { } mp4 && File.Exists(mp4) && new FileInfo(mp4).Length > 1000, $"MP4 exported ({editor.LastOutputPath})");
            if (editor.LastOutputPath is { } exported)
            {
                var probe = await MediaProbe.ProbeAsync(exported, CancellationToken.None);
                Check(probe.Width == probe.Height && probe.Width > 0, $"Export uses the 1:1 canvas ({probe.Width}×{probe.Height})");
                Check(probe.Duration?.TotalSeconds is > 1.6 and < 2.6, $"Export honours the trim ({probe.Duration?.TotalSeconds:0.00}s)");
            }
            await editor.ExportGifCommand.ExecuteAsync(null);
            Check(editor.LastOutputPath is { } gif && gif.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) && File.Exists(gif), $"GIF exported ({editor.LastOutputPath})");

            await doc.SaveAsync();
            var metadata = RecordingMetadata.Load(editor.FilePath);
            Check(metadata?.Presentation.CanvasPreset == "1:1", "Edits saved to the metadata sidecar");
            Check(metadata?.TrimEndSeconds is > 2.4 and < 2.6, "Trim saved to the metadata sidecar");
        }
        catch (Exception ex)
        {
            log.Add("FAIL exception: " + ex);
        }
        finally
        {
            File.WriteAllLines(Path.Combine(directory, "results.txt"), log);
            exit();
        }
    }
}
#endif
