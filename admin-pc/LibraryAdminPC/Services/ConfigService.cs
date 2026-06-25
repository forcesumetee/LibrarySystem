using System;
using System.IO;
using System.Text.Json;
using LibraryAdminPC.Models;

namespace LibraryAdminPC.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string _brandingDir;

    public ConfigService()
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LibraryAdminPC"
        );

        _configPath = Path.Combine(_configDir, "appsettings.json");
        _brandingDir = Path.Combine(_configDir, "branding");
    }

    public string ConfigPath => _configPath;
    public string BrandingDir => _brandingDir;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                var cfg = new AppConfig();
                Save(cfg);
                return cfg;
            }

            var json = File.ReadAllText(_configPath);
            var cfgLoaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return cfgLoaded ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_configDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public string GetBrandingFilePath(string fileName)
    {
        Directory.CreateDirectory(_brandingDir);
        return Path.Combine(_brandingDir, fileName);
    }

    // copy and return saved filename (logo.xxx / background.xxx)
    public string SaveBrandingFile(string sourcePath, string kind)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Branding file not found.", sourcePath);

        Directory.CreateDirectory(_brandingDir);

        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

        var targetName = kind == "logo" ? $"logo{ext}" : $"background{ext}";
        var targetPath = Path.Combine(_brandingDir, targetName);

        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetName;
    }
}