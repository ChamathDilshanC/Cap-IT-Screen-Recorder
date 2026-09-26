# Review & Export workspace

The existing WinUI application now opens a non-modal composition workspace after recording finalization. Home also offers **Open recording** for MP4 and MKV files. A review can remain open while another recording is made; opening another recording no longer cancels an earlier review's export.

## Editing

- Compact title/command bar with undo, redo, reset, inspector toggle and Export Video.
- Aspect-correct canvas with Original, 16:9, 9:16, 1:1, 4:5, 3:2, 4:3 and custom dimensions. Resolution presets preserve the chosen aspect ratio.
- Padding, 40–120% video scale, proportional Fit/Fill/Original/Custom modes, alignment and pixel offsets.
- Solid colours, a colour picker and recent swatches, eleven gradients, gradient angle, and PNG/JPEG image backgrounds with fit/fill/stretch, blur and dimming.
- Real rounded video clipping, shadow controls/presets, rounded border, six neutral frame styles and image watermarks with canvas-relative size, opacity, alignment, margin and offsets.
- Playback, seeking, frame step, mute/volume, full-screen preview and automatic fit. The inspector collapses at narrow widths and remains manually accessible.
- Thumbnail timeline with original-time ruler, playhead, keyboard-accessible trim handles, numeric in/out values and zoom markers. Zoom editing is under Video.
- Six built-in presentation presets plus custom save, rename and delete. Built-ins cannot be overwritten.
- MP4 export at canvas resolution with source/24/30/60 fps, quality presets, software or explicit hardware encoders, optional audio, real progress and cancellation. GIF uses the same composition with a two-pass palette, 720px width and 12 fps.

The original recording is retained. Export uses a separate destination, builds a temporary output next to it, and replaces the chosen destination only after successful encoding. Cancellation/failure removes the partial output and preserves a previous destination. Discarding the original remains available behind an explicit confirmation.

## Architecture and changed areas

| Area | Files / responsibilities |
| --- | --- |
| Shared design system | `Themes/DesignSystem.xaml`, merged by `App.xaml`: light/dark/high-contrast surfaces, spacing/radius tokens, native-template control styles, inspector navigation and cards. Main pages, shell and source picker consume these resources. |
| Review document | `ViewModels/ReviewViewModel.cs`: observable document state, bounded snapshot history, trim and zoom restoration, input sanitation, debounced atomic persistence, custom presets. |
| Data | `Models/PresentationSettings.cs`, `PresentationPreset.cs`, `ExportSettings.cs`, `RecordingMetadata.cs`: defaults, normalization, canvas resolution, legacy migration and trim retention. The existing cursor enum is isolated in `CursorStyle.cs` so composition checks can link the real models without loading WinUI. |
| Window | `Views/TrimExportWindow.xaml` and partial host/inspector/export files: window, picker and playback lifecycle; application commands; error/progress UI. |
| Live preview | `Views/Controls/VideoPreviewCanvas.*`: a reusable MediaPlayerElement viewport, GPU geometric corner clip, proportional crop/zoom positioning and static artwork layers. Styling does not transcode the video. |
| Timeline | `Views/Controls/EditorTimeline.*`, `Services/Export/TimelineThumbnailService.cs`: reusable timeline, draggable/keyboard trim handles, original-time markers and eight asynchronously sampled thumbnails. |
| Composition contract | `Services/Export/CompositionLayout.cs`: shared integer geometry and source crop calculations, including zoom overlap precedence. |
| Static artwork | `Services/Export/CompositionAssets.cs`: background, image blur, frame, shadow, border, watermark and mask rendering on worker threads. Preview and export consume identical artwork. Preview updates are cancelled/debounced and do not rebuild the video tree. |
| Export | `Services/Export/CompositionExportService.cs`: one graph shared by MP4 and both GIF passes, safe argument lists, bounded logs, progress, cancellation and temporary output ownership. Existing MP4/GIF service entry points remain as adapters. |
| Integration | `MainViewModel`, Home, App and MainWindow: post-finalization review, reopening, concurrent review ownership, theme propagation, Mica and standalone `--review <file>` support. Capture/encoder/audio/tracking services retain their existing recording behavior. |

### Preview/export agreement

All layout distances are **output canvas pixels**. The source crop remains proportional, rounded clipping covers the video itself, and overflow is clipped to the canvas. The shared layout drives both MediaPlayerElement positioning and FFmpeg's crop/scale stages. Static artwork is identical in both consumers; export uses a grayscale corner mask generated from the same rounded rectangle.

Zoom regions remain relative to the original recording timeline. Export subtracts the trim start only when building segment boundaries; regions crossing either trim boundary are retained, and the later-starting region wins overlaps. Preview uses that same precedence. No edit shifts stored zoom times.

MP4/GIF output metadata describes a neutral, already-composed recording. Reopening an export does not apply its styling twice. Stale output zoom sidecars are cleared when replacing an exported file.

### Persistence and compatibility

Schema 2 adds presentation fields and trim bounds without removing schema-1 JSON names. Missing fields get defaults; null/malformed metadata falls back safely; numeric values, colours and option names are normalized. The legacy `DeviceFrame` flag maps to the neutral Minimal frame. Cursor metadata remains retained; cursor graphics already baked into recordings are not presented as editable post-record effects.

Recording edits stay beside the source in `.metadata.json` and `.zoom.json`. Custom presets live in `%LocalAppData%\Cap-IT Screen Recorder\presentation-presets.json`. Temporary playback/inspector state is not persisted. Save failures surface in the status line instead of failing the recording.

## Verification

Validated on 26 September 2026: Debug and Release x64 builds both completed with zero warnings and errors; the composition suite passed 633 assertions; the final in-app smoke run passed 116 checks with no binding failures and exited cleanly. Separate legacy-metadata and missing-metadata playback runs passed 5 and 4 checks respectively.

From the repository root on Windows with .NET 8+ and `ffmpeg/ffmpeg.exe` available:

```powershell
dotnet build -p:Platform=x64
dotnet build -c Release -p:Platform=x64
dotnet run --project Tests/CompositionChecks
```

The dependency-light executable links the production composition/model/export code. It checks metadata migration and sanitation; aspect/fit/scale/padding combinations; bounded crops; real MP4 and GIF export; trim, audio and fps; original-time zoom precedence; rounded masks and static pixel comparisons; every frame style; missing images; Unicode/quoted paths; portrait overflow; 4K; cancellation and preservation of existing output. It writes fixtures and results under ignored `artifacts/composition-checks`.

A Debug-only in-app smoke harness can verify real MediaPlayer playback, seeking, frame step, mute, two-way bindings, nonblank selectors, inspector switching, numeric recovery, undo/redo, custom preset operations, themes, resizing, full screen and save/restore:

```powershell
$env:CAPIT_REVIEW_SMOKE_DIR = "$PWD\artifacts\ui-smoke"
& .\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\ScreenRecorderApp.exe --review "$PWD\artifacts\composition-checks\source.mp4"
```

Use a disposable fixture: this run exercises autosave. Custom presets are isolated inside the smoke folder. It writes `ui-smoke.txt`, any binding failures, and layout images, then closes. Do not regenerate the same source fixture while this process has it open. The harness and its preset override are excluded from Release builds.

**Layout image limitation:** WinUI RenderTargetBitmap excludes MediaPlayerElement's external video surface. These images verify chrome/layout, not the decoded video pixels. Playback is verified through the real MediaPlayer session; export pixels are checked separately against the shared geometry/artwork.

## Deliberate limitations and remaining manual checks

- Ambient moving-video backgrounds, waveform rendering, direct canvas dragging, WebP inputs, transparent export and file-size estimates are omitted. None renders a black canvas for both supported export formats.
- Image backgrounds use static image blur. Live styling regenerates static artwork after a 120ms debounce; large canvases can take longer. Playback remains independent of that work.
- Native Windows decoder limitations still apply to older 4:4:4 or otherwise unsupported recordings. Errors preserve the original and offer retry, external playback and FFmpeg export; no automatic proxy transcode is performed.
- Hardware export choices require compatible GPU drivers. Software H.264 is the default and the tested path. Hardware encoders, every capture/audio device combination and all physical DPI configurations still need manual validation on target machines.
- The automated layout run covers Dark, Light and System plus 1366×768, 1100×700 and 900×650 window sizes at the host's actual scaling. This is not certification of physical 100/125/150/200% scaling on every monitor.
- Native UI automation was unavailable in this environment. Final human visual review of the live video surface and an actual record → stop → style → export session is still recommended; the in-app harness and FFmpeg checks are repeatable independently.

Relevant API contracts: [MediaPlayer playback](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer), [FFmpeg filters](https://www.ffmpeg.org/ffmpeg-filters.html).
