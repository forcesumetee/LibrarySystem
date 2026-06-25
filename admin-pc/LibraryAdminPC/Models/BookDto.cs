using System.Text.Json.Serialization;

namespace LibraryAdminPC.Models;

public class BookDto
{
    [JsonPropertyName("regNo")]
    public string RegNo { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("shelf")]
    public string Shelf { get; set; } = "";

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "";
}