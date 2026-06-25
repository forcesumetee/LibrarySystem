using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibraryKiosk.Utils;
using LibraryShared;

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

    public void Dispose() => _http.Dispose();
}
