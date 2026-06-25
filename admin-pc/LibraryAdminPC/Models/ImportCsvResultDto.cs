using System.Text.Json.Serialization;

namespace LibraryAdminPC.Models;

public class ImportCsvResultDto
{
    [JsonPropertyName("imported")]
    public int Imported { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}