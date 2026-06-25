using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LibraryAdminPC.Utils;

public static class CrashLogger
{
    private static readonly object _gate = new();

    private static string LogDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LibrarySystem",
            "logs"
        );

    private static string LogPath => Path.Combine(LogDir, "adminpc_crash.log");

    public static void InitGlobalHandlers()
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                        Log("AppDomain.UnhandledException", ex);
                    else
                        Log("AppDomain.UnhandledException", "Non-Exception object: " + (e.ExceptionObject?.ToString() ?? "(null)"));
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    Log("TaskScheduler.UnobservedTaskException", e.Exception);
                    e.SetObserved();
                }
                catch { }
            };
        }
        catch
        {
            // ignore
        }
    }

    public static void Log(string title, Exception ex)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogDir);

                var sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine(title);
                sb.AppendLine(ex.ToString());
                sb.AppendLine("==================================================");
                sb.AppendLine();

                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void Log(string title, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogDir);

                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}\n{message}\n\n",
                    Encoding.UTF8
                );
            }
        }
        catch
        {
            // ignore
        }
    }

    public static string GetLogPath() => LogPath;
}