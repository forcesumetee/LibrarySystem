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

    /// <summary>Stable per-kiosk id (GUID, generated once) so the server can count
    /// distinct kiosks regardless of reconnects. Sent on the hub connect query (K3).</summary>
    public string? KioskId { get; set; }

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

    // --- Display name override (K2 item 5). Local per-kiosk system name; when
    //     null/blank the header falls back to the server's displayName (/api/config),
    //     i.e. the original behaviour. Never sent to the server. ---
    public string? SystemName { get; set; }

    // --- Design-canvas resolution (K2 item 4). The UI is laid out on a WxH canvas
    //     that a Uniform Viewbox fits to the screen. Default 1080x1920 (portrait).
    //     Portrait/custom-portrait only for now; landscape (W > H) is rejected until
    //     the orientation redesign is decided. ---
    public int CanvasWidth { get; set; } = 1080;
    public int CanvasHeight { get; set; } = 1920;

    // --- Local branding overrides (Phase 5). Hide the server logo/background on
    //     THIS kiosk only; the server is never touched. Mirrors the legacy Android
    //     app's "remove image (this device)" option. ---
    public bool HideLogo { get; set; }
    public bool HideBackground { get; set; }

    /// <summary>Opacity of the branding background image on THIS kiosk only (0.2–1.0).
    /// Lets the operator fade a busy background so the cards/text read more clearly.
    /// Default 1.0 = current behaviour; older settings files without this field load
    /// as 1.0 (System.Text.Json keeps the property initializer when the key is absent).</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    // --- Kiosk lockdown / polish (Phase 6). ---
    /// <summary>Launch on Windows sign-in (HKCU Run key). Toggled from Settings.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Idle seconds before the browse view resets for the next user (fullscreen only). 0 disables.</summary>
    public int IdleResetSeconds { get; set; } = 180;

    public const string DefaultBaseUrl = "http://192.168.1.105:5269";
}
