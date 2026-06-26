using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibraryAdminPC.Services;
using LibraryAdminPC.ViewModels;

namespace LibraryAdminPC.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;

    // K3: light auto-refresh of the kiosk-count KPI. Kiosks connect/disconnect rarely,
    // so a ~12s poll is plenty; the timer stops when the view is unloaded.
    private readonly DispatcherTimer _kioskTimer = new() { Interval = TimeSpan.FromSeconds(12) };

    // B7 additive: raised when the user clicks "ดูทั้งหมดในหน้าจัดการหนังสือ".
    // MainWindow subscribes and calls its existing NavigateToBooks().
    public event EventHandler? ViewAllBooksRequested;

    public DashboardView(ApiClient api)
    {
        InitializeComponent();

        _vm = new DashboardViewModel(api);
        DataContext = _vm;

        Loaded += DashboardView_Loaded;
        Unloaded += (_, __) => _kioskTimer.Stop();
        _kioskTimer.Tick += (_, __) => _ = _vm.LoadKioskCountAsync();
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
            // K3 additive: live kiosk count KPI, then poll lightly while open.
            await _vm.LoadKioskCountAsync();
            _kioskTimer.Start();
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