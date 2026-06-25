using System;
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

        _vm.DisplayModeChangeRequested += ApplyDisplayMode;

        // Apply the persisted display mode, then start the live connection once up.
        Loaded += async (_, _) =>
        {
            ApplyDisplayMode(_vm.DisplayMode);
            await _vm.StartAsync();
        };
        Closing += OnClosing;
    }

    /// <summary>
    /// Phase 5: fullscreen (kiosk) vs windowed (dev). Full lockdown — Topmost,
    /// blocking Alt+F4 — is deferred to Phase 6 so the app stays escapable for now.
    /// </summary>
    private void ApplyDisplayMode(string mode)
    {
        if (string.Equals(mode, "windowed", StringComparison.OrdinalIgnoreCase))
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Width = 540;
            Height = 960;
        }
        else
        {
            WindowState = WindowState.Normal; // reset so style change re-maximises cleanly
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        await _vm.ShutdownAsync();
    }
}
