using LibraryApiServer.Data;
using LibraryApiServer.Entities;
using LibraryShared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LibraryApiServer.Services;

public class SqliteBookImporter
{
    // ✅ Merge import: อ่านจากไฟล์ .db แล้ว upsert เข้า DB หลักด้วย RegNo
    public async Task<ImportCsvResultDto> MergeImportAsync(Stream stream, LibraryDbContext db)
    {
        var result = new ImportCsvResultDto();

        if (stream == null || stream.Length == 0)
        {
            result.Reason = "SQLite stream is empty";
            return result;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"library_import_{Guid.NewGuid():N}.db");
        try
        {
            await using (var fs = File.Create(tempPath))
            {
                await stream.CopyToAsync(fs);
            }

            await using var conn = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly;");
            await conn.OpenAsync();

            var tableName = await FindBooksTableAsync(conn);
            if (tableName == null)
            {
                result.Reason = "Books table not found in uploaded DB (expected table name like Books/Book)";
                return result;
            }

            var columns = await GetColumnsAsync(conn, tableName);
            var colMap = BuildColumnMap(columns);

            if (!colMap.ContainsKey("regno") || !colMap.ContainsKey("title"))
            {
                result.Reason = "Required columns not found in DB (need regNo and title)";
                return result;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{tableName}\";";

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var regNo = GetReaderValue(reader, colMap, "regno");
                var title = GetReaderValue(reader, colMap, "title");
                var category = GetReaderValue(reader, colMap, "category");
                var shelf = GetReaderValue(reader, colMap, "shelf");
                var publisher = GetReaderValue(reader, colMap, "publisher");

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
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private static async Task<string?> FindBooksTableAsync(SqliteConnection conn)
    {
        var candidates = new[] { "Books", "Book", "books", "book" };

        // 1) หาแบบ exact candidates ก่อน
        foreach (var name in candidates)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name LIMIT 1;";
            cmd.Parameters.AddWithValue("$name", name);

            var found = await cmd.ExecuteScalarAsync();
            if (found is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }

        // 2) fallback: หา table ที่ชื่อมีคำว่า book
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND lower(name) LIKE '%book%' LIMIT 1;";
            var found = await cmd.ExecuteScalarAsync();
            return found as string;
        }
    }

    private static async Task<List<string>> GetColumnsAsync(SqliteConnection conn, string table)
    {
        var cols = new List<string>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // PRAGMA table_info: name อยู่คอลัมน์ index 1
            cols.Add(reader.GetString(1));
        }

        return cols;
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> columns)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < columns.Count; i++)
        {
            var h = Normalize(columns[i]);

            if (h is "regno" or "reg_no" or "bookid" or "accessionno" or "ทะเบียน" or "เลขทะเบียน")
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

    private static string GetReaderValue(SqliteDataReader reader, Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var idx)) return "";

        try
        {
            if (reader.IsDBNull(idx)) return "";
            return Convert.ToString(reader.GetValue(idx))?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string Normalize(string s)
        => (s ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "_");
}