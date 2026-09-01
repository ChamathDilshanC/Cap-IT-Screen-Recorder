; Inno Setup script for Cap-IT Screen Recorder.
; Build with: ISCC.exe Installer\CapIT.iss
; Requires the app to already be published to:
;   bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\

#define MyAppName "Cap-IT Screen Recorder"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Cap-IT"
#define MyAppExeName "ScreenRecorderApp.exe"
#define PublishDir "..\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{8F3B7C1E-5E9B-4C9F-9D8A-2B6B7C3E9F10}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=CapIT-Screen-Recorder-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#PublishDir}\assets\AppIcon.ico
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
