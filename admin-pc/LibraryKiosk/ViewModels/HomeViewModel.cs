using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryKiosk.Models;
using LibraryKiosk.Services;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// Temporary home screen for Phase 2: surfaces the live connection state to the
/// server (loading / connected / unlicensed / unreachable) and the book count +
/// last-updated time from /api/meta. The real home UI replaces this from Phase 4.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private ApiClient _api;

    [ObservableProperty]
    private ConnectionState _state = ConnectionState.Loading;

    [ObservableProperty]
    private string _statusHeader = "กำลังเชื่อมต่อ…";

    [ObservableProperty]
    private string _statusDetail = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _bookCount;

    [ObservableProperty]
    private string _lastUpdated = "-";

    [ObservableProperty]
    private string _baseUrl = "";

    public HomeViewModel(SettingsService settings)
    {
        _settings = settings;
        var cfg = _settings.Load();
        BaseUrl = cfg.BaseUrl;
        _api = new ApiClient(cfg.BaseUrl);
    }

    private bool CanRefresh() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        IsConnected = false;
        State = ConnectionState.Loading;
        StatusHeader = "กำลังเชื่อมต่อ…";
        StatusDetail = BaseUrl;

        var result = await _api.GetMetaAsync();

        State = result.State;
        switch (result.State)
        {
            case ConnectionState.Connected:
                IsConnected = true;
                BookCount = result.Meta!.BookCount;
                LastUpdated = result.Meta.LastUpdated;
                StatusHeader = "เชื่อมต่อสำเร็จ";
                StatusDetail = BaseUrl;
                break;

            case ConnectionState.Unlicensed:
                StatusHeader = "เชื่อมต่อได้ แต่ Server ยังไม่ activate license";
                StatusDetail = result.Message ?? "";
                break;

            default: // Unreachable
                StatusHeader = "เชื่อมต่อ Server ไม่ได้";
                StatusDetail = result.Message ?? BaseUrl;
                break;
        }

        IsLoading = false;
    }
}
