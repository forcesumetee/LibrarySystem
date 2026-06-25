using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LibraryAdminPC.Converters;

/// <summary>
/// Task B / B5 (View-side, additive): turns a local file path string into a
/// BitmapImage loaded with CacheOption.OnLoad + frozen, so the source file is
/// NOT locked on disk (the user can re-pick / overwrite it). Empty/missing path
/// returns null so the bound Image shows nothing and the placeholder underneath
/// remains visible.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = (value as string ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;          // load fully, don't keep the file open
            img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
