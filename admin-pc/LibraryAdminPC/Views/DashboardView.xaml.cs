using System;
using System.Windows;
using System.Windows.Controls;
using LibraryAdminPC.Services;
using LibraryAdminPC.ViewModels;

namespace LibraryAdminPC.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;

    public DashboardView(ApiClient api)
    {
        InitializeComponent();

        _vm = new DashboardViewModel(api);
        DataContext = _vm;

        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= DashboardView_Loaded;

        try
        {
            await _vm.LoadAsync();
        }
        catch (Exception ex)
        {
            // กันกรณี exception หลุดมาถึง view (ปกติ VM ควร handle แล้ว)
            MessageBox.Show(ex.Message, "Dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}