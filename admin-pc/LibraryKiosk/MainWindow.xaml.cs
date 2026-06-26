using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibraryKiosk.Services;
using LibraryKiosk.ViewModels;

namespace LibraryKiosk;

public partial class MainWindow : Window
{
    private readonly HomeViewModel _vm;
    private readonly DispatcherTimer _idleTimer;

    private bool _isLockdown;
    private bool _allowExit;
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new HomeViewModel(new SettingsService());
        DataContext = _vm;

        _vm.DisplayModeChangeRequested += ApplyDisplayMode;
        _vm.ExitRequested += OnExitRequested;
        _vm.ScrollResetRequested += OnScrollResetRequested;

        // Idle reset: any user input restarts the timer; firing returns the kiosk to a
        // clean browse state for the next person. Active in fullscreen (kiosk) only.
        _idleTimer = new DispatcherTimer();
        _idleTimer.Tick += OnIdleTick;
        PreviewMouseDown += (_, _) => RestartIdle();
        PreviewMouseWheel += (_, _) => RestartIdle();
        PreviewKeyDown += (_, _) => RestartIdle();
        PreviewTouchDown += (_, _) => RestartIdle();
        PreviewStylusDown += (_, _) => RestartIdle();

        // Apply the persisted display mode BEFORE the window is shown, so a fullscreen
        // kiosk is created borderless from the start. Doing this after Show (in Loaded)
        // would change WindowStyle on a live HWND, which recreates it and re-raises
        // Loaded — a storm that destabilises the window.
        ApplyDisplayMode(_vm.DisplayMode);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Loaded can be raised more than once (e.g. on resize); only start once.
        if (_started) return;
        _started = true;
        await _vm.StartAsync();
    }

    /// <summary>
    /// Fullscreen = locked-down kiosk: borderless, maximised, always-on-top, exit
    /// blocked (Alt+F4 cancelled) — the only way out is the PIN-gated "ปิดโปรแกรม"
    /// button. Windowed = dev: normal chrome, freely closable, no idle reset.
    /// </summary>
    private void ApplyDisplayMode(string mode)
    {
        if (string.Equals(mode, "windowed", StringComparison.OrdinalIgnoreCase))
        {
            _isLockdown = false;
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            // Match the design-canvas aspect so the Viewbox isn't heavily letterboxed.
            var landscape = _vm.IsLandscape;
            Width = landscape ? 960 : 540;
            Height = landscape ? 540 : 960;
        }
        else
        {
            _isLockdown = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;
        }

        RestartIdle();
    }

    // ---------------- idle reset ----------------

    private void RestartIdle()
    {
        _idleTimer.Stop();
        var seconds = _vm.IdleResetSeconds;
        if (_isLockdown && seconds > 0)
        {
            _idleTimer.Interval = TimeSpan.FromSeconds(seconds);
            _idleTimer.Start();
        }
    }

    private void OnIdleTick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        // Idle → reset the grid for the next user and raise the Welcome overlay.
        _vm.ShowWelcome();
    }

    // ---------------- welcome overlay ----------------

    /// <summary>Tap anywhere on the Welcome backdrop → enter the (already reset) grid.</summary>
    private void Welcome_Dismiss(object sender, InputEventArgs e) => _vm.DismissWelcome();

    /// <summary>Tap the Welcome search box → enter the grid and focus the real search field
    /// so the user can type immediately.</summary>
    private void Welcome_TapSearch(object sender, InputEventArgs e)
    {
        e.Handled = true;
        _vm.DismissWelcome();
        // Let the dismiss/layout settle, then focus the active grid's search box
        // (portrait vs landscape root-swap).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var box = _vm.IsLandscape ? SearchBoxL : SearchBox;
            box.Focus();
            Keyboard.Focus(box);
        }), DispatcherPriority.Input);
    }

    private void OnScrollResetRequested(object? sender, EventArgs e)
    {
        // Scroll whichever grid is currently shown (portrait vs landscape root-swap).
        DependencyObject host = _vm.IsLandscape ? CardsHostL : CardsHost;
        var sv = FindVisualChild<ScrollViewer>(host);
        sv?.ScrollToTop();
    }

    // ---------------- exit / lockdown ----------------

    private void OnExitRequested(object? sender, EventArgs e)
    {
        _allowExit = true;
        Close();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        // In kiosk mode, swallow Alt+F4 / system-close; exit only via the PIN gate.
        if (_isLockdown && !_allowExit)
        {
            e.Cancel = true;
            return;
        }
        await _vm.ShutdownAsync();
    }

    // ---------------- helpers ----------------

    private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root == null) return null;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
