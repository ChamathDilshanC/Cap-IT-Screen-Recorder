<div align="left">

<img src="assets/Logo-Mark.png" alt="Cap-IT logo" width="72" />

# Cap-IT Screen Recorder

### A focused, GPU-assisted Windows screen recorder for polished tutorials, demos, bug reports, and social clips.

Capture the right source, follow the action with smart zoom, draw over the desktop, clean up audio, compose
the result, and export without leaving the app.

<a href="https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/latest"><img src="https://img.shields.io/badge/Download-Windows%20Installer-18dce8?style=for-the-badge&logo=windows&logoColor=white" alt="Download Windows installer" /></a>
<a href="docs/screenshots/README.md"><img src="https://img.shields.io/badge/Explore-Screenshot%20Gallery-7c5cff?style=for-the-badge&logo=googleimages&logoColor=white" alt="Explore screenshot gallery" /></a>
<a href="https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/issues"><img src="https://img.shields.io/badge/Report-an%20Issue-24292f?style=for-the-badge&logo=github&logoColor=white" alt="Report an issue" /></a>

<img src="https://img.shields.io/github/v/release/ChamathDilshanC/Cap-IT-Screen-Recorder?display_name=tag&sort=semver&color=18dce8&label=latest" alt="Latest release" />
<img src="https://img.shields.io/github/downloads/ChamathDilshanC/Cap-IT-Screen-Recorder/total?color=7c5cff&label=downloads" alt="Downloads" />
<img src="https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6?logo=windows&logoColor=white" alt="Windows 10 and 11" />
<img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
<img src="https://img.shields.io/badge/UI-WinUI%203-5C2D91" alt="WinUI 3" />

<br />
<br />

<img src="docs/screenshots/app/01-home.png" alt="Cap-IT Home dashboard" width="960" />

</div>

---

## Why Cap-IT

Cap-IT is built around one idea: **the recording should be easy, while the result should look intentional**.
The native Windows workflow keeps source selection, live preview, recording, audio monitoring, effects,
annotations, editing, and export in one place.

| Capture | Enhance | Finish |
|:---:|:---:|:---:|
| Displays, windows, live thumbnails | Smart zoom, cursor effects, webcam, keystrokes | Trim, compose, text, frames, MP4, GIF |
| DXGI Desktop Duplication + WGC | GPU-assisted effects and live preview | Review & Export workspace |

## Product workflow

```mermaid
flowchart LR
    A[Choose display or window] --> B[Preview source and audio]
    B --> C[Configure effects]
    C --> D[Record]
    D --> E[Review & Export]
    E --> F[Trim and compose]
    F --> G[MP4]
    F --> H[GIF]
```

---

## Feature highlights

<table>
<tr>
<td width="50%" valign="top">

### 🎥 Capture what matters

- Full-monitor capture through DXGI Desktop Duplication
- Single-window capture through Windows Graphics Capture
- Live source-picker thumbnails for displays and windows
- 15/24/30/60 FPS capture with 360p to 4K output
- NVIDIA NVENC, AMD AMF, Intel QSV, and software H.264 fallback

</td>
<td width="50%" valign="top">

### 🎯 Smart Tracking

- Interaction-triggered zoom that follows the cursor and text caret
- Critically damped, no-overshoot camera motion
- Click-only zoom mode
- Instant zoom-out toggle
- 0–50% animation speed control
- Live preview and mid-recording updates

</td>
</tr>
<tr>
<td width="50%" valign="top">

### ✨ Visual polish

- Cursor spotlight with adjustable radius
- Left and right click ripples
- Selectable cursor styles
- Webcam picture-in-picture templates
- Keystroke overlay
- Live desktop annotation overlay

</td>
<td width="50%" valign="top">

### 🧩 Review & Export

- Media preview with trim timeline
- Zoom regions and composition presets
- Canvas ratios: original, 16:9, 9:16, 1:1, 4:5, 3:2, 4:3, custom
- Backgrounds, rounded corners, shadows, borders, and device frames
- Multiple text layers with system fonts, color, opacity, alignment, and bounce-letter animation
- MP4 preservation and two-pass GIF export

</td>
</tr>
</table>

## Visual tour

The complete native **1920 × 1080** gallery is available in
[`docs/screenshots/README.md`](docs/screenshots/README.md).

<div align="center">
<table>
<tr>
<td><img src="docs/screenshots/app/02-capture.png" alt="Capture screen" width="440" /></td>
<td><img src="docs/screenshots/app/03-smart-tracking.png" alt="Smart Tracking screen" width="440" /></td>
</tr>
<tr>
<td align="center"><sub><b>Capture</b> · source, encoder, quality, cursor</sub></td>
<td align="center"><sub><b>Smart Tracking</b> · zoom, speed, keystrokes</sub></td>
</tr>
<tr>
<td><img src="docs/screenshots/app/04-webcam.png" alt="Webcam screen" width="440" /></td>
<td><img src="docs/screenshots/app/06-effects.png" alt="Effects screen" width="440" /></td>
</tr>
<tr>
<td align="center"><sub><b>Webcam</b> · picture-in-picture templates</sub></td>
<td align="center"><sub><b>Effects</b> · spotlight and click ripples</sub></td>
</tr>
<tr>
<td><img src="docs/screenshots/app/05-annotations.png" alt="Annotations screen" width="440" /></td>
<td><img src="docs/screenshots/app/09-review-export.png" alt="Review and Export screen" width="440" /></td>
</tr>
<tr>
<td align="center"><sub><b>Annotations</b> · draw while recording</sub></td>
<td align="center"><sub><b>Review & Export</b> · compose and deliver</sub></td>
</tr>
</table>
</div>

### Special feature states

<div align="center">
<img src="docs/screenshots/features/01-smart-tracking-enabled.png" alt="Smart Tracking enabled" width="440" />
<img src="docs/screenshots/features/02-cursor-effects-enabled.png" alt="Cursor effects enabled" width="440" />
<br />
<img src="docs/screenshots/features/03-source-picker.png" alt="Source picker" width="660" />
</div>

---

## Review & Export at a glance

```mermaid
flowchart TB
    VIDEO[Recorded MP4 or MKV] --> PREVIEW[Media preview]
    PREVIEW --> TRIM[Trim range]
    TRIM --> COMPOSE[Canvas composition]
    COMPOSE --> STYLE[Background · frame · text · watermark]
    STYLE --> MP4[Keep or export MP4]
    STYLE --> GIF[Two-pass GIF export]
    COMPOSE --> ZOOM[Zoom regions]
```

The editor keeps the original recording safe while presentation metadata lives beside the video in a
dedicated `Cap-IT Metadata` folder. Older sidecar files remain readable for compatibility.

---

## Installation

Download **`CapIT-Screen-Recorder-Setup-3.6.0.exe`** from
the [latest GitHub Release](https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/latest)
and run it. The installer is self-contained: no separate .NET runtime, Windows App SDK runtime, or
manual FFmpeg setup is required.

> **Requirements:** Windows 10 version 2004 (build 19041) or later, 64-bit. Windows 11 recommended.

The installer adds Cap-IT to the Start Menu and supports an optional desktop shortcut. Uninstalling
does not remove your recordings.

### Updating

Cap-IT checks GitHub Releases on startup and while the app is open. When a new release is available,
**Update now** downloads the installer, waits for the running app to close, installs the update, and
relaunches Cap-IT.

---

## Quick start

1. Open **Choose source** and select a display or window.
2. Check system audio and microphone levels on **Audio**.
3. Enable **Smart Tracking**, **Webcam**, **Effects**, or **Annotations** as needed.
4. Press **Start Recording**.
5. Stop recording to open **Review & Export**.
6. Trim, compose, add text or frames, then keep the MP4 or export a GIF.

---

## Keyboard shortcuts

These global shortcuts are available while Annotations is enabled:

| Shortcut | Action |
|---|---|
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>D</kbd> | Toggle drawing mode |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Z</kbd> | Undo the last stroke |
| Hold <kbd>Shift</kbd> | Constrain rectangles and circles |
| Hold <kbd>Alt</kbd> | Resize a shape from its center |
| <kbd>Ctrl</kbd> + <kbd>D</kbd> | Duplicate the selected annotation |
| <kbd>Esc</kbd> | Clear all drawings |

---

## Architecture

```mermaid
flowchart TB
    SHELL[WinUI 3 shell] --> VM[MVVM view models]
    VM --> MANAGER[RecordingManager]
    MANAGER --> CAPTURE[VideoCaptureService]
    MANAGER --> AUDIO[AudioCaptureService]
    MANAGER --> ENCODER[FFmpegEncoderService]
    CAPTURE --> DXGI[DXGI Desktop Duplication]
    CAPTURE --> WGC[Windows Graphics Capture]
    CAPTURE --> EFFECTS[Zoom · cursor · webcam · annotations]
    AUDIO --> METERS[Live audio meters]
    ENCODER --> FILE[MP4 / MKV]
    FILE --> REVIEW[TrimExportWindow]
    REVIEW --> COMPOSITION[Composition renderer]
    COMPOSITION --> EXPORT[MP4 / GIF]
```

### Project map

| Area | Location |
|---|---|
| WinUI shell and pages | [`Views/`](Views/) |
| MVVM state and commands | [`ViewModels/`](ViewModels/) |
| Capture and recording pipeline | [`Services/Capture/`](Services/Capture/) |
| Audio and encoding | [`Services/`](Services/) |
| Composition and exports | [`Services/Export/`](Services/Export/) |
| Domain settings and metadata | [`Models/`](Models/) |
| Regression checks | [`Tests/CompositionChecks/`](Tests/CompositionChecks/) |
| Release automation | [`.github/workflows/`](.github/workflows/) |

---

## Build from source

```powershell
dotnet restore
dotnet build ScreenRecorderApp.csproj --no-restore
dotnet run --project Tests\CompositionChecks\CompositionChecks.csproj --no-restore
```

The composition checks validate geometry, metadata compatibility, MP4 output, GIF output, and the
shared preview/export rendering path.

---

## Technical foundation

- **C# / .NET 8** and **WinUI 3**
- **CommunityToolkit.Mvvm** and **CommunityToolkit.WinUI.Controls**
- **DXGI Desktop Duplication** and **Windows Graphics Capture**
- **Vortice.Direct3D11 / Vortice.DXGI**
- **NAudio** WASAPI loopback and microphone capture
- **FFmpeg** named-pipe recording and two-pass GIF export
- **GDI / GDI+** source thumbnails and annotation overlay
- Raw Win32 hooks for global input and capture coordination

---

## Release history

| Version | Highlights |
|---|---|
| [v3.6.0](https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/tag/v3.6.0) | Smart Tracking instant zoom-out, adjustable animation speed, live propagation, and preset persistence |
| [v3.5.0](https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/tag/v3.5.0) | Multiple text layers, alignment, colors, opacity, bounce-letter animation, and Safari-style browser frame |
| [v3.4.3](https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/releases/tag/v3.4.3) | Responsive text dragging and review-canvas interaction |

---

## Author

Designed and developed by **[Chamath Dilshan](https://github.com/ChamathDilshanC)**.

## Feedback and contributing

Found a bug or have an idea? Open a
[GitHub issue](https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder/issues) with your Windows
version, capture target, and a short reproduction. Pull requests are welcome for focused improvements
that preserve the native Windows workflow.

## License

See [`LICENSE`](LICENSE) for the project license.
