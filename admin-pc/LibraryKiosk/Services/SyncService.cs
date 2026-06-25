using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using LibraryKiosk.Utils;
using LibraryShared;
using Microsoft.AspNetCore.SignalR.Client;

namespace LibraryKiosk.Services;

/// <summary>
/// Owns the live link to the server: a SignalR connection to
/// {baseUrl}/hubs/library plus the HTTP fetch that builds a full
/// <see cref="SyncSnapshot"/> (meta + books + categories + branding images).
///
/// The hub auto-reconnects on transient drops; if it closes permanently it is
/// restarted on a 5s loop (mirrors the Android client) so the kiosk recovers
/// from a server restart on its own. Every (re)connect raises
/// <see cref="SyncTriggered"/> so a missed update during the outage is caught.
/// The base URL is rebindable for the settings screen.
/// </summary>
public sealed class SyncService : IAsyncDisposable
{
    private readonly object _gate = new();
    private ApiClient _api;
    private HubConnection? _hub;
    private CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;
    private volatile bool _stopping;

    // Branding change-token cache: skip re-downloading an unchanged image.
    private string? _logoSha;
    private string? _bgSha;
    private BitmapSource? _logoCache;
    private BitmapSource? _bgCache;

    // Book-cover cache (keyed by regNo); null value = "already tried, none found".
    private readonly Dictionary<string, BitmapSource?> _coverCache = new();

    /// <summary>Raised on hub "SyncRequested" and on every (re)connect. May fire on a background thread.</summary>
    public event EventHandler? SyncTriggered;

    public SyncService(string baseUrl)
    {
        _api = new ApiClient(baseUrl);
    }

    public string BaseUrl => _api.BaseUrl;

    // ---------------- lifecycle ----------------

    public Task StartAsync() => ConnectAsync();

    /// <summary>Switch to a new server base URL and reconnect (settings screen).</summary>
    public async Task RebindAsync(string newBaseUrl)
    {
        await StopHubAsync().ConfigureAwait(false);

        lock (_gate)
        {
            _api.Dispose();
            _api = new ApiClient(newBaseUrl);
            // New server => previous branding/cover caches are meaningless.
            _logoSha = _bgSha = null;
            _logoCache = _bgCache = null;
            _coverCache.Clear();
            _lifetime = new CancellationTokenSource();
        }
        KioskLog.Info($"SyncService rebound to {newBaseUrl}");
        await ConnectAsync().ConfigureAwait(false);
    }

    private Task ConnectAsync()
    {
        if (_disposed) return Task.CompletedTask;

        var url = $"{_api.BaseUrl}/hubs/library";
        var hub = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero, TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)
            })
            .Build();

        hub.On("SyncRequested", () =>
        {
            KioskLog.Info("Hub: SyncRequested received.");
            SyncTriggered?.Invoke(this, EventArgs.Empty);
        });

        hub.Reconnected += _ =>
        {
            KioskLog.Info("Hub reconnected; triggering catch-up sync.");
            SyncTriggered?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };

        hub.Closed += OnHubClosed;

        lock (_gate) { _hub = hub; }

        // Kick off the (possibly long, 5s-retrying) connect in the background so
        // neither startup nor a settings rebind ever blocks on an unreachable
        // server. The hub raises SyncTriggered once it connects; the caller's own
        // HTTP sync (SyncNowAsync) surfaces the reachable/unreachable state meanwhile.
        _ = StartWithRetryAsync(hub);
        return Task.CompletedTask;
    }

    private async Task StartWithRetryAsync(HubConnection hub)
    {
        var token = _lifetime.Token;
        var firstFail = true;
        while (!_disposed && !token.IsCancellationRequested)
        {
            try
            {
                await hub.StartAsync(token).ConfigureAwait(false);
                KioskLog.Info($"Hub connected: {_api.BaseUrl}");
                // Catch-up sync on connect so we never miss data set before we joined.
                SyncTriggered?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (Exception ex) when (!_disposed && !token.IsCancellationRequested)
            {
                // Log the first failure only, then retry quietly every 5s (no log spam).
                if (firstFail)
                {
                    KioskLog.Warn($"Hub connect failed ({ex.Message}); retrying every 5s.");
                    firstFail = false;
                }
                try { await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task OnHubClosed(Exception? error)
    {
        if (_disposed || _stopping) return; // expected close during stop/rebind/dispose
        KioskLog.Warn($"Hub closed ({error?.Message ?? "no error"}); restarting in 5s.");
        try { await Task.Delay(TimeSpan.FromSeconds(5), _lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        HubConnection? hub;
        lock (_gate) { hub = _hub; }
        if (hub != null && !_disposed) await StartWithRetryAsync(hub).ConfigureAwait(false);
    }

    private async Task StopHubAsync()
    {
        _stopping = true;
        try
        {
            _lifetime.Cancel();
            HubConnection? hub;
            lock (_gate) { hub = _hub; _hub = null; }
            if (hub != null)
            {
                hub.Closed -= OnHubClosed;
                try { await hub.StopAsync().ConfigureAwait(false); } catch { /* ignore */ }
                await hub.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally { _stopping = false; }
    }

    // ---------------- data ----------------

    /// <summary>Fetch meta + books + categories + branding into one snapshot.</summary>
    public async Task<SyncSnapshot> SyncNowAsync(CancellationToken ct = default)
    {
        var api = _api; // local copy; rebind may swap the field

        var meta = await api.GetMetaAsync(ct).ConfigureAwait(false);
        if (meta.State != ConnectionState.Connected)
        {
            return new SyncSnapshot { State = meta.State, Message = meta.Message };
        }

        var books = await api.GetBooksAsync(ct).ConfigureAwait(false);
        var categories = BuildCategories(books);
        var displayName = await api.GetDisplayNameAsync(ct).ConfigureAwait(false);
        var (logo, background) = await LoadBrandingAsync(api, ct).ConfigureAwait(false);

        KioskLog.Info($"Sync ok: {books.Count} books, {categories.Count - 1} categories.");

        return new SyncSnapshot
        {
            State = ConnectionState.Connected,
            Meta = meta.Meta,
            DisplayName = displayName,
            Books = books,
            Categories = categories,
            Logo = logo,
            Background = background
        };
    }

    private static List<string> BuildCategories(IReadOnlyList<BookDto> books)
    {
        var cats = books
            .Select(b => (b.Category ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct()
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();
        cats.Insert(0, "ทั้งหมด");
        return cats;
    }

    private async Task<(BitmapSource? logo, BitmapSource? background)> LoadBrandingAsync(
        ApiClient api, CancellationToken ct)
    {
        var meta = await api.GetBrandingMetaAsync(ct).ConfigureAwait(false);
        if (meta == null)
        {
            // Could not read branding meta: keep whatever we already showed.
            return (_logoCache, _bgCache);
        }

        var logo = await ResolveImageAsync(
            api, "api/branding/logo", meta.HasLogo, meta.LogoSha256,
            () => _logoSha, s => _logoSha = s, () => _logoCache, b => _logoCache = b, ct).ConfigureAwait(false);

        var background = await ResolveImageAsync(
            api, "api/branding/background", meta.HasBackground, meta.BackgroundSha256,
            () => _bgSha, s => _bgSha = s, () => _bgCache, b => _bgCache = b, ct).ConfigureAwait(false);

        return (logo, background);
    }

    /// <summary>
    /// Download + decode an image only when its sha changed; otherwise reuse the
    /// cached frozen bitmap. Returns null (and clears cache) when absent on server.
    /// </summary>
    private static async Task<BitmapSource?> ResolveImageAsync(
        ApiClient api, string path, bool exists, string? sha,
        Func<string?> getSha, Action<string?> setSha,
        Func<BitmapSource?> getCache, Action<BitmapSource?> setCache,
        CancellationToken ct)
    {
        if (!exists)
        {
            setSha(null);
            setCache(null);
            return null;
        }

        if (sha != null && sha == getSha() && getCache() != null)
        {
            return getCache(); // unchanged, skip re-download
        }

        var bytes = await api.GetImageBytesAsync(path, ct).ConfigureAwait(false);
        var img = ImageLoader.FromBytes(bytes);
        if (img != null)
        {
            setSha(sha);
            setCache(img);
            return img;
        }
        return getCache(); // download failed; keep previous
    }

    /// <summary>
    /// Fetch + decode a book cover (cache-busted, frozen), memoised per regNo so
    /// re-filtering/re-syncing never refetches. Returns null when there is no cover.
    /// </summary>
    public async Task<BitmapSource?> LoadCoverAsync(string regNo)
    {
        if (string.IsNullOrWhiteSpace(regNo)) return null;

        lock (_gate)
        {
            if (_coverCache.TryGetValue(regNo, out var cached)) return cached;
        }

        var api = _api;
        var bytes = await api.GetImageBytesAsync($"api/books/{Uri.EscapeDataString(regNo)}/cover").ConfigureAwait(false);
        var img = ImageLoader.FromBytes(bytes);

        lock (_gate) { _coverCache[regNo] = img; }
        return img;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopHubAsync().ConfigureAwait(false);
        lock (_gate) { _api.Dispose(); }
    }
}
