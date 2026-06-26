using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryKiosk.Services;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// Phase 4 kiosk shell VM: drives the live <see cref="SyncService"/> connection,
/// holds the loaded catalogue, does client-side search/category filtering (no API
/// call per keystroke), exposes the selected book for the detail modal, lazily
/// loads covers, and surfaces loading/empty/error/unlicensed states.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private const string AllCategory = "ทั้งหมด";

    private readonly SettingsService _settings;
    private readonly SyncService _sync;
    private readonly PinService _pin;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private IReadOnlyList<BookCardViewModel> _allCards = new List<BookCardViewModel>();
    private int _coverGeneration;

    // Raw branding straight from the server; the displayed Logo/Background images are
    // derived from these via the local hide flags (settings can hide them on this kiosk).
    private BitmapSource? _rawLogo;
    private BitmapSource? _rawBackground;
    private bool _hideLogo;
    private bool _hideBackground;

    // System name (K2 item 5): the displayed header name is the local override when
    // set, otherwise the server's displayName. We keep both raw values so toggling the
    // local override re-resolves without a re-sync.
    private string? _serverDisplayName;
    private string? _localSystemName;

    // ---- connection / meta ----
    [ObservableProperty] private ConnectionState _state = ConnectionState.Loading;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _displayName = "Library Kiosk";
    [ObservableProperty] private int _bookCount;
    [ObservableProperty] private string _lastUpdated = "-";
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _statusMessage = "";

    // ---- branding ----
    [ObservableProperty] private BitmapSource? _logoImage;
    [ObservableProperty] private BitmapSource? _backgroundImage;
    [ObservableProperty] private bool _hasBackground;
    [ObservableProperty] private bool _hasLogo;

    // ---- visible-screen flags ----
    [ObservableProperty] private bool _isBrowseVisible;
    [ObservableProperty] private bool _isInitialLoading = true;
    [ObservableProperty] private bool _isErrorVisible;
    [ObservableProperty] private bool _isUnlicensedVisible;
    [ObservableProperty] private bool _isEmpty;

    // ---- search / filter ----
    [ObservableProperty] private string _query = "";
    private const int GridColumns = 3;
    private string _selectedCategory = AllCategory;
    public ObservableCollection<CategoryChipViewModel> Categories { get; } = new();
    public ObservableCollection<BookCardViewModel> FilteredBooks { get; } = new();
    /// <summary>Filtered books chunked into rows of 3 for the virtualizing grid.</summary>
    public ObservableCollection<BookRowViewModel> BookRows { get; } = new();
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private bool _hasQuery;

    // ---- detail modal ----
    [ObservableProperty] private BookCardViewModel? _selectedBook;
    [ObservableProperty] private bool _isDetailOpen;

    // ---- display (Phase 5) ----
    /// <summary>Extra zoom applied outside the fit Viewbox (bound to a ScaleTransform).</summary>
    [ObservableProperty] private double _uiScale = 1.0;

    // ---- design-canvas resolution (K2 item 4) ----
    /// <summary>Design-canvas width the Uniform Viewbox fits to the screen.</summary>
    [ObservableProperty] private double _canvasWidth = 1080;
    /// <summary>Design-canvas height the Uniform Viewbox fits to the screen.</summary>
    [ObservableProperty] private double _canvasHeight = 1920;

    /// <summary>Last-applied display mode ("fullscreen"/"windowed"); the window reads this on load.</summary>
    public string DisplayMode { get; private set; } = "fullscreen";

    /// <summary>Fullscreen = locked-down kiosk (block exit + idle reset); windowed = dev.</summary>
    public bool IsLockdown => string.Equals(DisplayMode, "fullscreen", StringComparison.OrdinalIgnoreCase);

    /// <summary>Idle seconds before resetting browse for the next user (0 disables).</summary>
    public int IdleResetSeconds { get; }

    /// <summary>Raised when the admin changes the display mode; the window applies it.</summary>
    public event Action<string>? DisplayModeChangeRequested;

    /// <summary>Raised when the admin chooses to exit the kiosk (after the PIN gate).</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised on idle reset so the window can scroll the grid back to the top.</summary>
    public event EventHandler? ScrollResetRequested;

    // ---- admin settings overlay (Phase 5) ----
    public SettingsViewModel Settings { get; }

    public HomeViewModel(SettingsService settings)
    {
        _settings = settings;
        _dispatcher = Application.Current.Dispatcher;
        _pin = new PinService(settings);

        var cfg = _settings.Load();
        BaseUrl = cfg.BaseUrl;
        _uiScale = Math.Clamp(cfg.UiScale, 0.8, 1.2);
        DisplayMode = string.Equals(cfg.DisplayMode, "windowed", StringComparison.OrdinalIgnoreCase)
            ? "windowed" : "fullscreen";
        _hideLogo = cfg.HideLogo;
        _hideBackground = cfg.HideBackground;
        IdleResetSeconds = cfg.IdleResetSeconds;

        _localSystemName = string.IsNullOrWhiteSpace(cfg.SystemName) ? null : cfg.SystemName!.Trim();
        _canvasWidth = cfg.CanvasWidth > 0 ? cfg.CanvasWidth : 1080;
        _canvasHeight = cfg.CanvasHeight > 0 ? cfg.CanvasHeight : 1920;
        ApplyDisplayName();

        _sync = new SyncService(cfg.BaseUrl, cfg.KioskId);
        _sync.SyncTriggered += OnSyncTriggered;
        _sync.HubConnectionChanged += OnHubConnectionChanged;

        Settings = new SettingsViewModel(
            settings, _sync, _pin,
            reloadAsync: RefreshAsync,
            getStatus: () => (State, LastUpdated),
            applyUiScale: v => UiScale = v,
            applyDisplayMode: ApplyDisplayMode,
            applyBranding: SetBrandingHidden,
            getDisplayName: () => DisplayName,
            getBrandingAvailable: () => (_rawLogo != null, _rawBackground != null),
            requestExit: () => ExitRequested?.Invoke(this, EventArgs.Empty),
            applySystemName: SetLocalSystemName,
            applyResolution: SetCanvasSize);
    }

    /// <summary>Resolve the header name: local override wins, else the server name.</summary>
    private void ApplyDisplayName()
    {
        DisplayName = !string.IsNullOrWhiteSpace(_localSystemName)
            ? _localSystemName!
            : (!string.IsNullOrWhiteSpace(_serverDisplayName) ? _serverDisplayName! : "Library Kiosk");
    }

    /// <summary>Apply a new local system-name override (K2 item 5); empty = use server name.</summary>
    public void SetLocalSystemName(string? name)
    {
        _localSystemName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        ApplyDisplayName();
    }

    /// <summary>Apply a new design-canvas resolution (K2 item 4); the Viewbox refits.</summary>
    public void SetCanvasSize(double width, double height)
    {
        CanvasWidth = width;
        CanvasHeight = height;
    }

    private void OnHubConnectionChanged(object? sender, bool connected)
    {
        // Realtime "disconnected" feedback for the settings panel; a (re)connect is
        // followed by a SyncTriggered -> RefreshAsync -> Apply, which flips it back.
        if (!connected) _ = OnUi(() => Settings.MarkDisconnected());
    }

    /// <summary>Reset the browse view for the next user (idle timeout, fullscreen only).</summary>
    public void ResetForIdle()
    {
        if (Settings.IsOpen) Settings.CloseCommand.Execute(null);
        IsDetailOpen = false;
        SelectedBook = null;
        Query = "";
        SelectCategory(AllCategory);
        ScrollResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDisplayMode(string mode)
    {
        DisplayMode = mode;
        OnPropertyChanged(nameof(IsLockdown));
        DisplayModeChangeRequested?.Invoke(mode);
    }

    /// <summary>Apply the local branding hide flags (called from the settings overlay).</summary>
    public void SetBrandingHidden(bool hideLogo, bool hideBackground)
    {
        _hideLogo = hideLogo;
        _hideBackground = hideBackground;
        ApplyBrandingVisibility();
    }

    private void ApplyBrandingVisibility()
    {
        LogoImage = _hideLogo ? null : _rawLogo;
        HasLogo = LogoImage != null;
        BackgroundImage = _hideBackground ? null : _rawBackground;
        HasBackground = BackgroundImage != null;
    }

    public async Task StartAsync()
    {
        await _sync.StartAsync();
        await RefreshAsync();
    }

    private void OnSyncTriggered(object? sender, EventArgs e) => _ = RefreshAsync();

    // ---------------- sync ----------------

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _syncGate.WaitAsync();
        try
        {
            await OnUi(() =>
            {
                IsLoading = true;
                if (_allCards.Count == 0) IsInitialLoading = true;
                IsErrorVisible = false;
                IsUnlicensedVisible = false;
                State = ConnectionState.Loading;
            });

            var snap = await _sync.SyncNowAsync();
            await OnUi(() => Apply(snap));
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void Apply(SyncSnapshot snap)
    {
        State = snap.State;
        IsLoading = false;

        switch (snap.State)
        {
            case ConnectionState.Connected:
                BookCount = snap.Meta!.BookCount;
                LastUpdated = snap.Meta.LastUpdated;
                if (!string.IsNullOrWhiteSpace(snap.DisplayName)) _serverDisplayName = snap.DisplayName;
                ApplyDisplayName();

                _rawLogo = snap.Logo;
                _rawBackground = snap.Background;
                ApplyBrandingVisibility();

                _allCards = snap.Books.Select(b => new BookCardViewModel(b)).ToList();
                RebuildCategories(snap.Categories);
                ApplyFilter();

                IsInitialLoading = false;
                IsBrowseVisible = true;
                IsErrorVisible = false;
                IsUnlicensedVisible = false;

                // Re-verify covers only when the server changed something since the last
                // scan (cover upload/edit/delete bumps LastUpdated). Idle/reconnect syncs
                // keep the same stamp -> covers reapply from cache with no network.
                var stamp = snap.Meta!.LastUpdated ?? "";
                StartCoverLoading(_allCards, _sync.CoversNeedVerify(stamp), stamp);
                break;

            case ConnectionState.Unlicensed:
                IsInitialLoading = false;
                IsBrowseVisible = false;
                IsErrorVisible = false;
                IsUnlicensedVisible = true;
                StatusMessage = snap.Message ?? "Server ยังไม่ได้ activate license";
                break;

            default: // Unreachable
                IsInitialLoading = false;
                // Keep showing cached browse if we already have data; otherwise error.
                if (_allCards.Count == 0)
                {
                    IsBrowseVisible = false;
                    IsErrorVisible = true;
                }
                IsUnlicensedVisible = false;
                StatusMessage = snap.Message ?? "เชื่อมต่อ Server ไม่ได้";
                break;
        }

        // Keep the settings panel's connection indicator live after every sync.
        Settings.RefreshConnectionIndicator();
    }

    private void RebuildCategories(IReadOnlyList<string> categories)
    {
        var names = categories.Count > 0 ? categories : new List<string> { AllCategory };
        // Keep the active category only if it still exists; otherwise reset to "ทั้งหมด".
        if (!names.Contains(_selectedCategory)) _selectedCategory = AllCategory;

        Categories.Clear();
        foreach (var c in names)
            Categories.Add(new CategoryChipViewModel(c, c == _selectedCategory));
    }

    // ---------------- filter (client-side) ----------------

    partial void OnQueryChanged(string value)
    {
        HasQuery = !string.IsNullOrEmpty(value);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var cat = (_selectedCategory ?? AllCategory).Trim();
        var q = (Query ?? "").Trim();
        var hasQ = q.Length > 0;
        var qLower = q.ToLowerInvariant();

        FilteredBooks.Clear();
        foreach (var card in _allCards)
        {
            var okCat = cat == AllCategory || string.Equals((card.Book.Category ?? "").Trim(), cat, StringComparison.Ordinal);
            if (!okCat) continue;

            if (hasQ)
            {
                var hay = string.Join(" ",
                    card.RegNo, card.Book.Title, card.Book.Category, card.Book.Publisher, card.Book.Shelf)
                    .ToLowerInvariant();
                if (!hay.Contains(qLower)) continue;
            }
            FilteredBooks.Add(card);
        }

        FilteredCount = FilteredBooks.Count;
        IsEmpty = State == ConnectionState.Connected && FilteredBooks.Count == 0;

        RebuildRows();
    }

    /// <summary>Chunk the filtered cards into rows of <see cref="GridColumns"/> for virtualization.</summary>
    private void RebuildRows()
    {
        BookRows.Clear();
        for (var i = 0; i < FilteredBooks.Count; i += GridColumns)
        {
            var count = Math.Min(GridColumns, FilteredBooks.Count - i);
            var cards = new List<BookCardViewModel>(count);
            for (var j = 0; j < count; j++) cards.Add(FilteredBooks[i + j]);
            BookRows.Add(new BookRowViewModel(cards));
        }
    }

    [RelayCommand]
    private void ClearQuery() => Query = "";

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        var cat = string.IsNullOrEmpty(category) ? AllCategory : category;
        _selectedCategory = cat;
        foreach (var chip in Categories) chip.IsSelected = chip.Name == cat;
        ApplyFilter();
    }

    [RelayCommand]
    private void ResetBrowse()
    {
        Query = "";
        SelectCategory(AllCategory);
    }

    // ---------------- detail modal ----------------

    [RelayCommand]
    private void SelectBook(BookCardViewModel? card)
    {
        if (card == null) return;
        SelectedBook = card;
        IsDetailOpen = true;
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailOpen = false;

    [RelayCommand]
    private void OpenSettings() => Settings.Open();

    // ---------------- covers ----------------

    private void StartCoverLoading(IReadOnlyList<BookCardViewModel> cards, bool verify, string stamp)
    {
        var generation = ++_coverGeneration;
        var snapshot = cards.ToList();

        _ = Task.Run(async () =>
        {
            // Throttled (6 concurrent) background load so a 500-book catalogue never
            // blocks the UI. When verify is false this is all cache hits (no network);
            // when true each book does one light cover/meta check and only changed
            // covers are re-downloaded.
            using var sem = new SemaphoreSlim(6);
            var tasks = snapshot.Select(async card =>
            {
                await sem.WaitAsync();
                try
                {
                    if (generation != _coverGeneration) return;
                    var img = await _sync.ResolveCoverAsync(card.RegNo, verify);
                    if (img != null && generation == _coverGeneration)
                        await OnUi(() => card.SetCover(img));
                }
                catch { /* a single cover never breaks the grid */ }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);

            // Mark this server stamp as scanned only after a full, current-generation
            // pass, so an interrupted scan re-verifies next time.
            if (verify && generation == _coverGeneration)
                _sync.MarkCoversScanned(stamp);
        });
    }

    // ---------------- helpers ----------------

    private Task OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return _dispatcher.InvokeAsync(action).Task;
    }

    public async Task ShutdownAsync()
    {
        _sync.SyncTriggered -= OnSyncTriggered;
        _sync.HubConnectionChanged -= OnHubConnectionChanged;
        await _sync.DisposeAsync();
    }
}
