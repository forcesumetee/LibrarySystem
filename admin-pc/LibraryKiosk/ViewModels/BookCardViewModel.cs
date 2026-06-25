using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LibraryShared;

namespace LibraryKiosk.ViewModels;

/// <summary>
/// One book in the kiosk grid / detail modal. Wraps a <see cref="BookDto"/> with
/// display-ready fallbacks, a deterministic placeholder colour (used when the
/// cover is missing, matching the mockup's coloured cards) and a lazily-loaded
/// frozen cover image.
/// </summary>
public partial class BookCardViewModel : ObservableObject
{
    // Card cover palette from the mockup.
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x1F, 0x5A, 0xA8), Color.FromRgb(0x2E, 0x9C, 0x8E),
        Color.FromRgb(0xE0, 0x92, 0x2E), Color.FromRgb(0x5A, 0x5B, 0xB8),
        Color.FromRgb(0x1E, 0x84, 0x75), Color.FromRgb(0xE2, 0x68, 0x5A),
        Color.FromRgb(0x3B, 0x6F, 0xB0), Color.FromRgb(0x56, 0x62, 0x73),
    };

    public BookDto Book { get; }

    public string RegNo => Book.RegNo ?? "";
    public string Title => string.IsNullOrWhiteSpace(Book.Title) ? "(ไม่มีชื่อ)" : Book.Title.Trim();
    public string Category => string.IsNullOrWhiteSpace(Book.Category) ? "ไม่ระบุหมวด" : Book.Category.Trim();
    public string Shelf => (Book.Shelf ?? "").Trim();
    public string Publisher => (Book.Publisher ?? "").Trim();

    public bool HasShelf => Shelf.Length > 0;
    public bool HasPublisher => Publisher.Length > 0;

    /// <summary>Deterministic placeholder fill, stable per regNo.</summary>
    public Brush PlaceholderBrush { get; }

    [ObservableProperty] private BitmapSource? _cover;
    [ObservableProperty] private bool _hasCover;

    public BookCardViewModel(BookDto book)
    {
        Book = book;
        var key = RegNo.Length > 0 ? RegNo : Title;
        var sum = 0;
        foreach (var c in key) sum += c;
        var brush = new SolidColorBrush(Palette[(sum & 0x7fffffff) % Palette.Length]);
        brush.Freeze();
        PlaceholderBrush = brush;
    }

    public void SetCover(BitmapSource image)
    {
        Cover = image;
        HasCover = true;
    }
}
