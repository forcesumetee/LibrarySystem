using System;
using System.IO;
using System.Windows.Media.Imaging;
using LibraryKiosk.Utils;

namespace LibraryKiosk.Services;

/// <summary>
/// Decodes image bytes into a frozen <see cref="BitmapImage"/>.
///
/// Critical for kiosks: WPF will cache decoded images keyed by URI and serve a
/// stale frame even after the source changes. We therefore (a) always fetch
/// bytes ourselves (cache-busted), and (b) decode from a MemoryStream with
/// CacheOption=OnLoad + CreateOptions=IgnoreImageCache so nothing is reused.
/// The result is Frozen so it can be handed to the UI thread from a worker.
/// </summary>
public static class ImageLoader
{
    /// <summary>Decode bytes to a frozen bitmap, or null on bad/empty data.</summary>
    public static BitmapSource? FromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            // BitmapDecoder.Create with OnLoad + IgnoreImageCache reads & decodes the
            // whole frame immediately and is reliable on a background thread (unlike
            // BitmapImage.StreamSource, which can throw odd errors off the UI thread).
            using var ms = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(
                ms,
                BitmapCreateOptions.IgnoreImageCache,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames[0];
            if (frame.CanFreeze) frame.Freeze();   // cross-thread safe
            return frame;
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"Image decode failed: {ex.Message}");
            return null;
        }
    }
}
