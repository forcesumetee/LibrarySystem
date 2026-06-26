using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using LibraryAdminPC.Models;

namespace LibraryAdminPC.ViewModels;

// View-row wrapper for the books table. Exposes the BookDto fields (so existing
// column bindings keep working) plus an async cover Thumbnail that the view
// lazy-loads for the current page and caches per regNo. When Thumbnail is null the
// table cell falls back to the category color chip.
public sealed class BookRowVm : INotifyPropertyChanged
{
    public BookDto Book { get; }

    public BookRowVm(BookDto book) => Book = book;

    public string RegNo => Book.RegNo;
    public string Title => Book.Title;
    public string Category => Book.Category;
    public string Publisher => Book.Publisher;
    public string Shelf => Book.Shelf;

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
