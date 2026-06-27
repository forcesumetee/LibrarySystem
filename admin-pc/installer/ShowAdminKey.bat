@echo off
REM ====================================================================
REM  ShowAdminKey.bat
REM  Shows the LibraHub Server AdminKey (from the server appsettings.json)
REM  so the install team can copy it into LibraHub Admin -> Settings.
REM  Double-click to run; it asks for administrator rights by itself.
REM  ASCII-only on purpose (no Thai) to avoid .bat code-page mojibake.
REM ====================================================================

REM --- Self-elevate: reading Program Files needs admin ---
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "APPSETTINGS=C:\Program Files\LibraHub\Server\appsettings.json"
set "ADMINKEY="
for /f "usebackq delims=" %%K in (`powershell -NoProfile -Command "try { (Get-Content '%APPSETTINGS%' -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json).AdminKey } catch { '' }"`) do set "ADMINKEY=%%K"

echo ====================================================================
echo    LibraHub Server - AdminKey
echo ====================================================================
echo.
if not "%ADMINKEY%"=="" (
    echo    AdminKey:
    echo.
    echo        %ADMINKEY%
    echo.
    echo %ADMINKEY%| clip
    echo    [copied to clipboard]
    echo.
    echo    Paste it into LibraHub Admin, Settings, AdminKey field, then Save.
) else (
    echo    AdminKey not found - is the server installed?
    echo    Expected file:
    echo        %APPSETTINGS%
)
echo.
pause
