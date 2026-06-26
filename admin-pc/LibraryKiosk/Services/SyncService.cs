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

    // Book-cover cache (keyed by regNo). Sha is the server's cover change-token; a
    // null Image with null Sha means "no cover". Storing the sha lets a sync detect
    // a changed/added/removed cover and refetch only that one (see ResolveCoverAsync).
    private sealed class CoverEntry
    {
        public BitmapSource? Image;
        public string? Sha;
    }
    private readonly Dictionary<string, CoverEntry> _coverCache = new();

    // Server LastUpdated value at the last cover verification; covers are only
    // re-verified when this changes (reset on rebind so a new server re-scans).
    private string? _coverScanStamp;

    /// <summary>True when covers should be re-verified for this server stamp.</summary>
    public bool CoversNeedVerify(string stamp) => _coverScanStamp != stamp;

    /// <summary>Record that covers were fully verified for this server stamp.</summary>
    public void MarkCoversScanned(string stamp) => _coverScanStamp = stamp;

    /// <summary>Raised on hub "SyncRequested" and on every (re)connect. May fire on a background thread.</summary>
    public event EventHandler? SyncTriggered;

    /// <summary>
    /// Raised when the live SignalR link goes up (true) or down (false). Reuses the
    /// hub's own connect/reconnect/close callbacks — no extra polling. The settings
    /// panel uses this for a realtime connected/disconnected indicator. May fire on a
    /// background thread.
    /// </summary>
    public event EventHandler<bool>? HubConnectionChanged;

    private volatile bool _hubConnected;

    /// <summary>Last known SignalR link state (true = connected).</summary>
    public bool IsHubConnected => _hubConnected;

    private void SetHubConnected(bool connected)
    {
        if (_hubConnected == connected) return;
        _hubConnected = connected;
        HubConnectionChanged?.Invoke(this, connected);
    }

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
            _coverScanStamp = null; // force a cover re-verify against the new server
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

        hub.Reconnecting += _ =>
        {
            SetHubConnected(false);
            return Task.CompletedTask;
        };

        hub.Reconnected += _ =>
        {
            KioskLog.Info("Hub reconnected; triggering catch-up sync.");
            SetHubConnected(true);
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
                SetHubConnected(true);
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
        SetHubConnected(false);
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
    /// Resolve the cover image for <paramref name="regNo"/>, frozen and memoised.
    ///
    /// When <paramref name="verify"/> is false the cached image is returned with no
    /// HTTP at all (the hot path: re-filtering, reconnects, syncs where nothing
    /// changed). When true — used after an actual server change — a lightweight
    /// cover/meta call decides the next step: unchanged sha reuses the cached image;
    /// a changed/new cover is re-downloaded; a removed cover collapses to null
    /// (placeholder). A failed meta check keeps whatever was cached.
    ///
    /// This is the cover analogue of <see cref="ResolveImageAsync"/> for branding,
    /// and the fix for "cover uploaded from admin never appears until kiosk restart".
    /// </summary>
    public async Task<BitmapSource?> ResolveCoverAsync(string regNo, bool verify)
    {
        if (string.IsNullOrWhiteSpace(regNo)) return null;

        CoverEntry? entry;
        lock (_gate) { _coverCache.TryGetValue(regNo, out entry); }

        if (!verify)
        {
            return entry?.Image; // cached (image or null); no network
        }

        var api = _api;
        var meta = await api.GetCoverMetaAsync(regNo).ConfigureAwait(false);
        if (meta == null)
        {
            return entry?.Image; // meta check failed; keep what we have
        }

        var (hasCover, sha) = meta.Value;

        if (!hasCover)
        {
            // Cover absent (never had one / just deleted) -> placeholder.
            lock (_gate) { _coverCache[regNo] = new CoverEntry { Image = null, Sha = null }; }
            return null;
        }

        // Unchanged sha and we already hold the image -> reuse, no download.
        if (entry?.Image != null && entry.Sha == sha)
        {
            return entry.Image;
        }

        // New or changed cover -> download the image once.
        var bytes = await api.GetImageBytesAsync($"api/books/{Uri.EscapeDataString(regNo)}/cover").ConfigureAwait(false);
        var img = ImageLoader.FromBytes(bytes);
        lock (_gate) { _coverCache[regNo] = new CoverEntry { Image = img, Sha = img != null ? sha : null }; }
        return img;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopHubAsync().ConfigureAwait(false);
        lock (_gate) { _api.Dispose(); }
    }
}
