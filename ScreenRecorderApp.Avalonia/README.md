# Cap-IT Avalonia migration

This is the production migration host for the presentation layer. The legacy WinUI host remains
available during the controlled migration so recording/capture regressions are not introduced by
changing the windowing toolkit in one unrecoverable step.

## Integration boundary

`Services/IRecordingWorkspaceAdapter` is the composition boundary between this platform-neutral
shell and capture/media/export infrastructure. `ExistingRecordingWorkspaceAdapter` is wired by
default and uses the shared engine library for metadata, probing, zoom sidecars, and MP4/GIF
composition export. Device capture and Win32 overlay ownership remain in the legacy host until
their services are moved behind the same boundary.

Build from the repository root with:

```text
dotnet build ScreenRecorderApp.Avalonia\ScreenRecorderApp.Avalonia.csproj
```

The complete migration is intentionally gated on feature-parity verification. The remaining
WinUI-specific surfaces are listed in the repository migration report rather than being silently
replaced with no-op controls.
