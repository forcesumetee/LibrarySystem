using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryKiosk.Services;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// Drives the admin overlay (port spec 7): a PIN gate, then a settings panel
/// (connection / display / security / about), plus a change-PIN sub-flow. All
/// host-level effects (reconnect, refresh, UI scale, display mode, local branding
/// hide) are delegated to <see cref="HomeViewModel"/> via callbacks so this VM
/// never touches the window or the SyncService internals directly.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly SyncService _sync;
    private readonly PinService _pin;

    private readonly Func<Task> _reloadAsync;
    private readonly Func<(ConnectionState state, string lastUpdated)> _getStatus;
    private readonly Action<double> _applyUiScale;
    private readonly Action<string> _applyDisplayMode;
    private readonly Action<bool, bool> _applyBranding;       // (hideLogo, hideBackground)
    private readonly Func<string> _getDisplayName;
    private readonly Func<(bool logo, bool background)> _getBrandingAvailable;
    private readonly Action _requestExit;
    private readonly Action<string?> _applySystemName;         // K2 item 5
    private readonly Action<double, double> _applyResolution;  // K2 item 4 (w, h)
    private readonly Action<double> _applyBackgroundOpacity;   // background image opacity (0.2–1.0)
    private readonly AutoStartService _autoStart;

    private readonly DispatcherTimer _lockTimer;
    private int _lockRemaining;

    // change-PIN state machine
    private int _changeStep;            // 0 = verify old, 1 = enter new, 2 = confirm new
    private string _pendingNewPin = "";

    public PinPadViewModel Pad { get; } = new();

    [ObservableProperty] private bool _isOpen;

    // ---- screen flags ----
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowKeypad))] private bool _showGate;
    [ObservableProperty] private bool _showPanel;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowKeypad))] private bool _showChangePin;

    // ---- lockout ----
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowKeypad))] private bool _isLocked;
    [ObservableProperty] private string _lockMessage = "";

    /// <summary>Keypad is shown for both the gate and the change-PIN flow, but never while locked.</summary>
    public bool ShowKeypad => (ShowGate || ShowChangePin) && !IsLocked;

    // ---- connection ----
    [ObservableProperty] private string _baseUrlInput = "";
    [ObservableProperty] private string _connectionStatus = "";
    [ObservableProperty] private string _lastUpdated = "-";
    [ObservableProperty] private bool _isBusy;
    /// <summary>Realtime connected/disconnected indicator (green/red) — K2 item 3.</summary>
    [ObservableProperty] private bool _isConnected;

    // ---- display ----
    [ObservableProperty] private string _displayName = "";
    /// <summary>Local per-kiosk system-name override (K2 item 5); blank = use server name.</summary>
    [ObservableProperty] private string _systemNameInput = "";

    // ---- resolution (K2 item 4) ----
    [ObservableProperty] private string _resWidthInput = "1080";
    [ObservableProperty] private string _resHeightInput = "1920";
    [ObservableProperty] private string _resolutionStatus = "";
    [ObservableProperty] private bool _logoAvailable;
    [ObservableProperty] private bool _backgroundAvailable;
    [ObservableProperty] private bool _hideLogo;
    [ObservableProperty] private bool _hideBackground;
    [ObservableProperty] private double _uiScalePercent = 100;
    [ObservableProperty] private bool _isFullscreen = true;
    /// <summary>Background-image opacity as a percentage (20–100); bound to a settings slider.</summary>
    [ObservableProperty] private double _backgroundOpacityPercent = 100;

    // ---- theme colour (primary) ----
    /// <summary>Preset primary-colour swatches (design-system, dark enough for white text).</summary>
    public string[] ThemeSwatches { get; } =
        { "#1F5AA8", "#2E8B86", "#2E9C7E", "#5A5BB8", "#C2622A", "#C0413B" };
    /// <summary>Current primary colour as hex "#RRGGBB" (two-way synced with the R/G/B sliders).</summary>
    [ObservableProperty] private string _primaryColorInput = ThemeService.DefaultPrimaryHex;
    [ObservableProperty] private double _colorR = 31;
    [ObservableProperty] private double _colorG = 90;
    [ObservableProperty] private double _colorB = 168;
    [ObservableProperty] private string _themeColorStatus = "";
    // Guard so hex<->RGB two-way sync (+ initial load) doesn't recurse / persist mid-sync.
    private bool _suppressColorSync;

    // ---- system (Phase 6) ----
    [ObservableProperty] private bool _autoStartEnabled;

    // ---- change-PIN / about ----
    [ObservableProperty] private string _changePinResult = "";

    public string AboutVersion => "LibraHub Search 1.0";

    public SettingsViewModel(
        SettingsService settings,
        SyncService sync,
        PinService pin,
        Func<Task> reloadAsync,
        Func<(ConnectionState state, string lastUpdated)> getStatus,
        Action<double> applyUiScale,
        Action<string> applyDisplayMode,
        Action<bool, bool> applyBranding,
        Func<string> getDisplayName,
        Func<(bool logo, bool background)> getBrandingAvailable,
        Action requestExit,
        Action<string?> applySystemName,
        Action<double, double> applyResolution,
        Action<double> applyBackgroundOpacity)
    {
        _settings = settings;
        _sync = sync;
        _pin = pin;
        _reloadAsync = reloadAsync;
        _getStatus = getStatus;
        _applyUiScale = applyUiScale;
        _applyDisplayMode = applyDisplayMode;
        _applyBranding = applyBranding;
        _getDisplayName = getDisplayName;
        _getBrandingAvailable = getBrandingAvailable;
        _requestExit = requestExit;
        _applySystemName = applySystemName;
        _applyResolution = applyResolution;
        _applyBackgroundOpacity = applyBackgroundOpacity;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        _autoStart = new AutoStartService(exePath);

        Pad.Submitted += OnPadSubmitted;

        _lockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockTimer.Tick += OnLockTick;
    }

    // ---------------- open / close ----------------

    /// <summary>Open the overlay at the PIN gate (or the lock screen if still locked).</summary>
    public void Open()
    {
        ChangePinResult = "";
        ConnectionStatus = "";

        var s = _settings.Load();
        BaseUrlInput = s.BaseUrl;
        UiScalePercent = Math.Round(Math.Clamp(s.UiScale, 0.8, 1.2) * 100);
        BackgroundOpacityPercent = Math.Round(ClampOpacity(s.BackgroundOpacity) * 100);
        LoadThemeColor(s.PrimaryColor);
        IsFullscreen = !string.Equals(s.DisplayMode, "windowed", StringComparison.OrdinalIgnoreCase);
        HideLogo = s.HideLogo;
        HideBackground = s.HideBackground;
        SystemNameInput = s.SystemName ?? "";
        ResWidthInput = (s.CanvasWidth > 0 ? s.CanvasWidth : 1080).ToString();
        ResHeightInput = (s.CanvasHeight > 0 ? s.CanvasHeight : 1920).ToString();
        ResolutionStatus = "";

        IsOpen = true;

        if (_pin.TryGetLockRemaining(out var remaining))
            BeginLock(remaining);
        else
            EnterGate();
    }

    [RelayCommand]
    private void Close()
    {
        _lockTimer.Stop();
        Pad.ClearEntry();
        IsOpen = false;
        ShowGate = ShowPanel = ShowChangePin = false;
    }

    /// <summary>An admin "reset PIN" broadcast cleared the PIN + lockout underneath us
    /// (<see cref="PinService.ResetToDefault"/>). If the overlay is currently sitting on the
    /// gate or the lock screen, drop back to a fresh UNLOCKED gate so the operator can enter
    /// the new default PIN right away. (If the panel is already open, leave it alone.)</summary>
    public void HandleExternalPinReset()
    {
        _lockTimer.Stop();
        if (IsOpen && (ShowGate || IsLocked))
            EnterGate();
    }

    // ---------------- gate ----------------

    private void EnterGate()
    {
        ShowGate = true;
        ShowPanel = false;
        ShowChangePin = false;
        IsLocked = false;
        Pad.IsInputEnabled = true;
        Pad.Reset("ใส่ PIN ผู้ดูแล", "ใส่ PIN เพื่อเข้าหน้าตั้งค่า");
    }

    private void OnPadSubmitted(string pin)
    {
        if (ShowChangePin) HandleChangePin(pin);
        else if (ShowGate) HandleGate(pin);
    }

    private void HandleGate(string pin)
    {
        var r = _pin.Verify(pin);
        switch (r.Kind)
        {
            case PinResultKind.Success:
                EnterPanel();
                break;
            case PinResultKind.Wrong:
                Pad.ClearEntry();
                Pad.SetError($"PIN ไม่ถูกต้อง • เหลืออีก {r.AttemptsRemaining} ครั้ง");
                break;
            case PinResultKind.Locked:
                BeginLock(r.LockSeconds);
                break;
        }
    }

    // ---------------- panel ----------------

    private void EnterPanel()
    {
        ShowPanel = true;
        ShowGate = false;
        ShowChangePin = false;
        IsLocked = false;
        Pad.ClearEntry();

        DisplayName = _getDisplayName();
        var (logo, bg) = _getBrandingAvailable();
        LogoAvailable = logo;
        BackgroundAvailable = bg;

        var s = _settings.Load();
        HideLogo = s.HideLogo;
        HideBackground = s.HideBackground;
        BackgroundOpacityPercent = Math.Round(ClampOpacity(s.BackgroundOpacity) * 100);
        LoadThemeColor(s.PrimaryColor);
        SystemNameInput = s.SystemName ?? "";
        ResWidthInput = (s.CanvasWidth > 0 ? s.CanvasWidth : 1080).ToString();
        ResHeightInput = (s.CanvasHeight > 0 ? s.CanvasHeight : 1920).ToString();
        AutoStartEnabled = _autoStart.IsEnabled();

        RefreshConnectionIndicator();
    }

    // ---- system: auto-start + exit ----

    [RelayCommand]
    private void ToggleAutoStart()
    {
        var result = _autoStart.Set(!AutoStartEnabled);
        AutoStartEnabled = result;

        var s = _settings.Load();
        s.AutoStart = result;
        _settings.Save(s);
    }

    [RelayCommand]
    private void ExitKiosk() => _requestExit();

    [RelayCommand]
    private async Task SaveBaseUrl()
    {
        var url = (BaseUrlInput ?? "").Trim();
        if (url.Length == 0)
        {
            ConnectionStatus = "กรุณากรอก URL ของเซิร์ฟเวอร์";
            return;
        }

        IsBusy = true;
        ConnectionStatus = "กำลังเชื่อมต่อ…";
        try
        {
            var s = _settings.Load();
            s.BaseUrl = url;
            _settings.Save(s);

            await _sync.RebindAsync(url);
            await _reloadAsync();
            RefreshConnectionIndicator();
        }
        catch (Exception)
        {
            ConnectionStatus = "เชื่อมต่อไม่สำเร็จ";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        IsBusy = true;
        ConnectionStatus = "กำลังซิงก์…";
        try
        {
            await _reloadAsync();
            RefreshConnectionIndicator();
            DisplayName = _getDisplayName();
            var (logo, bg) = _getBrandingAvailable();
            LogoAvailable = logo;
            BackgroundAvailable = bg;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Pull the live host status into the connected/disconnected indicator (K2 item 3).
    /// Called when the panel opens and after every sync (HomeViewModel.Apply) so the dot
    /// and last-sync time stay live — reuses the existing sync state, no extra polling.
    /// </summary>
    public void RefreshConnectionIndicator()
    {
        var (state, last) = _getStatus();
        LastUpdated = last;
        IsConnected = state == ConnectionState.Connected;
        ConnectionStatus = state switch
        {
            ConnectionState.Connected => $"เชื่อมต่อสำเร็จ • อัปเดตล่าสุด {last}",
            ConnectionState.Unlicensed => "เชื่อมต่อได้ • เซิร์ฟเวอร์ยังไม่เปิดลิขสิทธิ์",
            ConnectionState.Loading => "กำลังเชื่อมต่อ…",
            _ => "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ — ตรวจสอบ URL"
        };
    }

    /// <summary>Realtime hub-drop feedback (K2 item 3): show disconnected immediately. A
    /// reconnect runs a sync that flips this back via <see cref="RefreshConnectionIndicator"/>.</summary>
    public void MarkDisconnected()
    {
        IsConnected = false;
        ConnectionStatus = "การเชื่อมต่อหลุด • กำลังเชื่อมต่อใหม่…";
    }

    // ---- branding (local hide only; server is never touched) ----

    [RelayCommand]
    private void ToggleLogo()
    {
        HideLogo = !HideLogo;
        PersistBranding();
    }

    [RelayCommand]
    private void ToggleBackground()
    {
        HideBackground = !HideBackground;
        PersistBranding();
    }

    private void PersistBranding()
    {
        var s = _settings.Load();
        s.HideLogo = HideLogo;
        s.HideBackground = HideBackground;
        _settings.Save(s);
        _applyBranding(HideLogo, HideBackground);
    }

    // ---- UI scale ----

    partial void OnUiScalePercentChanged(double value)
    {
        var clamped = Math.Clamp(value, 80, 120);
        var scale = clamped / 100.0;
        _applyUiScale(scale);

        var s = _settings.Load();
        s.UiScale = scale;
        _settings.Save(s);
    }

    // ---- background image opacity ----

    /// <summary>Clamp a 0..1 opacity into the supported 0.2–1.0 slider range (0/legacy → 1.0).</summary>
    private static double ClampOpacity(double value) => value <= 0 ? 1.0 : Math.Clamp(value, 0.2, 1.0);

    partial void OnBackgroundOpacityPercentChanged(double value)
    {
        var opacity = ClampOpacity(Math.Clamp(value, 20, 100) / 100.0);
        _applyBackgroundOpacity(opacity);   // live on the background image

        var s = _settings.Load();
        s.BackgroundOpacity = opacity;
        _settings.Save(s);
    }

    // ---- theme colour (primary) ----

    /// <summary>Load a saved primary colour into the inputs (no apply/persist — read-only sync).</summary>
    private void LoadThemeColor(string? hex)
    {
        var c = ThemeService.ParseOrDefault(hex);
        _suppressColorSync = true;
        PrimaryColorInput = ThemeService.ToHex(c);
        ColorR = c.R; ColorG = c.G; ColorB = c.B;
        ThemeColorStatus = "";
        _suppressColorSync = false;
    }

    /// <summary>Pick a preset swatch (hex). Drives <see cref="PrimaryColorInput"/> -> apply.</summary>
    [RelayCommand]
    private void SelectThemeSwatch(string? hex) => PrimaryColorInput = hex ?? ThemeService.DefaultPrimaryHex;

    /// <summary>Reset the primary colour back to the default blue.</summary>
    [RelayCommand]
    private void ResetThemeColor() => PrimaryColorInput = ThemeService.DefaultPrimaryHex;

    // Hex box is the single source of truth: editing it (or a swatch/reset) re-syncs the RGB
    // sliders then applies+persists. RGB slider changes compose a new hex (handled below).
    partial void OnPrimaryColorInputChanged(string value)
    {
        if (_suppressColorSync) return;
        if (!ThemeService.TryParseHex(value, out var c))
        {
            ThemeColorStatus = "รหัสสีไม่ถูกต้อง (เช่น #1F5AA8)";
            return;
        }
        _suppressColorSync = true;
        ColorR = c.R; ColorG = c.G; ColorB = c.B;
        _suppressColorSync = false;
        ApplyThemeColor(c);
    }

    partial void OnColorRChanged(double value) => OnRgbSliderChanged();
    partial void OnColorGChanged(double value) => OnRgbSliderChanged();
    partial void OnColorBChanged(double value) => OnRgbSliderChanged();

    private void OnRgbSliderChanged()
    {
        if (_suppressColorSync) return;
        var c = System.Windows.Media.Color.FromRgb((byte)Math.Clamp(ColorR, 0, 255),
            (byte)Math.Clamp(ColorG, 0, 255), (byte)Math.Clamp(ColorB, 0, 255));
        _suppressColorSync = true;
        PrimaryColorInput = ThemeService.ToHex(c); // reflect in the hex box (no recurse)
        _suppressColorSync = false;
        ApplyThemeColor(c);
    }

    /// <summary>Apply the colour live to the shared theme brushes and persist it (PrimaryColor
    /// only — every other setting is loaded and saved back untouched).</summary>
    private void ApplyThemeColor(System.Windows.Media.Color c)
    {
        var hex = ThemeService.ToHex(c);
        ThemeService.Apply(hex);             // live: recolours the whole UI (both orientations)
        ThemeColorStatus = "";

        var s = _settings.Load();
        s.PrimaryColor = hex;
        _settings.Save(s);
    }

    // ---- display mode ----

    [RelayCommand]
    private void SetFullscreen() => SetDisplayMode(true);

    [RelayCommand]
    private void SetWindowed() => SetDisplayMode(false);

    private void SetDisplayMode(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        var mode = fullscreen ? "fullscreen" : "windowed";

        var s = _settings.Load();
        s.DisplayMode = mode;
        _settings.Save(s);

        _applyDisplayMode(mode);
    }

    // ---- system name (K2 item 5) ----

    [RelayCommand]
    private void SaveSystemName()
    {
        var name = (SystemNameInput ?? "").Trim();

        var s = _settings.Load();
        s.SystemName = name.Length == 0 ? null : name;
        _settings.Save(s);

        _applySystemName(s.SystemName);   // header updates immediately
        DisplayName = _getDisplayName();  // reflect the resolved name in the panel
    }

    // ---- resolution (K2 item 4) ----

    [RelayCommand]
    private void SetPortraitPreset()
    {
        ResWidthInput = "1080";
        ResHeightInput = "1920";
        ApplyResolution();
    }

    [RelayCommand]
    private void SetLandscapePreset()
    {
        ResWidthInput = "1920";
        ResHeightInput = "1080";
        ApplyResolution();
    }

    [RelayCommand]
    private void ApplyResolution()
    {
        if (!int.TryParse((ResWidthInput ?? "").Trim(), out var w) ||
            !int.TryParse((ResHeightInput ?? "").Trim(), out var h) ||
            w < 320 || h < 320 || w > 8000 || h > 8000)
        {
            ResolutionStatus = "กรอกความกว้าง/สูงเป็นตัวเลข (320–8000)";
            return;
        }

        // Landscape (width > height) now drives the landscape root-swap layout.
        var s = _settings.Load();
        s.CanvasWidth = w;
        s.CanvasHeight = h;
        _settings.Save(s);

        _applyResolution(w, h);
        ResolutionStatus = $"ใช้ความละเอียด {w} × {h} แล้ว";
    }

    // ---------------- change PIN ----------------

    [RelayCommand]
    private void StartChangePin()
    {
        _changeStep = 0;
        _pendingNewPin = "";
        ShowChangePin = true;
        ShowPanel = false;
        ShowGate = false;
        Pad.IsInputEnabled = true;
        Pad.Reset("เปลี่ยน PIN", "ใส่ PIN เดิมเพื่อยืนยันตัวตน");
    }

    [RelayCommand]
    private void CancelChangePin() => EnterPanel();

    private void HandleChangePin(string pin)
    {
        switch (_changeStep)
        {
            case 0: // verify the current PIN
                var r = _pin.Verify(pin);
                if (r.Kind == PinResultKind.Success)
                {
                    _changeStep = 1;
                    Pad.Reset("ตั้ง PIN ใหม่", "ตัวเลข 4–8 หลัก");
                }
                else if (r.Kind == PinResultKind.Locked)
                {
                    BeginLock(r.LockSeconds);
                }
                else
                {
                    Pad.ClearEntry();
                    Pad.SetError($"PIN เดิมไม่ถูกต้อง • เหลืออีก {r.AttemptsRemaining} ครั้ง");
                }
                break;

            case 1: // choose a new PIN
                if (!PinService.IsValidNewPin(pin))
                {
                    Pad.ClearEntry();
                    Pad.SetError("PIN ต้องเป็นตัวเลข 4–8 หลัก");
                    break;
                }
                _pendingNewPin = pin;
                _changeStep = 2;
                Pad.Reset("ยืนยัน PIN ใหม่", "พิมพ์ PIN ใหม่อีกครั้ง");
                break;

            case 2: // confirm
                if (pin != _pendingNewPin)
                {
                    _changeStep = 1;
                    _pendingNewPin = "";
                    Pad.Reset("ตั้ง PIN ใหม่", "PIN ไม่ตรงกัน ลองใหม่อีกครั้ง");
                    Pad.SetError("PIN ยืนยันไม่ตรงกัน");
                    break;
                }
                _pin.SetPin(_pendingNewPin);
                _pendingNewPin = "";
                ChangePinResult = "เปลี่ยน PIN เรียบร้อยแล้ว";
                EnterPanel();
                break;
        }
    }

    // ---------------- lockout countdown ----------------

    private void BeginLock(int seconds)
    {
        // Force back to the gate; the keypad is disabled while locked.
        ShowGate = true;
        ShowPanel = false;
        ShowChangePin = false;
        IsLocked = true;
        Pad.IsInputEnabled = false;
        Pad.ClearEntry();
        Pad.ClearError();

        _lockRemaining = Math.Max(1, seconds);
        UpdateLockMessage();
        _lockTimer.Start();
    }

    private void OnLockTick(object? sender, EventArgs e)
    {
        _lockRemaining--;
        if (_lockRemaining <= 0)
        {
            _lockTimer.Stop();
            EnterGate();
            return;
        }
        UpdateLockMessage();
    }

    private void UpdateLockMessage()
        => LockMessage = $"ใส่ PIN ผิดเกินกำหนด • ลองใหม่ในอีก {_lockRemaining} วินาที";
}
