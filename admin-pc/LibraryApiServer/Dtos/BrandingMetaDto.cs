namespace LibraryApiServer.Dtos;

public record BrandingMetaDto(
    bool hasLogo,
    bool hasBackground,
    string? updatedAt,
    string? logoSha256,
    string? backgroundSha256
);