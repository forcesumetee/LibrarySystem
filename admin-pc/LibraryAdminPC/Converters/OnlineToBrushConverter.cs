using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LibraryAdminPC.Converters;

/// <summary>
/// Task B / B6 (View-side, additive): maps the shell StatusText string to a brush
/// for the top-bar "Server" status pill (green when online, red otherwise). This is
/// pure presentation — it reads the existing StatusText binding and creates no new
/// state/logic. StatusText is exactly "ออนไลน์" only when the connection test passed;
/// every other value ("ออฟไลน์", "ออฟไลน์ (...)", "ตั้งค่าไม่ถูกต้อง: ...") is treated as offline.
/// </summary>
public sealed class OnlineToBrushConverter : IValueConverter
{
    private static readonly Brush Online = Freeze("#2E9C7E");   // SuccessColor
    private static readonly Brush Offline = Freeze("#D2544A");  // ErrorColor

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        return s.Contains("ออนไลน์") ? Online : Offline;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
