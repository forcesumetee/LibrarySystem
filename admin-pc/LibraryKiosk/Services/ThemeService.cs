using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LibraryKiosk.Services;

/// <summary>
/// Applies the per-kiosk primary theme colour at runtime. The whole UI references a handful
/// of shared brushes (PrimaryBrush/PrimaryDarkBrush/PrimaryTintBrush/OnPrimaryBrush) plus the
/// branding-fallback gradient; mutating those brush instances' <c>Color</c> updates every
/// element live (they are not frozen). Derived shades (dark/tint) and the on-primary text
/// colour are computed from the chosen primary so the whole set stays consistent and readable.
///
/// Theme-only: touches nothing but these visual brushes.
/// </summary>
public static class ThemeService
{
    public const string DefaultPrimaryHex = "#1F5AA8";

    // ---------- hex parsing ----------

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length != 6) return false;
        try
        {
            var r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            color = Color.FromRgb(r, g, b);
            return true;
        }
        catch { return false; }
    }

    public static Color ParseOrDefault(string? hex)
    {
        if (TryParseHex(hex, out var c)) return c;
        TryParseHex(DefaultPrimaryHex, out var d);
        return d;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ---------- apply ----------

    /// <summary>Apply a primary colour (hex; invalid/empty falls back to the default) to the
    /// shared theme brushes + the branding-fallback gradient's primary stop.</summary>
    public static void Apply(string? hex)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        var primary = ParseOrDefault(hex);
        SetBrushColor(res, "PrimaryBrush", primary);
        SetBrushColor(res, "PrimaryDarkBrush", ScaleLightness(primary, 0.78)); // ~22% darker -> pressed
        SetBrushColor(res, "PrimaryTintBrush", WithLightness(primary, 0.95));  // pale fill
        SetBrushColor(res, "OnPrimaryBrush", OnColor(primary));                // readable text on primary

        // Branding-absent fallback gradient: keep navy(start)/teal(end), retint the middle
        // (primary) stop so the idle background follows the theme (no stray blue). The XAML
        // resource may be frozen, so REPLACE it with a fresh brush (Rectangles reference it
        // via DynamicResource, so they pick up the new instance live).
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x13, 0x29, 0x4D), 0.0));   // navy
        gradient.GradientStops.Add(new GradientStop(primary, 0.52));                          // primary
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x8B, 0x86), 1.0));   // teal
        res["BackgroundGradientBrush"] = gradient;
    }

    private static void SetBrushColor(ResourceDictionary res, string key, Color c)
    {
        if (res[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
        else res[key] = new SolidColorBrush(c);
    }

    // ---------- colour math ----------

    /// <summary>Pick white or near-black for text on <paramref name="bg"/> by WCAG contrast.</summary>
    private static Color OnColor(Color bg)
    {
        var l = RelLuminance(bg);
        var contrastWhite = 1.05 / (l + 0.05);
        var contrastBlack = (l + 0.05) / 0.05;
        return contrastWhite >= contrastBlack ? Colors.White : Color.FromRgb(0x1A, 0x25, 0x32);
    }

    private static double RelLuminance(Color c)
    {
        double R = Lin(c.R / 255.0), G = Lin(c.G / 255.0), B = Lin(c.B / 255.0);
        return 0.2126 * R + 0.7152 * G + 0.0722 * B;
    }

    private static double Lin(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    /// <summary>Multiply HSL lightness by a factor (keep hue/sat) — used for the darker shade.</summary>
    private static Color ScaleLightness(Color c, double factor)
    {
        RgbToHsl(c, out var h, out var s, out var l);
        return HslToRgb(h, s, Clamp01(l * factor));
    }

    /// <summary>Set HSL lightness to an absolute value (keep hue/sat) — used for the pale tint.</summary>
    private static Color WithLightness(Color c, double lightness)
    {
        RgbToHsl(c, out var h, out var s, out _);
        return HslToRgb(h, s, Clamp01(lightness));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-9) { h = 0; s = 0; return; }
        var d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6.0;
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s <= 1e-9) { r = g = b = l; }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }
        return Color.FromRgb(To255(r), To255(g), To255(b));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    private static byte To255(double v) => (byte)Math.Round(Clamp01(v) * 255.0);
}
