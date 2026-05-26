; SmsOps HQ — Windows installer (Inno Setup 6)
; Build: run build-setup.ps1 from the repo root (publishes app + compiles this script).
;
; Prerequisites on the BUILD machine:
;   - .NET 8 SDK
;   - Inno Setup 6: https://jrsoftware.org/isdl.php

#ifndef PublishDir
  #define PublishDir "..\SmsOpsHQ.Desktop\bin\Publish\Store"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "SmsOps HQ"
#define MyAppPublisher "SmsOpsHQ"
#define MyAppExeName "SmsOpsHQ.Desktop.exe"
#define MyAppUrl "https://github.com/AceronX/SmsOpsHQ"

[Setup]
AppId={{A8F3C2E1-9B4D-4F6A-8E2C-1D5A7B9E3F40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\SmsOpsHQ.Desktop\bin\Publish
OutputBaseFilename=SmsOpsHQ-Setup-{#MyAppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
; Require Windows 10 1809+ (same baseline as .NET 8 desktop)
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained publish folder (Desktop + api subfolder). build-setup.ps1 must run publish-store.ps1 first.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Do not overwrite user data created at runtime under AppData (Twilio config, etc.) — those are not in the publish tree.

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Optional: launch after install (unchecked by default)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked
