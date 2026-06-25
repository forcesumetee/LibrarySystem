#define MyAppName "LibrarySystem"
#define MyAppVersion "1.1.0"
#define SourceRoot "C:\Projects\LibrarySystem\release\2026-02-27_v1.1.0"   ; <-- แก้ให้ตรง release ของคุณ

[Setup]
AppId={{D64B1A5F-0A2C-4B0F-9C1A-7B1A58E17B2A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=YourCompany
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir={#SourceRoot}\_installer_out
OutputBaseFilename=LibrarySystem_Setup_{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern

[Languages]
Name: "th"; MessagesFile: "compiler:Languages\Thai.isl"

[Files]
; ---- Admin-PC ----
Source: "{#SourceRoot}\admin-pc\AdminPC\*"; DestDir: "{app}\AdminPC"; Flags: recursesubdirs createallsubdirs ignoreversion
; ---- Server ----
Source: "{#SourceRoot}\admin-pc\Server\*"; DestDir: "{app}\Server"; Flags: recursesubdirs createallsubdirs ignoreversion
; ---- StartAll (ถ้ามี) ----
Source: "{#SourceRoot}\admin-pc\StartAll.bat"; DestDir: "{app}"; Flags: ignoreversion; Check: FileExists(ExpandConstant('{#SourceRoot}\admin-pc\StartAll.bat'))
; ---- Kiosk APK + data + docs (optional) ----
Source: "{#SourceRoot}\kiosk\*"; DestDir: "{app}\kiosk"; Flags: recursesubdirs createallsubdirs ignoreversion; Check: DirExists(ExpandConstant('{#SourceRoot}\kiosk'))
Source: "{#SourceRoot}\data\*";  DestDir: "{app}\data";  Flags: recursesubdirs createallsubdirs ignoreversion; Check: DirExists(ExpandConstant('{#SourceRoot}\data'))
Source: "{#SourceRoot}\docs\*";  DestDir: "{app}\docs";  Flags: recursesubdirs createallsubdirs ignoreversion; Check: DirExists(ExpandConstant('{#SourceRoot}\docs'))

[Icons]
Name: "{autoprograms}\{#MyAppName}\เปิดระบบ (StartAll)"; Filename: "{app}\StartAll.bat"; WorkingDir: "{app}"; Check: FileExists(ExpandConstant('{app}\StartAll.bat'))
Name: "{autoprograms}\{#MyAppName}\เปิด Server"; Filename: "{app}\Server\StartServer.bat"; WorkingDir: "{app}\Server"; Check: FileExists(ExpandConstant('{app}\Server\StartServer.bat'))
Name: "{autoprograms}\{#MyAppName}\เปิด Admin-PC"; Filename: "{app}\AdminPC\LibraryAdminPC.exe"; WorkingDir: "{app}\AdminPC"
Name: "{commondesktop}\{#MyAppName} - Admin"; Filename: "{app}\AdminPC\LibraryAdminPC.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "สร้างไอคอนบน Desktop"; GroupDescription: "ตัวเลือกเพิ่มเติม:"; Flags: unchecked

[Run]
; หลังติดตั้งเสร็จ ถ้าต้องการให้ “เปิดระบบทันที” ให้ uncomment บรรทัดนี้
; Filename: "{app}\StartAll.bat"; Description: "เปิดระบบหลังติดตั้ง"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetDesktopUrl =
    'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.24/windowsdesktop-runtime-8.0.24-win-x64.exe';
  AspNetCoreUrl =
    'https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/8.0.24/aspnetcore-runtime-8.0.24-win-x64.exe';

function RunAndWait(const FileName, Params: string; var ExitCode: Integer): Boolean;
begin
  Result := Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

function DownloadWithPowerShell(const Url, DestPath: string): Boolean;
var
  ExitCode: Integer;
  Cmd: string;
begin
  Cmd :=
    '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"try {' +
      '[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;' +
      'Invoke-WebRequest -Uri ''' + Url + ''' -OutFile ''' + DestPath + ''' -UseBasicParsing;' +
      'exit 0' +
    '} catch { exit 1 }"';
  Result := RunAndWait('powershell.exe', Cmd, ExitCode) and (ExitCode = 0);
end;

function GetDotNetRuntimesText(var TextOut: string): Boolean;
var
  ExitCode: Integer;
  TmpFile: string;
begin
  TmpFile := ExpandConstant('{tmp}\dotnet_runtimes.txt');

  { dump output to a file }
  Result := Exec(
    'cmd.exe',
    '/C dotnet --list-runtimes > "' + TmpFile + '" 2>&1',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode
  );

  if (not Result) then
  begin
    TextOut := '';
    Result := False;
    Exit;
  end;

  if LoadStringFromFile(TmpFile, TextOut) then
    Result := True
  else
  begin
    TextOut := '';
    Result := False;
  end;
end;

function HasAspNetCore8(): Boolean;
var
  T: string;
begin
  if not GetDotNetRuntimesText(T) then
  begin
    Result := False;
    Exit;
  end;

  { match any 8.* }
  Result := (Pos('Microsoft.AspNetCore.App 8.', T) > 0);
end;

function HasWindowsDesktop8(): Boolean;
var
  T: string;
begin
  if not GetDotNetRuntimesText(T) then
  begin
    Result := False;
    Exit;
  end;

  { match any 8.* }
  Result := (Pos('Microsoft.WindowsDesktop.App 8.', T) > 0);
end;

function InstallDotNetPrereq(const Url, FileNameOnly: string; var NeedsRestart: Boolean): Boolean;
var
  ExitCode: Integer;
  PathExe: string;
begin
  Result := True;

  PathExe := ExpandConstant('{tmp}\') + FileNameOnly;

  if not FileExists(PathExe) then
  begin
    if not DownloadWithPowerShell(Url, PathExe) then
    begin
      MsgBox('ดาวน์โหลดไฟล์ติดตั้งไม่สำเร็จ: ' + #13#10 + Url, mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if not RunAndWait(PathExe, '/install /quiet /norestart', ExitCode) then
  begin
    MsgBox('ติดตั้ง Runtime ไม่สำเร็จ (ไม่สามารถรันตัวติดตั้งได้)', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  { 0 = success, 3010 = success but needs restart }
  if (ExitCode = 3010) then
    NeedsRestart := True
  else if (ExitCode <> 0) then
  begin
    MsgBox('ติดตั้ง Runtime ไม่สำเร็จ (ExitCode=' + IntToStr(ExitCode) + ')', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ok: Boolean;
begin
  ok := True;

  { Install ASP.NET Core Runtime 8 x64 if missing }
  if not HasAspNetCore8() then
  begin
    MsgBox('กำลังติดตั้ง ASP.NET Core Runtime 8 (x64) ...', mbInformation, MB_OK);
    ok := InstallDotNetPrereq(AspNetCoreUrl, 'aspnetcore-runtime-8.0.24-win-x64.exe', NeedsRestart);
  end;

  { Install .NET Desktop Runtime 8 x64 if missing }
  if ok and (not HasWindowsDesktop8()) then
  begin
    MsgBox('กำลังติดตั้ง .NET Desktop Runtime 8 (x64) ...', mbInformation, MB_OK);
    ok := InstallDotNetPrereq(DotNetDesktopUrl, 'windowsdesktop-runtime-8.0.24-win-x64.exe', NeedsRestart);
  end;

  if ok then
    Result := ''
  else
    Result := 'ติดตั้ง Runtime ไม่สำเร็จ กรุณาตรวจสอบอินเทอร์เน็ต/สิทธิ์ผู้ดูแลระบบ แล้วลองใหม่';
end;