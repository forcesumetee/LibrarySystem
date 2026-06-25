using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LibraryKiosk.Utils;

/// <summary>true -> Collapsed, false -> Visible (the inverse of the built-in converter).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Logical NOT for booleans (e.g. disable a button while busy).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !(value is bool b && b);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => !(value is bool b && b);
}

/// <summary>Non-empty string -> Visible, null/empty -> Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
