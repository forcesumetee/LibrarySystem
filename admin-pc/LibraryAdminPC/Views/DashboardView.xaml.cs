using System;
using System.Windows;
using System.Windows.Controls;
using LibraryAdminPC.Services;
using LibraryAdminPC.ViewModels;

namespace LibraryAdminPC.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;

    // B7 additive: raised when the user clicks "ดูทั้งหมดในหน้าจัดการหนังสือ".
    // MainWindow subscribes and calls its existing NavigateToBooks().
    public event EventHandler? ViewAllBooksRequested;

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
            // B7 additive: populate doughnut/legend/recent/top-category extras
            // (separate method; the original LoadAsync above is unchanged).
            await _vm.LoadExtrasAsync();
        }
        catch (Exception ex)
        {
            // กันกรณี exception หลุดมาถึง view (ปกติ VM ควร handle แล้ว)
            MessageBox.Show(ex.Message, "Dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // B7 additive: "ดูทั้งหมด" link -> ask the shell to navigate to the Books page.
    private void BtnViewAllBooks_Click(object sender, RoutedEventArgs e)
        => ViewAllBooksRequested?.Invoke(this, EventArgs.Empty);
}