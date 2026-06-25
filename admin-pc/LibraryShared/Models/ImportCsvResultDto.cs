namespace LibraryShared;

public class ImportCsvResultDto
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public string? Reason { get; set; }
}