using System.Text.Json.Serialization;

namespace LibraryAdminPC.Models;

public class KioskMetaDto
{
    [JsonPropertyName("bookCount")]
    public int BookCount { get; set; }

    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }
}