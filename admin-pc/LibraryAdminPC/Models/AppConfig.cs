namespace LibraryAdminPC.Models;

public class AppConfig
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5269";
    public string? AdminKey { get; set; } = "";

    // ✅ Branding (เก็บเป็นชื่อไฟล์ใน AppData/LibraryAdminPC/branding/)
    public string? LogoFile { get; set; } = "";
    public string? BackgroundFile { get; set; } = "";
}