using System.ComponentModel;
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

        // Start the live connection (SignalR + first full sync) once the window is up.
        Loaded += async (_, _) => await _vm.StartAsync();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        await _vm.ShutdownAsync();
    }
}
