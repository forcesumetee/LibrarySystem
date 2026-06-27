@echo off
:: Restart the LibraHub Server Windows Service. For support use only.
:: Needs admin rights -> self-elevates via UAC if not already elevated.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)
echo ===================================================
echo    Restarting LibraHub Server service...
echo ===================================================
net stop LibraHubServer
net start LibraHubServer
echo.
echo Done. The server is running in the background.
pause
