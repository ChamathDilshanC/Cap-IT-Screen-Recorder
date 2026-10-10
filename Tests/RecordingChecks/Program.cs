using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using RecordingChecks;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;

// Real-recording regression harness. See RecordingChecks.csproj for what it is and why it is separate
// from CompositionChecks.
//
//   RecordingChecks [--out DIR] [--scenarios a,b,c] [--seconds N] [--analyze FILE]
//
// While a scenario runs, the primary display is covered by a topmost test window and the mouse is driven
// by the harness. Press Esc at any time to abort.

if (args.Length >= 5 && args[0] == "--source")
{
    // Child mode: host the timeline window in its own process so the source's paint thread never shares a
    // CPU/GC budget with the recorder under test. Exits when the parent closes our stdin or on Esc.
    var rect = new Rectangle(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
    ApplicationConfiguration.Initialize();
    if (args.Length >= 7 && args[5] == "caret")
    {
        var caretForm = new CaretForm(rect, args[6]);
        caretForm.Shown += (_, _) => Console.Out.WriteLine("READY");
        _ = Task.Run(() =>
        {
            while (Console.In.ReadLine() is not null) { }
            caretForm.BeginInvoke(() => caretForm.Close());
        });
        var caretAbort = new System.Windows.Forms.Timer { Interval = 100 };
        caretAbort.Tick += (_, _) => { if (caretForm.AbortRequested) { Console.Out.WriteLine("ABORT"); caretForm.Close(); } };
        caretAbort.Start();
        Application.Run(caretForm);
        return 0;
    }
    if (args.Length >= 7 && args[5] == "pattern")
    {
        var patternForm = new PatternForm(rect, args[6]);
        patternForm.Shown += (_, _) => Console.Out.WriteLine("READY");
        _ = Task.Run(() =>
        {
            while (Console.In.ReadLine() is not null) { }
            patternForm.BeginInvoke(() => patternForm.Close());
        });
        var patternAbort = new System.Windows.Forms.Timer { Interval = 100 };
        patternAbort.Tick += (_, _) => { if (patternForm.AbortRequested) { Console.Out.WriteLine("ABORT"); patternForm.Close(); } };
        patternAbort.Start();
        Application.Run(patternForm);
        return 0;
    }
    var sourceForm = new TimelineForm(rect);
    BeepPlayer? beeps = null;
    LoopbackCalibration? calib = null;
    sourceForm.Shown += (_, _) =>
    {
        if (args.Length >= 6 && args[5] == "audio")
        {
            beeps = new BeepPlayer(() => sourceForm.ElapsedMs);
            beeps.Provider.Anchored += ms => Console.Out.WriteLine($"ANCHOR {ms}");
            beeps.Start();
            calib = new LoopbackCalibration(() => sourceForm.ElapsedMs, () => beeps.Provider.AnchorMs, l => Console.Out.WriteLine($"CALIB {l:F1}"));
            calib.Start();
        }
        Console.Out.WriteLine("READY");
    };
    _ = Task.Run(() =>
    {
        while (Console.In.ReadLine() is not null) { }
        sourceForm.BeginInvoke(() => sourceForm.Close());
    });
    var abortWatcher = new System.Windows.Forms.Timer { Interval = 100 };
    abortWatcher.Tick += (_, _) => { if (sourceForm.AbortRequested) { Console.Out.WriteLine("ABORT"); sourceForm.Close(); } };
    abortWatcher.Start();
    var sourceClock = Stopwatch.StartNew();
    Application.Run(sourceForm);
    calib?.Dispose();
    beeps?.Dispose();
    Console.Out.WriteLine($"SOURCE paints={sourceForm.Paints} over {sourceClock.Elapsed.TotalSeconds:F1}s = {sourceForm.Paints / sourceClock.Elapsed.TotalSeconds:F0}/s, longest gap {sourceForm.MaxPaintGapMs:F0} ms");
    return 0;
}

if (args.Contains("--audio-state"))
{
    using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
    var dev = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
    Console.WriteLine($"default render device: {dev.FriendlyName}  state {dev.State}  mute {dev.AudioEndpointVolume.Mute}  master {dev.AudioEndpointVolume.MasterVolumeLevelScalar:P0}  peak now {dev.AudioMeterInformation.MasterPeakValue:F3}");
    foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.All))
        Console.WriteLine($"  {d.FriendlyName}: {d.State}");
    return 0;
}

if (args.Contains("--timer-demo"))
{
    // Product-independent demonstration of the two pacer flaws that cost timeline: PeriodicTimer's
    // millisecond-truncated period, and a tick lost whenever the pacer wakes up late. Compares it with a
    // FrameClock-driven loop over the same 10 seconds, idle and with every core kept busy.
    const int fps = 60;
    const double demoSeconds = 10;
    async Task<long> OldPacer(CancellationToken stop)
    {
        long ticks = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / fps));
        try { while (await timer.WaitForNextTickAsync(stop)) ticks++; } catch (OperationCanceledException) { }
        return ticks;
    }
    long NewPacer(CancellationToken stop)
    {
        var clock = new ScreenRecorderApp.Services.FrameClock(fps, Stopwatch.Frequency, Stopwatch.GetTimestamp());
        while (!stop.IsCancellationRequested)
        {
            long owed = clock.Owed(Stopwatch.GetTimestamp());
            if (owed > 0) { clock.MarkEmitted(owed); continue; }
            Thread.Sleep(1);
        }
        return clock.Emitted;
    }
    foreach (var loaded in new[] { false, true })
    {
        using var cts = new CancellationTokenSource();
        var burners = new List<Thread>();
        if (loaded)
        {
            for (int i = 0; i < Environment.ProcessorCount * 2; i++)
            {
                var t = new Thread(() => { double x = 0; while (!cts.IsCancellationRequested) x += Math.Sqrt(x + 1); }) { IsBackground = true };
                t.Start(); burners.Add(t);
            }
        }
        var oldTask = Task.Run(() => OldPacer(cts.Token));
        var newTask = Task.Factory.StartNew(() => NewPacer(cts.Token), TaskCreationOptions.LongRunning);
        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(demoSeconds));
        cts.Cancel();
        long oldTicks = await oldTask, newTicks = await newTask;
        double elapsed = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"{(loaded ? "all cores busy" : "idle         ")}: wall {elapsed:F2}s -> expected {elapsed * fps:F0} frames at {fps}fps;  PeriodicTimer pacer: {oldTicks} ({(oldTicks / (elapsed * fps) - 1) * 100:+0.0;-0.0}%)   FrameClock pacer: {newTicks} ({(newTicks / (elapsed * fps) - 1) * 100:+0.0;-0.0}%)");
        foreach (var b in burners) b.Join();
    }
    return 0;
}

var outDir = Path.GetFullPath(ArgValue("--out") ?? "artifacts/recording-checks");
Directory.CreateDirectory(outDir);
var ffmpeg = Path.GetFullPath("ffmpeg/ffmpeg.exe");
Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(ffmpeg) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));

if (ArgValue("--analyze") is { } analyzePath)
{
    Console.WriteLine(TimelineAnalyzer.Analyze(ffmpeg, analyzePath, ArgValue("--anchor") is { } a ? long.Parse(a) : -1, double.NaN).Format());
    return 0;
}

int seconds = int.TryParse(ArgValue("--seconds"), out var s) ? s : 20;
var wanted = (ArgValue("--scenarios") ?? "baseline30,zoom30,baseline60,zoom60").Split(',', StringSplitOptions.RemoveEmptyEntries);

var all = new Dictionary<string, Scenario>
{
    // Smart Tracking off: the reference. Anything the harness or the machine itself costs shows up here.
    ["baseline30"] = new("baseline30", 30, Zoom: false, ClickOnly: false, Motion.Cycles),
    ["baseline60"] = new("baseline60", 60, Zoom: false, ClickOnly: false, Motion.Cycles),
    // Smart Tracking on, with the cursor alternating between circling (zoom in / pan) and idling (zoom out).
    ["zoom30"] = new("zoom30", 30, Zoom: true, ClickOnly: false, Motion.Cycles),
    ["zoom60"] = new("zoom60", 60, Zoom: true, ClickOnly: false, Motion.Cycles),
    ["still60"] = new("still60", 60, Zoom: true, ClickOnly: false, Motion.None),
    ["rapid60"] = new("rapid60", 60, Zoom: true, ClickOnly: false, Motion.Rapid),
    ["clicks60"] = new("clicks60", 60, Zoom: true, ClickOnly: true, Motion.Clicks),
    ["typing60"] = new("typing60", 60, Zoom: true, ClickOnly: false, Motion.Typing),
    ["zoom60-sw"] = new("zoom60-sw", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Encoder: HardwareEncoder.SoftwareX264),
    ["zoom60-amf"] = new("zoom60-amf", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Encoder: HardwareEncoder.Amf),
    ["zoom30-audio"] = new("zoom30-audio", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true),
    // Not Cap-IT at all: ffmpeg's own Desktop Duplication grab of the same test source, analysed the same
    // way. It says how much of the duplicate/jump floor belongs to the source and the display rather than
    // to the recorder under test.
    ["ref60"] = new("ref60", 60, Zoom: false, ClickOnly: false, Motion.Cycles, Reference: true),
    ["ref30"] = new("ref30", 30, Zoom: false, ClickOnly: false, Motion.Cycles, Reference: true),
    // Pause in the middle: the file must lose exactly the paused interval — one jump of about that size at
    // the resume point, a timeline as long as the un-paused time, and nothing else.
    ["pause30"] = new("pause30", 30, Zoom: true, ClickOnly: false, Motion.Cycles, PauseAtSeconds: 6, PauseForSeconds: 3),
    ["pause60"] = new("pause60", 60, Zoom: true, ClickOnly: false, Motion.Cycles, PauseAtSeconds: 6, PauseForSeconds: 3),
    // CPU kernels (a machine whose GPU pipeline cannot be built) — forced here via CAPIT_FORCE_CPU_PIPELINE.
    ["cpu30"] = new("cpu30", 30, Zoom: true, ClickOnly: false, Motion.Cycles, ForceCpu: true),
    ["cpu60"] = new("cpu60", 60, Zoom: true, ClickOnly: false, Motion.Cycles, ForceCpu: true),
    // Single-window capture (Windows Graphics Capture), recording the test source's own window.
    ["window30"] = new("window30", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Window: true),
    // Live preview only (no recording): the path the UI's preview card reads. Checks that frames flow,
    // decode, move forward in time and are never torn.
    ["preview"] = new("preview", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Preview: true),
    ["baseline30-audio"] = new("baseline30-audio", 30, Zoom: false, ClickOnly: false, Motion.Cycles, Audio: true),
    ["zoom60-audio"] = new("zoom60-audio", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true),
    ["pause30-audio"] = new("pause30-audio", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, PauseAtSeconds: 6, PauseForSeconds: 3),
    ["window30-audio"] = new("window30-audio", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, Window: true),
    // System audio + microphone with Studio Mic noise suppression: the dual-leg pipeline (two pumps, filter graph).
    ["mic30"] = new("mic30", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, Mic: true),
    ["amf60-audio"] = new("amf60-audio", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, Encoder: HardwareEncoder.Amf),
    ["long60-audio"] = new("long60-audio", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, Seconds: 150),
    ["sharp-zoom"] = new("sharp-zoom", 30, Zoom: true, ClickOnly: false, Motion.Jitter, Pattern: true),
    ["sharp-zoom-cpu"] = new("sharp-zoom-cpu", 30, Zoom: true, ClickOnly: false, Motion.Jitter, ForceCpu: true, Pattern: true),
    ["sharp-1x"] = new("sharp-1x", 30, Zoom: false, ClickOnly: false, Motion.None, Pattern: true),
    ["caret30"] = new("caret30", 30, Zoom: true, ClickOnly: false, Motion.CaretTyping, Caret: true),
    ["long30-off-sw-audio"] = new("long30-off-sw-audio", 30, Zoom: false, ClickOnly: false, Motion.Cycles, Audio: true, Seconds: 130, Encoder: HardwareEncoder.SoftwareX264),
    ["long30-window-audio"] = new("long30-window-audio", 30, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, Seconds: 130, Window: true),
    ["long60-amf-audio"] = new("long60-amf-audio", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Audio: true, Seconds: 130, Encoder: HardwareEncoder.Amf),
    ["long60"] = new("long60", 60, Zoom: true, ClickOnly: false, Motion.Cycles, Seconds: 120),
};

var summary = new StringBuilder();
bool failed = false;

foreach (var name in wanted)
{
    if (!all.TryGetValue(name, out var scenario)) { Console.WriteLine($"unknown scenario '{name}'"); failed = true; continue; }
    Console.WriteLine($"=== {scenario.Name} ===");
    var (report, diagText, uses) = await RunScenario(scenario with { Seconds = scenario.Seconds ?? seconds });
    if (report is null) { if (diagText != "PREVIEW-OK") failed = true; continue; }

    var text = $"=== {scenario.Name}  (pipeline: {(uses ? "GPU" : "CPU")}, encoder: {scenario.Encoder}) ===\n{report.Format()}--- diagnostics ---\n{diagText}\n";
    Console.WriteLine(text);
    summary.AppendLine(text);
}

await File.WriteAllTextAsync(Path.Combine(outDir, "summary.txt"), summary.ToString());
Console.WriteLine($"summary written to {Path.Combine(outDir, "summary.txt")}");
return failed ? 1 : 0;

string? ArgValue(string key)
{
    int i = Array.IndexOf(args, key);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

async Task<(TimelineReport?, string, bool)> RunScenario(Scenario sc)
{
    var manager = new RecordingManager();
    var monitor = manager.GetMonitors().First(m => m.IsPrimary);
    var bounds = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);

    var host = Environment.ProcessPath!;
    var entry = System.Reflection.Assembly.GetEntryAssembly()!.Location;
    var sourceArgs = $"--source {monitor.X} {monitor.Y} {monitor.Width} {monitor.Height}" + (sc.Audio ? " audio" : "") + (sc.Pattern ? $" pattern \"{Path.Combine(outDir, sc.Name)}\"" : "") + (sc.Caret ? $" caret \"{Path.Combine(outDir, sc.Name)}\"" : "");
    var psi = new ProcessStartInfo(host, Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? $"\"{entry}\" {sourceArgs}" : sourceArgs)
    { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
    using var source = Process.Start(psi)!;
    var abortRequested = false;
    string sourceStats = "";
    long audioAnchorMs = -1;
    double audioCalibMs = double.NaN;
    var caretClock = new Stopwatch();
    var caretLog = new List<(double T, int X, int Y)>();
    var ready = new ManualResetEventSlim();
    source.OutputDataReceived += (_, e) =>
    {
        if (e.Data == "READY") ready.Set();
        else if (e.Data is not null && e.Data.StartsWith("ANCHOR ")) audioAnchorMs = long.Parse(e.Data[7..]);
        else if (e.Data is not null && e.Data.StartsWith("CALIB ")) audioCalibMs = double.Parse(e.Data[6..], System.Globalization.CultureInfo.InvariantCulture);
        else if (e.Data is not null && e.Data.StartsWith("CARET "))
        {
            var parts = e.Data.Split(' ');
            lock (caretLog) caretLog.Add((caretClock.IsRunning ? caretClock.Elapsed.TotalSeconds : -1, int.Parse(parts[1]), int.Parse(parts[2])));
        }
        else if (e.Data is not null && e.Data.StartsWith("SOURCE")) sourceStats = e.Data;
        else if (e.Data == "ABORT") abortRequested = true;
    };
    source.BeginOutputReadLine();
    if (!ready.Wait(TimeSpan.FromSeconds(15))) { Console.WriteLine("test source window did not start"); return (null, "", false); }
    await Task.Delay(800); // let DWM show the window before the first frame is captured

    if (sc.Reference)
    {
        var refDir = Path.Combine(outDir, sc.Name);
        Directory.CreateDirectory(refDir);
        var refFile = Path.Combine(refDir, "reference.mp4");
        var driverRef = new CancellationTokenSource();
        CursorDriver.MoveTo(monitor.X + monitor.Width / 2, monitor.Y + monitor.Height / 2);
        var driverRefTask = Task.Run(() => CursorDriver.Run(sc.Motion, monitor, driverRef.Token));
        var refArgs = $"-y -hide_banner -loglevel error -f lavfi -i ddagrab=framerate={sc.Fps}:draw_mouse=0 -t {sc.Seconds} -vf hwdownload,format=bgra -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p \"{refFile}\"";
        TimelineAnalyzer.RunCapture(ffmpeg, refArgs, out int refExit);
        driverRef.Cancel();
        await driverRefTask;
        try { source.StandardInput.Close(); } catch { /* already gone */ }
        if (!source.WaitForExit(3000)) source.Kill();
        source.WaitForExit();
        if (refExit != 0 || !File.Exists(refFile)) { Console.WriteLine("reference capture failed"); return (null, "", false); }
        return (TimelineAnalyzer.Analyze(ffmpeg, refFile), $"test source             : {sourceStats}", false);
    }

    Environment.SetEnvironmentVariable("CAPIT_FORCE_CPU_PIPELINE", sc.ForceCpu ? "1" : null);

    WindowInfo? targetWindow = null;
    if (sc.Window)
    {
        targetWindow = manager.GetWindows().FirstOrDefault(w => w.Title.Contains(TimelineForm.WindowTitle, StringComparison.Ordinal));
        if (targetWindow is null) { Console.WriteLine("test source window not found by the window enumerator"); return (null, "", false); }
    }

    if (sc.Preview)
    {
        var driverPrev = new CancellationTokenSource();
        CursorDriver.MoveTo(monitor.X + monitor.Width / 2, monitor.Y + monitor.Height / 2);
        var driverPrevTask = Task.Run(() => CursorDriver.Run(sc.Motion, monitor, driverPrev.Token));
        manager.StartPreview(CaptureTargetKind.Monitor, monitor, null, captureCursor: false, CursorStyle.Arrow,
            zoomEnabled: true, zoomFactor: 2.0);
        await Task.Delay(1500);

        int w = manager.PreviewWidth, h = manager.PreviewHeight;
        var bgra = new byte[w * h * 4];
        var gray = new byte[w];
        long? Decode(int y)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                gray[x] = (byte)((bgra[i] + bgra[i + 1] + bgra[i + 2]) / 3);
            }
            return TimelineCode.DecodeRow(gray, 0, w);
        }

        int got = 0, decoded = 0, torn = 0, backwards = 0, repeatedFrame = 0;
        long? last = null;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < sc.Seconds && !abortRequested)
        {
            if (manager.TryGetPreviewFrame(bgra))
            {
                got++;
                var a = Decode(h / 2 - 150); var b = Decode(h / 2); var c = Decode(h / 2 + 150);
                if (b is long v)
                {
                    decoded++;
                    if (last is long l) { if (v < l) backwards++; else if (v == l) repeatedFrame++; }
                    last = v;
                }
                if (a is long x1 && c is long x2 && x1 != x2) torn++;
            }
            await Task.Delay(100);
        }
        driverPrev.Cancel();
        await driverPrevTask;
        bool gpuPrev = manager.UsesGpuPipeline;
        manager.StopPreview();
        try { source.StandardInput.Close(); } catch { /* already gone */ }
        if (!source.WaitForExit(3000)) source.Kill();
        source.WaitForExit();
        Console.WriteLine($"preview: {got} frames pulled, {decoded} decoded, {torn} torn, {backwards} went backwards in time, {repeatedFrame} identical to the previous pull" + (abortRequested ? "  [ABORTED: Esc reached the test window, run is incomplete]" : ""));
        if (got == 0 || decoded == 0 || torn > 0 || backwards > 0) { Console.WriteLine("PREVIEW CHECK FAILED"); return (null, "", gpuPrev); }
        return (null, "PREVIEW-OK", gpuPrev);
    }

    var settings = new RecordingSettings
    {
        CaptureTargetKind = sc.Window ? CaptureTargetKind.Window : CaptureTargetKind.Monitor,
        MonitorHandle = monitor.Handle,
        TargetWindowHandle = targetWindow?.Handle ?? 0,
        TargetWindowTitle = targetWindow?.Title,
        Fps = sc.Fps,
        VideoBitrateKbps = 12000,
        CaptureSystemAudio = sc.Audio,
        CaptureMicrophone = sc.Mic,
        EnableMicNoiseSuppression = sc.Mic,
        // Cursor icon off: it would sit on top of the timeline strip. Smart Tracking still follows the
        // pointer position, which is what the zoom actually keys on.
        CaptureCursor = false,
        MouseTrackingZoomEnabled = sc.Zoom,
        ZoomFactor = 2.0,
        ZoomOnClickOnly = sc.ClickOnly,
        Encoder = sc.Encoder,
        Container = OutputContainer.Mp4,
        Resolution = OutputResolution.Native,
        OutputDirectory = Path.Combine(outDir, sc.Name),
    };

    string? path = null;
    bool usesGpu = false;
    string? gpuReason = null;
    var driver = new CancellationTokenSource();
    try
    {
        CursorDriver.MoveTo(monitor.X + monitor.Width / 2, monitor.Y + monitor.Height / 2);
        await manager.StartAsync(settings, sc.Window ? null : monitor, targetWindow);
        var wall = Stopwatch.StartNew();
        caretClock.Restart();
        var driverTask = Task.Run(() => CursorDriver.Run(sc.Motion, monitor, driver.Token));

        bool paused = false, pauseDone = false;
        double pausedSeconds = 0;
        while (wall.Elapsed.TotalSeconds - pausedSeconds < sc.Seconds && !abortRequested && !source.HasExited)
        {
            if (sc.PauseAtSeconds is int pauseAt && !pauseDone && !paused && wall.Elapsed.TotalSeconds >= pauseAt)
            {
                manager.Pause();
                paused = true;
                Console.WriteLine($"paused at {wall.Elapsed.TotalSeconds:F2}s");
            }
            if (paused && wall.Elapsed.TotalSeconds >= sc.PauseAtSeconds + sc.PauseForSeconds)
            {
                manager.Resume();
                paused = false; pauseDone = true;
                pausedSeconds = sc.PauseForSeconds;
                Console.WriteLine($"resumed at {wall.Elapsed.TotalSeconds:F2}s");
            }
            await Task.Delay(50);
        }

        driver.Cancel();
        await driverTask;
        // Read while the capture is still alive: Stop() disposes the GPU processor.
        usesGpu = manager.UsesGpuPipeline;
        gpuReason = manager.GpuUnavailableReason;
        path = await manager.StopAsync();
        double wallSeconds = wall.Elapsed.TotalSeconds;
        manager.Diagnostics.WallSeconds = wallSeconds;
        Console.WriteLine($"recording wall time {wallSeconds:F2}s -> {path}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("recording failed: " + ex);
        return (null, "", false);
    }
    finally
    {
        try { source.StandardInput.Close(); } catch { /* already gone */ }
        if (!source.WaitForExit(3000)) source.Kill();
        source.WaitForExit();
    }

    if (path is null || !File.Exists(path)) { Console.WriteLine("no output file produced"); return (null, "", false); }
    if (sc.Caret)
    {
        var sb2 = new StringBuilder("t_seconds,caret_x,caret_y\n");
        lock (caretLog) foreach (var c in caretLog) sb2.Append(System.Globalization.CultureInfo.InvariantCulture, $"{c.T:F3},{c.X - monitor.X},{c.Y - monitor.Y}\n");
        File.WriteAllText(Path.Combine(outDir, sc.Name, "caret-log.csv"), sb2.ToString());
        Console.WriteLine($"CARET recording: {path}  ({caretLog.Count} caret positions logged)");
        bool caretGpu = usesGpu;
        manager.Dispose();
        return (null, "PREVIEW-OK", caretGpu);
    }
    if (sc.Pattern)
    {
        Console.WriteLine($"PATTERN recording: {path}");
        Console.WriteLine(manager.Diagnostics.Summarize(sc.Fps));
        bool patternGpu = usesGpu;
        manager.Dispose();
        return (null, "PREVIEW-OK", patternGpu);
    }
    var report = TimelineAnalyzer.Analyze(ffmpeg, path, sc.Audio ? audioAnchorMs : -1, audioCalibMs);
    var diag = $"test source             : {sourceStats}{Environment.NewLine}" + manager.Diagnostics.Summarize(sc.Fps) + $"\nrecording wall time     : {manager.Diagnostics.WallSeconds:F2} s";
    bool gpu = usesGpu;
    if (!gpu) diag += $"{Environment.NewLine}GPU pipeline unavailable : {gpuReason ?? "n/a"}";
    manager.Dispose();
    return (report, diag, gpu);
}

internal enum Motion { None, Cycles, Rapid, Clicks, Typing, Jitter, CaretTyping }

internal sealed record Scenario(string Name, int Fps, bool Zoom, bool ClickOnly, Motion Motion,
    HardwareEncoder Encoder = HardwareEncoder.Auto, bool Audio = false, int? Seconds = null, bool Reference = false, int? PauseAtSeconds = null, int PauseForSeconds = 0, bool ForceCpu = false, bool Window = false, bool Preview = false, bool Mic = false, bool Pattern = false, bool Caret = false);

/// <summary>Drives the real mouse/keyboard so Smart Tracking gets genuine activity, all kept near screen centre so the timeline strip stays in frame while the camera pans.</summary>
internal static class CursorDriver
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);

    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void Run(Motion motion, ScreenRecorderApp.Models.MonitorInfo m, CancellationToken ct)
    {
        int cx = m.X + m.Width / 2, cy = m.Y + m.Height / 2;
        var clock = Stopwatch.StartNew();
        double nextClick = 0.5;
        double nextKey = 0.3;
        bool caretClicked = false;
        int keyCount = 0;

        while (!ct.IsCancellationRequested)
        {
            double t = clock.Elapsed.TotalSeconds;
            switch (motion)
            {
                case Motion.None:
                    break;
                case Motion.Cycles:
                    // 2.5s of circling (zoom in, camera follows), 3.5s idle (zoom out), repeating. The
                    // click at the start of each burst exercises the ripple/activity hook as well.
                    if (t % 6.0 < 2.5)
                    {
                        double a = t * 2 * Math.PI * 0.8;
                        SetCursorPos(cx + (int)(200 * Math.Cos(a)), cy + (int)(90 * Math.Sin(a)));
                        if (t % 6.0 < 0.05) Click();
                    }
                    break;
                case Motion.Rapid:
                    {
                        double a = t * 2 * Math.PI * 3.0;
                        SetCursorPos(cx + (int)(220 * Math.Cos(a)), cy + (int)(100 * Math.Sin(a * 1.3)));
                        break;
                    }
                case Motion.Clicks:
                    if (t >= nextClick) { Click(); nextClick = t + 3.2; }
                    break;
                case Motion.Jitter:
                    // Keeps Smart Tracking active (pointer moves more than its activity threshold) while the
                    // camera holds still inside its dead zone: a steady 2x crop to measure.
                    { int phase = (int)(t * 8) % 4; SetCursorPos(cx + (phase is 1 or 2 ? 10 : -10), cy + (phase >= 2 ? 10 : -10)); }
                    if (t < 0.1) Click();
                    break;
                case Motion.CaretTyping:
                    {
                        // Click into the edit box, park the mouse far away and let it go idle, then type for real:
                        // the mouse is the stale signal, the caret the fresh one.
                        var box = CaretLayout.Box(m.Width, m.Height);
                        var park = CaretLayout.MouseParking(m.Width, m.Height);
                        if (t < 0.3) { SetCursorPos(m.X + box.X + 40, m.Y + box.Y + 20); }
                        else if (!caretClicked) { Click(); caretClicked = true; }
                        else if (t >= 0.8 && t < 0.9) SetCursorPos(m.X + park.X, m.Y + park.Y);
                        else if (t >= 3.0 && t >= nextKey)
                        {
                            keyCount++;
                            if (keyCount % 40 == 0) Key(0x0D);
                            else if (keyCount % 6 == 0) Key(0x20);
                            else Key((byte)('A' + (keyCount * 7) % 26));
                            nextKey = t + 0.11;
                        }
                        break;
                    }
                case Motion.Typing:
                    if (t % 5.0 < 2.0 && t >= nextKey) { Key((byte)('A' + (int)(t * 7) % 26)); nextKey = t + 0.12; }
                    break;
            }
            Thread.Sleep(8);
        }
    }

    private static void Click()
    {
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
    }

    private static void Key(byte vk)
    {
        keybd_event(vk, 0, 0, 0);
        keybd_event(vk, 0, 2, 0);
    }
}
