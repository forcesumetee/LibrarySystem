using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LibraryAdminPC.Models;

namespace LibraryAdminPC.Services;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(AppConfig config)
    {
        var baseUrl = (config.ApiBaseUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("ApiBaseUrl is empty.");

        if (!baseUrl.EndsWith("/"))
            baseUrl += "/";

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(config.AdminKey))
        {
            _http.DefaultRequestHeaders.Remove("X-Admin-Key");
            _http.DefaultRequestHeaders.Add("X-Admin-Key", config.AdminKey.Trim());
        }
    }

    // -----------------------------
    // License
    // -----------------------------
    public async Task<LicenseStatusDto> GetLicenseStatusAsync()
    {
        using var resp = await _http.GetAsync("api/license/status");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetLicenseStatus failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<LicenseStatusDto>(body, _jsonOptions);
        return dto ?? new LicenseStatusDto { IsLicensed = false, Message = "Empty response" };
    }

    public async Task<LicenseActivateResultDto> ActivateLicenseAsync(string productKey)
    {
        var key = (productKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Product key is empty.");

        var payload = JsonSerializer.Serialize(new { key });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync("api/license/activate", content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"ActivateLicense failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<LicenseActivateResultDto>(body, _jsonOptions);
        return dto ?? new LicenseActivateResultDto { IsLicensed = false, Message = "Empty response" };
    }

    // -----------------------------
    // Basic / Health
    // Treat 403 from /api/meta as "server reachable but unlicensed"
    // -----------------------------
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var resp = await _http.GetAsync("api/meta");
            if (resp.IsSuccessStatusCode) return true;

            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                // check license endpoint to confirm server is up
                using var r2 = await _http.GetAsync("api/license/status");
                return r2.IsSuccessStatusCode;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // -----------------------------
    // Meta / Dashboard
    // -----------------------------
    public async Task<KioskMetaDto> GetMetaAsync()
    {
        using var resp = await _http.GetAsync("api/meta");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetMeta failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<KioskMetaDto>(body, _jsonOptions);
        return dto ?? throw new Exception("API returned empty meta response.");
    }

    public async Task<List<CategoryCountDto>> GetCategoryCountsAsync()
    {
        using var resp = await _http.GetAsync("api/stats/category-counts");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetCategoryCounts failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<List<CategoryCountDto>>(body, _jsonOptions);
        return dto ?? new List<CategoryCountDto>();
    }

    // -----------------------------
    // Books
    // -----------------------------
    public async Task<List<BookDto>> GetBooksAsync(string? q = null, string? category = null)
    {
        var url = "api/books";
        var qs = new List<string>();

        if (!string.IsNullOrWhiteSpace(q))
            qs.Add("q=" + Uri.EscapeDataString(q.Trim()));

        if (!string.IsNullOrWhiteSpace(category))
            qs.Add("category=" + Uri.EscapeDataString(category.Trim()));

        if (qs.Count > 0)
            url += "?" + string.Join("&", qs);

        using var resp = await _http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetBooks failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<List<BookDto>>(body, _jsonOptions);
        return dto ?? new List<BookDto>();
    }

    // -----------------------------
    // Import via /api/admin/import-file
    // -----------------------------
    public Task<ImportCsvResultDto> ImportCsvAsync(string filePath)
        => ImportFileAsync(filePath);

    public async Task<ImportCsvResultDto> ImportFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is empty.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var mime = ext == ".csv" ? "text/csv" : "application/octet-stream";

        await using var fs = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);

        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        using var resp = await _http.PostAsync("api/admin/import-file", form);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Import failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<ImportCsvResultDto>(body, _jsonOptions);
        return dto ?? new ImportCsvResultDto
        {
            Imported = 0,
            Skipped = 0,
            Reason = "Empty response from server"
        };
    }

    // -----------------------------
    // Admin: Clear Books
    // -----------------------------
    public async Task<int> ClearAllBooksAsync()
    {
        using var resp = await _http.PostAsync("api/admin/books/clear", content: null);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"ClearAllBooks failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("deleted", out var del) && del.TryGetInt32(out var n))
                return n;
        }
        catch { }

        return 0;
    }

    // -----------------------------
    // Branding (optional)
    // -----------------------------
    public async Task<BrandingMetaDto> GetBrandingMetaAsync()
    {
        using var resp = await _http.GetAsync("api/branding/meta");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetBrandingMeta failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");

        var dto = JsonSerializer.Deserialize<BrandingMetaDto>(body, _jsonOptions);
        return dto ?? throw new Exception("Empty BrandingMeta response.");
    }

    public async Task UploadBrandingAsync(string? logoPath, string? backgroundPath)
    {
        if ((string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath)) &&
            (string.IsNullOrWhiteSpace(backgroundPath) || !File.Exists(backgroundPath)))
            throw new ArgumentException("No valid files to upload.");

        using var form = new MultipartFormDataContent();

        Stream? logoStream = null;
        Stream? bgStream = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                logoStream = File.OpenRead(logoPath);
                var logoContent = new StreamContent(logoStream);
                logoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(logoContent, "logo", Path.GetFileName(logoPath));
            }

            if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
            {
                bgStream = File.OpenRead(backgroundPath);
                var bgContent = new StreamContent(bgStream);
                bgContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(bgContent, "background", Path.GetFileName(backgroundPath));
            }

            using var resp = await _http.PostAsync("api/admin/branding/upload", form);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"UploadBranding failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");
        }
        finally
        {
            logoStream?.Dispose();
            bgStream?.Dispose();
        }
    }

    public async Task DeleteLogoAsync()
    {
        using var resp = await _http.DeleteAsync("api/admin/branding/logo");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"DeleteLogo failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");
    }

    public async Task DeleteBackgroundAsync()
    {
        using var resp = await _http.DeleteAsync("api/admin/branding/background");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"DeleteBackground failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");
    }

    // -----------------------------
    // Book Covers
    // -----------------------------
    public async Task<byte[]?> GetBookCoverBytesAsync(string regNo)
    {
        if (string.IsNullOrWhiteSpace(regNo))
            throw new ArgumentException("regNo is empty.");

        using var resp = await _http.GetAsync($"api/books/{Uri.EscapeDataString(regNo)}/cover");
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bytes = await resp.Content.ReadAsByteArrayAsync();

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new Exception($"GetBookCoverBytes failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");
        }

        return bytes;
    }

    public async Task UploadBookCoverAsync(string regNo, string filePath)
    {
        if (string.IsNullOrWhiteSpace(regNo))
            throw new ArgumentException("regNo is empty.");

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Cover file not found.", filePath);

        await using var fs = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        using var resp = await _http.PostAsync($"api/admin/books/{Uri.EscapeDataString(regNo)}/cover", form);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"UploadBookCover failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");
    }

    public async Task DeleteBookCoverAsync(string regNo)
    {
        if (string.IsNullOrWhiteSpace(regNo))
            throw new ArgumentException("regNo is empty.");

        using var resp = await _http.DeleteAsync($"api/admin/books/{Uri.EscapeDataString(regNo)}/cover");
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"DeleteBookCover failed: {(int)resp.StatusCode} {resp.StatusCode}\n{body}");
    }
}

// DTOs (keep here to avoid creating extra files)
public class LicenseStatusDto
{
    public bool IsLicensed { get; set; }
    public string? Message { get; set; }
    public string? LicenseFile { get; set; }
    public bool RequireKeyList { get; set; }
    public string? KeysListPath { get; set; }
    public bool KeysListFound { get; set; }
}

public class LicenseActivateResultDto
{
    public bool IsLicensed { get; set; }
    public string? Message { get; set; }
    public string? LicenseFile { get; set; }
}