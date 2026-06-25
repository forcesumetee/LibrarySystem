using System.Windows;
using LibraryKiosk.Services;
using LibraryKiosk.ViewModels;

namespace LibraryKiosk;

public partial class MainWindow : Window
{
    private readonly HomeViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new HomeViewModel(new SettingsService());
        DataContext = _vm;

        // Kick off the first /api/meta fetch once the window is up.
        Loaded += async (_, _) => await _vm.RefreshAsync();
    }
}
