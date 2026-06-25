using System;
using System.IO;
using System.Text.Json;
using LibraryKiosk.Models;
using LibraryKiosk.Utils;

namespace LibraryKiosk.Services;

/// <summary>
/// Reads/writes <see cref="KioskSettings"/> as JSON at
/// %ProgramData%\LibrarySystem\kiosk-settings.json.
/// Writes are atomic (temp file + rename) so a crash mid-write cannot corrupt
/// the live settings. Reads never crash: a missing file is created with
/// defaults, and a corrupt file falls back to defaults (logged).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _dir;
    private readonly string _path;
    private readonly object _gate = new();

    public SettingsService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LibrarySystem");
        _path = Path.Combine(_dir, "kiosk-settings.json");
    }

    public string SettingsPath => _path;

    public KioskSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    var fresh = new KioskSettings();
                    SaveInternal(fresh);
                    KioskLog.Info($"Settings file not found; created defaults at {_path}");
                    return fresh;
                }

                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<KioskSettings>(json, JsonOptions);
                if (loaded == null)
                {
                    KioskLog.Warn("Settings deserialized to null; using defaults.");
                    return new KioskSettings();
                }
                return loaded;
            }
            catch (Exception ex)
            {
                // Corrupt/locked file — never crash the kiosk over settings.
                KioskLog.Error("Failed to load settings; falling back to defaults.", ex);
                return new KioskSettings();
            }
        }
    }

    public void Save(KioskSettings settings)
    {
        lock (_gate)
        {
            SaveInternal(settings);
        }
    }

    private void SaveInternal(KioskSettings settings)
    {
        Directory.CreateDirectory(_dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        // Atomic write: write to a temp file in the same directory, then replace.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);

        if (File.Exists(_path))
        {
            // File.Replace is atomic on NTFS and preserves the destination on failure.
            File.Replace(tmp, _path, null);
        }
        else
        {
            File.Move(tmp, _path);
        }
    }
}
