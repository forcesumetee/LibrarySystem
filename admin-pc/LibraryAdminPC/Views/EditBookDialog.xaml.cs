using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using LibraryAdminPC.Models;
using LibraryAdminPC.Services;
using Microsoft.Win32;

namespace LibraryAdminPC.Views;

// C2 (+UX): edit one book (title/category/publisher/shelf) AND manage its cover.
// RegNo is the identity and is read-only. The title PUT happens on save (via the
// caller). Cover ops (change/clear) apply immediately to the server using the
// existing ApiClient cover methods; CoverChanged tells the caller to refresh.
public partial class EditBookDialog : Window
{
    private readonly ApiClient _api;

    public string RegNo { get; }
    public BookDto? Result { get; private set; }
    public bool CoverChanged { get; private set; }

    public EditBookDialog(BookDto book, ApiClient api)
    {
        InitializeComponent();
        _api = api;
        RegNo = book.RegNo;
        TxtRegNoHint.Text = $"รหัส: {book.RegNo}";
        TxtTitle.Text = book.Title ?? "";
        TxtCategory.Text = book.Category ?? "";
        TxtPublisher.Text = book.Publisher ?? "";
        TxtShelf.Text = book.Shelf ?? "";

        Loaded += async (_, __) => await LoadCoverAsync();
    }

    private async System.Threading.Tasks.Task LoadCoverAsync()
    {
        try
        {
            TxtCoverStatus.Text = "กำลังโหลดรูปปก...";
            var bytes = await _api.GetBookCoverBytesAsync(RegNo);
            if (bytes == null || bytes.Length == 0)
            {
                ImgCover.Source = null;
                TxtNoCover.Visibility = Visibility.Visible;
                TxtCoverStatus.Text = "ยังไม่มีรูปปก";
                return;
            }
            ImgCover.Source = BytesToBitmap(bytes);
            TxtNoCover.Visibility = Visibility.Collapsed;
            TxtCoverStatus.Text = "รูปปกจาก Server";
        }
        catch (Exception ex)
        {
            ImgCover.Source = null;
            TxtNoCover.Visibility = Visibility.Visible;
            TxtCoverStatus.Text = $"โหลดปกไม่สำเร็จ: {ex.Message}";
        }
    }

    private async void BtnChangeCover_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "เลือกไฟล์รูปปก",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp|All Files|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            TxtCoverStatus.Text = "กำลังอัปโหลดรูปปก...";
            await _api.UploadBookCoverAsync(RegNo, dlg.FileName);
            CoverChanged = true;
            await LoadCoverAsync();
        }
        catch (Exception ex)
        {
            TxtCoverStatus.Text = $"อัปโหลดไม่สำเร็จ: {ex.Message}";
        }
    }

    private async void BtnClearCover_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Ask(this, "ล้างรูปปก", $"ต้องการล้างรูปปกของ {RegNo} ใช่หรือไม่?", "ล้างรูปปก"))
            return;

        try
        {
            TxtCoverStatus.Text = "กำลังลบรูปปก...";
            await _api.DeleteBookCoverAsync(RegNo);
            CoverChanged = true;
            ImgCover.Source = null;
            TxtNoCover.Visibility = Visibility.Visible;
            TxtCoverStatus.Text = "ลบรูปปกแล้ว";
        }
        catch (Exception ex)
        {
            TxtCoverStatus.Text = $"ลบไม่สำเร็จ: {ex.Message}";
        }
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

    private static BitmapImage BytesToBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;   // load fully now; stream can be disposed
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
