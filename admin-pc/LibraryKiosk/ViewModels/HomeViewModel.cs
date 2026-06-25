using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryKiosk.Services;
using LibraryShared;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// Phase 3 home VM: drives the <see cref="SyncService"/> (SignalR + full fetch),
/// reflects connection state, exposes book/category counts and branding images.
/// Server pushes (or reconnects) arrive on background threads, so every property
/// mutation is marshalled onto the UI dispatcher.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly SyncService _sync;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    [ObservableProperty] private ConnectionState _state = ConnectionState.Loading;
    [ObservableProperty] private string _statusHeader = "กำลังเชื่อมต่อ…";
    [ObservableProperty] private string _statusDetail = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoading;

    [ObservableProperty] private bool _isConnected;

    // From /api/meta
    [ObservableProperty] private int _bookCount;
    [ObservableProperty] private string _lastUpdated = "-";

    // From /api/books (actually loaded into memory) + categories
    [ObservableProperty] private int _loadedBookCount;
    [ObservableProperty] private int _categoryCount;

    // Branding
    [ObservableProperty] private BitmapSource? _logoImage;
    [ObservableProperty] private BitmapSource? _backgroundImage;
    [ObservableProperty] private bool _hasBackground;

    [ObservableProperty] private string _baseUrl = "";

    // Held for Phase 4 (search/browse UI); not bound yet.
    public IReadOnlyList<BookDto> Books { get; private set; } = new List<BookDto>();
    public IReadOnlyList<string> Categories { get; private set; } = new List<string>();

    public HomeViewModel(SettingsService settings)
    {
        _settings = settings;
        _dispatcher = Application.Current.Dispatcher;

        var cfg = _settings.Load();
        BaseUrl = cfg.BaseUrl;

        _sync = new SyncService(cfg.BaseUrl);
        _sync.SyncTriggered += OnSyncTriggered;
    }

    /// <summary>Start the live connection and do the first load. Call once at startup.</summary>
    public async Task StartAsync()
    {
        await _sync.StartAsync();   // raises SyncTriggered on connect
        await RefreshAsync();       // immediate load regardless of hub timing
    }

    // Server push / reconnect (background thread). Marshalling happens in RefreshAsync.
    private void OnSyncTriggered(object? sender, EventArgs e) => _ = RefreshAsync();

    private bool CanRefresh() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        // Serialize overlapping syncs (manual retry + hub push + Retry button).
        await _syncGate.WaitAsync();
        try
        {
            await OnUi(() =>
            {
                IsLoading = true;
                IsConnected = false;
                State = ConnectionState.Loading;
                StatusHeader = "กำลังเชื่อมต่อ…";
                StatusDetail = BaseUrl;
            });

            var snap = await _sync.SyncNowAsync();   // network + image decode off UI

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
                IsConnected = true;
                BookCount = snap.Meta!.BookCount;
                LastUpdated = snap.Meta.LastUpdated;

                Books = snap.Books;
                Categories = snap.Categories;
                LoadedBookCount = snap.Books.Count;
                CategoryCount = Math.Max(0, snap.Categories.Count - 1); // exclude "ทั้งหมด"

                LogoImage = snap.Logo;
                BackgroundImage = snap.Background;
                HasBackground = snap.Background != null;

                StatusHeader = "เชื่อมต่อสำเร็จ";
                StatusDetail = BaseUrl;
                break;

            case ConnectionState.Unlicensed:
                StatusHeader = "เชื่อมต่อได้ แต่ Server ยังไม่ activate license";
                StatusDetail = snap.Message ?? "";
                break;

            default: // Unreachable
                StatusHeader = "เชื่อมต่อ Server ไม่ได้";
                StatusDetail = snap.Message ?? BaseUrl;
                break;
        }
    }

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
