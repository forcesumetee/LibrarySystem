using System.Windows;
using LibraryAdminPC.Models;

namespace LibraryAdminPC.Views;

// C2 additive: small modal to edit one book (title/category/publisher/shelf).
// RegNo is the identity and is shown read-only. On save it returns a BookDto via
// the Result property; the caller sends it to PUT /api/admin/books/{regNo}.
public partial class EditBookDialog : Window
{
    public string RegNo { get; }
    public BookDto? Result { get; private set; }

    public EditBookDialog(BookDto book)
    {
        InitializeComponent();
        RegNo = book.RegNo;
        TxtRegNoHint.Text = $"รหัส: {book.RegNo}";
        TxtTitle.Text = book.Title ?? "";
        TxtCategory.Text = book.Category ?? "";
        TxtPublisher.Text = book.Publisher ?? "";
        TxtShelf.Text = book.Shelf ?? "";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var title = (TxtTitle.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            TxtError.Text = "กรุณากรอกชื่อหนังสือ";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        Result = new BookDto
        {
            RegNo = RegNo,
            Title = title,
            Category = (TxtCategory.Text ?? "").Trim(),
            Publisher = (TxtPublisher.Text ?? "").Trim(),
            Shelf = (TxtShelf.Text ?? "").Trim()
        };
        DialogResult = true;
    }
}
