<div align="center">

<img src="assets/Logo-CapIT.png" alt="Cap-IT Screen Recorder logo" width="96" />

# Cap-IT Screen Recorder

A fast, modern Windows screen recorder — GPU-accelerated capture, an interaction-aware smart zoom,
a live keystroke overlay, and ultra-sharp text quality, wrapped in a clean 3-tab WinUI 3 interface.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6?logo=windows&logoColor=white)](#)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)](#)
[![WinUI](https://img.shields.io/badge/UI-WinUI%203-5C2D91)](#)
[![FFmpeg](https://img.shields.io/badge/encoder-FFmpeg-007808?logo=ffmpeg&logoColor=white)](#)

[**⬇ Download the installer**](../../releases/latest) &nbsp;·&nbsp; [Features](#features) &nbsp;·&nbsp; [Installation](#installation) &nbsp;·&nbsp; [Building from source](#building-from-source)

<br/>

<img src="assets/Screenshots/Home.png" alt="Cap-IT Screen Recorder — Home dashboard with live preview" width="820" />

</div>

## Features

### Capture
- **GPU-accelerated capture** of any connected monitor via the DXGI Desktop Duplication API (no screen-scraping, no WinRT interop)
- **Live preview** of exactly what's being captured, shown as soon as a display is selected — not just while recording
- **Selectable output quality**, 360p up to 4K, scaled independently of the native capture resolution
- **Selectable cursor overlay** — the real system cursor shape (decoded live from DXGI, whatever cursor theme is active), or a stylized Arrow / Circle highlight / Dot / Crosshair
- **Frame rate** (15/24/30/60) and **H.264 encoder** choice (Auto / NVIDIA NVENC / AMD AMF / Intel QSV / software x264, with automatic fallback)
- **System audio** (WASAPI loopback) and/or **microphone**, mixed to one AAC track
- **Pause / Resume** — freezes the frame and mutes audio without ending the file
- **MP4 or MKV** output, to a configurable folder

### Smart Tracking
- **Interaction-triggered smart zoom** — eases into your chosen zoom level only while you're actively moving the mouse, clicking, or typing, then eases back out to the full desktop automatically ~1.5s after you stop. Motion is a proper time-based ease, not a snap.
- **Caret-follow while typing** — while typing, the zoom pans to the actual text caret (via the same technique Magnifier's "follow text cursor" mode uses) instead of a stale mouse position, so it follows what's actually happening even if the mouse hasn't moved
- **Keystroke overlay** — recent keystrokes fade in/out on screen as you type, great for tutorials and demos
- **ffmpeg auto-setup** — if `ffmpeg.exe` isn't found, Start Recording offers to download and install it automatically, with a live progress bar, instead of failing with an error

### Quality
- **"Maximize text clarity" mode** — 4:4:4 chroma (`yuv444p`) + a higher-quality x264 profile, eliminating the color bleed/blur 4:2:0 chroma subsampling causes around anti-aliased text edges
- **Content-adaptive (CRF/CQ) encoding** by default on every encoder — bits go where the frame actually needs them (e.g. text) instead of a flat average that wastes bits on static regions, with your bitrate setting kept as a hard ceiling
- **Bilinear-resampled zoom** — smooth, non-blocky zoomed text instead of nearest-neighbor aliasing

### UI
- **3-tab NavigationView shell** — a minimal Home dashboard, a Smart Tracking tab, and a Settings tab
- Clean, card-based Fluent Design settings (`SettingsCard`/`SettingsExpander`) throughout

## Screenshots

<div align="center">
<table>
<tr>
<td align="center" width="50%"><img src="assets/Screenshots/Smart-Tracking.png" width="420"/><br/><sub>Smart Tracking — interaction-triggered zoom & keystroke overlay</sub></td>
<td align="center" width="50%"><img src="assets/Screenshots/Settings.png" width="420"/><br/><sub>Settings — grouped Capture / Audio / Output cards</sub></td>
</tr>
</table>
</div>

## Installation

Download the latest installer from **[Releases](../../releases/latest)** and run it — no admin rights
required (you can install just for yourself, or for all users if you choose to elevate).

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
uninstall later), with an optional desktop shortcut.

## Tech stack

- **C# / .NET 8**, **WinUI 3** (Windows App SDK), MVVM (CommunityToolkit.Mvvm)
- **DXGI Desktop Duplication API** (`Vortice.Direct3D11` / `Vortice.DXGI`) for GPU-accelerated monitor capture (BGRA frames) and live cursor shape/position tracking
- **NAudio** (WASAPI loopback + microphone capture, mixed to one PCM stream)
- **FFmpeg** (bundled `ffmpeg.exe`, auto-downloadable on first run) for H.264/AAC encoding and MP4/MKV muxing, fed over named pipes
- **CommunityToolkit.WinUI.Controls.SettingsControls** (`SettingsCard`/`SettingsExpander`) for the Settings tab
- Unpackaged, self-contained deployment (no separate Windows App SDK runtime install needed); an [Inno Setup](https://jrsoftware.org/isinfo.php) script builds a normal Windows installer on top of that

## Project layout

```
Views/                  MainWindow, ShellPage (3-tab NavigationView shell), HomePage (dashboard),
                        TrackingPage (Smart Tracking), SettingsPage (Capture/Audio/Output)
ViewModels/             BaseViewModel, MainViewModel (CommunityToolkit.Mvvm)
Models/                 RecordingSettings, MonitorInfo, RecordingState, enums
Services/
├── Capture/            VideoCaptureService (DXGI Desktop Duplication + cursor rendering,
│                       interaction-triggered smart zoom, keystroke overlay compositing),
│                       AudioCaptureService, Monitor/AudioDevice enumerators, CursorIcons,
│                       KeystrokeOverlayRenderer
│   └── Interop/        Win32 monitor-enumeration P/Invokes
├── Encoding/            FFmpegEncoderService (process + named pipes), FFmpegLocator, FFmpegDownloader
├── Tracking/            GlobalKeyboardHook, GlobalMouseHook (WH_KEYBOARD_LL/WH_MOUSE_LL — keystroke
│                       overlay + zoom activity signal)
└── RecordingManager.cs  Orchestrates capture + encoder into record/pause/stop
ffmpeg/                 Bundled encoder binary goes here (see ffmpeg/README.md)
Installer/CapIT.iss     Inno Setup script that packages the published output into a Windows installer
app.manifest            DPI awareness, OS compatibility
```

## Building from source

Requires the **Windows 10/11 SDK** and **MSBuild** (Visual Studio, or the standalone Build Tools) in
addition to the .NET 8 SDK — WinUI 3 projects need the platform toolset, not just `dotnet`. You'll
also need `ffmpeg.exe` in `ffmpeg\` — see [ffmpeg/README.md](ffmpeg/README.md).

```powershell
dotnet restore
dotnet build -p:Platform=x64 -c Debug
.\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\ScreenRecorderApp.exe
```

The build automatically restores `Microsoft.WindowsAppSDK`, `CommunityToolkit.Mvvm`, `NAudio`,
`Vortice.Direct3D11`/`Vortice.DXGI` from NuGet.

### Publishing a standalone exe

```powershell
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

Output goes to `bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\`. That folder is fully
self-contained (.NET runtime, Windows App SDK runtime, and `ffmpeg\ffmpeg.exe` are all included) —
zip it up or copy it as-is to another Windows 10/11 x64 PC and run `ScreenRecorderApp.exe` directly.

### Building the installer

```powershell
& "C:\Users\<you>\AppData\Local\Programs\Inno Setup 6\ISCC.exe" Installer\CapIT.iss
```

(Install [Inno Setup 6](https://jrsoftware.org/isdl.php) first, or `winget install -e --id JRSoftware.InnoSetup`.)
Publish first so `bin\x64\Release\...\publish\` is up to date — the script packages that folder.
The resulting installer is written to `Installer\Output\CapIT-Screen-Recorder-Setup.exe`.

## How recording works (high level)

1. `VideoCaptureService.Prepare()` creates a D3D11 device on the adapter that owns the chosen monitor and calls `IDXGIOutput1.DuplicateOutput()` to get an `IDXGIOutputDuplication` — this resolves the real capture resolution without starting frame delivery yet.
2. `FFmpegEncoderService.StartAsync()` spawns `ffmpeg.exe` and waits for it to connect to the **video** named pipe only.
3. `VideoCaptureService.BeginCapture()` starts a dedicated capture thread that calls `AcquireNextFrame`/`ReleaseFrame` in a loop, copying each frame into a shared buffer and blending in the selected cursor overlay. A pacing loop (`RecordingManager.PacerLoopAsync`) writes the most recently captured frame to the pipe on every tick at the target FPS — this decouples Desktop Duplication's event-driven delivery from FFmpeg's fixed-rate `rawvideo` input, and duplicates the last frame when nothing on screen has changed (or while paused) so the output stays in sync with real elapsed time. The same shared frame buffer also feeds the live on-screen preview.
4. Once real frame bytes are flowing, `FFmpegEncoderService.WaitForAudioConnectionAsync()` connects the **audio** pipe (FFmpeg won't probe a second input until the first one has data — connecting both pipes before any writer exists deadlocks).
5. `AudioCaptureService` mixes WASAPI loopback + microphone into 48kHz stereo PCM16 and pumps it into the audio pipe.
6. On Stop, the pipes are closed and `q` is sent to FFmpeg's stdin so it finalizes the MP4/MKV cleanly. Output uses fragmented MP4 rather than `+faststart`, so there's no expensive rewrite-the-whole-file step on stop — large/high-bitrate recordings finalize quickly and stay valid even under a forced shutdown.

## How the smart zoom & text clarity work

The zoom and the "sharp text" work are both done frame-by-frame in `VideoCaptureService`, on the same
raw BGRA buffer the cursor overlay is already composited into — not with FFmpeg filters, since a live
`zoompan` can't practically be steered by an external, constantly-changing cursor/caret signal.

- **Activity tracking**: mouse movement comes from DXGI's own per-frame pointer position (no extra
  hook needed); clicks come from a `WH_MOUSE_LL` hook (`GlobalMouseHook`); typing comes from the
  existing `WH_KEYBOARD_LL` hook (`GlobalKeyboardHook`, shared with the keystroke overlay). Whichever
  fired most recently decides the pan target — typing looks up the real text caret via
  `GetGUIThreadInfo` (`CaretLocator`), not just the last mouse position.
- **Easing**: both the zoom factor and the pan position are eased toward their targets using
  `1 - e^(-dt/τ)` — a proper time-constant-based exponential ease driven by real elapsed time, so
  motion stays smooth regardless of the capture thread's actual (variable) frame timing, not a fixed
  per-frame blend that assumes a constant FPS.
- **Resampling**: the zoomed crop is resampled back to full frame size with 4-tap bilinear
  interpolation (parallelized per row), not nearest-neighbor — at the fractional zoom ratios this
  feature uses (1.5x/2x/3x), nearest-neighbor visibly blocks/aliases text edges.
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
uncatchable by any managed exception handler. Fixed by rewriting capture from scratch on the DXGI
Desktop Duplication API, which is plain COM with no WinRT projection involved — eliminating the entire
class of finalizer-thread crashes rather than patching around it.

**"Item is unplayable" in VLC on large/4K recordings.** MP4s used `-movflags +faststart`, which
rewrites the *entire file* on stop to move the moov atom to the front — for a large, high-bitrate
recording that rewrite could outlast the shutdown grace period, and a forced kill mid-rewrite left a
file with no moov atom at all. Fixed by switching to fragmented MP4, which is written incrementally as
recording progresses, so there's no expensive rewrite step and the file stays valid throughout.

**Cursor overlay not appearing.** DXGI's `PointerPosition` is only valid on the specific frame where
the cursor actually changed; on every other frame it's zeroed out rather than repeating the last known
state. The fix retains the last known position/visibility instead of overwriting it with those stale
zeroed values every frame.
