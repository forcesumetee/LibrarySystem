using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LibraryKiosk.Utils;

namespace LibraryKiosk;

public partial class App : Application
{
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
