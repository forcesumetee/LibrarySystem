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
    private string _selectedCategory = AllCategory;
    public ObservableCollection<CategoryChipViewModel> Categories { get; } = new();
    public ObservableCollection<BookCardViewModel> FilteredBooks { get; } = new();
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private bool _hasQuery;

    // ---- detail modal ----
    [ObservableProperty] private BookCardViewModel? _selectedBook;
    [ObservableProperty] private bool _isDetailOpen;

    // ---- display (Phase 5) ----
    /// <summary>Extra zoom applied outside the fit Viewbox (bound to a ScaleTransform).</summary>
    [ObservableProperty] private double _uiScale = 1.0;

    /// <summary>Last-applied display mode ("fullscreen"/"windowed"); the window reads this on load.</summary>
    public string DisplayMode { get; private set; } = "fullscreen";

    /// <summary>Raised when the admin changes the display mode; the window applies it.</summary>
    public event Action<string>? DisplayModeChangeRequested;

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

        _sync = new SyncService(cfg.BaseUrl);
        _sync.SyncTriggered += OnSyncTriggered;

        Settings = new SettingsViewModel(
            settings, _sync, _pin,
            reloadAsync: RefreshAsync,
            getStatus: () => (State, LastUpdated),
            applyUiScale: v => UiScale = v,
            applyDisplayMode: ApplyDisplayMode,
            applyBranding: SetBrandingHidden,
            getDisplayName: () => DisplayName,
            getBrandingAvailable: () => (_rawLogo != null, _rawBackground != null));
    }

    private void ApplyDisplayMode(string mode)
    {
        DisplayMode = mode;
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
                if (!string.IsNullOrWhiteSpace(snap.DisplayName)) DisplayName = snap.DisplayName!;

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

                StartCoverLoading(_allCards);
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

    private void StartCoverLoading(IReadOnlyList<BookCardViewModel> cards)
    {
        var generation = ++_coverGeneration;
        var snapshot = cards.ToList();

        _ = Task.Run(async () =>
        {
            using var sem = new SemaphoreSlim(6);
            var tasks = snapshot.Select(async card =>
            {
                await sem.WaitAsync();
                try
                {
                    if (generation != _coverGeneration) return;
                    var img = await _sync.LoadCoverAsync(card.RegNo);
                    if (img != null && generation == _coverGeneration)
                        await OnUi(() => card.SetCover(img));
                }
                catch { /* a single cover never breaks the grid */ }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
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
        await _sync.DisposeAsync();
    }
}
