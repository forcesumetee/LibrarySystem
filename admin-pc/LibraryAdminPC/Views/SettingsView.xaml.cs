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

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _vm.AdminKey = PwdAdminKey.Password;
        _vm.Save();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        _vm.AdminKey = PwdAdminKey.Password;
        await _vm.TestConnectionAsync();
    }
}