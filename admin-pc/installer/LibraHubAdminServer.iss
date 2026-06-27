; ============================================================================
;  LibraHub - Admin + Server - Inno Setup script (INST-3)
;  Installs the self-contained API server + admin app on the librarian PC.
;  No .NET runtime needed (self-contained). Requires admin (Program Files + firewall).
;
;  On install it provisions a strong random AdminKey into Server\appsettings.json
;  (kept as-is on upgrade if one already exists), shows it on the finish page, and
;  writes {app}\AdminKey.txt. Also opens TCP 45269 in Windows Firewall for kiosks.
;
;  Build the payload first:   powershell -File build-release.ps1   (creates dist\Server, dist\AdminPC)
;  Compile this installer:    iscc LibraHubAdminServer.iss          (needs Inno Setup 6)
;  Override payload location: iscc /DDistDir=..\some\path\dist LibraHubAdminServer.iss
; ============================================================================

#define MyAppName "LibraHub"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "NTY MULTIMEDIA CO.,LTD."

#ifndef DistDir
  #define DistDir "dist"
#endif

[Setup]
; New AppId for the rebranded LibraHub (distinct from the old "LibrarySystem" installer and from
; the Kiosk installer). Keep CONSTANT across LibraHub versions so upgrades replace cleanly.
AppId={{463CB147-4553-4D8E-A370-43DCBEAAF276}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LibraHub
DefaultGroupName=LibraHub
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=LibraHubAdminServer_Setup_{#MyAppVersion}
SetupIconFile=LibraHub.ico
UninstallDisplayIcon={app}\AdminPC\LibraryAdminPC.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; Self-contained build is win-x64 only. (x64compatible: Inno 6.3+; older 6.x use x64)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "th"; MessagesFile: "compiler:Languages\Thai.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; Pre-create the data root (admin rights) so the server (running as a normal user) can write
; library.db without permission issues.
Name: "{commonappdata}\LibrarySystem"

[Files]
Source: "{#DistDir}\Server\*";  DestDir: "{app}\Server";  Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#DistDir}\AdminPC\*"; DestDir: "{app}\AdminPC"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "LibraHub.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "StartAll.bat";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\LibraHub Admin";  Filename: "{app}\AdminPC\LibraryAdminPC.exe"; WorkingDir: "{app}\AdminPC"; IconFilename: "{app}\LibraHub.ico"
Name: "{group}\LibraHub Server"; Filename: "{app}\Server\LibraryApiServer.exe"; WorkingDir: "{app}\Server"; IconFilename: "{app}\LibraHub.ico"
Name: "{group}\Start LibraHub (Server + Admin)"; Filename: "{app}\StartAll.bat"; WorkingDir: "{app}"; IconFilename: "{app}\LibraHub.ico"
Name: "{group}\Uninstall LibraHub"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LibraHub Admin"; Filename: "{app}\AdminPC\LibraryAdminPC.exe"; WorkingDir: "{app}\AdminPC"; IconFilename: "{app}\LibraHub.ico"; Tasks: desktopicon

[Run]
; Firewall: allow inbound TCP 45269 for kiosks on other PCs. Delete any same-named rule first
; (avoid duplicates on reinstall), then add.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LibraHub Server 45269"""; Flags: runhidden; StatusMsg: "Configuring Firewall (port 45269)..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LibraHub Server 45269"" dir=in action=allow protocol=TCP localport=45269"; Flags: runhidden; StatusMsg: "Configuring Firewall (port 45269)..."
; Optional: launch the system right after install
Filename: "{app}\StartAll.bat"; Description: "Start LibraHub now"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LibraHub Server 45269"""; Flags: runhidden; RunOnceId: "DelLibraHubFwRule"

[Code]
var
  GAdminKey: string;
  GKeyIsNew: Boolean;

{ Strong random key via PowerShell (.NET Framework-compatible RNG for Windows PowerShell 5.1). }
function GenerateKey(): string;
var
  tmp: string;
  s: AnsiString;
  rc: Integer;
begin
  Result := '';
  tmp := ExpandConstant('{tmp}\lhk.txt');
  if Exec('powershell.exe',
      '-NoProfile -ExecutionPolicy Bypass -Command "$b=[byte[]]::new(32);[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b);[IO.File]::WriteAllText(''' + tmp + ''',[Convert]::ToBase64String($b))"',
      '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0) then
  begin
    if LoadStringFromFile(tmp, s) then
      Result := Trim(String(s));
  end;
end;

procedure ProvisionAdminKey();
var
  path: string;
  raw: AnsiString;     { LoadStringFromFile / SaveStringToFile use AnsiString }
  content: string;     { StringChangeEx needs a Unicode String (var) }
begin
  path := ExpandConstant('{app}\Server\appsettings.json');
  if not LoadStringFromFile(path, raw) then Exit;
  content := String(raw);

  if Pos('"AdminKey": ""', content) > 0 then
  begin
    { fresh install - generate, write, expose }
    GAdminKey := GenerateKey();
    if GAdminKey = '' then
      GAdminKey := 'CHANGE-ME-' + GetDateTimeString('yyyymmddhhnnss', #0, #0);
    StringChangeEx(content, '"AdminKey": ""', '"AdminKey": "' + GAdminKey + '"', True);
    SaveStringToFile(path, AnsiString(content), False);
    GKeyIsNew := True;
    SaveStringToFile(ExpandConstant('{app}\AdminKey.txt'),
      AnsiString('LibraHub AdminKey' + #13#10 +
      '=================' + #13#10 +
      GAdminKey + #13#10 + #13#10 +
      'Copy this key into LibraHub Admin -> Settings -> AdminKey and save.' + #13#10 +
      'This key protects the admin API and is required to reset the Kiosk PIN. Keep it secret.' + #13#10),
      False);
  end
  else
    GKeyIsNew := False;  { upgrade - keep the existing AdminKey untouched }
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ProvisionAdminKey();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if GKeyIsNew then
      WizardForm.FinishedLabel.Caption :=
        WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'AdminKey (paste into LibraHub Admin -> Settings -> AdminKey):' + #13#10 +
        GAdminKey + #13#10 +
        'Saved at: ' + ExpandConstant('{app}\AdminKey.txt')
    else
      WizardForm.FinishedLabel.Caption :=
        WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'Existing AdminKey kept (upgrade) - no change needed.';
  end;
end;
