using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LibraryAdminPC.Converters;

/// <summary>
/// Task C2 (View-side, additive): maps a category name to a stable color so the
/// books table cover-chip and category badge are colored consistently per category.
/// ConverterParameter "tint" returns a soft (mostly-white) version for badge
/// backgrounds; default returns the solid color (chip / badge text).
/// The index uses a deterministic char-sum hash (NOT string.GetHashCode, which is
/// randomized per process in .NET Core) so colors are stable across runs.
/// </summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly string[] Palette =
        { "#1F5AA8", "#2E9C8E", "#E0922E", "#E2685A", "#5A5BB8", "#0E9F8E", "#C2557A", "#5566C7" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat = (value as string ?? "").Trim();
        var idx = StableIndex(cat, Palette.Length);
        var solid = (Color)ColorConverter.ConvertFromString(Palette[idx]);

        if ((parameter as string) == "tint")
            return Frozen(Mix(solid, Colors.White, 0.86));

        return Frozen(solid);
    }

    private static int StableIndex(string s, int mod)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        unchecked
        {
            int h = 0;
            foreach (var c in s) h = (h * 31 + c) & 0x7FFFFFFF;
            return h % mod;
        }
    }

    private static Color Mix(Color a, Color b, double tBtoWeight)
    {
        // tBtoWeight = how much of b (white). 0 => a, 1 => b.
        double t = Math.Clamp(tBtoWeight, 0, 1);
        byte ch(byte ca, byte cb) => (byte)Math.Round(ca * (1 - t) + cb * t);
        return Color.FromRgb(ch(a.R, b.R), ch(a.G, b.G), ch(a.B, b.B));
    }

    private static Brush Frozen(Color c)
    {
        var br = new SolidColorBrush(c);
        br.Freeze();
        return br;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
