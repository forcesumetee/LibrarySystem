using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LibraryAdminPC.Services;

public sealed class LicenseService
{
    // 🎯 ชี้เป้าไปที่เซิร์ฟเวอร์หลังบ้าน (Admin-PC Backend)
    private readonly string _baseUrl = "http://localhost:45269";
    
    // ข้อความแจ้งเตือนกลาง
    private const string MsgInvalidKey = "Product key ไม่ถูกต้อง";
    private const string MsgCannotValidate = "ไม่สามารถตรวจสอบสิทธิ์ได้ กรุณาติดต่อผู้ดูแล";
    private const string MsgServerOffline = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์หลักได้ กรุณาตรวจสอบว่ารันพอร์ต 45269 ไว้หรือไม่";
    private const string MsgActivated = "Activate สำเร็จ";

    /// <summary>
    /// เช็คสถานะลิขสิทธิ์จาก Backend (เรียกใช้ตอนเปิดโปรแกรม)
    /// </summary>
    public LicenseStatus GetStatus()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // ยิงไปถามสถานะที่ /api/license/status
            var response = client.GetAsync($"{_baseUrl}/api/license/status").GetAwaiter().GetResult();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                bool isLicensed = root.GetProperty("isLicensed").GetBoolean();
                string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "ok" : "ok";
                string? machineId = root.TryGetProperty("machineId", out var id) ? id.GetString() : null;

                return new LicenseStatus(isLicensed, message, machineId);
            }
            
            return new LicenseStatus(false, MsgCannotValidate, null);
        }
        catch (Exception)
        {
            // ถ้าเชื่อมต่อไม่ได้ (เช่น ลืมเปิด Backend)
            return new LicenseStatus(false, MsgServerOffline, null);
        }
    }

    /// <summary>
    /// ส่ง Product Key ไปให้ Backend ตรวจสอบ (เรียกใช้ตอนกดปุ่มยืนยัน)
    /// </summary>
    public ActivationResult TryActivate(string inputKey)
    {
        try
        {
            var key = (inputKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return new ActivationResult(false, "กรุณากรอก Product key");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // เตรียมข้อมูลส่งไปแบบ JSON
            var payload = new { key = key };
            
            // ยิง POST ไปที่ /api/license/activate
            var response = client.PostAsJsonAsync($"{_baseUrl}/api/license/activate", payload).GetAwaiter().GetResult();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                return new ActivationResult(true, MsgActivated);
            }
            else
            {
                // ถ้า Error ลองดึง Message จาก Backend มาโชว์
                try 
                {
                    using var doc = JsonDocument.Parse(json);
                    string errMsg = doc.RootElement.GetProperty("message").GetString() ?? MsgInvalidKey;
                    return new ActivationResult(false, errMsg);
                }
                catch {
                    return new ActivationResult(false, MsgInvalidKey);
                }
            }
        }
        catch (Exception)
        {
            return new ActivationResult(false, MsgServerOffline);
        }
    }

    // ฟังก์ชันช่วยจัดรูปแบบ Key เพื่อโชว์สวยๆ (ถ้าต้องการ)
    private static string MaskKey(string? key)
    {
        var t = (key ?? "").Trim();
        if (t.Length < 8) return t;
        var tail = t.Length >= 4 ? t.Substring(t.Length - 4) : t;
        return "LS-****-****-" + tail;
    }
}

// ปรับโครงสร้าง Record ให้ตรงกับการใช้งานใน MainWindow.xaml.cs
public readonly record struct LicenseStatus(bool IsLicensed, string Message, string? MaskedKey);
public readonly record struct ActivationResult(bool Success, string Message);