# WinUI 3 → Avalonia UI migration

Cap-IT's presentation layer now runs entirely on **Avalonia UI 11.3** (.NET 8, Windows 10 2004+ x64). The
WinUI 3 / Windows App SDK host has been removed. The recording engine — DXGI Desktop Duplication,
Windows Graphics Capture, D3D11 effects, WASAPI audio, FFmpeg encoding, global hooks, Win32 annotation
overlays, composition/export, metadata and the updater — was carried over unchanged apart from the
narrow decoupling and fixes listed below.

## Solution structure

```text
CapIT.sln
Directory.Build.props                 shared version (3.x) and language settings for every project
src/
  CapIT.Core/                         net8.0-windows · no UI framework, no WinRT
    Models/                           settings, presets, metadata, presentation, zoom regions
    Services/Export/                  composition layout, artwork renderer, MP4/GIF export, thumbnails
    Services/Encoding/                FFmpeg locator, media probe
    Services/                         settings persistence, media output paths, metadata paths
  CapIT.Infrastructure.Windows/       net8.0-windows10.0.19041.0 · Windows-only engine
    Services/Capture/                 DXGI / WGC capture, GPU frame processing, audio, webcam, thumbnails
    Services/Encoding/                FFmpeg encoder, downloader, remuxer
    Services/Tracking/                global mouse / keyboard / hotkey hooks, caret locator
    Services/Overlay/                 Win32 layered annotation overlay + toolbar
    Services/Playback/                frame-server video player (editor playback)
    Services/Library/                 recordings library: enumeration, shell thumbnails, details, delete
    Services/Platform/                window interop (capture exclusion, monitor work areas)
    Services/                         RecordingManager, UpdateService, ClickSoundService
  CapIT.Desktop/                      Avalonia app · AssemblyName ScreenRecorderApp
    App.axaml, Program.cs             composition root (DI), single instance, --review, crash log
    Styles/                           design system (see below)
    Controls/                         reusable controls
    ViewModels/                       Shell, Main, Recordings, SourcePicker, Editor/*
    Views/                            MainWindow, Pages/*, ReviewWindow, Editor/*, Chrome/*, Components/*
    Services/                         toasts, dialogs, pickers, shell integration, UI state, review windows,
                                      recording-session presenter
    Diagnostics/                      Debug-only snapshot and smoke harnesses
Tests/CompositionChecks/              regression checks against CapIT.Core (real FFmpeg exports)
```

Dependency direction: `CapIT.Desktop → CapIT.Infrastructure.Windows → CapIT.Core`. Views contain no
recording logic; view models call the engine; P/Invoke lives in the infrastructure project (the
single-instance window lookup in `Program.cs` is the one startup-time exception).

The executable is still **ScreenRecorderApp.exe** and publishes to the same `publish/` folder, so the
Inno Setup installer, its `AppMutex`, Start Menu shortcuts and the in-place updater are unchanged.
`UpdateService` reads the version from its assembly, which now gets it from `Directory.Build.props`.

## Screens migrated

| WinUI | Avalonia |
| --- | --- |
| `MainWindow` + `ShellPage` (NavigationView, Mica, system title bar) | `Views/MainWindow` — frameless custom chrome, grouped collapsible sidebar, always-visible record control, update banner, page transitions, toasts, dialogs |
| `HomePage` | `Pages/HomePage` — hero + primary action, live 16:9 preview with status chips and meters, quick capture, recording setup summary, presets, recent recordings |
| `CapturePage` | `Pages/CapturePage` — source mode/display/window, resolution, frame rate, encoder, bitrate, format, text clarity, live preview |
| `TrackingPage` | `Pages/TrackingPage` — enable, tracking mode, zoom level, speed, instant zoom-out, keystrokes, live preview |
| `WebcamPage` | `Pages/WebcamPage` — enable, device, visual frame templates, picture adjustments, camera preview |
| `AnnotationsPage` | `Pages/AnnotationsPage` — enable, tool palette + full tool list, colours, stroke, lifetime, shortcuts |
| `EffectsPage` (+ cursor rows from Capture) | `Pages/EffectsPage` — cursor, style, spotlight, ripples, click sounds, live preview |
| `AudioPage` | `Pages/AudioPage` — system audio and microphone with live meters, device, noise suppression |
| `SettingsPage` | `Pages/SettingsPage` — General / Recording / Storage / Shortcuts / Updates / About |
| — (new) | `Pages/RecordingsPage` — searchable, date-grouped grid/list library with thumbnails, durations, resolution, play / reveal / delete / edit |
| `SourcePickerDialog` | source-picker overlay in `MainWindow` (live thumbnails, select / select & record) |
| `TrimExportWindow` (+ `VideoPreviewCanvas`, `EditorTimeline`) | `Views/ReviewWindow`, `Editor/CompositionPreview`, `Controls/EditorTimeline` |
| ffmpeg setup banner, update InfoBar, ContentDialogs | in-app dialog/overlay layer and toasts |
| — (new) | `Views/RecordingControllerWindow` — floating timer, pause, stop, freeze, mic/cam/draw/zoom toggles; excluded from capture |

## Design system

- `Styles/Tokens.axaml` — spacing scale (4–48), page/card/row paddings, content widths, radii (4–14),
  control heights (30 / 36 / 44), type scale (Inter), icon sizes, motion durations, Fluent metric hooks.
- `Styles/Colors.axaml` — semantic tokens for dark (flagship) and light themes (backgrounds, surfaces,
  borders, text, accent, status, recording), plus Fluent control resources redirected onto them.
- `Styles/Icons.axaml` — one stroke icon family (Lucide-style, 24px grid) drawn by `Controls/Icon`.
- `Styles/Buttons.axaml` — one template: secondary, primary, ghost, danger, danger-ghost, link, icon,
  toolbar, card; small / large / pill; hover, pressed, disabled and focus-ring states.
- `Styles/Lists.axaml` — Segmented, Chips, Tiles, PlainList, NavItem.
- `Styles/Controls.axaml` — PageHeader, SettingsSection, SettingsRow, SliderField, EmptyState,
  StatusBadge, AudioMeter, EditorTimeline themes; compact 36×20 toggle switch.
- `Styles/Typography.axaml`, `Surfaces.axaml`, `Overlays.axaml` (dialog host, toast card), `Motion.axaml`,
  `Templates.axaml` (recording card / row).
- Controls: `AdaptiveColumns` (side panel ↔ stacked), `AspectRatioBox`, `FrameView` (render-only bitmap
  view for live frames), `AudioMeter`, `DialogHost`, `FadeUpTransition`, `Ui.Icon` attached icon.

## Engine changes (all deliberate, all small)

- `AnnotationColorOption` uses `System.Drawing.Color` (+ `Hex`) instead of WinUI colour/brush types.
- `AnnotationOverlayService` posts hook callbacks through the UI `SynchronizationContext` instead of a
  WinUI `DispatcherQueue`, and exposes `ToggleDrawingMode()` for the floating controller.
- New: `FrameServerVideoPlayer` (editor playback without a UI-framework media element),
  `RecordingLibrary`, `WindowInterop`.
- **Bug fix — text layers could not be exported.** `CompositionExportService.BuildFilter` never labelled
  the text branch's output `[composed]`, chained bouncing-letter overlays without labels (and from the
  wrong input), used an expression-less filter for Fade and disabled the Bounce overlay after its intro.
  Any export with visible text failed. Fixed, with 13 new regression checks.
- Single-instance focus now raises the app's titled window instead of `Process.MainWindowHandle`, which
  is the annotation overlay whenever annotations are on.

## Behaviour notes

- Settings, presets, recordings and metadata formats are unchanged; existing installs keep everything.
  Presentation-only state (window placement, sidebar, library layout, controller preferences) is stored
  separately in `%LocalAppData%\Cap-IT Screen Recorder\ui-state.json`; restored positions are checked
  against connected monitors.
- The live preview only reads frames back from the GPU while a page that shows it is visible and the
  window isn't minimised — no readback cost during recording otherwise.
- The editor preview no longer applies video scale twice (it now matches export exactly). Tilt X/Y is
  shown as a preview-only perspective, as before, and labelled so.
- New UI shortcuts: Ctrl+, (settings), Ctrl+O (open recording), Ctrl+B (sidebar); in the editor Space,
  Ctrl+E, Ctrl+Z / Ctrl+Y, F11, I / O. Global annotation hotkeys are unchanged.
- Closing Cap-IT during a recording asks first, stops and saves it; open editors save before exit.

## Validation

| Command | Result |
| --- | --- |
| `dotnet restore CapIT.sln` | succeeded |
| `dotnet build CapIT.sln -c Release --no-restore` | succeeded, 0 warnings, 0 errors |
| `dotnet run --project Tests/CompositionChecks/CompositionChecks.csproj` | 651 assertions passed (incl. real MP4/GIF exports and new text-export checks) |
| `dotnet publish src/CapIT.Desktop/CapIT.Desktop.csproj -c Release -r win-x64 --self-contained true` | ScreenRecorderApp.exe, v3.12.4, assets and native libraries present; launches; single instance hands off; WM_CLOSE exits cleanly (code 0) |
| Debug recording smoke (`CAPIT_RECORD_SMOKE_DIR`) | 15/15: record → pause → resume → stop on the primary display, floating controller shown/closed, 1920×1080 MP4 saved under `Recordings/<date>`, editor opened |
| Debug editor smoke (`CAPIT_EDITOR_SMOKE_DIR`) | 20/20: decode, playback, trim, zoom region, text layer, undo/redo, MP4 (1:1 canvas, trim honoured) and GIF export, metadata saved |
| Debug layout snapshots | every page in dark and light at 1280×720 and 1500×850, minimum 1024×640, collapsed sidebar, source picker, dialog + toasts, settings sections, all eight editor inspector tabs |

## Remaining limitations

- Avalonia **11.3** was chosen over 12.x for API stability; moving to 12 is a separate upgrade.
- Display-scaling coverage was exercised at the development machine's 125% (two monitors). 100/150/175/200%
  and mixed-DPI layouts are expected to work (all layout is in device-independent units) but were not
  visually checked on physical displays.
- Capture exclusion of the floating controller uses `WDA_EXCLUDEFROMCAPTURE` (Windows 10 2004+); it was
  not confirmed frame-by-frame in a recording because Smart Tracking zoom was active during the test.
- The README screenshot gallery still shows the WinUI interface and should be re-captured.
- Webcam position/size presets beyond the existing frame templates are not offered: the engine doesn't
  support them, and the UI doesn't fake them.
