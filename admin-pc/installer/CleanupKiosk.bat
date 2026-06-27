@echo off
REM ====================================================================
REM  CleanupKiosk.bat
REM  Removes an old LibraHub Kiosk from THIS PC so the new version
REM  installs clean. Does NOT touch the shared server data
REM  (library.db / license.key) - only the kiosk-settings.json.
REM  Double-click to run; it asks for administrator rights by itself.
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
echo    LibraHub Kiosk Cleanup
echo ====================================================================
echo.
echo This will remove the LibraHub Kiosk from this PC:
echo    - Running Kiosk program (LibraryKiosk.exe)
echo    - Kiosk auto-start (this user's sign-in)
echo    - Kiosk settings (kiosk-settings.json)
echo    - Kiosk program files
echo.
echo The shared server data (library.db / license.key) is NOT touched.
echo.
echo Press ENTER to continue, or close this window now to CANCEL.
pause >nul

echo.
echo [1/4] Closing the Kiosk program...
taskkill /F /IM LibraryKiosk.exe >nul 2>&1
echo       done.

echo [2/4] Removing Kiosk auto-start (HKCU Run)...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v LibraryKiosk /f >nul 2>&1
echo       done.

echo [3/4] Deleting Kiosk settings (kiosk-settings.json only)...
del /F /Q "C:\ProgramData\LibrarySystem\kiosk-settings.json" >nul 2>&1
echo       done.

echo [4/4] Deleting Kiosk program files...
rmdir /S /Q "%LOCALAPPDATA%\Programs\LibraHub Kiosk" >nul 2>&1
rmdir /S /Q "C:\Program Files\LibraHub Kiosk" >nul 2>&1
echo       done.

echo.
echo ====================================================================
echo    Kiosk cleanup complete. You can now install the new Kiosk.
echo ====================================================================
echo.
pause
