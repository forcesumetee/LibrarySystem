using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibraryKiosk.Utils;
using LibraryShared;
using LibraryShared.Dtos;

namespace LibraryKiosk.Services;

/// <summary>
/// Read-only HTTP client for the Library API server. The kiosk only ever GETs;
/// it never mutates server state. Property casing from the server (ASP.NET
/// camelCase) is handled with case-insensitive deserialization, matching the
/// existing LibraryAdminPC client.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public string BaseUrl { get; }

    public ApiClient(string baseUrl, TimeSpan? timeout = null)
    {
        BaseUrl = NormalizeBase(baseUrl);

        _http = new HttpClient
        {
            // Short timeout so a dead server never freezes the UI thread chain.
            Timeout = timeout ?? TimeSpan.FromSeconds(8)
        };
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Trim trailing slashes so path joins never produce "//".</summary>
    private static string NormalizeBase(string baseUrl)
    {
        var b = (baseUrl ?? "").Trim();
        return string.IsNullOrEmpty(b) ? "" : b.TrimEnd('/');
    }

    /// <summary>Join base + relative path safely (no double slashes).</summary>
    private string BuildUrl(string path)
    {
        var p = (path ?? "").TrimStart('/');
        return $"{BaseUrl}/{p}";
    }

    /// <summary>
    /// Append a cache-busting <c>?t=&lt;unixMs&gt;</c> query so image responses are
    /// never served stale (covers/branding fetch in later phases).
    /// </summary>
    public string WithCacheBuster(string path)
    {
        var url = BuildUrl(path);
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    /// <summary>
    /// GET /api/meta. Returns a state-tagged result instead of throwing on the
    /// expected failure modes (403 unlicensed, server unreachable).
    /// </summary>
    public async Task<MetaResult> GetMetaAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BuildUrl("api/meta"), ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                KioskLog.Warn("GET /api/meta -> 403 (server reachable, unlicensed).");
                return MetaResult.Unlicensed("Server ยังไม่ได้ activate license");
            }

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                KioskLog.Warn($"GET /api/meta -> {(int)resp.StatusCode}. Body: {body}");
                return MetaResult.Unreachable($"Server ตอบกลับผิดปกติ ({(int)resp.StatusCode})");
            }

            var meta = JsonSerializer.Deserialize<KioskMetaDto>(body, JsonOptions);
            if (meta == null)
            {
                KioskLog.Warn("GET /api/meta -> 200 but empty/invalid body.");
                return MetaResult.Unreachable("Server ส่งข้อมูลว่าง");
            }

            return MetaResult.Ok(meta);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (HttpClient.Timeout cancels with this exception).
            KioskLog.Warn("GET /api/meta timed out.");
            return MetaResult.Unreachable("หมดเวลาเชื่อมต่อ");
        }
        catch (HttpRequestException ex)
        {
            KioskLog.Warn($"GET /api/meta connection failed: {ex.Message}");
            return MetaResult.Unreachable("เชื่อมต่อ Server ไม่ได้");
        }
    }

    /// <summary>
    /// GET /api/books with no filter (full catalogue). Filtering/search is done
    /// client-side in later phases. Returns an empty list on any failure.
    /// </summary>
    public async Task<List<BookDto>> GetBooksAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BuildUrl("api/books"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                KioskLog.Warn($"GET /api/books -> {(int)resp.StatusCode}.");
                return new List<BookDto>();
            }
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<BookDto>>(body, JsonOptions) ?? new List<BookDto>();
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"GET /api/books failed: {ex.Message}");
            return new List<BookDto>();
        }
    }

    /// <summary>GET /api/config — library display name. Null on failure.</summary>
    public async Task<string?> GetDisplayNameAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BuildUrl("api/config"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("displayName", out var dn))
                return dn.GetString();
            return null;
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"GET /api/config failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>GET /api/branding/meta. Null on failure.</summary>
    public async Task<BrandingMetaDto?> GetBrandingMetaAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BuildUrl("api/branding/meta"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<BrandingMetaDto>(body, JsonOptions);
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"GET /api/branding/meta failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// GET /api/books/{regNo}/cover/meta — lightweight cover change-token. Returns
    /// (hasCover, sha256) so a sync can decide whether to (re)download a cover
    /// without fetching the image. Null on any failure (caller keeps what it has).
    /// </summary>
    public async Task<(bool hasCover, string? sha)?> GetCoverMetaAsync(string regNo, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BuildUrl($"api/books/{Uri.EscapeDataString(regNo)}/cover/meta"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var hasCover = root.TryGetProperty("hasCover", out var hc) && hc.ValueKind == JsonValueKind.True;
            string? sha = root.TryGetProperty("sha256", out var s) ? s.GetString() : null;
            return (hasCover, sha);
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"GET cover/meta {regNo} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download raw image bytes for a relative path (e.g. "api/branding/logo"),
    /// always cache-busted. Returns null on 404 or any failure.
    /// </summary>
    public async Task<byte[]?> GetImageBytesAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(WithCacheBuster(path), ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            if (!resp.IsSuccessStatusCode)
            {
                KioskLog.Warn($"GET {path} -> {(int)resp.StatusCode}.");
                return null;
            }
            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"GET {path} (image) failed: {ex.Message}");
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
