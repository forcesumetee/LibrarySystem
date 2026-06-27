@echo off
REM ====================================================================
REM  CleanupLibraHub.bat
REM  Removes an old LibraHub install so the new version installs clean.
REM  Just double-click it - it asks for administrator rights by itself.
REM  ASCII-only on purpose (no Thai) to avoid .bat code-page mojibake.
REM ====================================================================

REM --- Self-elevate: relaunch as administrator if not already elevated ---
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo ====================================================================
echo    LibraHub Cleanup
echo ====================================================================
echo.
echo This will COMPLETELY REMOVE LibraHub from this PC:
echo    - LibraHub Server background service
echo    - Running LibraHub programs (Server / Admin / Kiosk)
echo    - ALL data: book catalog, settings, branding, license key
echo         (C:\ProgramData\LibrarySystem  and  %APPDATA%\LibraryAdminPC)
echo    - Program files (C:\Program Files\LibraHub)
echo    - Firewall rule for port 45269
echo.
echo WARNING: Your existing book data and settings will be DELETED.
echo.
echo Press ENTER to continue, or close this window now to CANCEL.
pause >nul

echo.
echo [1/5] Stopping and removing the LibraHub Server service...
sc stop LibraHubServer >nul 2>&1
sc delete LibraHubServer >nul 2>&1
echo       done.

echo [2/5] Closing any running LibraHub programs...
taskkill /F /IM LibraryApiServer.exe /IM LibraryAdminPC.exe /IM LibraryKiosk.exe >nul 2>&1
echo       done.

echo [3/5] Deleting LibraHub data...
rmdir /S /Q "C:\ProgramData\LibrarySystem" >nul 2>&1
rmdir /S /Q "%APPDATA%\LibraryAdminPC" >nul 2>&1
echo       done.

echo [4/5] Deleting LibraHub program files...
rmdir /S /Q "C:\Program Files\LibraHub" >nul 2>&1
echo       done.

echo [5/5] Removing the firewall rule...
netsh advfirewall firewall delete rule name="LibraHub Server 45269" >nul 2>&1
echo       done.

echo.
echo ====================================================================
echo    LibraHub cleanup complete. You can now install the new version.
echo ====================================================================
echo.
pause
