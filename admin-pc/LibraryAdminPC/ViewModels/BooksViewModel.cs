using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using LibraryAdminPC.Models;
using LibraryAdminPC.Services;

namespace LibraryAdminPC.ViewModels;

public class BooksViewModel : INotifyPropertyChanged
{
    private readonly ApiClient _api;

    // ✅ เก็บข้อมูลทั้งหมดไว้ครั้งเดียว แล้วกรองด้วย ICollectionView (เร็วและไม่ค้าง)
    public ObservableCollection<BookDto> AllBooks { get; } = new();
    public ICollectionView BooksView { get; }

    public ObservableCollection<string> Categories { get; } = new();

    private readonly DispatcherTimer _debounceTimer;

    private string _query = "";
    public string Query
    {
        get => _query;
        set
        {
            _query = value;
            OnPropertyChanged();
            DebounceFilter();
        }
    }

    private string _selectedCategory = "ทั้งหมด";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            ApplyFilterNow();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set { _error = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string _countText = "จำนวน 0 รายการ";
    public string CountText
    {
        get => _countText;
        set { _countText = value; OnPropertyChanged(); }
    }

    private bool _loadedOnce;

    public BooksViewModel(ApiClient api)
    {
        _api = api;

        BooksView = CollectionViewSource.GetDefaultView(AllBooks);
        BooksView.Filter = FilterPredicate;

        Categories.Clear();
        Categories.Add("ทั้งหมด");
        SelectedCategory = "ทั้งหมด";

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += (_, __) =>
        {
            _debounceTimer.Stop();
            ApplyFilterNow();
        };
    }

    private void DebounceFilter()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not BookDto b) return false;

        // category
        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "ทั้งหมด")
        {
            if (!string.Equals((b.Category ?? "").Trim(), SelectedCategory.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // query
        var q = (Query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q)) return true;

        return ContainsSafe(b.RegNo, q)
            || ContainsSafe(b.Title, q)
            || ContainsSafe(b.Category, q)
            || ContainsSafe(b.Publisher, q)
            || ContainsSafe(b.Shelf, q);
    }

    private static bool ContainsSafe(string? src, string keyword)
    {
        if (string.IsNullOrWhiteSpace(src)) return false;
        return src.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce) return;
        await ReloadFromServerAsync();
        _loadedOnce = true;
    }

    public async Task ReloadFromServerAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            Error = null;

            // ✅ โหลดทั้งหมดครั้งเดียว (500 รายการเร็วมาก) แล้วกรองในเครื่อง
            var items = await _api.GetBooksAsync(q: null, category: null);

            AllBooks.Clear();
            foreach (var b in items.OrderBy(x => x.RegNo).ThenBy(x => x.Title))
                AllBooks.Add(b);

            // categories จาก AllBooks (ไม่ขึ้นกับผลค้นหา)
            var cats = AllBooks
                .Select(x => x.Category?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var current = SelectedCategory;
            Categories.Clear();
            Categories.Add("ทั้งหมด");
            foreach (var c in cats) Categories.Add(c!);

            if (!Categories.Contains(current))
                SelectedCategory = "ทั้งหมด";

            ApplyFilterNow();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            AllBooks.Clear();
            ApplyFilterNow();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ClearSearch()
    {
        Query = "";
        SelectedCategory = "ทั้งหมด";
        ApplyFilterNow();
    }

    public void ApplyFilterNow()
    {
        BooksView.Refresh();

        // Count (นับเฉพาะที่ผ่าน filter)
        var count = BooksView.Cast<object>().Count();
        CountText = $"จำนวน {count:N0} รายการ";
    }

    public async Task<int> ClearAllBooksAsync()
    {
        if (IsLoading) return 0;

        try
        {
            IsLoading = true;
            Error = null;

            var deleted = await _api.ClearAllBooksAsync();

            AllBooks.Clear();
            ApplyFilterNow();

            return deleted;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}