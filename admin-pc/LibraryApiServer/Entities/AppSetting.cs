namespace LibraryApiServer.Entities;

public class AppSetting
{
    // ใช้ Key เป็น Primary Key
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}