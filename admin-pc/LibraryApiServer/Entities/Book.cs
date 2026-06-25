namespace LibraryApiServer.Entities;

public class Book
{
    public int Id { get; set; }

    public string RegNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Shelf { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
}