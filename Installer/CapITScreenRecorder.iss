; Cap-IT Screen Recorder — Inno Setup script
;
; Expects the published, self-contained output of `dotnet publish` to already exist at ..\publish
; (relative to this .iss file) before compiling — see the dotnet publish command in the deployment
; notes. Requires Inno Setup 6.x (uses the {autopf}/{autodesktop} constants introduced in Inno 6).

#define MyAppName "Cap-IT Screen Recorder"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "Chamath Dilshan"
#define MyAppURL "https://github.com/ChamathDilshanC/Cap-IT-Screen-Recorder"
#define MyAppExeName "ScreenRecorderApp.exe"
#define MyPublishDir "..\publish"
#define MySettingsFolderName "Cap-IT Screen Recorder"  ; must match SettingsService's %LocalAppData% folder name exactly

[Setup]
; This is the product's established AppId — Windows uses it to recognize "this is the same product"
; across versions for upgrade/uninstall. It must NEVER change: altering it would make every existing
; install look like a different product, breaking in-place upgrades and leaving orphaned entries in
; Add/Remove Programs for everyone already on an earlier version.
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
; On an upgrade, reinstall into wherever the previous version was installed rather than resetting to
; DefaultDirName — this is what makes the in-app auto-updater land the new build in the same location
; the user originally chose. (UsePreviousAppDir defaults to yes; set explicitly so it's not lost.)
UsePreviousAppDir=yes
; Lets Setup detect the app running (matches SingleInstanceMutexName in App.xaml.cs) and, together with
; CloseApplications=yes, shut it down cleanly before overwriting files during an auto-update.
AppMutex=CapITScreenRecorderSingleInstanceMutex
CloseApplications=yes
RestartApplications=no
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
; No "skipifsilent": the in-app auto-updater runs Setup with /SILENT, and we still want the app to
; relaunch itself once the update is in place. In a normal interactive install this is still just the
; usual optional "launch now" checkbox.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall

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
