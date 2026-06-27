using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LibraryKiosk.Services;
using LibraryKiosk.Utils;

namespace LibraryKiosk;

public partial class App : Application
{
    /// <summary>Apply the saved per-kiosk primary theme colour to the shared brushes BEFORE
    /// the main window is built (StartupUri), so it renders themed with no flash of default
    /// blue. Never throws — a bad/missing colour falls back to the default.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            var settings = new SettingsService().Load();
            ThemeService.Apply(settings.PrimaryColor);
        }
        catch (Exception ex)
        {
            KioskLog.Error("Failed to apply theme colour at startup; using default.", ex);
        }
        base.OnStartup(e); // processes StartupUri -> creates MainWindow (now themed)
    }

    public App()
    {
        // A kiosk must never crash to the desktop in front of a user. Cover all three
        // unhandled-exception channels: the UI dispatcher, background threads, and
        // faulted Tasks (e.g. fire-and-forget SignalR work).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        KioskLog.Error("Unobserved task exception", e.Exception);
        e.SetObserved(); // swallow so the process is not torn down
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        KioskLog.Error("Unhandled UI exception", e.Exception);
        // Keep the kiosk alive rather than crashing on a non-fatal UI error.
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            KioskLog.Error("Unhandled domain exception", ex);
    }
}
