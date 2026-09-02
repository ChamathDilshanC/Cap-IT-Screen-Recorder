; Cap-IT Screen Recorder — Inno Setup script
;
; Expects the published, self-contained output of `dotnet publish` to already exist at ..\publish
; (relative to this .iss file) before compiling — see the dotnet publish command in the deployment
; notes. Requires Inno Setup 6.x (uses the {autopf}/{autodesktop} constants introduced in Inno 6).

#define MyAppName "Cap-IT Screen Recorder"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Your Company Name"           ; TODO: replace with your real publisher name
#define MyAppURL "https://github.com/yourusername/Cap-IT-Screen-Recorder"  ; TODO: replace with your real URL
#define MyAppExeName "ScreenRecorderApp.exe"
#define MyPublishDir "..\publish"
#define MySettingsFolderName "Cap-IT Screen Recorder"  ; must match SettingsService's %LocalAppData% folder name exactly

[Setup]
; Generate your own GUID in the Inno Setup IDE via Tools > Generate GUID and paste it here — this one
; is a placeholder and must not be reused across real, unrelated products (it's what Windows uses to
; recognize "this is the same product" across versions for upgrade/uninstall purposes).
AppId={{4F2B7B1A-9C3E-4B7A-8E1D-2A6C7F5E9D01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=CapIT-Screen-Recorder-Setup-{#MyAppVersion}
SetupIconFile=..\assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; The app itself runs asInvoker (see app.manifest) and needs no elevation to run — admin here is only
; for writing to Program Files during install/uninstall, not something the app requires at runtime.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Recursively grabs everything dotnet publish produced — the exe, every WindowsAppSDK/.NET runtime DLL
; the self-contained publish bundled, assets\, and ffmpeg\ffmpeg.exe if it was present at publish time.
; See the deployment notes: ffmpeg.exe MUST be present here, or the installed app will be unable to
; record at all (a non-elevated app can't write into Program Files, so its own auto-download fallback
; can't rescue a missing ffmpeg.exe post-install the way it can for a dev running from a normal folder).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Belt-and-suspenders: removes the whole install directory even if some file inside it wasn't tracked
; by [Files] (e.g. a crash.log the app itself wrote next to the exe, or a fresh ffmpeg.exe someone
; dropped in by hand). Settings under %LocalAppData% live outside {app} entirely and are handled
; separately below, since those are user data the uninstaller should ask about, not silently nuke.
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{localappdata}\{#MySettingsFolderName}');
    if DirExists(SettingsDir) then
    begin
      if MsgBox('Also remove your saved settings (settings.json)?' + #13#10 +
                'Your recorded videos are stored elsewhere and are never affected by this.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(SettingsDir, True, True, True);
    end;
  end;
end;
