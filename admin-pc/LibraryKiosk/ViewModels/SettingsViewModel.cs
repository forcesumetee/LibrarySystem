using System;
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

    // ---- display ----
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private bool _logoAvailable;
    [ObservableProperty] private bool _backgroundAvailable;
    [ObservableProperty] private bool _hideLogo;
    [ObservableProperty] private bool _hideBackground;
    [ObservableProperty] private double _uiScalePercent = 100;
    [ObservableProperty] private bool _isFullscreen = true;

    // ---- change-PIN / about ----
    [ObservableProperty] private string _changePinResult = "";

    public string AboutVersion => "LibraryKiosk 1.0 (Phase 5)";

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
        Func<(bool logo, bool background)> getBrandingAvailable)
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
        IsFullscreen = !string.Equals(s.DisplayMode, "windowed", StringComparison.OrdinalIgnoreCase);
        HideLogo = s.HideLogo;
        HideBackground = s.HideBackground;

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

        var (state, last) = _getStatus();
        LastUpdated = last;
    }

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
            UpdateConnectionStatus();
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
            UpdateConnectionStatus();
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

    private void UpdateConnectionStatus()
    {
        var (state, last) = _getStatus();
        LastUpdated = last;
        ConnectionStatus = state switch
        {
            ConnectionState.Connected => $"เชื่อมต่อสำเร็จ • อัปเดต {last}",
            ConnectionState.Unlicensed => "เชื่อมต่อได้ แต่เซิร์ฟเวอร์ยังไม่ได้เปิดลิขสิทธิ์",
            _ => "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ — ตรวจสอบ URL"
        };
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
