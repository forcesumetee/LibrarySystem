Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

' หาเส้นทางของโฟลเดอร์ที่สคริปต์นี้วางอยู่
scriptPath = fso.GetParentFolderName(WScript.ScriptFullName)

' สร้างเส้นทางแบบเต็ม (Absolute Path) เพื่อป้องกันปัญหาหาไฟล์ไม่เจอ
serverPath = """" & scriptPath & "\Server\LibraryApiServer.exe" & """"
adminPath = """" & scriptPath & "\AdminPC\LibraryAdminPC.exe" & """"

' 1. สั่งรันเซิร์ฟเวอร์หลังบ้านแบบซ่อนหน้าต่าง (0)
WshShell.Run serverPath, 0, False

' 2. รอระบบหลังบ้านพร้อมทำงาน 3 วินาที
WScript.Sleep 3000

' 3. สั่งรันหน้าจอแอดมินแบบแสดงหน้าต่างปกติ (1)
WshShell.Run adminPath, 1, False