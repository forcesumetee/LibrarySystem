using System;
using System.Windows;
using System.Windows.Controls;
using LibraryAdminPC.Services;
using LibraryAdminPC.ViewModels;
using Microsoft.Win32;

namespace LibraryAdminPC.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel _vm;

    public event EventHandler? Saved;

    public SettingsView(ConfigService configService)
    {
        InitializeComponent();
        _vm = new SettingsViewModel(configService);
        DataContext = _vm;

        // sync password box
        PwdAdminKey.Password = _vm.AdminKey ?? string.Empty;
        PwdAdminKey.PasswordChanged += (_, __) => _vm.AdminKey = PwdAdminKey.Password;

        RefreshLicenseInfo();
        TxtAppVersion.Text = GetAppVersion();
    }

    // Additive (B6): "เกี่ยวกับระบบ" version, read from the running assembly — not
    // hardcoded. Falls back to "-" if the version is unavailable.
    private static string GetAppVersion()
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "-" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "-";
        }
    }

    // Additive (Task B / B5): license section. Reads LicenseService (unchanged
    // service) for status + Machine ID and lets the admin (re)activate via the
    // existing LicenseKeyDialog. Per the design brief there is NO expiry date and
    // NO device-count display (license is per-machine / perpetual).
    private readonly LicenseService _licenseService = new();

    private void RefreshLicenseInfo()
    {
        try
        {
            var s = _licenseService.GetStatus();

            if (s.IsLicensed)
            {
                TxtLicenseStatus.Text = "เปิดใช้งานแล้ว (ACTIVE)";
                TxtLicenseStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessTextBrush");
                LicenseStatusPill.Background = (System.Windows.Media.Brush)FindResource("SuccessTintBrush");
            }
            else
            {
                TxtLicenseStatus.Text = "ยังไม่เปิดใช้งาน";
                TxtLicenseStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorTextBrush");
                LicenseStatusPill.Background = (System.Windows.Media.Brush)FindResource("ErrorTintBrush");
            }

            // LicenseStatus.MaskedKey carries the machine id from the server status.
            TxtMachineId.Text = string.IsNullOrWhiteSpace(s.MaskedKey) ? "-" : s.MaskedKey;
            TxtLicenseMessage.Text = s.Message ?? "";
        }
        catch (Exception ex)
        {
            TxtLicenseStatus.Text = "ตรวจสอบไม่ได้";
            TxtLicenseMessage.Text = ex.Message;
        }
    }

    private void BtnActivateLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new LicenseKeyDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var key = (dlg.ProductKey ?? "").Trim();
            var result = _licenseService.TryActivate(key);

            MessageBox.Show(
                result.Message,
                result.Success ? "Activated" : "Invalid Key",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            RefreshLicenseInfo();

            // let MainWindow refresh its license badge (reuses the existing Saved hook).
            if (result.Success)
                Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "License Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCopyKioskUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = (_vm.SelectedKioskUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("ไม่มี URL ให้คัดลอก", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(text);
            MessageBox.Show("คัดลอก URL แล้ว", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRefreshIps_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshKioskUrls();
    }

    private void BtnPickLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "เลือกไฟล์โลโก้",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.SelectedLogoSource = dlg.FileName;
            _vm.ClearLogo = false;
            _vm.LogoPreviewPath = dlg.FileName;
        }
    }

    private void BtnClearLogo_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedLogoSource = null;
        _vm.ClearLogo = true;
        _vm.LogoPreviewPath = "";
    }

    private void BtnPickBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "เลือกไฟล์พื้นหลัง",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.SelectedBackgroundSource = dlg.FileName;
            _vm.ClearBackground = false;
            _vm.BackgroundPreviewPath = dlg.FileName;
        }
    }

    private void BtnClearBg_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedBackgroundSource = null;
        _vm.ClearBackground = true;
        _vm.BackgroundPreviewPath = "";
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _vm.AdminKey = PwdAdminKey.Password;
        // K1: save locally AND push any branding change to the server so kiosks live-update.
        await _vm.SaveAndPushBrandingAsync();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        _vm.AdminKey = PwdAdminKey.Password;
        await _vm.TestConnectionAsync();
    }

    private async void BtnResetKioskPin_Click(object sender, RoutedEventArgs e)
    {
        // Pick up the latest AdminKey from the password box (same as Test/Save).
        _vm.AdminKey = PwdAdminKey.Password;

        // Confirm before firing — this resets the PIN on every connected kiosk.
        if (!ConfirmDialog.Ask(Window.GetWindow(this), "รีเซ็ต PIN จอ Kiosk",
                "รีเซ็ต PIN ของจอ Kiosk ทุกจอเป็น 1234?\nผู้ดูแลต้องตั้ง PIN ใหม่ที่จอ", "รีเซ็ต PIN"))
            return;

        await _vm.ResetKioskPinAsync();
    }
}