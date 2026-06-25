using System;
using System.Windows;
using System.Windows.Threading;
using LibraryAdminPC.Utils;

namespace LibraryAdminPC;

public partial class App : Application
{
    public App()
    {
        CrashLogger.InitGlobalHandlers();

        DispatcherUnhandledException += (_, e) =>
        {
            CrashLogger.Log("DispatcherUnhandledException", e.Exception);
            MessageBox.Show(
                "Application crashed.\n\nLog: " + CrashLogger.GetLogPath() + "\n\n" + e.Exception.Message,
                "Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            e.Handled = true;
            Shutdown(-1);
        };
    }
}