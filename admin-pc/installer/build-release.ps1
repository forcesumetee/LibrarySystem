<#
  LibraHub — build/publish + staging script (INST-1)
  Publishes the 3 .NET projects SELF-CONTAINED (win-x64, no .NET runtime needed on target),
  lays them out under .\dist\{Server,AdminPC,Kiosk}, stages the server appsettings.json
  (deployment template — AdminKey filled by the installer) + the license CSV, and converts
  the LibraHub logo PNG -> multi-size LibraHub.ico for the apps/installer/shortcuts.

  Run with Windows PowerShell 5.1 (needs System.Drawing for the .ico step):
      powershell.exe -ExecutionPolicy Bypass -File build-release.ps1
  Optional params override the logo / license-csv locations.
#>
param(
  [string]$LogoPng    = "$env:USERPROFILE\Downloads\LibraHub_logo.png",
  [string]$LicenseCsv = "C:\Projects\LibrarySystem\release\2026-02-27_v1.1.0\license_keys_10000.csv",
  [string]$Configuration = "Release",
  [string]$Rid = "win-x64"
)
$ErrorActionPreference = "Stop"
$adminPc = Split-Path -Parent $PSScriptRoot           # ...\admin-pc
$dist    = Join-Path $PSScriptRoot "dist"
$ico     = Join-Path $PSScriptRoot "LibraHub.ico"

Write-Host "=== LibraHub build-release ===" -ForegroundColor Cyan
Write-Host "adminPc=$adminPc"
Write-Host "dist=$dist"

# ---- clean ----
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# ---- publish (self-contained, single-file off so WPF is reliable) ----
$projects = @(
  @{ Name="Server";  Proj="LibraryApiServer\LibraryApiServer.csproj" },
  @{ Name="AdminPC"; Proj="LibraryAdminPC\LibraryAdminPC.csproj" },
  @{ Name="Kiosk";   Proj="LibraryKiosk\LibraryKiosk.csproj" }
)
foreach ($p in $projects) {
  $proj = Join-Path $adminPc $p.Proj
  $out  = Join-Path $dist $p.Name
  Write-Host "`n--- publish $($p.Name) ($($p.Proj)) ---" -ForegroundColor Yellow
  dotnet publish $proj -c $Configuration -r $Rid --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $out
  if ($LASTEXITCODE -ne 0) { throw "publish failed for $($p.Name) (exit $LASTEXITCODE)" }
}

# ---- stage server appsettings.json (deployment template; AdminKey set by installer) ----
# Web SDK copies the dev appsettings into the output, so overwrite it with a clean template.
# KeysFile/IssuerUrl omitted -> server uses its built-in defaults (exeDir license CSV fallback).
$serverSettings = Join-Path $dist "Server\appsettings.json"
@'
{
  "AdminKey": "",
  "Urls": "http://0.0.0.0:45269",
  "Storage": {
    "DataRoot": "C:\\ProgramData\\LibrarySystem",
    "DbPath": "C:\\ProgramData\\LibrarySystem\\library.db"
  },
  "License": {
    "RequireKeyList": true,
    "BypassInDevelopment": false
  },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*"
}
'@ | Set-Content -Path $serverSettings -Encoding UTF8
# remove any dev-only Development overrides that publish may have carried in
$devSettings = Join-Path $dist "Server\appsettings.Development.json"
if (Test-Path $devSettings) { Remove-Item $devSettings -Force }
Write-Host "staged Server\appsettings.json (AdminKey placeholder)" -ForegroundColor Green

# ---- stage license CSV next to the server exe (Program.cs exeDir fallback) ----
if (Test-Path $LicenseCsv) {
  Copy-Item $LicenseCsv (Join-Path $dist "Server\license_keys_10000.csv") -Force
  Write-Host "staged license_keys_10000.csv" -ForegroundColor Green
} else {
  Write-Warning "license CSV not found at: $LicenseCsv  (stage it into dist\Server before packaging)"
}

# ---- logo PNG -> multi-size .ico (16/32/48/256) ----
function Convert-PngToIco {
  param([string]$Png, [string]$IcoOut, [int[]]$Sizes)
  Add-Type -AssemblyName System.Drawing
  $orig = [System.Drawing.Image]::FromFile($Png)
  try {
    # center-crop to a square so the centered glyph maps cleanly into the icon
    $side = [Math]::Min($orig.Width, $orig.Height)
    $sx = [int](($orig.Width  - $side) / 2)
    $sy = [int](($orig.Height - $side) / 2)
    $square = New-Object System.Drawing.Bitmap $side, $side
    $g = [System.Drawing.Graphics]::FromImage($square)
    $g.DrawImage($orig,
      (New-Object System.Drawing.Rectangle 0,0,$side,$side),
      (New-Object System.Drawing.Rectangle $sx,$sy,$side,$side),
      [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    $imgs = @()
    foreach ($s in $Sizes) {
      $bmp = New-Object System.Drawing.Bitmap $s, $s
      $g2 = [System.Drawing.Graphics]::FromImage($bmp)
      $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
      $g2.DrawImage($square, 0, 0, $s, $s)
      $g2.Dispose()
      $pngms = New-Object System.IO.MemoryStream
      $bmp.Save($pngms, [System.Drawing.Imaging.ImageFormat]::Png)
      $imgs += ,(@{ Size=$s; Data=$pngms.ToArray() })
      $bmp.Dispose(); $pngms.Dispose()
    }
    $square.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$imgs.Count)  # ICONDIR
    $offset = 6 + 16 * $imgs.Count
    foreach ($im in $imgs) {
      $dim = [byte]($(if ($im.Size -ge 256) { 0 } else { $im.Size }))   # 0 means 256
      $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
      $bw.Write([uint16]1); $bw.Write([uint16]32)
      $bw.Write([uint32]$im.Data.Length); $bw.Write([uint32]$offset)
      $offset += $im.Data.Length
    }
    foreach ($im in $imgs) { $bw.Write($im.Data) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($IcoOut, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
  } finally { $orig.Dispose() }
}

if (Test-Path $LogoPng) {
  Convert-PngToIco -Png $LogoPng -IcoOut $ico -Sizes @(16,32,48,256)
  Write-Host "created $ico ($((Get-Item $ico).Length) bytes)" -ForegroundColor Green
} else {
  Write-Warning "logo PNG not found at: $LogoPng  (.ico not created)"
}

# ---- verify + report ----
Write-Host "`n=== RESULT ===" -ForegroundColor Cyan
$exes = @(
  (Join-Path $dist "Server\LibraryApiServer.exe"),
  (Join-Path $dist "AdminPC\LibraryAdminPC.exe"),
  (Join-Path $dist "Kiosk\LibraryKiosk.exe")
)
$allOk = $true
foreach ($e in $exes) {
  if (Test-Path $e) {
    $folder = Split-Path -Parent $e
    $sz = [math]::Round(((Get-ChildItem $folder -Recurse -File | Measure-Object Length -Sum).Sum/1MB),1)
    # self-contained proof: the .NET host/runtime is bundled next to the exe
    $sc = (Test-Path (Join-Path $folder "hostfxr.dll")) -and (Test-Path (Join-Path $folder "coreclr.dll"))
    Write-Host ("  OK  {0,-22} folder={1} MB  self-contained={2}" -f (Split-Path -Leaf $e), $sz, $sc)
  } else { Write-Host "  MISSING $e" -ForegroundColor Red; $allOk = $false }
}
Write-Host ("license CSV staged: " + (Test-Path (Join-Path $dist 'Server\license_keys_10000.csv')))
Write-Host ("ico created: " + (Test-Path $ico))
if (-not $allOk) { throw "one or more exes missing" }
Write-Host "`nBUILD OK" -ForegroundColor Green
