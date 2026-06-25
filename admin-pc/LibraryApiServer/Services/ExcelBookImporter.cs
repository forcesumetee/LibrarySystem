using ClosedXML.Excel;
using LibraryApiServer.Data;
using LibraryApiServer.Entities;
using LibraryShared;
using Microsoft.EntityFrameworkCore;

namespace LibraryApiServer.Services;

public class ExcelBookImporter
{
    public async Task<ImportCsvResultDto> ImportAsync(Stream stream, LibraryDbContext db)
    {
        var result = new ImportCsvResultDto();

        if (stream == null || stream.Length == 0)
        {
            result.Reason = "Excel stream is empty";
            return result;
        }

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault();
        if (ws == null)
        {
            result.Reason = "Excel worksheet not found";
            return result;
        }

        var used = ws.RangeUsed();
        if (used == null)
        {
            result.Reason = "Excel is empty";
            return result;
        }

        // อ่าน header row (row 1)
        var headerRow = used.FirstRow();
        var headers = headerRow.Cells().Select(c => (c.GetString() ?? "").Trim()).ToList();
        var headerMap = BuildHeaderMap(headers);

        // ต้องมีอย่างน้อย regno + title
        if (!headerMap.ContainsKey("regno") || !headerMap.ContainsKey("title"))
        {
            result.Reason = "Required columns not found (need regNo and title)";
            return result;
        }

        // อ่านตั้งแต่ row 2 เป็นต้นไป
        var lastRow = used.LastRow().RowNumber();
        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);

            string regNo = GetCell(row, headerMap, "regno");
            string title = GetCell(row, headerMap, "title");
            string category = GetCell(row, headerMap, "category");
            string shelf = GetCell(row, headerMap, "shelf");
            string publisher = GetCell(row, headerMap, "publisher");

            if (string.IsNullOrWhiteSpace(regNo) || string.IsNullOrWhiteSpace(title))
            {
                result.Skipped++;
                continue;
            }

            category = string.IsNullOrWhiteSpace(category) ? "-" : category.Trim();
            shelf = string.IsNullOrWhiteSpace(shelf) ? "-" : shelf.Trim();
            publisher = string.IsNullOrWhiteSpace(publisher) ? "-" : publisher.Trim();

            var key = regNo.Trim();
            var existing = await db.Books.FirstOrDefaultAsync(x => x.RegNo == key);

            if (existing == null)
            {
                db.Books.Add(new Book
                {
                    RegNo = key,
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

    private static string GetCell(IXLRow row, Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var idx)) return "";
        // map เก็บ index แบบ 0-based แต่ Excel cell เป็น 1-based
        var cell = row.Cell(idx + 1);
        return (cell.GetString() ?? "").Trim();
    }

    private static Dictionary<string, int> BuildHeaderMap(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < headers.Count; i++)
        {
            var h = NormalizeHeader(headers[i]);

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
        if (string.IsNullOrWhiteSpace(input)) return "";
        return input.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "_");
    }
}