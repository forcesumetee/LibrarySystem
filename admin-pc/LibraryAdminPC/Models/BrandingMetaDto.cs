namespace LibraryAdminPC.Models;

public class BrandingMetaDto
{
    public bool HasLogo { get; set; }
    public bool HasBackground { get; set; }
    public string? UpdatedAt { get; set; }
    public string? LogoSha256 { get; set; }
    public string? BackgroundSha256 { get; set; }
}