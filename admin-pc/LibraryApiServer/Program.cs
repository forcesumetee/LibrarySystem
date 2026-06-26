using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibraryApiServer.Data;
using LibraryApiServer.Dtos;
using LibraryApiServer.Entities;
using LibraryApiServer.Services;
using LibraryShared;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR; 
using System.Management; // สำหรับอ่านรหัสเมนบอร์ด (Motherboard Serial)
using System.Net.Http.Json; // สำหรับสื่อสารกับ Issuer VPS

var builder = WebApplication.CreateBuilder(args);

// ✅ บังคับให้ Server ฟังทุก IP ในวง LAN (เปิดทางให้ Kiosk คุยได้)
// Default port 45269 (เลี่ยงชนระบบอื่น); ปรับได้ผ่าน config "Urls" (appsettings/env) เผื่ออนาคต.
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://0.0.0.0:45269");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR(options =>
{
    // K3: detect an ungracefully-killed kiosk faster than the 30s default. The kiosk
    // client pings every ~8s (WithKeepAliveInterval), so a 20s server timeout keeps a
    // safe >2x margin while staying light on bandwidth (a tiny ping per kiosk per 8s).
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(20);
    options.KeepAliveInterval = TimeSpan.FromSeconds(8);
});
// K3: in-memory registry of connected kiosks (distinct kioskId), used by the hub
// connect/disconnect hooks and the GET /api/kiosks/active endpoint.
builder.Services.AddSingleton<KioskRegistry>();

// ------------------------------------------------------
// Storage root (ProgramData) + License state configs
// ------------------------------------------------------
var programDataRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "LibrarySystem"
);
Directory.CreateDirectory(programDataRoot);

var licenseFilePath = Path.Combine(programDataRoot, "license.key");
var defaultKeysListPath = Path.Combine(programDataRoot, "license_keys_10000.csv");

// 🌐 การตั้งค่าเชื่อมต่อกับ Issuer VPS และ กุญแจสาธารณะ
// 🌐 เปลี่ยน localhost เป็นลิงก์ Serveo เพื่อให้ Tester เชื่อมต่อได้
var issuerUrl = builder.Configuration["License:IssuerUrl"] ?? "https://46ee8083041dad99-184-22-108-83.serveousercontent.com";
var bypassInDev = builder.Configuration.GetValue("License:BypassInDevelopment", true);

// 🔑 นำ Public Key ที่คุณเจนได้จาก KeyGenTool มาวางที่นี่
var publicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAv8XKsCGfz8UUkiwLprIW
HeY4d81sNpb1zluaqCKRowexp2FaNiAlhmFGyamRYOft5cWqSpLjwigf3ApvOttm
py5uZ3VW/H4HgIgimU6YrAw1AvpFN9rMGZnlcuUa9o2Tn+yBx/443FNKT1AQEnvP
MIFCpM1YbHqXmj6JGF8FYMLQYrG1XajlSpdEz5/WtZFCiq+TgDBFgNmbXANQeV4N
LG9OyCr/YGZiVUocTcT8BA9kToum4tC/7VvlbO/gCE8mX13Tsfpojx6eQL8uYmpL
hTgW98zm+7LQai0151myoAAHk9MJ3XVP/LUA2oUbmDPr6IPLkGqPwJY6Yn/ey7G2
9QIDAQAB
-----END PUBLIC KEY-----";









var keysListPath = builder.Configuration["License:KeysFile"] ?? defaultKeysListPath;
var requireKeyList = builder.Configuration.GetValue("License:RequireKeyList", false);

var exeDir = AppContext.BaseDirectory;
var keysListFallback = Path.Combine(exeDir, "license_keys_10000.csv");
if (!File.Exists(keysListPath) && File.Exists(keysListFallback))
    keysListPath = keysListFallback;

var dbPath = builder.Configuration["Storage:DbPath"] ?? Path.Combine(programDataRoot, "library.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// ------------------------------------------------------
// DbContext & Importers
// ------------------------------------------------------
builder.Services.AddDbContext<LibraryDbContext>(opt =>
{
    opt.UseSqlite($"Data Source={dbPath}");
});

builder.Services.AddScoped<CsvBookImporter>();
builder.Services.AddScoped<ExcelBookImporter>();
builder.Services.AddScoped<SqliteBookImporter>();
builder.Services.AddHostedService<MdnsAdvertiserHostedService>();

// ✅ เปิด CORS สำหรับ SignalR ให้ทุก Device วิ่งเข้ามาได้
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.SetIsOriginAllowed(_ => true) 
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()); 
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");

// ✅ แมป Hub สำหรับการสื่อสารกับ Android แบบ Real-time
app.MapHub<LibraryHub>("/hubs/library");

var contentTypeProvider = new FileExtensionContentTypeProvider();

// ------------------------------------------------------
// 🚨 License runtime state (Hardware-Bound / RSA Verify)
// ------------------------------------------------------
var licenseState = new LicenseState(
    envName: builder.Environment.EnvironmentName,
    licensePath: licenseFilePath,
    issuerUrl: issuerUrl,
    publicKeyPem: publicKeyPem,       // ✅ แก้ให้ชื่อตรงกับพารามิเตอร์
    bypassInDevelopment: bypassInDev  // ✅ แก้ให้ชื่อตรงกับพารามิเตอร์
);

// ตรวจสอบสถานะลิขสิทธิ์จากบอร์ดทันทีที่เปิดโปรแกรม
await licenseState.ReloadAsync();

app.Use(async (ctx, next) =>
{
    var path = (ctx.Request.Path.Value ?? "").ToLowerInvariant();

    if (path.StartsWith("/swagger") || path == "/" || path.StartsWith("/favicon") || path.StartsWith("/api/license"))
    {
        await next();
        return;
    }

    if (path.StartsWith("/api/") && !licenseState.IsLicensed)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new
        {
            message = "Unlicensed. Please activate product key tied to this motherboard.",
            machineId = licenseState.MachineId, 
            licenseFile = licenseFilePath
        });
        return;
    }

    await next();
});

// ------------------------------
// Helpers (Static)
// ------------------------------
static string? GetSetting(LibraryDbContext db, string key)
    => db.AppSettings.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefault();

static void UpsertSetting(LibraryDbContext db, string key, string value)
{
    var row = db.AppSettings.FirstOrDefault(x => x.Key == key);
    if (row == null) db.AppSettings.Add(new AppSetting { Key = key, Value = value });
    else row.Value = value;
}

static bool IsAdmin(HttpRequest req, IConfiguration cfg)
{
    var adminKey = (cfg["AdminKey"] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(adminKey)) return true;
    if (!req.Headers.TryGetValue("X-Admin-Key", out var v)) return false;
    return string.Equals(v.ToString().Trim(), adminKey, StringComparison.Ordinal);
}

// Strict variant for security-sensitive actions (e.g. reset kiosk PIN): unlike IsAdmin,
// this NEVER allows through when no AdminKey is configured. A blank/unset server AdminKey
// is treated as "deny" so an unconfigured deployment can't have its kiosk PINs reset by
// any anonymous caller. The admin must configure a matching AdminKey on both ends.
static bool IsAdminStrict(HttpRequest req, IConfiguration cfg)
{
    var adminKey = (cfg["AdminKey"] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(adminKey)) return false; // no key set => reject (no open door)
    if (!req.Headers.TryGetValue("X-Admin-Key", out var v)) return false;
    return string.Equals(v.ToString().Trim(), adminKey, StringComparison.Ordinal);
}

static string Sha256Hex(Stream s)
{
    s.Position = 0;
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(s);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static string NowText() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

static void NoCache(HttpResponse resp)
{
    resp.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
    resp.Headers["Pragma"] = "no-cache";
}

// ------------------------------------------------------
// Branding & Covers Helpers (ครบถ้วนทุกฟังก์ชัน)
// ------------------------------------------------------
string BrandingDir()
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var dir = Path.Combine(appData, "LibraryAdminPC", "branding");
    Directory.CreateDirectory(dir);
    return dir;
}

string? FindBrandingFile(string baseName)
{
    var dir = BrandingDir();
    var extensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };
    foreach (var ext in extensions)
    {
        var p = Path.Combine(dir, baseName + ext);
        if (File.Exists(p)) return p;
    }
    return null;
}

string CoversDir()
{
    var dir = Path.Combine(AppContext.BaseDirectory, "covers");
    Directory.CreateDirectory(dir);
    return dir;
}

string SafeKey(string regNo)
{
    var chars = regNo.Trim()
        .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_')
        .ToArray();
    return new string(chars);
}

string? FindCoverPath(string regNo, out string? contentType)
{
    contentType = null;
    var key = SafeKey(regNo);
    var candidates = new[]
    {
        Path.Combine(CoversDir(), key + ".png"),
        Path.Combine(CoversDir(), key + ".jpg"),
        Path.Combine(CoversDir(), key + ".jpeg"),
        Path.Combine(CoversDir(), key + ".webp"),
    };
    foreach (var p in candidates)
    {
        if (!File.Exists(p)) continue;
        if (!contentTypeProvider.TryGetContentType(p, out var ct)) ct = "application/octet-stream";
        contentType = ct;
        return p;
    }
    return null;
}

// ======================================================
// 🚨 LICENSE API (ผูกบอร์ด + VPS)
// ======================================================
app.MapGet("/api/license/status", () => Results.Ok(new
{
    isLicensed = licenseState.IsLicensed,
    message = licenseState.Message,
    machineId = licenseState.MachineId,
    licenseFile = licenseFilePath,
    issuerUrl = issuerUrl
}));

app.MapPost("/api/license/activate", async (ActivateLicenseDto body) =>
{
    var key = (body?.key ?? "").Trim();
    if (string.IsNullOrWhiteSpace(key)) return Results.BadRequest(new { message = "key is required" });

    var (ok, msg) = await licenseState.TryActivateAsync(key);
    if (!ok) return Results.BadRequest(new { message = msg });

    return Results.Ok(new
    {
        isLicensed = licenseState.IsLicensed,
        message = licenseState.Message,
        machineId = licenseState.MachineId,
        licenseFile = licenseFilePath
    });
}).DisableAntiforgery();

// ======================================================
// APP CONFIG & META
// ======================================================
app.MapGet("/api/config", (LibraryDbContext db) =>
{
    var name = GetSetting(db, "DisplayName") ?? "Library Kiosk";
    var updated = GetSetting(db, "DisplayNameUpdatedAt") ?? "-";
    return Results.Ok(new AppConfigDto(displayName: name, updatedAt: updated));
});

app.MapPost("/api/admin/config/display-name", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var body = await request.ReadFromJsonAsync<SetDisplayNameDto>();
    var name = (body?.displayName ?? "").Trim();
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "displayName is required" });

    UpsertSetting(db, "DisplayName", name);
    UpsertSetting(db, "DisplayNameUpdatedAt", NowText());
    await db.SaveChangesAsync();
    return Results.Ok(new { displayName = name });
}).DisableAntiforgery();

app.MapGet("/api/meta", async (LibraryDbContext db) =>
{
    var count = await db.Books.CountAsync();
    var lastUpdated = await db.AppSettings.Where(x => x.Key == "LastUpdated").Select(x => x.Value).FirstOrDefaultAsync() ?? "-";
    var appVersion = await db.AppSettings.Where(x => x.Key == "AppVersion").Select(x => x.Value).FirstOrDefaultAsync() ?? "1.0";
    return Results.Ok(new KioskMetaDto { BookCount = count, LastUpdated = lastUpdated, AppVersion = appVersion });
});

app.MapGet("/api/stats/category-counts", async (LibraryDbContext db) =>
{
    var items = await db.Books.AsNoTracking()
        .GroupBy(b => string.IsNullOrWhiteSpace((b.Category ?? "").Trim()) ? "ไม่ระบุหมวดหมู่" : (b.Category ?? "").Trim())
        .Select(g => new CategoryCountDto { Category = g.Key!, Count = g.Count() })
        .OrderByDescending(x => x.Count)
        .ToListAsync();
    return Results.Ok(items);
});

// ======================================================
// API: BOOKS & IMPORTING
// ======================================================
app.MapGet("/api/books", async (LibraryDbContext db, string? q, string? category) =>
{
    var query = db.Books.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(category) && category.Trim() != "ทั้งหมด") {
        var cat = category.Trim();
        query = query.Where(b => (b.Category ?? "").Trim() == cat);
    }
    if (!string.IsNullOrWhiteSpace(q)) {
        var keyword = q.Trim();
        query = query.Where(b => (b.RegNo ?? "").Contains(keyword) || (b.Title ?? "").Contains(keyword) || (b.Category ?? "").Contains(keyword) || (b.Publisher ?? "").Contains(keyword) || (b.Shelf ?? "").Contains(keyword));
    }
    var items = await query.OrderBy(b => b.RegNo).Select(b => new BookDto { RegNo = b.RegNo, Title = b.Title, Category = b.Category, Shelf = b.Shelf, Publisher = b.Publisher }).ToListAsync();
    return Results.Ok(items);
});

app.MapPost("/api/admin/import-file", async (HttpRequest request, LibraryDbContext db, CsvBookImporter csv, ExcelBookImporter excel, SqliteBookImporter sqlite, IConfiguration cfg, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var form = await request.ReadFormAsync();
    var file = form.Files["file"] ?? form.Files.FirstOrDefault();
    if (file == null || file.Length == 0) return Results.BadRequest();

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    ImportCsvResultDto result;
    using (var stream = file.OpenReadStream()) {
        result = ext switch {
            ".csv" => await csv.ImportAsync(stream, db),
            ".xlsx" => await excel.ImportAsync(stream, db),
            ".db" or ".sqlite" => await sqlite.MergeImportAsync(stream, db),
            _ => new ImportCsvResultDto { Imported = 0, Skipped = 0, Reason = "Unsupported file type" }
        };
    }
    UpsertSetting(db, "LastUpdated", NowText()); await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok(result);
}).DisableAntiforgery();

app.MapPost("/api/admin/books/clear", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var deleted = await db.Books.ExecuteDeleteAsync();
    UpsertSetting(db, "LastUpdated", NowText());
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok(new { deleted });
}).DisableAntiforgery();

// ------------------------------------------------------
// 📚 SINGLE-BOOK CRUD (C1) — additive, mirrors existing admin patterns:
// IsAdmin auth, touch LastUpdated, broadcast SyncRequested, DisableAntiforgery.
// (Add stays as import-file; only edit + delete are added here.)
// ------------------------------------------------------
app.MapPut("/api/admin/books/{regNo}", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, string regNo, BookDto body, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var key = (regNo ?? "").Trim();
    if (string.IsNullOrWhiteSpace(key)) return Results.BadRequest(new { message = "regNo is required" });
    if (body == null) return Results.BadRequest(new { message = "body is required" });

    var book = await db.Books.FirstOrDefaultAsync(b => b.RegNo == key);
    if (book == null) return Results.NotFound(new { message = "Book not found", regNo = key });

    // แก้เฉพาะฟิลด์ที่อนุญาต; RegNo เป็น identity (มาจาก route) จึงไม่แก้
    book.Title = (body.Title ?? "").Trim();
    book.Category = (body.Category ?? "").Trim();
    book.Publisher = (body.Publisher ?? "").Trim();
    book.Shelf = (body.Shelf ?? "").Trim();

    UpsertSetting(db, "LastUpdated", NowText());
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok(new BookDto { RegNo = book.RegNo, Title = book.Title, Category = book.Category, Shelf = book.Shelf, Publisher = book.Publisher });
}).DisableAntiforgery();

app.MapDelete("/api/admin/books/{regNo}", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, string regNo, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var key = (regNo ?? "").Trim();
    if (string.IsNullOrWhiteSpace(key)) return Results.BadRequest(new { message = "regNo is required" });

    var book = await db.Books.FirstOrDefaultAsync(b => b.RegNo == key);
    if (book == null) return Results.NotFound(new { message = "Book not found", regNo = key });

    db.Books.Remove(book);

    // กัน orphan cover: ลบไฟล์ปก + ล้าง hash ที่ผูกกับ regNo นี้ (พฤติกรรมเดียวกับ DELETE .../cover)
    var coverKey = SafeKey(key);
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" }) {
        var p = Path.Combine(CoversDir(), coverKey + ext); if (File.Exists(p)) File.Delete(p);
    }
    UpsertSetting(db, $"BookCover_{coverKey}_Sha256", "");

    UpsertSetting(db, "LastUpdated", NowText());
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok(new { deleted = 1, regNo = key });
}).DisableAntiforgery();

// ------------------------------------------------------
// 🔥 BRANDING API (Logo & Background)
// ------------------------------------------------------
app.MapGet("/api/branding/meta", (LibraryDbContext db) =>
{
    var lp = FindBrandingFile("logo"); var bp = FindBrandingFile("background");
    var hasLogo = lp != null; var hasBg = bp != null;
    var logoSha = GetSetting(db, "BrandingLogoSha256") ?? (hasLogo ? File.GetLastWriteTime(lp!).Ticks.ToString() : "");
    var bgSha = GetSetting(db, "BrandingBackgroundSha256") ?? (hasBg ? File.GetLastWriteTime(bp!).Ticks.ToString() : "");
    return Results.Ok(new BrandingMetaDto(hasLogo, hasBg, GetSetting(db, "BrandingUpdatedAt"), logoSha, bgSha));
});

app.MapGet("/api/branding/logo", (HttpResponse resp) =>
{
    NoCache(resp); var path = FindBrandingFile("logo");
    if (path == null) return Results.NotFound();
    return Results.File(path, contentTypeProvider.TryGetContentType(path, out var ct) ? ct : "image/png", null, null, null, true); 
});

app.MapGet("/api/branding/background", (HttpResponse resp) =>
{
    NoCache(resp); var path = FindBrandingFile("background");
    if (path == null) return Results.NotFound();
    return Results.File(path, contentTypeProvider.TryGetContentType(path, out var ct) ? ct : "image/png", null, null, null, true); 
});

app.MapPost("/api/admin/branding/upload", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var form = await request.ReadFormAsync();
    var logo = form.Files["logo"]; var bg = form.Files["background"];
    
    async Task SaveImg(IFormFile f, string name, string setKey) {
        if (f == null || f.Length == 0) return;
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp" }) {
            var oldPath = Path.Combine(BrandingDir(), name + extension);
            if (File.Exists(oldPath)) try { File.Delete(oldPath); } catch {}
        }
        var path = Path.Combine(BrandingDir(), name + Path.GetExtension(f.FileName));
        using (var fs = new FileStream(path, FileMode.Create)) await f.CopyToAsync(fs);
        using (var v = File.OpenRead(path)) UpsertSetting(db, setKey, Sha256Hex(v));
    }
    await SaveImg(logo!, "logo", "BrandingLogoSha256");
    await SaveImg(bg!, "background", "BrandingBackgroundSha256");
    var now = NowText();
    UpsertSetting(db, "BrandingUpdatedAt", now); UpsertSetting(db, "LastUpdated", now);
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok();
}).DisableAntiforgery();

app.MapDelete("/api/admin/branding/logo", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" }) {
        var p = Path.Combine(BrandingDir(), "logo" + ext); if (File.Exists(p)) File.Delete(p);
    }
    UpsertSetting(db, "BrandingLogoSha256", ""); UpsertSetting(db, "BrandingUpdatedAt", NowText());
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok();
}).DisableAntiforgery();

app.MapDelete("/api/admin/branding/background", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" }) {
        var p = Path.Combine(BrandingDir(), "background" + ext); if (File.Exists(p)) File.Delete(p);
    }
    UpsertSetting(db, "BrandingBackgroundSha256", ""); UpsertSetting(db, "BrandingUpdatedAt", NowText());
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok();
}).DisableAntiforgery();

// ======================================================
// BOOK COVER API
// ======================================================
app.MapGet("/api/books/{regNo}/cover", (HttpResponse resp, string regNo) =>
{
    NoCache(resp); var path = FindCoverPath(regNo, out var ct);
    if (path == null) return Results.NotFound();
    return Results.File(path, ct ?? "application/octet-stream");
});

app.MapGet("/api/books/{regNo}/cover/meta", (LibraryDbContext db, string regNo) =>
{
    var key = SafeKey(regNo);
    var sha = GetSetting(db, $"BookCover_{key}_Sha256");
    return Results.Ok(new { regNo, hasCover = !string.IsNullOrWhiteSpace(sha) && FindCoverPath(regNo, out _) != null, sha256 = sha, updatedAt = GetSetting(db, $"BookCover_{key}_UpdatedAt") });
});

app.MapPost("/api/admin/books/{regNo}/cover", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, string regNo, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var form = await request.ReadFormAsync();
    var file = form.Files["file"] ?? form.Files.FirstOrDefault(); // ✅ ตัวแปรชื่อ file
    if (file == null) return Results.BadRequest();
    var key = SafeKey(regNo);
    foreach (var old in new[] { ".png", ".jpg", ".jpeg", ".webp" }) {
        var op = Path.Combine(CoversDir(), key + old); if (File.Exists(op)) File.Delete(op);
    }
    var savePath = Path.Combine(CoversDir(), key + Path.GetExtension(file.FileName));
    using (var fs = new FileStream(savePath, FileMode.Create)) 
    {
        await file.CopyToAsync(fs); // ✅ แก้จาก f เป็น file
    }
    var now = NowText();
    using (var v = File.OpenRead(savePath)) UpsertSetting(db, $"BookCover_{key}_Sha256", Sha256Hex(v));
    UpsertSetting(db, $"BookCover_{key}_UpdatedAt", now); UpsertSetting(db, "LastUpdated", now);
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok();
}).DisableAntiforgery();

app.MapDelete("/api/admin/books/{regNo}/cover", async (HttpRequest request, LibraryDbContext db, IConfiguration cfg, string regNo, IHubContext<LibraryHub> hub) =>
{
    if (!IsAdmin(request, cfg)) return Results.Unauthorized();
    var key = SafeKey(regNo);
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" }) {
        var p = Path.Combine(CoversDir(), key + ext); if (File.Exists(p)) File.Delete(p);
    }
    UpsertSetting(db, $"BookCover_{key}_Sha256", ""); UpsertSetting(db, "LastUpdated", NowText());
    await db.SaveChangesAsync(); await hub.Clients.All.SendAsync("SyncRequested");
    return Results.Ok();
}).DisableAntiforgery();

// K3: live kiosk count for the admin dashboard. Public read (like /api/meta); cap is a
// server constant, overridable via config Storage:MaxKiosks. Display-only — not enforced.
app.MapGet("/api/kiosks/active", (KioskRegistry registry, IConfiguration cfg) =>
{
    var cap = cfg.GetValue<int?>("Storage:MaxKiosks") ?? 10;
    return Results.Ok(new { active = registry.ActiveCount, cap });
});

// Reset the admin PIN on every connected kiosk (the on-site operator forgot it). Pushes a
// "PinResetRequested" broadcast — same fire-and-forget pattern as SyncRequested — and each
// kiosk clears its local PIN back to the default "1234". The PIN lives only on the kiosk
// (kiosk-settings.json); the server never stores/sends a PIN, so this only signals the reset.
// SECURITY: IsAdminStrict — a configured, matching X-Admin-Key is REQUIRED (a blank server
// AdminKey is rejected). Returns the number of kiosks the broadcast was pushed to.
app.MapPost("/api/admin/kiosks/reset-pin", async (HttpRequest request, IConfiguration cfg, IHubContext<LibraryHub> hub, KioskRegistry registry) =>
{
    if (!IsAdminStrict(request, cfg)) return Results.Unauthorized();
    var pushed = registry.ActiveCount;
    await hub.Clients.All.SendAsync("PinResetRequested");
    return Results.Ok(new { pushed });
}).DisableAntiforgery();

app.MapGet("/", () => Results.Ok(new { message = "Library API Server running (Hardware-Bound OEM Mode)" }));

app.Run();

// ======================================================
// 🚨 LICENSE STATE (ผูกเมนบอร์ด + RSA Verification)
// ======================================================
public sealed class LicenseState {
    public bool IsLicensed { get; private set; }
    public string Message { get; private set; } = "Unlicensed";
    public string MachineId { get; private set; }

    private readonly string _envName;
    private readonly string _licensePath;
    private readonly string _issuerUrl;
    private readonly string _publicKey;
    private readonly bool _bypassDev;
    private static readonly HttpClient _http = new HttpClient();

    // ✅ ปรับชื่อพารามิเตอร์ให้ตรงกับจุดที่เรียกใช้งานด้านบน
    public LicenseState(string envName, string licensePath, string issuerUrl, string publicKeyPem, bool bypassInDevelopment) {
         _envName = envName; 
        _licensePath = licensePath; 
        _issuerUrl = issuerUrl; 
        _publicKey = publicKeyPem; 
        _bypassDev = bypassInDevelopment; 
        MachineId = GetMainboardSerial();
    }

    public async Task ReloadAsync() {
        if (string.Equals(_envName, "Development", StringComparison.OrdinalIgnoreCase) && _bypassDev) { 
            IsLicensed = true; Message = "Licensed (Bypass Mode)"; return; 
        }
        if (!File.Exists(_licensePath)) { IsLicensed = false; Message = "Unlicensed (No key found)"; return; }

        try {
            var json = File.ReadAllText(_licensePath);
            var license = JsonSerializer.Deserialize<LicenseFile>(json);
            
            if (license?.machineId != MachineId) {
                IsLicensed = false; Message = "Invalid License: Machine mismatch."; return;
            }

            var payload = $"{license.key}|{license.machineId}|{license.issuedAt}";
            if (VerifySignature(_publicKey, payload, license.sig ?? "")) {
                IsLicensed = true; Message = "Licensed (OEM Hardware Authenticated)";
            } else {
                IsLicensed = false; Message = "Invalid Signature: License file tampered.";
            }
        } catch { IsLicensed = false; Message = "Invalid license format."; }
    }

    public async Task<(bool ok, string msg)> TryActivateAsync(string key) {
        try {
            var res = await _http.PostAsJsonAsync($"{_issuerUrl}/api/issue", new { key, machineId = MachineId });
            if (!res.IsSuccessStatusCode) return (false, "Activation failed: " + await res.Content.ReadAsStringAsync());

            var licenseData = await res.Content.ReadAsStringAsync();
            File.WriteAllText(_licensePath, licenseData); 
            await ReloadAsync();
            return (IsLicensed, Message);
        } catch (Exception ex) { return (false, "Connection Error: " + ex.Message); }
    }

    private static bool VerifySignature(string publicKeyPem, string payload, string signatureBase64) {
        try {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var data = Encoding.UTF8.GetBytes(payload);
            var signature = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        } catch { return false; }
    }

    private static string GetMainboardSerial() {
        try {
            using var s = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (var w in s.Get()) {
                var sn = w["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(sn) && sn != "Default string") return sn.ToUpper();
            }
        } catch { }
        return "MB-UNKNOWN-" + Environment.MachineName.ToUpper();
    }
}

// ------------------------------------------------------
// 📦 MODELS & TYPES
// ------------------------------------------------------

// K3: the same hub the kiosk already uses for SyncRequested now also counts kiosks.
// A connection is a kiosk only when it joins with ?client=kiosk&kioskId=<id>; admin /
// other clients send no such flag and are never counted. Counting is by DISTINCT
// kioskId (not connectionId) so a reconnect that briefly holds two connections — or any
// extra connection from one device — counts as one. Existing SyncRequested broadcast is
// untouched (it goes through IHubContext, independent of these hooks).
public class LibraryHub : Hub
{
    private readonly KioskRegistry _registry;
    public LibraryHub(KioskRegistry registry) => _registry = registry;

    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var client = http?.Request.Query["client"].ToString();
        if (string.Equals(client, "kiosk", StringComparison.OrdinalIgnoreCase))
        {
            var kioskId = http!.Request.Query["kioskId"].ToString();
            if (string.IsNullOrWhiteSpace(kioskId)) kioskId = Context.ConnectionId; // fallback
            _registry.Add(kioskId, Context.ConnectionId);
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Harmless no-op for non-kiosk connections (their connectionId isn't tracked).
        _registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

// Thread-safe live count of connected kiosks, keyed by kioskId. Each kioskId holds the
// set of its current connectionIds; the kioskId drops out only when its last connection
// goes away — so a transient reconnect (new connectionId before the old one times out)
// never changes the distinct count.
public sealed class KioskRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _kiosks = new();

    public void Add(string kioskId, string connectionId)
    {
        var set = _kiosks.GetOrAdd(kioskId, _ => new ConcurrentDictionary<string, byte>());
        set[connectionId] = 0;
    }

    public void Remove(string connectionId)
    {
        foreach (var kv in _kiosks)
        {
            if (kv.Value.TryRemove(connectionId, out _))
            {
                if (kv.Value.IsEmpty)
                {
                    _kiosks.TryRemove(kv.Key, out _);
                    // Guard a race with a concurrent Add to the same set instance.
                    if (!kv.Value.IsEmpty) _kiosks.TryAdd(kv.Key, kv.Value);
                }
                break;
            }
        }
    }

    /// <summary>Number of distinct connected kiosks.</summary>
    public int ActiveCount => _kiosks.Count;
}
public sealed class ActivateLicenseDto { public string? key { get; set; } }
public sealed class LicenseFile { public string? key { get; set; } public string? machineId { get; set; } public string? issuedAt { get; set; } public string? sig { get; set; } }