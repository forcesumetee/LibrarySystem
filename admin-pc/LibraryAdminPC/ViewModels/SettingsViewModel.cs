using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LibraryAdminPC.Models;
using LibraryAdminPC.Services;
using LibraryAdminPC.Utils;

namespace LibraryAdminPC.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _configService;

    public string ConfigPath => _configService.ConfigPath;

    private string _apiBaseUrl = "http://localhost:5269";
    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set
        {
            _apiBaseUrl = value;
            OnPropertyChanged();
            RefreshKioskUrls(); // ✅ อัปเดตรายการ IP/URL ทันทีเมื่อ BaseUrl เปลี่ยน
        }
    }

    private string _adminKey = "";
    public string AdminKey { get => _adminKey; set { _adminKey = value; OnPropertyChanged(); } }

    // -------- Branding ----------
    private string _logoPreviewPath = "";
    public string LogoPreviewPath { get => _logoPreviewPath; set { _logoPreviewPath = value; OnPropertyChanged(); } }

    private string _backgroundPreviewPath = "";
    public string BackgroundPreviewPath { get => _backgroundPreviewPath; set { _backgroundPreviewPath = value; OnPropertyChanged(); } }

    public string? SelectedLogoSource { get; set; }
    public string? SelectedBackgroundSource { get; set; }
    public bool ClearLogo { get; set; }
    public bool ClearBackground { get; set; }

    // -------- ✅ Kiosk URL (แสดง IP ให้ user) ----------
    public ObservableCollection<string> KioskUrls { get; } = new();

    private string _selectedKioskUrl = "";
    public string SelectedKioskUrl
    {
        get => _selectedKioskUrl;
        set { _selectedKioskUrl = value; OnPropertyChanged(); }
    }

    private string _kioskHint =
        "ถ้าจอ Kiosk เชื่อมอัตโนมัติ (mDNS) ไม่ได้ ให้ไปที่หน้าตั้งค่าใน Kiosk แล้วใส่ URL จากรายการนี้";
    public string KioskHint
    {
        get => _kioskHint;
        set { _kioskHint = value; OnPropertyChanged(); }
    }

    // -------- Status ----------
    private string? _message;
    public string? Message { get => _message; set { _message = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    private string? _error;
    public string? Error { get => _error; set { _error = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    public SettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        Load();
    }

    public void Load()
    {
        var cfg = _configService.Load();

        ApiBaseUrl = string.IsNullOrWhiteSpace(cfg.ApiBaseUrl) ? "http://localhost:5269" : cfg.ApiBaseUrl!;
        AdminKey = cfg.AdminKey ?? "";

        LogoPreviewPath = ResolveBrandingPath(cfg.LogoFile);
        BackgroundPreviewPath = ResolveBrandingPath(cfg.BackgroundFile);

        SelectedLogoSource = null;
        SelectedBackgroundSource = null;
        ClearLogo = false;
        ClearBackground = false;

        RefreshKioskUrls();
    }

    private string ResolveBrandingPath(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return "";
        var full = _configService.GetBrandingFilePath(file.Trim());
        return File.Exists(full) ? full : "";
    }

    public void RefreshKioskUrls()
    {
        KioskUrls.Clear();

        // parse base url to get scheme/port
        var scheme = "http";
        var port = 5269;
        var host = "localhost";

        if (Uri.TryCreate((ApiBaseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
        {
            scheme = uri.Scheme;
            port = uri.Port;
            host = uri.Host;
        }

        // ถ้า baseUrl เป็น host ที่ไม่ใช่ localhost ก็ใส่ให้เป็นตัวเลือกแรกด้วย
        if (!IsLocalhost(host))
        {
            var baseCandidate = $"{scheme}://{host}:{port}";
            KioskUrls.Add(baseCandidate);
        }

        // ใส่ IP เครื่องนี้ใน LAN
        var ips = NetworkHelper.GetLanIPv4Addresses();
        foreach (var ip in ips)
        {
            KioskUrls.Add($"{scheme}://{ip}:{port}");
        }

        // fallback เผื่อไม่มี ip
        if (KioskUrls.Count == 0)
            KioskUrls.Add($"{scheme}://localhost:{port}");

        // select default
        SelectedKioskUrl = KioskUrls[0];
    }

    private static bool IsLocalhost(string host)
    {
        host = (host ?? "").Trim().ToLowerInvariant();
        return host == "localhost" || host == "127.0.0.1" || host == "::1";
    }

    public void Save()
    {
        Error = null;
        Message = null;

        var cfg = _configService.Load();
        cfg.ApiBaseUrl = (ApiBaseUrl ?? "").Trim();
        cfg.AdminKey = (AdminKey ?? "").Trim();

        // Logo
        if (ClearLogo)
        {
            cfg.LogoFile = "";
            LogoPreviewPath = "";
        }
        else if (!string.IsNullOrWhiteSpace(SelectedLogoSource) && File.Exists(SelectedLogoSource))
        {
            cfg.LogoFile = _configService.SaveBrandingFile(SelectedLogoSource, "logo");
            LogoPreviewPath = _configService.GetBrandingFilePath(cfg.LogoFile);
        }

        // Background
        if (ClearBackground)
        {
            cfg.BackgroundFile = "";
            BackgroundPreviewPath = "";
        }
        else if (!string.IsNullOrWhiteSpace(SelectedBackgroundSource) && File.Exists(SelectedBackgroundSource))
        {
            cfg.BackgroundFile = _configService.SaveBrandingFile(SelectedBackgroundSource, "background");
            BackgroundPreviewPath = _configService.GetBrandingFilePath(cfg.BackgroundFile);
        }

        _configService.Save(cfg);

        SelectedLogoSource = null;
        SelectedBackgroundSource = null;
        ClearLogo = false;
        ClearBackground = false;

        Message = "บันทึกการตั้งค่าเรียบร้อย";
    }

    public async Task TestConnectionAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            Error = null;
            Message = null;

            var cfg = new AppConfig
            {
                ApiBaseUrl = (ApiBaseUrl ?? "").Trim(),
                AdminKey = (AdminKey ?? "").Trim()
            };

            var api = new ApiClient(cfg);
            var ok = await api.TestConnectionAsync();
            Message = ok ? "เชื่อมต่อสำเร็จ" : "เชื่อมต่อไม่สำเร็จ";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}