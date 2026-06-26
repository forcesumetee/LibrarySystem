using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

    // C2 additive: client-side pagination. _filtered keeps the full filtered set
    // (existing search/category logic is untouched); the grid is bound to _paged,
    // a single page sliced out of _filtered. Whenever _filtered changes (a filter
    // pass) we reset to page 1 and re-slice.
    private const int PageSize = 8;
    private readonly ObservableCollection<BookDto> _paged = new();
    private int _currentPage = 1;
    private bool _repageScheduled;

    // C2 additive: pending cover for the "add book" form.
    private string? _newCoverPath;

    public BooksView(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        // C2: grid shows the current page (sliced from _filtered).
        BooksGrid.ItemsSource = _paged;
        _filtered.CollectionChanged += OnFilteredCollectionChanged;

        Loaded += async (_, __) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            TxtStatus.Text = "กำลังโหลดข้อมูล...";
            _all = await _api.GetBooksAsync();
            BuildCategory();
            ApplyFilter();
            TxtStatus.Text = "";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"โหลดไม่สำเร็จ: {ex.Message}";
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
    }

    // ============================================================
    // C2 ADDITIVE — pagination, add-book form, per-row edit/delete.
    // Everything above is unchanged.
    // ============================================================

    // ---- pagination ----
    private void OnFilteredCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ApplyFilter does Clear()+Add() (many events); coalesce to one repage.
        if (_repageScheduled) return;
        _repageScheduled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _repageScheduled = false;
            _currentPage = 1;
            RebuildPage();
        }), DispatcherPriority.Background);
    }

    private void RebuildPage()
    {
        var total = _filtered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (_currentPage > totalPages) _currentPage = totalPages;
        if (_currentPage < 1) _currentPage = 1;

        var start = (_currentPage - 1) * PageSize;
        var end = Math.Min(start + PageSize, total);

        _paged.Clear();
        for (int i = start; i < end; i++) _paged.Add(_filtered[i]);

        var from = total == 0 ? 0 : start + 1;
        TxtPageInfo.Text = $"แสดง {from}–{end} จาก {total}";
        TxtPageNum.Text = $"หน้า {_currentPage}/{totalPages}";
        BtnPrevPage.IsEnabled = _currentPage > 1;
        BtnNextPage.IsEnabled = _currentPage < totalPages;

        // empty state when there are no results (layout stays stable via MinHeight)
        TxtEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        RebuildPage();
    }

    private void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        if (_currentPage >= totalPages) return;
        _currentPage++;
        RebuildPage();
    }

    // ---- add book (via existing import-file endpoint, 1-row CSV in memory) ----
    private void BtnPickNewCover_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "เลือกไฟล์รูปปก",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp|All Files|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;
        _newCoverPath = dlg.FileName;
        TxtNewCoverName.Text = Path.GetFileName(dlg.FileName);
    }

    private void BtnClearAddForm_Click(object sender, RoutedEventArgs e) => ClearAddForm();

    private void ClearAddForm()
    {
        TxtNewRegNo.Text = "";
        TxtNewTitle.Text = "";
        TxtNewCategory.Text = "";
        TxtNewPublisher.Text = "";
        TxtNewShelf.Text = "";
        _newCoverPath = null;
        TxtNewCoverName.Text = "ยังไม่ได้เลือก";
        TxtAddStatus.Text = "";
    }

    private async void BtnAddBook_Click(object sender, RoutedEventArgs e)
    {
        var regNo = (TxtNewRegNo.Text ?? "").Trim();
        var title = (TxtNewTitle.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(regNo) || string.IsNullOrWhiteSpace(title))
        {
            TxtAddStatus.Text = "กรุณากรอกรหัสและชื่อหนังสือ";
            return;
        }

        // The importer upserts on a duplicate regNo, so warn before overwriting.
        if (_all.Any(b => string.Equals((b.RegNo ?? "").Trim(), regNo, StringComparison.OrdinalIgnoreCase)))
        {
            if (MessageBox.Show(
                    $"รหัส {regNo} มีอยู่แล้ว — การบันทึกจะเขียนทับข้อมูลเดิม ต้องการดำเนินการต่อหรือไม่?",
                    "รหัสซ้ำ", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        var category = (TxtNewCategory.Text ?? "").Trim();
        var publisher = (TxtNewPublisher.Text ?? "").Trim();
        var shelf = (TxtNewShelf.Text ?? "").Trim();

        try
        {
            TxtAddStatus.Text = "กำลังบันทึก...";

            var csv = new StringBuilder();
            csv.AppendLine("reg_no,title,category,publisher,shelf");
            csv.AppendLine(string.Join(",", new[]
            {
                CsvField(regNo), CsvField(title), CsvField(category), CsvField(publisher), CsvField(shelf)
            }));

            var result = await _api.ImportCsvTextAsync(csv.ToString());

            if (result.Imported >= 1)
            {
                var coverNote = "";
                if (!string.IsNullOrWhiteSpace(_newCoverPath) && File.Exists(_newCoverPath))
                {
                    try
                    {
                        await _api.UploadBookCoverAsync(regNo, _newCoverPath!);
                        coverNote = " (พร้อมรูปปก)";
                    }
                    catch (Exception cex)
                    {
                        coverNote = $" (แต่ปกไม่สำเร็จ: {cex.Message})";
                    }
                }

                TxtAddStatus.Text = $"เพิ่มแล้ว: {regNo}{coverNote}";
                ClearAddForm();
                await ReloadAsync();
            }
            else
            {
                TxtAddStatus.Text = $"เพิ่มไม่สำเร็จ: {result.Reason ?? $"ข้าม {result.Skipped} แถว"}";
            }
        }
        catch (Exception ex)
        {
            TxtAddStatus.Text = $"ผิดพลาด: {ex.Message}";
        }
    }

    // Quote every CSV field and escape embedded quotes (importer handles quoted fields).
    private static string CsvField(string s)
        => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    // ---- per-row edit / delete (C1 endpoints) ----
    private async void BtnEditRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BookDto b) return;

        try
        {
            // Edit dialog also manages the cover (change/clear apply immediately on the server).
            var dlg = new EditBookDialog(b, _api) { Owner = Window.GetWindow(this) };
            var saved = dlg.ShowDialog() == true && dlg.Result != null;

            if (saved)
            {
                TxtStatus.Text = "กำลังบันทึกการแก้ไข...";
                await _api.UpdateBookAsync(b.RegNo, dlg.Result!);
                TxtStatus.Text = $"แก้ไขแล้ว: {b.RegNo}";
            }

            // refresh if the title was saved or the cover changed in the dialog
            if (saved || dlg.CoverChanged)
                await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "แก้ไขไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BookDto b) return;

        if (!ConfirmDialog.Ask(Window.GetWindow(this), "ยืนยันการลบ",
                $"ต้องการลบหนังสือ {b.RegNo} — {b.Title} ใช่หรือไม่? (รูปปกที่ผูกกับเล่มนี้จะถูกลบด้วย)",
                "ลบ"))
            return;

        try
        {
            TxtStatus.Text = "กำลังลบหนังสือ...";
            await _api.DeleteBookAsync(b.RegNo);
            TxtStatus.Text = $"ลบแล้ว: {b.RegNo}";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ลบไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}