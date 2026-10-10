using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ScreenRecorderApp.Models;
using ScreenRecorderApp.Services;
using ScreenRecorderApp.Services.Encoding;
using ScreenRecorderApp.Services.Export;
using ScreenRecorderApp.Services.Capture;

var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/composition-checks");
Directory.CreateDirectory(root);
var ffmpeg = Path.GetFullPath("ffmpeg/ffmpeg.exe");
Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(ffmpeg) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
var assertions = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }

// Smart Tracking springs must make the same progress at the same wall-clock time at 30 and 60 FPS,
// stay monotonic toward a fixed target, and retain momentum when a target changes mid-transition.
static (double Position, double Velocity) SimulateZoom(double totalSeconds, double frameSeconds)
{
    double position = 1.0, velocity = 0.0, elapsed = 0.0;
    while (elapsed < totalSeconds - 1e-9)
    {
        var dt = Math.Min(frameSeconds, totalSeconds - elapsed);
        position = VideoCaptureService.SmoothDamp(position, 2.0, ref velocity, .5, dt);
        elapsed += dt;
    }
    return (position, velocity);
}

var zoomAt30Fps = SimulateZoom(.3, 1.0 / 30.0);
var zoomAt60Fps = SimulateZoom(.3, 1.0 / 60.0);
Check(Math.Abs(zoomAt30Fps.Position - zoomAt60Fps.Position) < .005, "Zoom spring is frame-rate independent");
Check(Math.Abs(VideoCaptureService.SmoothTimeForAnimationSpeed(.5, 0) - .5) < 1e-9
    && Math.Abs(VideoCaptureService.SmoothTimeForAnimationSpeed(.5, 50) - .25) < 1e-9
    && Math.Abs(VideoCaptureService.SmoothTimeForAnimationSpeed(.5, 100) - .25) < 1e-9, "Animation speed has a bounded smooth-time range");

double springPosition = 1.0, springVelocity = 0.0;
double redirectPosition = 1.0, redirectVelocity = 0.0;
for (var i = 0; i < 10; i++)
    redirectPosition = VideoCaptureService.SmoothDamp(redirectPosition, 2.0, ref redirectVelocity, .32, 1.0 / 60.0);
var velocityBeforeRedirect = redirectVelocity;
var redirectedPosition = VideoCaptureService.SmoothDamp(redirectPosition, 2.5, ref redirectVelocity, .32, 1.0 / 60.0);
Check(velocityBeforeRedirect > 0 && redirectVelocity > 0 && redirectedPosition > redirectPosition,
    "Target redirect preserves momentum during a transition");

for (var i = 0; i < 120; i++)
{
    var next = VideoCaptureService.SmoothDamp(springPosition, 2.0, ref springVelocity, .32, 1.0 / 60.0);
    Check(next >= springPosition - 1e-9 && next <= 2.0 + 1e-9, "Zoom spring approaches without overshoot");
    springPosition = next;
}
Check(springVelocity > 0, "Zoom spring reaches its target with no residual reverse motion");

// The recording's frame schedule: slot k opens at exactly k/fps (no millisecond truncation of the period),
// a late wakeup owes every slot it missed instead of losing them, and a pause cuts its interval out.
{
    const long freq = 10_000_000;
    var c60 = new FrameClock(60, freq, 1_000);
    Check(c60.Owed(1_000) == 1, "Frame slot 0 opens at the start");
    c60.MarkEmitted(1);
    Check(c60.Owed(1_000 + freq - 1) == 59 && c60.Owed(1_000 + freq) == 60, "60fps slot 60 opens at exactly one second, not at 16ms*60");

    // 60fps with a period truncated to 16ms would have produced 62.5 frames in a second; 100 000 jittered
    // wakeups over about an hour must end up with exactly floor(elapsed*fps)+1 slots filled.
    var rng = new Random(12345);
    foreach (var fps in new[] { 24, 30, 60, 120 })
    {
        var clock = new FrameClock(fps, freq, 0);
        long now = 0;
        for (var i = 0; i < 100_000; i++)
        {
            now += rng.Next(1, 400_000); // 0.1ms to 40ms between wakeups, i.e. up to ~2 frames late at 60fps
            var owed = clock.Owed(now);
            Check(owed >= 0, "Owed is never negative");
            clock.MarkEmitted(owed);
        }
        Check(clock.Emitted == now * fps / freq + 1, $"Jittered wakeups at {fps}fps lose no slot: {clock.Emitted} vs {now * fps / freq + 1}");
    }

    var stalled = new FrameClock(60, freq, 0);
    stalled.MarkEmitted(stalled.Owed(0));
    Check(stalled.Owed(freq / 10) == 6, "A 100ms stall owes the six slots it missed");

    var paused = new FrameClock(30, freq, 0);
    paused.MarkEmitted(paused.Owed(freq));
    Check(paused.Emitted == 31, "One second at 30fps fills 31 slots (0..30)");
    paused.Pause(freq);
    Check(paused.IsPaused && paused.Owed(5 * freq) == 0, "A paused clock owes nothing");
    paused.Resume(5 * freq);
    Check(!paused.IsPaused && paused.Owed(5 * freq) == 0, "Resuming does not owe the paused interval");
    Check(paused.Owed(5 * freq + freq / 30) == 0 && paused.Owed(5 * freq + freq / 30 + 1) == 1, "The first slot after a pause opens one period after the resume, on the tick the schedule says");
    Check(paused.NextSlotTimestamp() == paused.SlotTimestamp(31), "The wait target and the slot-open test agree on the same tick");
    Check(paused.SlotTimestamp(31) == 4 * freq + (31 * freq + 29) / 30, "Slot timestamps shift by exactly the paused time");
    paused.Pause(6 * freq); paused.Pause(7 * freq); paused.Resume(8 * freq); paused.Resume(9 * freq);
    Check(paused.SlotTimestamp(31) == 6 * freq + (31 * freq + 29) / 30, "Pause/Resume are idempotent: only the first Pause and first Resume count");
    Check(paused.PausedTicks(20 * freq) == 6 * freq, "PausedTicks totals every closed pause interval");

    var audioClock = new FrameClock(30, freq, 0);
    Check(audioClock.PausedTicks(3 * freq) == 0, "PausedTicks is zero before any pause");
    audioClock.Pause(2 * freq);
    Check(audioClock.PausedTicks(5 * freq) == 3 * freq, "PausedTicks counts a pause that is still open up to 'now'");
    audioClock.Resume(6 * freq);
    Check(audioClock.PausedTicks(9 * freq) == 4 * freq, "PausedTicks stops growing once resumed (audio elapsed = now - start - paused)");
}

// Missing, legacy, explicit-null and damaged sidecars must be safe to open.
var legacyPath = Path.Combine(root, "legacy.mp4");
Directory.CreateDirectory(Path.GetDirectoryName(RecordingMetadata.GetPath(legacyPath))!);
await File.WriteAllTextAsync(RecordingMetadata.GetPath(legacyPath), """{"SchemaVersion":1,"Cursor":null,"Presentation":{"DeviceFrame":true,"BackgroundMode":null,"BackgroundColor":"bad","VideoScale":99}}""");
var legacy = RecordingMetadata.Load(legacyPath)!;
Check(legacy.Cursor is not null && legacy.Presentation.DeviceFrameStyle == "Minimal", "Legacy frame migration");
Check(legacy.Presentation.VideoScale == 1.2 && legacy.Presentation.BackgroundMode == "gradient", "Sanitize legacy values");
await File.WriteAllTextAsync(RecordingMetadata.GetPath(legacyPath), "{broken json");
Check(RecordingMetadata.Load(legacyPath) is null, "Malformed metadata fallback");
Check(RecordingMetadata.Load(Path.Combine(root, "absent.mp4")) is null, "Absent metadata fallback");
var metadata = new RecordingMetadata { TrimStartSeconds = 1.2, TrimEndSeconds = 2.8, Presentation = new() { CanvasPreset = "9:16", CornerRadius = 42, BorderEnabled = true } };
await metadata.SaveAsync(legacyPath);
Check(RecordingMetadata.Load(legacyPath) is { TrimStartSeconds: 1.2, TrimEndSeconds: 2.8, Presentation.CornerRadius: 42 }, "Metadata roundtrip");
var settings = new PresentationSettings { CornerRadius = double.NaN, VideoScale = double.PositiveInfinity, WatermarkOpacity = -1 };
settings.Normalize();
Check(settings.CornerRadius == 20 && settings.VideoScale == .92 && settings.WatermarkOpacity == 0, "Non-finite sanitation");
var transform = new PresentationSettings { RotationX = 99, RotationY = -99, RotationZ = 99, PerspectiveDepth = 10, BackgroundPreset = "Aurora Orbit" };
transform.Normalize();
Check(transform.RotationX == 35 && transform.RotationY == -35 && transform.RotationZ == 15 && transform.PerspectiveDepth == 400, "Transform sanitation");
Check(BackgroundPreset.All.Count >= 10 && BackgroundPreset.All.Any(x => x.Key == "Warm Grid" && x.Pattern), "Background preset catalogue");

foreach (var aspect in new[] { "Original", "16:9", "9:16", "1:1", "4:5", "3:2", "4:3", "Custom" })
foreach (var fit in new[] { "Fit", "Fill", "Original", "Custom" })
foreach (var scale in new[] { .4, .92, 1.2 })
foreach (var padding in new[] { 0, 64, 200 })
{
    var p = new PresentationSettings { CanvasPreset = aspect, FitMode = fit, VideoScale = scale, Padding = padding };
    var layout = CompositionLayout.Create(p, 1920, 1080);
    Check(layout.Video.Width > 0 && layout.Video.Height > 0 && layout.Width % 2 == 0 && layout.Height % 2 == 0, "Valid even layout");
    var crop = CompositionLayout.SourceCrop(1920, 1080, layout.Video, new() { Scale = 4, CenterX = 1, CenterY = 0 });
    Check(crop.X >= 0 && crop.Y >= 0 && crop.X + crop.Width <= 1920 && crop.Y + crop.Height <= 1080, "Crop inside source");
}
Console.WriteLine($"Geometry and metadata: {assertions} assertions passed");

// Four-colour source makes cropping, rounded corners and static-layer parity measurable.
var source = Path.Combine(root, "source.mp4");
await CompositionExportService.RunAsync(ffmpeg, ["-y", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=0xCE5544:s=320x180:r=30:d=4", "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
    "-vf", "drawbox=x=160:y=0:w=160:h=90:color=0x44AB67:t=fill,drawbox=x=0:y=90:w=160:h=90:color=0x4779D1:t=fill,drawbox=x=160:y=90:w=160:h=90:color=0xDFBC55:t=fill", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source], TimeSpan.FromSeconds(4), null, default);
var probe = await MediaProbe.ProbeAsync(source);
Check(probe.Width == 320 && probe.Height == 180 && probe.FrameRate == 30 && probe.HasAudio, "Probe dimensions and audio");
var watermark = Path.Combine(root, "logo.png");
using (var logo = new Bitmap(48, 32)) { using var g = Graphics.FromImage(logo); g.Clear(Color.White); logo.Save(watermark, ImageFormat.Png); }
var photo = Path.Combine(root, "background.jpg");
using (var bg = new Bitmap(100, 100)) { using var g = Graphics.FromImage(bg); g.Clear(Color.SteelBlue); bg.Save(photo, ImageFormat.Jpeg); }
var pBase = new PresentationSettings { CanvasPreset = "Custom", CanvasWidth = 320, CanvasHeight = 320, Padding = 24, VideoScale = .92, CornerRadius = 24,
    BorderEnabled = true, BorderWidth = 2, Shadow = true, ShadowBlur = 16, WatermarkEnabled = true, WatermarkPath = watermark, WatermarkScale = .1 };
foreach (var preset in new[] { "Midnight Gold", "Warm Grid", "Aurora Orbit" })
{
    var presetSettings = pBase.Clone();
    presetSettings.BackgroundPreset = preset;
    var rendered = CompositionAssetRenderer.Render(presetSettings, 320, 180);
    Check(rendered.Warning is null && rendered.Background.Length > 100, preset + " background render");
}
var regions = new List<ZoomRegion> { new() { StartSeconds = .5, EndSeconds = 1.5, Scale = 2, CenterX = 1, CenterY = 0 }, new() { StartSeconds = 1.4, EndSeconds = 2.0, Scale = 1.5, CenterX = 0, CenterY = 1 } };
Check(ReferenceEquals(CompositionLayout.ActiveZoom(regions, 1.45), regions[1]), "Overlap precedence");
Check(CompositionLayout.ActiveZoom(regions, 2) is null, "Exclusive zoom end");
foreach (var mode in new[] { "solid", "gradient", "image" })
{
    var p = pBase.Clone(); p.BackgroundMode = mode; p.BackgroundPath = photo; p.BackgroundBlur = 5; p.BackgroundDim = .15;
    var output = Path.Combine(root, mode + ".mp4");
    var progressValues = new List<double>();
    var progress = new ImmediateProgress(v => progressValues.Add(v.PercentComplete));
    await CompositionExportService.ExportAsync(source, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), output, false, p, regions, new(), progress, default);
    var result = await MediaProbe.ProbeAsync(output);
    Check(result.Width == 320 && result.Height == 320 && result.HasAudio, mode + " dimensions/audio");
    Check(Math.Abs(result.Duration!.Value.TotalSeconds - 1.5) < .12, mode + " trim duration");
    Check(progressValues.Count > 2 && progressValues[^1] == 100, "Real progress");
    var framePath = Path.Combine(root, mode + "-frame.png");
    await CompositionExportService.RunAsync(ffmpeg, ["-y", "-loglevel", "error", "-ss", "0.25", "-i", output, "-frames:v", "1", framePath], TimeSpan.FromSeconds(1), null, default);
    var assets = CompositionAssetRenderer.Render(p, 320, 180);
    await File.WriteAllBytesAsync(Path.Combine(root, mode + "-background.png"), assets.Background);
    using var expected = new Bitmap(new MemoryStream(assets.Background));
    using var actual = new Bitmap(framePath);
    foreach (var xy in new[] { (8,8), (160,20), (300,30), (20,280) })
    {
        var a = expected.GetPixel(xy.Item1,xy.Item2); var b = actual.GetPixel(xy.Item1,xy.Item2);
        Check(Math.Abs(a.R-b.R) + Math.Abs(a.G-b.G) + Math.Abs(a.B-b.B) < 24, mode + " static pixel parity");
    }
    using var mask = new Bitmap(new MemoryStream(assets.Mask));
    Check(mask.GetPixel(0,0).R == 0 && mask.GetPixel(mask.Width/2,mask.Height/2).R == 255, "Real corner mask");
    var c = actual.GetPixel(160,160);
    Check(c.G > c.R && c.G > c.B, "Original timeline zoom selects top-right green quadrant after trim");
}
var gifPath = Path.Combine(root, "styled.gif");
await CompositionExportService.ExportAsync(source, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.75), gifPath, true, pBase, regions, new(), null, default);
var gifProbe = await MediaProbe.ProbeAsync(gifPath);
Check(gifProbe.Width == 720 && gifProbe.Height == 720, "GIF shares canvas aspect");
var muted = Path.Combine(root, "muted.mp4");
await CompositionExportService.ExportAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(.5), muted, false, pBase, [], new() { IncludeAudio = false, FrameRate = "24" }, null, default);
var mutedProbe = await MediaProbe.ProbeAsync(muted);
Check(!mutedProbe.HasAudio && mutedProbe.FrameRate == 24, "Mute and frame rate options");
var missing = pBase.Clone(); missing.BackgroundMode = "image"; missing.BackgroundPath = "missing.jpg"; missing.WatermarkPath = "missing.png";
Check(CompositionAssetRenderer.Render(missing,320,180).Warning is not null, "Missing assets safe fallback");
var cancelOutput = Path.Combine(root, "cancelled.mp4"); await File.WriteAllTextAsync(cancelOutput,"preserve");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await CompositionExportService.ExportAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(4), cancelOutput, false, pBase, [], new(), null, cancel.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(await File.ReadAllTextAsync(cancelOutput) == "preserve", "Cancellation preserves prior output"); }
}
Check(RecordingMetadata.Load(Path.Combine(root,"solid.mp4")) is { PresentationBaked: true, Presentation.VideoScale: 1 }, "Export reopening avoids double styling");
// All neutral frames, hard corner limits, off-canvas placement and odd legacy source dimensions.
foreach (var frameStyle in new[] { "None", "Minimal", "Browser", "Studio", "Windows", "Device" })
foreach (var radius in new[] { 0d, 64d })
{
    var variant = pBase.Clone(); variant.DeviceFrameStyle = frameStyle; variant.CornerRadius = radius;
    variant.VideoScale = 1.2; variant.FitMode = "Fill"; variant.VideoOffsetX = -45; variant.VideoOffsetY = 20;
    var rendered = CompositionAssetRenderer.Render(variant, 321, 181);
    using var cornerMask = new Bitmap(new MemoryStream(rendered.Mask));
    Check(cornerMask.GetPixel(0,0).R == (radius == 0 ? 255 : 0), "Corner limit " + frameStyle);
}
var unusualOutput = Path.Combine(root, "video with spaces & café 'quote'.mp4");
var portrait = pBase.Clone(); portrait.CanvasWidth = 360; portrait.CanvasHeight = 640; portrait.FitMode = "Fill";
portrait.VideoScale = 1.2; portrait.VideoOffsetX = -40; portrait.DeviceFrameStyle = "Windows";
await CompositionExportService.ExportAsync(source, TimeSpan.FromSeconds(.25), TimeSpan.FromSeconds(.3), unusualOutput, false, portrait, [], new() { FrameRate = "60" }, null, default);
var portraitProbe = await MediaProbe.ProbeAsync(unusualOutput);
Check(portraitProbe.Width == 360 && portraitProbe.Height == 640 && portraitProbe.FrameRate == 60, "Portrait overflow, window frame, 60 fps and quoted Unicode paths");
var fourK = pBase.Clone(); fourK.CanvasWidth = 3840; fourK.CanvasHeight = 2160; fourK.Shadow = false;
var fourKOutput = Path.Combine(root, "4k.mp4");
await CompositionExportService.ExportAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(.1), fourKOutput, false, fourK, [], new() { IncludeAudio = false }, null, default);
var fourKProbe = await MediaProbe.ProbeAsync(fourKOutput);
Check(fourKProbe.Width == 3840 && fourKProbe.Height == 2160, "4K canvas export");
using (var cancel = new CancellationTokenSource())
{
    var progress = new ImmediateProgress(v => { if (v.Stage == "Exporting video…") cancel.Cancel(); });
    try { await CompositionExportService.ExportAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(4), cancelOutput, false, pBase, [], new(), progress, cancel.Token); throw new Exception("Active cancellation ignored"); }
    catch (OperationCanceledException) { Check(await File.ReadAllTextAsync(cancelOutput) == "preserve", "Active FFmpeg cancellation preserves prior output"); }
}
// Text layers: every intro animation (plus a second static layer) must export, and the text must be
// on screen once its animation has finished.
foreach (var animation in new[] { "None", "Fade", "Pop", "SlideUp", "Bounce", "BounceLetters" })
{
    var texted = pBase.Clone();
    texted.TextOverlays.Add(new PresentationTextOverlay { Text = "CAPIT", FontSize = 44, Color = "#FFFF00", Bold = true, X = .5, Y = .5, Animation = animation, AnimationDuration = .2 });
    texted.TextOverlays.Add(new PresentationTextOverlay { Text = "Second", FontSize = 20, X = .5, Y = .15 });
    var textOutput = Path.Combine(root, $"text-{animation}.mp4");
    await CompositionExportService.ExportAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(.8), textOutput, false, texted, [], new() { IncludeAudio = false }, null, default);
    Check(File.Exists(textOutput) && (await MediaProbe.ProbeAsync(textOutput)).Width > 0, "Text layer export · " + animation);
    var textFrame = Path.Combine(root, $"text-{animation}.png");
    await CompositionExportService.RunAsync(ffmpeg, ["-y", "-loglevel", "error", "-ss", "0.7", "-i", textOutput, "-frames:v", "1", textFrame], TimeSpan.FromSeconds(1), null, default);
    using var textBitmap = new Bitmap(textFrame);
    var yellow = 0;
    for (var y = 0; y < textBitmap.Height; y += 2)
    for (var x = 0; x < textBitmap.Width; x += 2)
    {
        var px = textBitmap.GetPixel(x, y);
        if (px.R > 190 && px.G > 190 && px.B < 110) yellow++;
    }
    Check(yellow > 20, $"Text visible after the intro · {animation} ({yellow} samples)");
}
var bounceGif = pBase.Clone();
bounceGif.TextOverlays.Add(new PresentationTextOverlay { Text = "GIF", FontSize = 40, Animation = "BounceLetters", AnimationDuration = .3 });
var textGifPath = Path.Combine(root, "text-bounce.gif");
await CompositionExportService.ExportAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(.6), textGifPath, true, bounceGif, [], new(), null, default);
Check((await MediaProbe.ProbeAsync(textGifPath)).Width == 720, "Bouncing-letter text in GIF export");
Check(!Directory.EnumerateFiles(root, ".capit-*").Any(), "No partial exports left behind");
Console.WriteLine($"PASS: {assertions} assertions including real MP4/GIF exports. Artifacts: {root}");

sealed class ImmediateProgress(Action<GifExportProgress> callback) : IProgress<GifExportProgress>
{ public void Report(GifExportProgress value) => callback(value); }
