<div align="center">

<img src="assets/Logo-CapIT.png" alt="Cap-IT Screen Recorder logo" width="96" />

# Cap-IT Screen Recorder

**A premium, GPU-accelerated Windows screen recorder that does the boring parts of making a great
tutorial for you** — smart zoom that follows what you're actually doing, live on-screen annotations,
studio-grade mic cleanup, and a one-click GIF export, all wrapped in a clean, modern WinUI 3 app.

[![Release](https://img.shields.io/badge/release-v2.0.0-success?logo=github)](../../releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6?logo=windows&logoColor=white)](#)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)](#)
[![WinUI](https://img.shields.io/badge/UI-WinUI%203-5C2D91)](#)
[![FFmpeg](https://img.shields.io/badge/encoder-FFmpeg-007808?logo=ffmpeg&logoColor=white)](#)

[**⬇ Download the installer**](../../releases/latest) &nbsp;·&nbsp; [Key Features](#-key-features) &nbsp;·&nbsp; [Tech Stack](#-tech-stack) &nbsp;·&nbsp; [Installation](#-installation) &nbsp;·&nbsp; [Building from source](#building-from-source)

<br/>

<img src="assets/Screenshots/Home.png" alt="Cap-IT Screen Recorder — Home dashboard with live preview" width="820" />

</div>

## ✨ Key Features

### 🎥 Capture, sharply
- **GPU-accelerated monitor capture** via the DXGI Desktop Duplication API — no screen-scraping, no per-frame WinRT overhead
- **WGC window capture** — record one specific window instead of the whole display, via Windows Graphics Capture, with overlapping windows correctly excluded
- **Catmull-Rom Smart Animated Zoom** — eases into your chosen zoom level only while you're actively moving the mouse, clicking, or typing (a proper time-constant ease, not a snap), pans to the real text caret while you type instead of a stale mouse position, and resamples the zoomed region with a 16-tap Catmull-Rom kernel — sharper than bilinear, with none of the blur/haloing a naive sharpen filter adds on top of text
- **4:4:4 Chroma Text Clarity mode** — an opt-in `yuv444p`/`high444` encode path that eliminates the color bleed/blur ordinary 4:2:0 chroma subsampling causes around anti-aliased text edges
- **Content-adaptive encoding** on every encoder (CRF for libx264, quality-target VBR for NVENC/AMF/QSV) — bits go where the frame actually needs them instead of a flat average, with your bitrate as a hard ceiling
- 360p up to 4K output, 15/24/30/60 fps, and automatic hardware encoder selection (NVIDIA NVENC / AMD AMF / Intel QSV / software x264) with fallback

### 🖊️ Live on-screen creativity
- **Live Ink Annotations** — draw arrows, highlights, and freehand notes directly over your desktop while recording, via a transparent always-on-top overlay. Toggle drawing mode with **Ctrl+Shift+D** from anywhere (no need to have the app focused), clear everything with **Esc**, and pick from preset pen colors and a stroke-thickness slider that both apply live, mid-recording
- **Circular Webcam PiP** — a round, always-on-top picture-in-picture webcam overlay, composited straight into the recording
- **Advanced Cursor Effects** — a dimming spotlight that follows the cursor, plus expanding click-ripples on every click, so viewers never lose track of where you're pointing
- **Keystroke overlay** — recent keystrokes fade in/out on screen as you type, ideal for tutorials and demos

### 🎙️ Audio that doesn't sound like a screen recording
- **System audio + microphone**, mixed to one clean AAC track
- **Studio Mic (AI-level noise suppression)** — an FFmpeg `highpass` + `adeclick` + adaptive `afftdn` filter chain scrubs background hum, fan noise, and keyboard clicks from your voice, applied *only* to the microphone signal (via a dual-pipe FFmpeg pipeline) so it never touches or degrades your system/game audio

### ✂️ Post-production, without leaving the app
- **Quick Trim & Export** — the moment you stop recording, a review window opens with a scrubbable preview and a dual-thumb trim range
- **High-quality GIF export** — a proper two-pass `palettegen`/`paletteuse` FFmpeg pipeline (not a naive single-pass conversion), with live progress reporting, so shareable GIFs actually look good instead of banded and dithered
- Keep the full MP4, export a trimmed GIF, or discard the recording outright — all from the same screen

### 🧭 A UI that stays out of your way
- **7-tab NavigationView shell** — Home, Smart Tracking, Webcam, Annotations, Effects, Audio, and Settings, each a focused, card-based Fluent Design page
- **Live preview** of exactly what's being captured, shown the moment a display is selected — not just while recording
- **Pause / Resume** — freezes the frame and mutes audio without ending the file
- **ffmpeg auto-setup** — if `ffmpeg.exe` isn't found, Start Recording offers to download and install it automatically with a live progress bar instead of failing with an error

## Screenshots

<div align="center">
<table>
<tr>
<td align="center" width="50%"><img src="assets/Screenshots/Smart-Tracking.png" width="420"/><br/><sub>Smart Tracking — interaction-triggered zoom & keystroke overlay</sub></td>
<td align="center" width="50%"><img src="assets/Screenshots/Settings.png" width="420"/><br/><sub>Settings — grouped Capture / Audio / Output cards</sub></td>
</tr>
</table>
</div>

## 📦 Installation

Grab **`CapIT-Screen-Recorder-Setup-<version>.exe`** from **[Releases](../../releases/latest)** and run
it. It's a normal Windows installer (built with Inno Setup) — self-contained, so there's nothing else
to install first: no separate .NET runtime, no Windows App SDK runtime, no manual FFmpeg download.

<div align="center">
<table>
<tr>
<td align="center" width="33%"><img src="assets/Install-Steps/Screenshot 2026-07-25 194444.png" width="260"/><br/><sub>1. Choose install mode</sub></td>
<td align="center" width="33%"><img src="assets/Install-Steps/Screenshot 2026-07-25 194456.png" width="260"/><br/><sub>2. Choose destination folder</sub></td>
<td align="center" width="33%"><img src="assets/Install-Steps/Screenshot 2026-07-25 194512.png" width="260"/><br/><sub>3. Optional desktop shortcut</sub></td>
</tr>
<tr>
<td align="center" width="33%"><img src="assets/Install-Steps/Screenshot 2026-07-25 194519.png" width="260"/><br/><sub>4. Confirm and install</sub></td>
<td align="center" width="33%"><img src="assets/Install-Steps/Screenshot 2026-07-25 194525.png" width="260"/><br/><sub>5. Installing</sub></td>
<td align="center" width="33%"><img src="assets/Install-Steps/Screenshot 2026-07-25 194540.png" width="260"/><br/><sub>6. Done — launch it</sub></td>
</tr>
</table>
</div>

That's it — Cap-IT Screen Recorder is added to the Start Menu (and Add/Remove Programs for a clean
uninstall later), with an optional desktop shortcut. Uninstalling asks whether to also remove your
saved settings; your recordings are never touched.

## 🛠️ Tech Stack

- **C# / .NET 8**, **WinUI 3** (Windows App SDK), MVVM (`CommunityToolkit.Mvvm`)
- **DXGI Desktop Duplication** (`Vortice.Direct3D11` / `Vortice.DXGI`) for GPU-accelerated full-monitor capture, and **Windows Graphics Capture (WGC)** for single-window capture
- **NAudio** — WASAPI loopback + microphone capture, kept on independent pipes when Studio Mic noise suppression is active so FFmpeg's filters only ever touch the mic signal
- **FFmpeg** (bundled `ffmpeg.exe`, auto-downloadable on first run) — H.264/AAC encoding and MP4/MKV muxing fed over named pipes for live recording, plus a separate two-pass `palettegen`/`paletteuse` pipeline for GIF export
- **`CommunityToolkit.WinUI.Controls`** — `SettingsCard`/`SettingsExpander` throughout, and `RangeSelector` for the trim window's dual-thumb range
- `MediaPlayerElement` for the post-recording trim/preview window
- Raw Win32 interop (`SetWindowsHookEx`, `WS_EX_LAYERED`/`WS_EX_TRANSPARENT`) for the global annotation hotkeys and the click-through-toggling annotation overlay window
- Unpackaged, self-contained deployment (no separate Windows App SDK runtime install needed); an [Inno Setup](https://jrsoftware.org/isinfo.php) script builds a normal Windows installer on top of that

## Project layout

```
Views/                  MainWindow, ShellPage (7-tab NavigationView shell), HomePage, TrackingPage,
                        WebcamPage, AnnotationsPage, EffectsPage, AudioPage, SettingsPage,
                        AnnotationOverlayWindow (transparent live-drawing overlay),
                        TrimExportWindow (post-recording trim/GIF-export review)
ViewModels/             BaseViewModel, MainViewModel (CommunityToolkit.Mvvm)
Models/                 AppSettings, RecordingSettings (+ its option-pair records: ResolutionOption,
                        CursorStyleOption, ZoomLevelOption, AnnotationColorOption), MonitorInfo,
                        WindowInfo, CaptureTargetKind, RecordingState
Services/
├── Capture/            VideoCaptureService (DXGI Desktop Duplication + WGC window capture, cursor
│                       rendering, smart zoom, spotlight/click-ripple/keystroke-overlay compositing,
│                       webcam PiP), AudioCaptureService (WASAPI mix, or dual-pipe unmixed legs for
│                       mic noise suppression), Monitor/AudioDevice/Window enumerators
│   └── Interop/        Win32 P/Invokes (monitor/window enumeration, window styles, cursor position)
├── Encoding/            FFmpegEncoderService (process + named pipes, dual-leg filter_complex for mic
│                       noise suppression), FFmpegLocator, FFmpegDownloader
├── Export/              GifExportService (two-pass palettegen/paletteuse GIF export, progress parsing)
├── Overlay/             AnnotationOverlayService (arms/disarms the live-drawing overlay + hotkey hook)
├── Tracking/            GlobalKeyboardHook, GlobalMouseHook (keystroke overlay + zoom activity),
│                       GlobalHotkeyHook (Ctrl+Shift+D / Esc for live annotations)
└── RecordingManager.cs  Orchestrates capture + encoder + audio + annotation overlay into record/pause/stop
ffmpeg/                 Bundled encoder binary goes here (see ffmpeg/README.md)
installer/              Inno Setup script that packages the published output into a Windows installer
app.manifest            DPI awareness, OS compatibility
```

## Building from source

Requires the **Windows 10/11 SDK** and **MSBuild** (Visual Studio, or the standalone Build Tools) in
addition to the .NET 8 SDK — WinUI 3 projects need the platform toolset, not just `dotnet`. You'll
also need `ffmpeg.exe` in `ffmpeg\` — see [ffmpeg/README.md](ffmpeg/README.md).

```powershell
dotnet restore
dotnet build -c Debug
.\bin\Debug\net8.0-windows10.0.19041.0\win-x64\ScreenRecorderApp.exe
```

The build automatically restores `Microsoft.WindowsAppSDK`, `CommunityToolkit.Mvvm`,
`CommunityToolkit.WinUI.Controls.SettingsControls`/`RangeSelector`, `NAudio`, and
`Vortice.Direct3D11`/`Vortice.DXGI` from NuGet.

### Publishing a standalone build

```powershell
dotnet publish ScreenRecorderApp.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish\` is fully self-contained (.NET runtime, Windows App SDK runtime, and `ffmpeg\ffmpeg.exe` are
all included, as long as `ffmpeg\ffmpeg.exe` existed locally *before* you ran this) — zip it up or copy
it as-is to another Windows 10/11 x64 PC and run `ScreenRecorderApp.exe` directly. This is also the
folder the installer script packages.

### Building the installer

```powershell
& "C:\Users\<you>\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer\CapITScreenRecorder.iss
```

(Install [Inno Setup 6](https://jrsoftware.org/isdl.php) first, or
`winget install -e --id JRSoftware.InnoSetup`.) Publish first so `publish\` is up to date — the script
packages that folder. The resulting installer is written to
`installer\Output\CapIT-Screen-Recorder-Setup-<version>.exe`.

## How recording works (high level)

1. `VideoCaptureService.Prepare()` sets up either a DXGI Desktop Duplication session (monitor capture) or a Windows Graphics Capture session (window capture) on the chosen target, resolving the real capture resolution without starting frame delivery yet.
2. `FFmpegEncoderService.StartAsync()` spawns `ffmpeg.exe` and waits for it to connect to the **video** named pipe only.
3. `VideoCaptureService.BeginCapture()` starts a dedicated capture thread that copies each frame into a shared buffer and composites in the selected cursor overlay, smart zoom, spotlight/click-ripples, keystroke overlay, and webcam PiP. A pacing loop (`RecordingManager.PacerLoopAsync`) writes the most recently captured frame to the pipe on every tick at the target FPS, decoupling event-driven capture delivery from FFmpeg's fixed-rate `rawvideo` input, and duplicates the last frame when nothing has changed (or while paused) so the output stays in sync with real elapsed time.
4. Once real frame bytes are flowing, `FFmpegEncoderService.WaitForAudioConnectionAsync()` connects the **audio** pipe (FFmpeg won't probe a second input until the first one has data). If Studio Mic noise suppression is active with both system audio and a mic enabled, a *third* pipe is connected the same way, and FFmpeg's own `-filter_complex` applies `highpass`/`adeclick`/`afftdn` to the mic leg alone before mixing it back with the untouched system audio via `amix`.
5. `AudioCaptureService` mixes (or, in the dual-pipe case, keeps separate) WASAPI loopback + microphone into 48kHz stereo PCM16 and pumps it into the audio pipe(s).
6. If Annotations are enabled (monitor-capture mode only), a transparent, click-through, always-on-top `AnnotationOverlayWindow` is armed for the session — since it's a real on-screen window, DXGI Desktop Duplication captures it as part of the normal desktop composition, with no extra compositing work needed.
7. On Stop, the pipes are closed and `q` is sent to FFmpeg's stdin so it finalizes the MP4/MKV cleanly. Output uses fragmented MP4 rather than `+faststart`, so there's no expensive rewrite-the-whole-file step on stop — large/high-bitrate recordings finalize quickly and stay valid even under a forced shutdown. A `TrimExportWindow` then opens automatically to preview, trim, GIF-export, or discard the result.

## How the smart zoom & text clarity work

The zoom and the "sharp text" work are both done frame-by-frame in `VideoCaptureService`, on the same
raw BGRA buffer the cursor overlay is already composited into — not with FFmpeg filters, since a live
`zoompan` can't practically be steered by an external, constantly-changing cursor/caret signal.

- **Activity tracking**: mouse movement comes from the capture API's own per-frame pointer position (no extra
  hook needed); clicks come from a `WH_MOUSE_LL` hook (`GlobalMouseHook`); typing comes from the
  existing `WH_KEYBOARD_LL` hook (`GlobalKeyboardHook`, shared with the keystroke overlay). Whichever
  fired most recently decides the pan target — typing looks up the real text caret via
  `GetGUIThreadInfo` (`CaretLocator`), not just the last mouse position.
- **Easing**: both the zoom factor and the pan position are eased toward their targets using
  `1 - e^(-dt/τ)` — a proper time-constant-based exponential ease driven by real elapsed time, so
  motion stays smooth regardless of the capture thread's actual (variable) frame timing, not a fixed
  per-frame blend that assumes a constant FPS.
- **Resampling**: the zoomed crop is resampled back to full frame size with a 16-tap, separable
  **Catmull-Rom** kernel (Mitchell-Netravali B=0, C=0.5), not bilinear or nearest-neighbor. Bilinear's
  weights are a plain positive-only average — exactly what softens edges; Catmull-Rom's small negative
  side lobes recover that lost edge contrast, which is what actually keeps zoomed text legible instead
  of visibly blurring, at about 4x bilinear's per-pixel cost.
- **Encoding**: every encoder uses content-adaptive rate control (CRF for libx264, quality-target VBR
  for NVENC) capped by your bitrate setting as `-maxrate`/`-bufsize`, instead of a flat average bitrate
  — detailed regions (text) get more bits automatically, static regions don't waste them. The optional
  "Maximize text clarity" mode additionally switches to `yuv444p`/`high444` for libx264, removing the
  chroma-subsampling blur/fringing 4:2:0 causes around colored text — opt-in since it costs meaningfully
  more bitrate and isn't reliably supported by consumer NVENC/AMF/QSV.

## Notable fixes along the way

**Intermittent crash after a few seconds of recording.** Earlier builds used `Windows.Graphics.Capture`
via hand-written WinRT interop, and would reliably crash with `System.AccessViolationException` inside
`WinRT.IObjectReference.Finalize()` — a native/managed-boundary crash on the GC finalizer thread,
uncatchable by any managed exception handler. Fixed by rewriting monitor capture from scratch on the
DXGI Desktop Duplication API, which is plain COM with no WinRT projection involved — eliminating the
entire class of finalizer-thread crashes rather than patching around it. (WGC was later reintroduced,
deliberately scoped to only window capture, where DXGI Desktop Duplication has no equivalent.)

**"Item is unplayable" in VLC on large/4K recordings.** MP4s used `-movflags +faststart`, which
rewrites the *entire file* on stop to move the moov atom to the front — for a large, high-bitrate
recording that rewrite could outlast the shutdown grace period, and a forced kill mid-rewrite left a
file with no moov atom at all. Fixed by switching to fragmented MP4, which is written incrementally as
recording progresses, so there's no expensive rewrite step and the file stays valid throughout.

**Cursor overlay not appearing.** DXGI's `PointerPosition` is only valid on the specific frame where
the cursor actually changed; on every other frame it's zeroed out rather than repeating the last known
state. The fix retains the last known position/visibility instead of overwriting it with those stale
zeroed values every frame.

**Webcam PiP lag and dropout on monitor switch.** An early webcam compositing pass re-decoded and
re-scaled the circular overlay on every single video frame, and tore the camera down and rebuilt it
whenever the video capture target changed underneath it, causing visible lag and a brief dropout on
every monitor switch. Fixed by decoupling webcam lifecycle from video-capture lifecycle entirely — the
camera only restarts when its own device/enabled setting actually changes.

**GIFs looking dithered and banded.** A direct single-pass MP4-to-GIF `ffmpeg` conversion falls back to
a generic, fixed 256-color palette, which bands and dithers badly on anything but very simple content.
Fixed with the standard two-pass approach instead — `palettegen` builds an optimal palette for the
*exact* trimmed clip first, then `paletteuse` dithers onto it — which is what actually gets GIF output
close to the source video's real color fidelity.

