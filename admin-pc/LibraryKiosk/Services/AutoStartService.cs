using System;
using Microsoft.Win32;
using LibraryKiosk.Utils;

namespace LibraryKiosk.Services;

/// <summary>
/// Optional "launch on Windows sign-in" via the per-user Run key
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). Per-user needs no admin
/// rights. This is only a convenience toggle — a true locked-down kiosk should use
/// Windows Assigned Access / Shell Launcher (see README), which is configured on the
/// device, not in code. All operations are best-effort and never throw.
/// </summary>
public sealed class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LibraryKiosk";

    private readonly string _exePath;

    public AutoStartService(string exePath) => _exePath = exePath;

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var val = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(val);
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"AutoStart read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Enable or disable; returns the resulting state (unchanged on failure).</summary>
    public bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return IsEnabled();

            if (enabled)
                key.SetValue(ValueName, $"\"{_exePath}\"");
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            KioskLog.Info($"AutoStart set to {enabled}.");
            return enabled;
        }
        catch (Exception ex)
        {
            KioskLog.Error("AutoStart write failed.", ex);
            return IsEnabled();
        }
    }
}
