using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibraryAdminPC.Models;
using LibraryAdminPC.Services;
using LibraryAdminPC.Utils;
using LibraryAdminPC.Views;

namespace LibraryAdminPC;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ConfigService _configService = new();
    private readonly LicenseService _licenseService = new();

    private AppConfig _config = new();
    private ApiClient? _api;

    // -------------------- Bindings (Sidebar / Topbar) --------------------

    private string _statusText = "ออฟไลน์";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _apiBaseUrlText = "API: -";
    public string ApiBaseUrlText
    {
        get => _apiBaseUrlText;
        set { _apiBaseUrlText = value; OnPropertyChanged(); }
    }

    private string _pageTitle = "แดชบอร์ด";
    public string PageTitle
    {
        get => _pageTitle;
        set { _pageTitle = value; OnPropertyChanged(); }
    }

    private string _pageSubtitle = "ภาพรวมระบบ";
    public string PageSubtitle
    {
        get => _pageSubtitle;
        set { _pageSubtitle = value; OnPropertyChanged(); }
    }

    private bool _isDashboardSelected = true;
    public bool IsDashboardSelected
    {
        get => _isDashboardSelected;
        set { _isDashboardSelected = value; OnPropertyChanged(); }
    }

    private bool _isBooksSelected;
    public bool IsBooksSelected
    {
        get => _isBooksSelected;
        set { _isBooksSelected = value; OnPropertyChanged(); }
    }

    private bool _isImportSelected;
    public bool IsImportSelected
    {
        get => _isImportSelected;
        set { _isImportSelected = value; OnPropertyChanged(); }
    }

    private bool _isSettingsSelected;
    public bool IsSettingsSelected
    {
        get => _isSettingsSelected;
        set { _isSettingsSelected = value; OnPropertyChanged(); }
    }

    // Additive (Task B / B1): sidebar "โลโก้ & พื้นหลัง" selected-state.
    // Real BrandingView is wired in B5; for now navigation shows a placeholder.
    private bool _isBrandingSelected;
    public bool IsBrandingSelected
    {
        get => _isBrandingSelected;
        set { _isBrandingSelected = value; OnPropertyChanged(); }
    }

    // -------------------- License badge bindings --------------------

    private bool _isLicensed;
    public bool IsLicensed
    {
        get => _isLicensed;
        set { _isLicensed = value; OnPropertyChanged(); }
    }

    private string _licenseBadgeText = "⚠ ยังไม่เปิดใช้งาน";
    public string LicenseBadgeText
    {
        get => _licenseBadgeText;
        set { _licenseBadgeText = value; OnPropertyChanged(); }
    }

    private string _licenseBadgeToolTip = "Click to enter Product Key";
    public string LicenseBadgeToolTip
    {
        get => _licenseBadgeToolTip;
        set { _licenseBadgeToolTip = value; OnPropertyChanged(); }
    }

    private Brush _licenseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
    public Brush LicenseBadgeBackground
    {
        get => _licenseBadgeBackground;
        set { _licenseBadgeBackground = value; OnPropertyChanged(); }
    }

    private Brush _licenseBadgeForeground = Brushes.White;
    public Brush LicenseBadgeForeground
    {
        get => _licenseBadgeForeground;
        set { _licenseBadgeForeground = value; OnPropertyChanged(); }
    }

    private Brush _licenseBadgeBorderBrush = (Brush)new BrushConverter().ConvertFromString("#DC2626")!;
    public Brush LicenseBadgeBorderBrush
    {
        get => _licenseBadgeBorderBrush;
        set { _licenseBadgeBorderBrush = value; OnPropertyChanged(); }
    }

    // -------------------- Lifecycle --------------------

    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("MainWindow.InitializeComponent failed", ex);
            MessageBox.Show(
                "InitializeComponent crashed.\n\nLog: " + CrashLogger.GetLogPath() + "\n\n" + ex.Message,
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            throw;
        }

        DataContext = this;

        // IMPORTANT: อย่า Navigate ใน constructor (กัน crash / dialog ก่อน window พร้อม)
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        try
        {
            // 1. โหลด Config และสถานะเซิร์ฟเวอร์
            await InitializeAsync(); 

            // 2. ตรวจสอบ License และแสดงบน Badge ทันที
            RefreshLicenseBadge();   

            // 3. 🚨 บังคับเปิดใช้งาน: ถ้ายังไม่มี License ให้เด้งหน้าต่างกรอก Key ทันที
            if (!IsLicensed)
            {
                // วนลูปถามจนกว่าจะกรอกถูกต้อง หรือผู้ใช้กดยกเลิก (ปิดหน้าต่าง)
                bool isActivated = ShowLicenseDialogAndActivate();
                
                // ถ้าผู้ใช้กดยกเลิกการกรอก Key ให้ปิดโปรแกรมทิ้งไปเลย (บังคับว่าต้องมี Key ถึงจะใช้ได้)
                if (!isActivated)
                {
                    MessageBox.Show("ต้องเปิดใช้งานโปรแกรมก่อนเข้าสู่ระบบ", "System Exit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                    return; // หยุดการทำงานต่อ
                }
            }

            // 4. ถ้ามี License แล้ว (หรือเพิ่งกรอกสำเร็จเมื่อกี้) ให้เข้าหน้า Dashboard ตามปกติ
            NavigateToDashboard(); 
        }
        catch (Exception ex)
        {
            CrashLogger.Log("MainWindow_Loaded crashed", ex);
            MessageBox.Show(
                "App crashed during startup.\n\nLog: " + CrashLogger.GetLogPath() + "\n\n" + ex.Message,
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async Task InitializeAsync()
    {
        LoadConfig();
        await RefreshStatusAsync();
    }

    // -------------------- Config / API --------------------

    private void LoadConfig()
    {
        try
        {
            _config = _configService.Load();
            ApiBaseUrlText = $"API: {(_config.ApiBaseUrl ?? "-")}";
            TryBuildApiClient();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("LoadConfig failed", ex);
            _api = null;
            ApiBaseUrlText = "API: -";
            StatusText = "ออฟไลน์";
        }
    }

    private void TryBuildApiClient()
    {
        try
        {
            _api = new ApiClient(_config);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TryBuildApiClient failed", ex);
            _api = null;
            StatusText = $"ตั้งค่าไม่ถูกต้อง: {ex.Message}";
        }
    }

    private async Task RefreshStatusAsync()
    {
        TryBuildApiClient();

        if (_api == null)
        {
            StatusText = "ออฟไลน์";
            return;
        }

        try
        {
            var ok = await _api.TestConnectionAsync();
            StatusText = ok ? "ออนไลน์" : "ออฟไลน์";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("RefreshStatusAsync failed", ex);
            StatusText = $"ออฟไลน์ ({ex.Message})";
        }
    }

    // -------------------- License helpers --------------------

    private void RefreshLicenseBadge()
    {
        try
        {
            var s = _licenseService.GetStatus();
            IsLicensed = s.IsLicensed;

            if (s.IsLicensed)
            {
                LicenseBadgeText = "✓ ลิขสิทธิ์ถูกต้อง";
                LicenseBadgeToolTip = s.Message;

                LicenseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#10B981")!;
                LicenseBadgeBorderBrush = (Brush)new BrushConverter().ConvertFromString("#059669")!;
                LicenseBadgeForeground = Brushes.White;
            }
            else
            {
                LicenseBadgeText = "⚠ ยังไม่เปิดใช้งาน";
                LicenseBadgeToolTip = s.Message + "\nClick to enter Product Key";

                LicenseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
                LicenseBadgeBorderBrush = (Brush)new BrushConverter().ConvertFromString("#DC2626")!;
                LicenseBadgeForeground = Brushes.White;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("RefreshLicenseBadge failed", ex);

            IsLicensed = false;
            LicenseBadgeText = "⚠ ตรวจสอบไม่ได้";
            LicenseBadgeToolTip = "License check error: " + ex.Message;

            LicenseBadgeBackground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
            LicenseBadgeBorderBrush = (Brush)new BrushConverter().ConvertFromString("#DC2626")!;
            LicenseBadgeForeground = Brushes.White;
        }
    }

    private bool EnsureLicensedOrPrompt()
    {
        // B6 perf fix: navigation must be instant (<100ms). The license state is
        // already validated at startup (MainWindow_Loaded forces activation or shuts
        // the app down) and is refreshed by the "รีเฟรชสถานะ" button, so trust the
        // cached IsLicensed flag here instead of doing a BLOCKING synchronous
        // GetStatus() HTTP round-trip on every page switch — that round-trip (5s
        // timeout, on the UI thread) was the multi-second navigation delay.
        if (IsLicensed) return true;

        // Cached state says not-licensed (rare mid-session): re-verify against the
        // server before prompting, exactly as before.
        RefreshLicenseBadge();
        if (IsLicensed) return true;

        return ShowLicenseDialogAndActivate();
    }

    private bool ShowLicenseDialogAndActivate()
    {
        try
        {
            var dlg = new LicenseKeyDialog { Owner = this };

            var ok = dlg.ShowDialog() == true;
            if (!ok) return false;

            var key = (dlg.ProductKey ?? "").Trim();
            var result = _licenseService.TryActivate(key);

            MessageBox.Show(
                result.Message,
                result.Success ? "Activated" : "Invalid Key",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning
            );

            RefreshLicenseBadge();
            return result.Success;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ShowLicenseDialogAndActivate failed", ex);
            MessageBox.Show(ex.Message, "License Error", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshLicenseBadge();
            return false;
        }
    }

    // -------------------- Navigation --------------------

    private void SetSelected(string page)
    {
        IsDashboardSelected = page == "dashboard";
        IsBooksSelected = page == "books";
        IsImportSelected = page == "import";
        IsSettingsSelected = page == "settings";
        IsBrandingSelected = page == "branding";   // additive (B1)
    }

    private void NavigateToDashboard()
    {
        if (!EnsureLicensedOrPrompt())
        {
            NavigateToSettings();
            return;
        }

        SetSelected("dashboard");
        PageTitle = "แดชบอร์ด";
        PageSubtitle = "ภาพรวมระบบ";

        if (_api == null)
        {
            MainContent.Content = BuildConfigHint();
            return;
        }

        var dash = new DashboardView(_api);
        // B7 additive: the dashboard's "ดูทั้งหมด" link navigates to the Books page
        // through the existing navigation method.
        dash.ViewAllBooksRequested += (_, __) => NavigateToBooks();
        MainContent.Content = dash;
    }

    private void NavigateToBooks()
    {
        if (!EnsureLicensedOrPrompt())
        {
            NavigateToSettings();
            return;
        }

        SetSelected("books");
        PageTitle = "หนังสือ";
        PageSubtitle = "ค้นหาและดูรายการหนังสือ";

        if (_api == null)
        {
            MainContent.Content = BuildConfigHint();
            return;
        }

        MainContent.Content = new BooksView(_api);
    }

    private void NavigateToImport()
    {
        if (!EnsureLicensedOrPrompt())
        {
            NavigateToSettings();
            return;
        }

        SetSelected("import");
        PageTitle = "นำเข้า";
        PageSubtitle = "นำเข้ารายการหนังสือจากไฟล์";

        if (_api == null)
        {
            MainContent.Content = BuildConfigHint();
            return;
        }

        var view = new ImportView(_api);

        // ✅ ไม่ใช้ view.ImportCompleted (เพราะ ImportView ไม่มี event นี้)
        // ✅ Hook แบบปลอดภัย: ถ้า DataContext มี property IsBusy และเปลี่ยนเป็น false => refresh status
        HookImportAutoRefresh(view);

        MainContent.Content = view;
    }

    private void HookImportAutoRefresh(UserControl view)
    {
        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, __) =>
        {
            view.Loaded -= loadedHandler;

            try
            {
                var dc = view.DataContext;
                if (dc is not INotifyPropertyChanged npc) return;

                PropertyChangedEventHandler? h = null;
                h = async (s, e) =>
                {
                    try
                    {
                        // ถ้า IsBusy เปลี่ยน หรือบางทีส่งเป็น null/empty
                        if (!string.IsNullOrWhiteSpace(e.PropertyName) &&
                            !string.Equals(e.PropertyName, "IsBusy", StringComparison.OrdinalIgnoreCase))
                            return;

                        if (s == null) return;

                        if (TryGetBoolProperty(s, "IsBusy", out var busy) && busy == false)
                        {
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                await RefreshStatusAsync();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.Log("HookImportAutoRefresh handler failed", ex);
                    }
                };

                npc.PropertyChanged += h;

                // unhook ตอนออกจากหน้า
                RoutedEventHandler? unloadedHandler = null;
                unloadedHandler = (_, __) =>
                {
                    view.Unloaded -= unloadedHandler;
                    try { npc.PropertyChanged -= h; } catch { }
                };
                view.Unloaded += unloadedHandler;
            }
            catch (Exception ex)
            {
                CrashLogger.Log("HookImportAutoRefresh setup failed", ex);
            }
        };

        view.Loaded += loadedHandler;
    }

    private static bool TryGetBoolProperty(object obj, string propName, out bool value)
    {
        value = false;

        try
        {
            var p = obj.GetType().GetProperty(propName);
            if (p == null) return false;

            var v = p.GetValue(obj);
            if (v is bool b)
            {
                value = b;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void NavigateToSettings()
    {
        SetSelected("settings");
        PageTitle = "ตั้งค่า";
        PageSubtitle = "ตั้งค่า API BaseUrl / AdminKey / Product Key";

        var view = new SettingsView(_configService);
        view.Saved += async (_, __) =>
        {
            try
            {
                LoadConfig();
                await RefreshStatusAsync();
                RefreshLicenseBadge();
            }
            catch (Exception ex)
            {
                CrashLogger.Log("SettingsView Saved handler failed", ex);
            }
        };

        MainContent.Content = view;
    }

    private UIElement BuildConfigHint()
    {
        var msg =
            "ยังเชื่อมต่อ API ไม่ได้ (ApiBaseUrl ว่าง/ผิดรูปแบบ หรือ Server ยังไม่รัน)\n\n" +
            $"แก้ไฟล์ตั้งค่าที่: {_configService.ConfigPath}\n" +
            "ตัวอย่าง:\n{\n  \"ApiBaseUrl\": \"http://192.168.1.105:45269\",\n  \"AdminKey\": \"...\"\n}";

        return new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "ตั้งค่า", FontSize = 20, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = msg, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            }
        };
    }

    // Additive (Task B / B1): branding page. Real BrandingView arrives in B5;
    // this navigates to a lightweight placeholder so the shell + selected-state
    // are fully testable now without touching the existing navigation methods.
    private void NavigateToBranding()
    {
        if (!EnsureLicensedOrPrompt())
        {
            NavigateToSettings();
            return;
        }

        SetSelected("branding");
        PageTitle = "โลโก้ & พื้นหลัง";
        PageSubtitle = "จัดการโลโก้และภาพพื้นหลังของจอแสดงผล";

        var view = new BrandingView(_configService);
        view.Saved += async (_, __) =>
        {
            try
            {
                LoadConfig();
                await RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.Log("BrandingView Saved handler failed", ex);
            }
        };

        MainContent.Content = view;
    }

    // -------------------- Events (from MainWindow.xaml) --------------------

    private void BtnDashboard_Click(object sender, RoutedEventArgs e) => NavigateToDashboard();
    private void BtnBooks_Click(object sender, RoutedEventArgs e) => NavigateToBooks();
    private void BtnImport_Click(object sender, RoutedEventArgs e) => NavigateToImport();
    private void BtnSettings_Click(object sender, RoutedEventArgs e) => NavigateToSettings();
    private void BtnBranding_Click(object sender, RoutedEventArgs e) => NavigateToBranding();

    private void BtnLicenseBadge_Click(object sender, RoutedEventArgs e)
    {
        RefreshLicenseBadge();

        if (IsLicensed)
        {
            MessageBox.Show(LicenseBadgeToolTip, "License", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowLicenseDialogAndActivate();
    }

    private async void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        RefreshLicenseBadge();
        await RefreshStatusAsync();

        if (IsDashboardSelected) NavigateToDashboard();
        else if (IsBooksSelected) NavigateToBooks();
        else if (IsImportSelected) NavigateToImport();
        else if (IsBrandingSelected) NavigateToBranding();   // additive (B1)
        else NavigateToSettings();
    }

    // -------------------- INotifyPropertyChanged --------------------

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}