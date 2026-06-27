@echo off
:: LibraHub - start Server then Admin. Uses start /D so each app's working directory is its
:: own folder (the server must run with cwd = its folder to read appsettings.json / AdminKey
:: and find the license CSV next to the exe).
cd /d "%~dp0"
echo ===================================================
echo    LibraHub - Server + Admin
echo ===================================================

echo [1/2] Starting LibraHub Server...
if exist "Server\LibraryApiServer.exe" (
    start "LibraHub Server" /D "%~dp0Server" "%~dp0Server\LibraryApiServer.exe"
) else (
    echo [ERROR] Server\LibraryApiServer.exe not found.
    pause
    exit /b 1
)

timeout /t 3 /nobreak >nul

echo [2/2] Starting LibraHub Admin...
if exist "AdminPC\LibraryAdminPC.exe" (
    start "" /D "%~dp0AdminPC" "%~dp0AdminPC\LibraryAdminPC.exe"
) else (
    echo [ERROR] AdminPC\LibraryAdminPC.exe not found.
    pause
    exit /b 1
)
exit /b 0
