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
; Pre-create the data root (admin rights) so the server (the LibraHubServer service, running as
; LocalSystem) can write library.db / branding / logs without permission issues.
Name: "{commonappdata}\LibrarySystem"

[Files]
Source: "{#DistDir}\Server\*";  DestDir: "{app}\Server";  Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#DistDir}\AdminPC\*"; DestDir: "{app}\AdminPC"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "LibraHub.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "StartAll.bat";  DestDir: "{app}"; Flags: ignoreversion
Source: "RestartLibraHubServer.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; The server runs as the "LibraHubServer" Windows Service (auto-start) - no server shortcut, no
; "start both" launcher (a second server copy would clash on TCP 45269). Only the Admin app.
Name: "{group}\LibraHub Admin";  Filename: "{app}\AdminPC\LibraryAdminPC.exe"; WorkingDir: "{app}\AdminPC"; IconFilename: "{app}\LibraHub.ico"
Name: "{group}\Restart LibraHub Server (support)"; Filename: "{app}\RestartLibraHubServer.bat"; WorkingDir: "{app}"; IconFilename: "{app}\LibraHub.ico"
Name: "{group}\Uninstall LibraHub"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LibraHub Admin"; Filename: "{app}\AdminPC\LibraryAdminPC.exe"; WorkingDir: "{app}\AdminPC"; IconFilename: "{app}\LibraHub.ico"; Tasks: desktopicon

[Run]
; Firewall: allow inbound TCP 45269 for kiosks on other PCs. Delete any same-named rule first
; (avoid duplicates on reinstall), then add.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LibraHub Server 45269"""; Flags: runhidden; StatusMsg: "Configuring Firewall (port 45269)..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LibraHub Server 45269"" dir=in action=allow protocol=TCP localport=45269"; Flags: runhidden; StatusMsg: "Configuring Firewall (port 45269)..."
; The LibraHubServer service is (re)created and started from [Code] (CurStepChanged ssPostInstall),
; AFTER the AdminKey is provisioned into appsettings.json so the service reads the correct key.
; Optional: open the Admin app right after install (the server is already running as a service).
Filename: "{app}\AdminPC\LibraryAdminPC.exe"; Description: "Open LibraHub Admin now"; WorkingDir: "{app}\AdminPC"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Service stop/delete is done in [Code] (CurUninstallStepChanged) so it can wait for the process
; to release the exe before files are removed. Here we only drop the firewall rule.
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

{ Stop + delete the service (best-effort). Used before file copy on upgrade so the running
  service releases LibraryApiServer.exe, and to clear any stale definition before (re)creating. }
procedure StopAndDeleteService();
var rc: Integer;
begin
  Exec('sc.exe', 'stop LibraHubServer', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(1500); { let the process exit so the exe is unlocked }
  Exec('sc.exe', 'delete LibraHubServer', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(500);
end;

{ Create the auto-start service as LocalSystem, set crash auto-restart, and start it.
  Called AFTER ProvisionAdminKey so the service reads the freshly written AdminKey. The .NET
  host pins ContentRoot to the exe folder, so appsettings.json (License:Salt) is found even
  though a service starts with CWD = C:\Windows\System32. }
procedure CreateAndStartService();
var rc: Integer; binPath: string;
begin
  binPath := ExpandConstant('{app}\Server\LibraryApiServer.exe');
  Exec('sc.exe', 'create LibraHubServer binPath= "' + binPath + '" start= auto obj= LocalSystem DisplayName= "LibraHub Server"', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec('sc.exe', 'description LibraHubServer "LibraHub library API server (auto-start, runs in background)."', '', SW_HIDE, ewWaitUntilTerminated, rc);
  { On crash: restart after 5s, up to 3 times, reset the counter daily. }
  Exec('sc.exe', 'failure LibraHubServer reset= 86400 actions= restart/5000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec('sc.exe', 'start LibraHubServer', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopAndDeleteService();   { release exe lock on upgrade, before [Files] copy }
  if CurStep = ssPostInstall then
  begin
    ProvisionAdminKey();      { write AdminKey into appsettings.json FIRST }
    CreateAndStartService();  { then (re)create + start the service so it reads that key }
  end;
end;

{ Uninstall: stop + delete the service before Inno removes the files (waits for the process
  to release the exe). Firewall rule is dropped via [UninstallRun]. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var rc: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('sc.exe', 'stop LibraHubServer', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Sleep(2000);
    Exec('sc.exe', 'delete LibraHubServer', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Sleep(500);
  end;
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
