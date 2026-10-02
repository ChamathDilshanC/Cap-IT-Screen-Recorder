# Avalonia migration report

## 1. Architecture before/after

Before, the WinUI executable owned the shell, pages, window services, ViewModels, capture engine,
composition renderer, and export pipeline in one project. The migration adds a controlled boundary:

```text
Avalonia views/ViewModels
    -> IRecordingWorkspaceAdapter
    -> ScreenRecorderApp.Engine
    -> metadata, composition, FFmpeg probing/export
```

The original WinUI host remains the capture host while Win32/window-specific services are moved
behind interfaces. This prevents a UI toolkit change from breaking active recording.

## 2. Projects/files added

- `ScreenRecorderApp.Engine/ScreenRecorderApp.Engine.csproj`: shared platform-neutral composition
  and export library.
- `ScreenRecorderApp.Avalonia/`: Avalonia application, Cap-IT theme, navigation shell, adapter
  boundary, and Avalonia ViewModels.
- `CapIT.sln`: solution containing the legacy host, shared engine, and Avalonia host.

## 3. WinUI dependencies removed

No WinUI dependency was removed from the legacy host in this controlled phase. The shared engine
does not reference WinUI. Removing the remaining dependencies requires migrating the capture,
overlay, source-picker, and review-window services behind interfaces and completing parity tests.

## 4. Avalonia dependencies added

- Avalonia 11.2.3
- Avalonia.Desktop 11.2.3
- Avalonia.Themes.Fluent 11.2.3
- Avalonia.Fonts.Inter 11.2.3
- CommunityToolkit.Mvvm 8.3.2

## 5. Screens migrated

The Avalonia shell and Review/Export workspace entry point are migrated. Capture, Smart Tracking,
Webcam, Annotations, Effects, Audio, Settings, and the full interactive editor remain on the
legacy host until their service boundaries and parity checks are complete.

## 6. Shared controls/themes created

`Themes/CapItTheme.axaml` defines the Cap-IT dark creative-tool palette, surfaces, typography,
spacing, card radius, rail buttons, accent actions, and editor layout tokens.

## 7. Native Windows interoperability approach

Windows capture, hooks, audio, and overlay code remain isolated in the Windows host. The Avalonia
host communicates through adapter interfaces; no Avalonia control is referenced by the engine.
The next migration step is an `IWindowService`/`IFilePickerService` implementation using Avalonia
top-level handles and Win32 interop only at the platform adapter edge.

## 8. Preview rendering approach

The shared composition renderer remains the source of truth for export pixels. Avalonia preview
surfaces will consume the same media probe/composition state; the current shell deliberately avoids
a second divergent renderer.

## 9. Performance impact

The shared engine introduces no additional recording thread or frame copy. Avalonia runs as a
separate host during migration, so the legacy recording path has unchanged performance.

## 10. Feature parity status

Metadata loading, media probing, zoom sidecar loading, and composition MP4/GIF export are wired.
Display/window capture, live preview, audio monitors, webcam, annotations, global hooks, source
pickers, and full editor manipulation still require adapter migration.

## 11. Tests executed

- `dotnet run --project Tests/CompositionChecks/CompositionChecks.csproj --no-restore`
- Result: 638 assertions passed, including real MP4/GIF exports.

## 12. Build result

- `dotnet build ScreenRecorderApp.csproj --no-restore`: passed with 0 warnings/errors.
- `dotnet build ScreenRecorderApp.Avalonia/ScreenRecorderApp.Avalonia.csproj`: passed with
  0 warnings/errors.

## 13. Remaining migration blockers

The legacy ViewModels currently contain WinUI DispatcherQueue, BitmapImage, brush, navigation, and
window references. The capture and annotation overlay services also use Windows App SDK windows
directly. Those must be extracted behind interfaces before the WinUI package references can be
removed. The migration is therefore not yet at the final cutover; the existing host remains the
production default until that parity work is completed.
