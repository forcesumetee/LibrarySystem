@echo off
:: บังคับให้หน้าต่าง CMD เปลี่ยนตำแหน่งการทำงานมาที่โฟลเดอร์ที่ไฟล์ .bat นี้วางอยู่
cd /d "%~dp0"

echo ===================================================
echo   LIBRARY SYSTEM - STARTUP (DEBUG MODE)
echo ===================================================

echo [1/2] กำลังเปิดเซิร์ฟเวอร์หลังบ้าน...
:: ตรวจสอบว่ามีไฟล์อยู่จริงก่อนรัน เพื่อป้องกัน Error เดิม
if exist "Server\LibraryApiServer.exe" (
    start "Library System Backend" "Server\LibraryApiServer.exe"
) else (
    echo [ERROR] ไม่พบไฟล์ Server\LibraryApiServer.exe กรุณาตรวจสอบโฟลเดอร์ติดตั้ง!
    pause
    exit
)

:: รอ 3 วินาที
timeout /t 3 /nobreak

echo [2/2] กำลังเปิดหน้าจอโปรแกรม...
if exist "AdminPC\LibraryAdminPC.exe" (
    start "" "AdminPC\LibraryAdminPC.exe"
) else (
    echo [ERROR] ไม่พบไฟล์ AdminPC\LibraryAdminPC.exe
    pause
    exit
)

exit