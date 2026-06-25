namespace LibraryShared.Dtos;

/// <summary>
/// Branding availability + change tokens from GET /api/branding/meta.
/// Server emits camelCase (hasLogo, hasBackground, updatedAt, logoSha256,
/// backgroundSha256); consumers deserialize case-insensitively.
///
/// NOTE: this lives in the sub-namespace <c>LibraryShared.Dtos</c> on purpose.
/// LibraryApiServer already declares its own <c>LibraryApiServer.Dtos.BrandingMetaDto</c>
/// and imports both <c>using LibraryShared;</c> and <c>using LibraryApiServer.Dtos;</c>.
/// Putting this type directly in <c>LibraryShared</c> would make the server's
/// unqualified <c>BrandingMetaDto</c> references ambiguous (CS0104) and break its
/// build. The sub-namespace keeps the shared type out of the server's plain
/// <c>using LibraryShared;</c> so existing code is untouched. Kiosk references this
/// via <c>using LibraryShared.Dtos;</c>.
/// </summary>
public sealed class BrandingMetaDto
{
    public bool HasLogo { get; set; }
    public bool HasBackground { get; set; }
    public string? UpdatedAt { get; set; }
    public string? LogoSha256 { get; set; }
    public string? BackgroundSha256 { get; set; }
}
