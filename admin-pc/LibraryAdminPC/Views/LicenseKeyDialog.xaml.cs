using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LibraryAdminPC.Services;

namespace LibraryAdminPC.Views;

public partial class LicenseKeyDialog : Window
{
    public string? ProductKey { get; private set; }

    // Same source SettingsView/dashboard use for the Machine ID (GET /api/license/status).
    private readonly LicenseService _licenseService = new();
    private string? _machineId;

    public LicenseKeyDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // GetStatus() is a blocking HTTP call (5s timeout) -> run it off the UI thread so the
        // dialog opens immediately showing "กำลังโหลด..." and fills in the Machine ID when it returns.
        try
        {
            var s = await Task.Run(() => _licenseService.GetStatus());
            _machineId = string.IsNullOrWhiteSpace(s.MaskedKey) ? null : s.MaskedKey;
            TxtMachineId.Text = _machineId ?? "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ — เปิดเซิร์ฟเวอร์แล้วเปิดหน้านี้ใหม่";
        }
        catch
        {
            _machineId = null;
            TxtMachineId.Text = "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้";
        }
        BtnCopy.IsEnabled = _machineId != null;
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_machineId)) return;
        try { Clipboard.SetText(_machineId); } catch { /* clipboard may be briefly locked */ }

        // Brief "copied" feedback, then revert the button label.
        BtnCopy.Content = "ก๊อปแล้ว";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { BtnCopy.Content = "ก๊อป"; timer.Stop(); };
        timer.Start();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ProductKey = (TxtKey.Text ?? "").Trim();
        DialogResult = true;
        Close();
    }
}
