using System;
using System.Diagnostics;
using System.IO;

namespace LibraryKiosk.Utils;

/// <summary>
/// Best-effort diagnostic logging. Writes to the debug output and appends to
/// %ProgramData%\LibrarySystem\logs\kiosk.log. Never throws — logging must not
/// be able to crash the kiosk.
/// </summary>
public static class KioskLog
{
    private static readonly object Gate = new();

    private static string LogFilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LibrarySystem", "logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "kiosk.log");
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging is best-effort; swallow any IO error.
        }
    }
}
