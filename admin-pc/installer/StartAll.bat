@echo off
:: LibraHub - launch the Admin app.
:: The Server now runs as a Windows Service ("LibraHubServer", start=auto), so it is already
:: running in the background from boot - this script no longer starts the server (starting a
:: second copy would clash on TCP 45269). To control the service use RestartLibraHubServer.bat
:: or services.msc.
cd /d "%~dp0"
echo ===================================================
echo    LibraHub - Admin
echo ===================================================

echo Starting LibraHub Admin...
if exist "AdminPC\LibraryAdminPC.exe" (
    start "" /D "%~dp0AdminPC" "%~dp0AdminPC\LibraryAdminPC.exe"
) else (
    echo [ERROR] AdminPC\LibraryAdminPC.exe not found.
    pause
    exit /b 1
)
exit /b 0
