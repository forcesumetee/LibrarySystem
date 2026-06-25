namespace LibraryKiosk.Models;

/// <summary>
/// Persisted kiosk configuration (%ProgramData%\LibrarySystem\kiosk-settings.json).
/// The full schema is declared now so later phases (PIN, scaling, display mode)
/// have a stable shape to read/write; Phase 2 only reads/writes <see cref="BaseUrl"/>.
/// </summary>
public sealed class KioskSettings
{
    /// <summary>Library API server base URL (HTTP + SignalR origin).</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    // --- Admin PIN (Phase 5). PBKDF2-HMAC-SHA256, 256-bit, base64 salt/hash. ---
    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }
    public int PinIterations { get; set; } = 120_000;

    /// <summary>Consecutive failed PIN attempts (Phase 5 lockout).</summary>
    public int FailCount { get; set; }

    /// <summary>Unix epoch seconds until which PIN entry is locked (0 = unlocked).</summary>
    public long LockUntil { get; set; }

    // --- Display (Phase 6). ---
    /// <summary>Extra UI scale multiplier applied on top of the Viewbox fit.</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>"fullscreen" (kiosk) or "windowed" (dev).</summary>
    public string DisplayMode { get; set; } = "fullscreen";

    public const string DefaultBaseUrl = "http://192.168.1.105:5269";
}
