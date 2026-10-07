# Review & Export workspace

Stopping a recording opens a non-modal Review & Export window. **Recordings** (and **Open recording**,
Ctrl+O) open any MP4 or MKV in it; `ScreenRecorderApp.exe --review <file>` opens one standalone. A
review can stay open while another recording is made, and each recording gets its own window.

## Editing

- Title bar with back (keep original), layers and inspector toggles, undo, redo, reset styling, GIF and
  Export video (Ctrl+E).
- **Layers panel:** recording details, the video layer, zoom regions (click to jump, remove) and text layers.
- **Canvas:** Original, 16:9, 9:16, 1:1, 4:5, 3:2, 4:3 and custom sizes as visual chips; 1080p / 1440p /
  2160p size presets keep the aspect; padding; built-in and custom style presets (save, rename, delete).
- **Background:** thirteen scenes, solid colour, eleven gradients with angle, recent colours, a colour
  picker, and PNG/JPEG images with fit/fill/stretch, blur and dimming.
- **Video:** 40–120% scale, Fit/Fill/Original/Custom, position and pixel offsets, rotation, perspective
  presets and tilt (preview), and zoom regions with timing, focus point and zoom level.
- **Corners & shadow:** radius with presets, shadow presets and custom blur/opacity/offsets, rounded border.
- **Frame:** six neutral window styles, dark/light, title bar and window controls, frame padding.
- **Watermark:** logo image with canvas-relative size, opacity, position, margin and offsets.
- **Text:** multiple layers with any installed font, size, colour, opacity, bold/italic, alignment,
  position (or drag on the canvas) and intro animations (bounce letters, fade, pop, slide up).
- **Export:** MP4 at canvas resolution with source/24/30/60 fps, quality presets, software or explicit
  hardware encoders, optional audio, real progress and cancellation; GIF uses the same composition with a
  two-pass palette at 720 px / 12 fps.
- **Playback:** play/pause (Space), frame step, scrubbing, mute/volume, full-screen preview (F11).
- **Timeline:** time ruler, thumbnail strip, draggable trim handles, zoom-region lane, scrubbable
  playhead; ←/→ step the playhead (Shift = 1 s), I / O set the in and out points, plus numeric in/out fields.

The original recording is never modified. Exports go to `Edited/<date>/` beside the `Recordings/` folder,
are written to a temporary file first and only replace the destination after encoding succeeds.
Cancellation or failure removes the partial file. **Discard original…** deletes the source and its edit
metadata only after an explicit confirmation; exported copies are kept.

## Architecture

| Area | Files / responsibilities |
| --- | --- |
| Window | `src/CapIT.Desktop/Views/ReviewWindow.axaml(.cs)`: custom chrome, layers / stage / inspector / timeline layout, responsive panels, keyboard shortcuts, full screen, save-before-close. |
| Session | `ViewModels/Editor/EditorViewModel.cs`: inspector state, canvas/background/frame/perspective helpers, zoom regions, export (MP4/GIF), discard, errors. |
| Document | `ViewModels/Editor/ReviewViewModel.cs`: observable document state, bounded snapshot history (undo/redo), trim and zoom restoration, input sanitation, debounced atomic persistence, custom presets. |
| Text layers | `ViewModels/Editor/TextLayerEditor.cs`: observable editor for the selected `PresentationTextOverlay`; every change goes through the document (normalised, recorded in history, saved). |
| Playback | `ViewModels/Editor/VideoPlaybackController.cs` over `CapIT.Infrastructure.Windows/Services/Playback/FrameServerVideoPlayer.cs`: Windows `MediaPlayer` (Media Foundation decoding and audio) in frame-server mode; frames are copied into a D3D11 texture, read back as BGRA (scaled to ≤1920×1440) and shown in an Avalonia bitmap. Only the newest frame is uploaded. |
| Live preview | `Views/Editor/CompositionPreview.axaml(.cs)`: static artwork layers, rounded video viewport, zoom-region crop, text animation and text dragging. |
| Timeline | `Controls/EditorTimeline.cs` (single-pass custom drawing), `CapIT.Core/Services/Export/TimelineThumbnailService.cs`. |
| Composition contract | `CapIT.Core/Services/Export/CompositionLayout.cs`: shared integer geometry and source-crop calculation, including zoom overlap precedence. |
| Static artwork | `CapIT.Core/Services/Export/CompositionAssets.cs`: background, image blur, frame, shadow, border, watermark, text and mask rendering on worker threads. Preview and export consume identical artwork. |
| Export | `CapIT.Core/Services/Export/CompositionExportService.cs`: one FFmpeg graph shared by MP4 and both GIF passes, safe argument lists, progress, cancellation and temporary output ownership. |

### Preview/export agreement

All layout distances are **output canvas pixels**. Preview and export use the same `CompositionLayout`,
the same static artwork, and the same `SourceCrop` for zoom regions (the preview crops the decoded frame;
export crops in FFmpeg). Z rotation is applied the way export applies it (rotate, then fit the rotated
bounds back into the video rectangle). **Tilt X/Y and depth are a preview-only perspective** — export
applies scale, offsets and Z rotation — and the inspector says so.

Zoom regions stay relative to the original recording timeline. Export subtracts the trim start only when
building segment boundaries; regions crossing a trim boundary are kept, and the later-starting region wins
overlaps. MP4/GIF output metadata describes a neutral, already-composed recording, so reopening an export
never applies its styling twice.

### Persistence and compatibility

Schema 2 adds presentation fields and trim bounds without removing schema-1 JSON names. Missing fields get
defaults; malformed metadata falls back safely; numbers, colours and option names are normalised. Edits
live beside the source in `Cap-IT Metadata/<file>.metadata.json` and `.zoom.json` (older sidecars next to
the video are still read). Custom presets live in
`%LocalAppData%\Cap-IT Screen Recorder\presentation-presets.json`.

## Verification

```powershell
dotnet run --project Tests/CompositionChecks/CompositionChecks.csproj
```

The composition suite links the production model/composition/export code (via `CapIT.Core`) and checks
metadata migration and sanitation, aspect/fit/scale/padding combinations, bounded crops, real MP4 and GIF
export, trim/audio/fps, original-time zoom precedence, rounded masks and static pixel parity, every frame
style, missing images, Unicode/quoted paths, portrait overflow, 4K, cancellation, and every text animation
(text must be on screen after its intro) including bouncing letters in GIF. It writes fixtures under the
ignored `artifacts/composition-checks`. Requires `ffmpeg/ffmpeg.exe`.

Debug builds also include opt-in, in-app checks (excluded from Release):

```powershell
# Editor: playback, trim, zoom region, text layer, undo/redo, MP4 + GIF export, metadata persistence.
$env:CAPIT_EDITOR_SMOKE_DIR = "$PWD\artifacts\editor-smoke"
& .\src\CapIT.Desktop\bin\Debug\net8.0-windows10.0.19041.0\win-x64\ScreenRecorderApp.exe --review "C:\path\to\a\copy.mp4"

# Layout: every page (or the editor's inspector tabs) in both themes at the given sizes, as PNGs.
$env:CAPIT_UI_SNAPSHOT_DIR = "$PWD\artifacts\ui-snapshots"; $env:CAPIT_UI_SNAPSHOT_SIZES = "1280x720,1500x850"
```

Use a disposable copy of a recording for the editor run — it saves edits and writes exports beside it.

## Limitations

- Tilt X/Y and perspective depth are previewed but not exported (unchanged from earlier releases).
- Native Windows decoders still can't preview some recordings (for example yuv444p "maximize text
  clarity" files). The original is preserved; the editor offers retry, external playback and FFmpeg export.
- Hardware export encoders need compatible GPU drivers; Software H.264 is the default and tested path.
- Image backgrounds use static image blur. Styling regenerates static artwork after a short debounce;
  large canvases can take longer. Playback is independent of that work.
