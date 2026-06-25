using System.Text;
using LibraryApiServer.Data;
using LibraryApiServer.Entities;
using LibraryShared;
using Microsoft.EntityFrameworkCore;

namespace LibraryApiServer.Services;

public class CsvBookImporter
{
    public async Task<ImportCsvResultDto> ImportAsync(Stream stream, LibraryDbContext db)
    {
        var result = new ImportCsvResultDto();

        if (stream == null || stream.Length == 0)
        {
            result.Reason = "CSV stream is empty";
            return result;
        }

        // กัน BOM / UTF-8
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var headerLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            result.Reason = "CSV header not found";
            return result;
        }

        var headers = ParseCsvLine(headerLine);
        var headerMap = BuildHeaderMap(headers);

        // ต้องมีอย่างน้อย regno + title
        if (!headerMap.ContainsKey("regno") || !headerMap.ContainsKey("title"))
        {
            result.Reason = "Required columns not found (need regNo and title)";
            return result;
        }

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                result.Skipped++;
                continue;
            }

            var cols = ParseCsvLine(line);

            string regNo = GetValue(cols, headerMap, "regno");
            string title = GetValue(cols, headerMap, "title");
            string category = GetValue(cols, headerMap, "category");
            string shelf = GetValue(cols, headerMap, "shelf");
            string publisher = GetValue(cols, headerMap, "publisher");

            if (string.IsNullOrWhiteSpace(regNo) || string.IsNullOrWhiteSpace(title))
            {
                result.Skipped++;
                continue;
            }

            // default กันค่าว่าง
            category = string.IsNullOrWhiteSpace(category) ? "-" : category.Trim();
            shelf = string.IsNullOrWhiteSpace(shelf) ? "-" : shelf.Trim();
            publisher = string.IsNullOrWhiteSpace(publisher) ? "-" : publisher.Trim();

            var existing = await db.Books.FirstOrDefaultAsync(x => x.RegNo == regNo.Trim());

            if (existing == null)
            {
                db.Books.Add(new Book
                {
                    RegNo = regNo.Trim(),
                    Title = title.Trim(),
                    Category = category,
                    Shelf = shelf,
                    Publisher = publisher
                });
            }
            else
            {
                existing.Title = title.Trim();
                existing.Category = category;
                existing.Shelf = shelf;
                existing.Publisher = publisher;
            }

            result.Imported++;
        }

        await db.SaveChangesAsync();
        return result;
    }

    private static Dictionary<string, int> BuildHeaderMap(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < headers.Count; i++)
        {
            var h = NormalizeHeader(headers[i]);

            // alias -> canonical key
            if (h is "regno" or "reg_no" or "ทะเบียน" or "เลขทะเบียน" or "bookid" or "accessionno")
                map["regno"] = i;
            else if (h is "title" or "ชื่อหนังสือ" or "booktitle" or "ชื่อเรื่อง")
                map["title"] = i;
            else if (h is "category" or "หมวด" or "หมวดหมู่")
                map["category"] = i;
            else if (h is "shelf" or "ชั้น" or "ชั้นวาง" or "location")
                map["shelf"] = i;
            else if (h is "publisher" or "สำนักพิมพ์")
                map["publisher"] = i;
        }

        return map;
    }

    private static string NormalizeHeader(string input)
    {
        return (input ?? string.Empty)
            .Trim()
            .Trim('"')
            .Replace(" ", "")
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static string GetValue(List<string> cols, Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var index)) return string.Empty;
        if (index < 0 || index >= cols.Count) return string.Empty;
        return cols[index].Trim();
    }

    // CSV parser แบบรองรับ comma ใน quote
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null)
        {
            result.Add(string.Empty);
            return result;
        }

        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // escaped quote ("")
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }
}