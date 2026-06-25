using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LibraryAdminPC.Models;
using LibraryAdminPC.Services;
using Microsoft.Win32;

namespace LibraryAdminPC.Views;

public partial class BooksView : UserControl
{
    private readonly ApiClient _api;

    private List<BookDto> _all = new();
    private readonly ObservableCollection<BookDto> _filtered = new();

    private CancellationTokenSource? _debounceCts;

    public BooksView(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        BooksGrid.ItemsSource = _filtered;

        Loaded += async (_, __) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            TxtCoverStatus.Text = "กำลังโหลดข้อมูล...";
            _all = await _api.GetBooksAsync();
            BuildCategory();
            ApplyFilter();
            TxtCoverStatus.Text = $"โหลดแล้ว {_all.Count} รายการ";
        }
        catch (Exception ex)
        {
            TxtCoverStatus.Text = $"โหลดไม่สำเร็จ: {ex.Message}";
        }
    }

    private void BuildCategory()
    {
        var cats = _all
            .Select(x => (x.Category ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        cats.Insert(0, "ทั้งหมด");

        CmbCategory.ItemsSource = cats;

        if (CmbCategory.SelectedItem == null)
            CmbCategory.SelectedItem = "ทั้งหมด";
    }

    private void ApplyFilter()
    {
        var q = (TxtSearch.Text ?? "").Trim();
        var cat = (CmbCategory.SelectedItem?.ToString() ?? "ทั้งหมด").Trim();

        IEnumerable<BookDto> query = _all;

        if (!string.IsNullOrWhiteSpace(cat) && cat != "ทั้งหมด")
            query = query.Where(b => string.Equals((b.Category ?? "").Trim(), cat, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(b =>
                (b.RegNo ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (b.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (b.Category ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (b.Publisher ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (b.Shelf ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            );
        }

        var list = query.ToList();

        _filtered.Clear();
        foreach (var item in list) _filtered.Add(item);
    }

    private void DebounceFilter()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);
                if (token.IsCancellationRequested) return;

                await Dispatcher.InvokeAsync(ApplyFilter);
            }
            catch { }
        }, token);
    }

    // -------- Event handlers ที่ XAML อ้างถึง --------
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => DebounceFilter();

    private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ถ้าเพิ่ง build category จะ fire ครั้งแรก
        ApplyFilter();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
        await LoadSelectedCoverPreviewAsync();
    }

    private async void BooksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadSelectedCoverPreviewAsync();
    }

    private BookDto? SelectedBook => BooksGrid.SelectedItem as BookDto;

    private async Task LoadSelectedCoverPreviewAsync()
    {
        var b = SelectedBook;
        if (b == null)
        {
            TxtSelectedReg.Text = "ยังไม่ได้เลือกหนังสือ";
            BtnUploadCover.IsEnabled = false;
            BtnDeleteCover.IsEnabled = false;
            ImgCover.Source = null;
            TxtNoCover.Visibility = Visibility.Visible;
            return;
        }

        TxtSelectedReg.Text = $"รหัส: {b.RegNo}";
        BtnUploadCover.IsEnabled = true;
        BtnDeleteCover.IsEnabled = true;

        try
        {
            TxtCoverStatus.Text = "กำลังโหลดรูปปกจาก Server...";
            var bytes = await _api.GetBookCoverBytesAsync(b.RegNo);

            if (bytes == null || bytes.Length == 0)
            {
                ImgCover.Source = null;
                TxtNoCover.Visibility = Visibility.Visible;
                TxtCoverStatus.Text = "ยังไม่มีรูปปกใน Server";
                return;
            }

            ImgCover.Source = BytesToBitmap(bytes);
            TxtNoCover.Visibility = Visibility.Collapsed;
            TxtCoverStatus.Text = "แสดงรูปปกจาก Server";
        }
        catch (Exception ex)
        {
            ImgCover.Source = null;
            TxtNoCover.Visibility = Visibility.Visible;
            TxtCoverStatus.Text = $"โหลดรูปปกไม่สำเร็จ: {ex.Message}";
        }
    }

    private static BitmapImage BytesToBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private async void BtnUploadCover_Click(object sender, RoutedEventArgs e)
    {
        var b = SelectedBook;
        if (b == null) return;

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
            await _api.UploadBookCoverAsync(b.RegNo, dlg.FileName);

            TxtCoverStatus.Text = "อัปโหลดสำเร็จ";
            await LoadSelectedCoverPreviewAsync();
        }
        catch (Exception ex)
        {
            TxtCoverStatus.Text = $"อัปโหลดไม่สำเร็จ: {ex.Message}";
        }
    }

    private async void BtnDeleteCover_Click(object sender, RoutedEventArgs e)
    {
        var b = SelectedBook;
        if (b == null) return;

        if (MessageBox.Show($"ต้องการล้างรูปปกของ {b.RegNo} ใช่หรือไม่?",
                "ยืนยัน",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            TxtCoverStatus.Text = "กำลังลบรูปปก...";
            await _api.DeleteBookCoverAsync(b.RegNo);

            TxtCoverStatus.Text = "ลบรูปปกแล้ว";
            await LoadSelectedCoverPreviewAsync();
        }
        catch (Exception ex)
        {
            TxtCoverStatus.Text = $"ลบไม่สำเร็จ: {ex.Message}";
        }
    }
}