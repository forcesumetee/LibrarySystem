using System;
using System.Windows;
using System.Windows.Controls;
using LibraryAdminPC.Services;
using LibraryAdminPC.ViewModels;
using Microsoft.Win32;

namespace LibraryAdminPC.Views;

// Task B / B5: dedicated "โลโก้ & พื้นหลัง" page. Uses its own SettingsViewModel
// instance (like SettingsView). This is safe: SettingsViewModel.Save() re-loads
// the config from disk and only overwrites the branding fields when a logo/bg was
// actually picked or cleared, so editing branding here never clobbers the API
// settings saved from SettingsView, and vice-versa. The branding handlers mirror
// SettingsView's exactly (same VM properties / same flow).
public partial class BrandingView : UserControl
{
    private readonly SettingsViewModel _vm;

    public event EventHandler? Saved;

    public BrandingView(ConfigService configService)
    {
        InitializeComponent();
        _vm = new SettingsViewModel(configService);
        DataContext = _vm;
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

    private void BtnSaveBranding_Click(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
