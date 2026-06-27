; ============================================================================
;  LibraHub Kiosk — Inno Setup script (INST-2)
;  Installs the self-contained WPF kiosk (LibraryKiosk.exe) on each touchscreen.
;  No .NET runtime needed (self-contained). Per-user install so the HKCU auto-start
;  matches the kiosk app's own in-app toggle (same Run value name "LibraryKiosk").
;
;  Build the payload first:   powershell -File build-release.ps1   (creates dist\Kiosk)
;  Compile this installer:    iscc LibraHubKiosk.iss                (needs Inno Setup 6)
;  Override payload location: iscc /DSourceDir=..\some\path\Kiosk LibraHubKiosk.iss
; ============================================================================

#define MyAppName "LibraHub Kiosk"
#define MyDisplayName "LibraHub Search"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "NTY MULTIMEDIA CO.,LTD."
#define MyAppExeName "LibraryKiosk.exe"

; Payload folder (self-contained publish output). Relative to this .iss; override with /DSourceDir=...
#ifndef SourceDir
  #define SourceDir "dist\Kiosk"
#endif

[Setup]
; Kiosk-specific AppId (distinct from the Admin+Server installer). Keep CONSTANT across versions.
AppId={{09AADCFE-1810-4F0B-80C3-86B987B11035}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName=LibraHub
DisableProgramGroupPage=yes
; Per-user: no UAC, and HKCU auto-start belongs to the kiosk's logged-in user (matches in-app toggle)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=LibraHubKiosk_Setup_{#MyAppVersion}
SetupIconFile=LibraHub.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; Self-contained build is win-x64 only. (Inno 6.3+: use x64compatible)
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "th"; MessagesFile: "compiler:Languages\Thai.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "เปิด {#MyDisplayName} อัตโนมัติเมื่อเข้าสู่ระบบ Windows (แนะนำสำหรับจอ Kiosk)"; GroupDescription: "ตัวเลือก:"

[Files]
; Whole self-contained kiosk folder (exe + bundled .NET runtime).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; App icon used by the shortcuts (the exe itself keeps the default icon unless <ApplicationIcon> is added to the csproj).
Source: "LibraHub.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; "LibraHub Search" = the customer-facing name
Name: "{group}\{#MyDisplayName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\LibraHub.ico"
Name: "{group}\ถอนการติดตั้ง {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyDisplayName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\LibraHub.ico"; Tasks: desktopicon

[Registry]
; Auto-start at sign-in. Per-user Run key + the SAME value name the kiosk app uses
; (Services\AutoStartService: HKCU ...\Run, value "LibraryKiosk" = "<quoted exe>"), so the
; in-app Settings toggle stays in sync (it can later turn this off/on, no duplicate entry).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LibraryKiosk"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Offer to launch right after install (first run generates a default kiosk-settings.json =
; fullscreen, server port 45269). The installer never writes kiosk-settings.json itself, so an
; existing config (KioskId / server URL / orientation / theme) is preserved on reinstall.
Filename: "{app}\{#MyAppExeName}"; Description: "เปิด {#MyDisplayName} ทันที"; Flags: nowait postinstall skipifsilent
