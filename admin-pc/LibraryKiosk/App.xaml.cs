using System;
using System.Windows;
using System.Windows.Threading;
using LibraryKiosk.Utils;

namespace LibraryKiosk;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
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
