using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LibraryAdminPC.Services;
using SkiaSharp;

namespace LibraryAdminPC.ViewModels;

public class DashboardViewModel : INotifyPropertyChanged
{
    private readonly ApiClient _api;

    // ✅ แก้ปัญหาภาษาไทยเป็น □□□□ ใน Tooltip/Legend
    // LiveChartsCore วาดข้อความด้วย SkiaSharp จึงต้องกำหนด Typeface ที่รองรับภาษาไทย
    private static readonly SKTypeface ThaiTypeface = SKTypeface.FromFamilyName("Tahoma");

    public SolidColorPaint LegendTextPaint { get; } = new SolidColorPaint
    {
        Color = SKColors.DimGray,
        SKTypeface = ThaiTypeface
    };

    public SolidColorPaint TooltipTextPaint { get; } = new SolidColorPaint
    {
        Color = SKColors.Black,
        SKTypeface = ThaiTypeface
    };

    private ISeries[] _categorySeries = Array.Empty<ISeries>();
    public ISeries[] CategorySeries
    {
        get => _categorySeries;
        set { _categorySeries = value; OnPropertyChanged(); }
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

    private int _totalBooks;
    public int TotalBooks
    {
        get => _totalBooks;
        set { _totalBooks = value; OnPropertyChanged(); }
    }

    private string? _lastUpdated;
    public string? LastUpdated
    {
        get => _lastUpdated;
        set { _lastUpdated = value; OnPropertyChanged(); }
    }

    public DashboardViewModel(ApiClient api)
    {
        _api = api;
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            Error = null;

            var metaTask = _api.GetMetaAsync();
            var catTask = _api.GetCategoryCountsAsync();

            await Task.WhenAll(metaTask, catTask);

            var meta = metaTask.Result;
            var items = catTask.Result;

            TotalBooks = meta.BookCount;

            // format LastUpdated ให้เป็นมิตรขึ้น (รองรับได้ทั้ง "yyyy-MM-dd HH:mm:ss" และ string อื่น)
            LastUpdated = FormatLastUpdated(meta.LastUpdated);

            CategorySeries = items
                .Select(x => new PieSeries<int>
                {
                    Name = x.Category,
                    Values = new[] { x.Count }
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            CategorySeries = Array.Empty<ISeries>();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatLastUpdated(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "-";

        // server เดิมส่ง "yyyy-MM-dd HH:mm:ss"
        if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            // แสดง dd/MM/yyyy HH:mm (ไทยอ่านง่าย)
            return dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("th-TH"));
        }

        // fallback
        if (DateTime.TryParse(raw, out var dt2))
            return dt2.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("th-TH"));

        return raw;
    }

    // ============================================================
    // B7 ADDITIVE — redesigned dashboard extras (doughnut + custom
    // legend + recent additions + top category). Nothing below mutates
    // the existing LoadAsync()/CategorySeries/other members. A separate
    // LoadExtrasAsync() (called from the View's Loaded handler, after the
    // original LoadAsync) populates these. The chart re-binds to
    // DonutSeries in XAML; CategorySeries stays as-is (still drives the
    // "จำนวนหมวดหมู่" KPI via {Binding CategorySeries.Length}).
    // ============================================================

    // Shared palette so doughnut slices, legend swatches and recent-book
    // category chips all use the same color per category index.
    private static readonly string[] ChartPalette =
        { "#1F5AA8", "#2E9C8E", "#E0922E", "#E2685A", "#5A5BB8", "#94A3B8" };

    private ISeries[] _donutSeries = Array.Empty<ISeries>();
    public ISeries[] DonutSeries
    {
        get => _donutSeries;
        set { _donutSeries = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CategoryLegendItem> CategoryLegend { get; } = new();
    public ObservableCollection<RecentBookItem> RecentBooks { get; } = new();

    private string _topCategoryName = "—";
    public string TopCategoryName
    {
        get => _topCategoryName;
        set { _topCategoryName = value; OnPropertyChanged(); }
    }

    private int _topCategoryCount;
    public int TopCategoryCount
    {
        get => _topCategoryCount;
        set { _topCategoryCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TopCategoryCountText)); }
    }

    // "2 เล่ม" when there is data; a friendly placeholder when the library is empty.
    public string TopCategoryCountText => TopCategoryCount > 0 ? $"{TopCategoryCount} เล่ม" : "ยังไม่มีข้อมูล";

    public async Task LoadExtrasAsync()
    {
        try
        {
            var cats = await _api.GetCategoryCountsAsync();
            var total = cats.Sum(c => c.Count);

            // Doughnut series (InnerRadius => doughnut), explicit per-category color.
            DonutSeries = cats.Select((c, i) => (ISeries)new PieSeries<int>
            {
                Name = c.Category,
                Values = new[] { c.Count },
                InnerRadius = 78,
                Fill = new SolidColorPaint(SKColor.Parse(ChartPalette[i % ChartPalette.Length]))
            }).ToArray();

            // Custom legend: color + name + count + percent (of the category total).
            CategoryLegend.Clear();
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                var pct = total > 0 ? (double)c.Count / total * 100.0 : 0;
                CategoryLegend.Add(new CategoryLegendItem
                {
                    Name = string.IsNullOrWhiteSpace(c.Category) ? "(ไม่ระบุ)" : c.Category,
                    Count = c.Count,
                    PercentText = $"{pct:0}%",
                    ColorBrush = MakeBrush(ChartPalette[i % ChartPalette.Length])
                });
            }

            // Top category (max count). Empty library => placeholder, never error.
            if (cats.Count > 0)
            {
                var top = cats.OrderByDescending(c => c.Count).First();
                TopCategoryName = string.IsNullOrWhiteSpace(top.Category) ? "—" : top.Category;
                TopCategoryCount = top.Count;
            }
            else
            {
                TopCategoryName = "—";
                TopCategoryCount = 0;
            }

            // Recent additions: BookDto has no date field, so newest = tail of the
            // server list (per spec). Reverse => newest first, take 5. Each book's
            // chip color matches its category's doughnut/legend color.
            var catColor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cats.Count; i++)
                catColor[cats[i].Category ?? ""] = ChartPalette[i % ChartPalette.Length];

            var books = await _api.GetBooksAsync();
            RecentBooks.Clear();
            foreach (var b in books.AsEnumerable().Reverse().Take(5))
            {
                var hex = catColor.TryGetValue((b.Category ?? "").Trim(), out var h) ? h : "#94A3B8";
                RecentBooks.Add(new RecentBookItem
                {
                    Title = string.IsNullOrWhiteSpace(b.Title) ? "(ไม่มีชื่อ)" : b.Title,
                    RegNo = b.RegNo,
                    Category = b.Category,
                    ColorBrush = MakeBrush(hex)
                });
            }
        }
        catch
        {
            // Extras are non-critical; the original LoadAsync owns the error banner.
        }
    }

    private static Brush MakeBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ============================================================
    // K3 ADDITIVE — live "จอ Kiosk เชื่อมต่อ" KPI (X/cap). Separate from
    // LoadAsync/LoadExtrasAsync; the view loads this on open + on the existing
    // refresh + on a light timer. -1 = unknown (server unreachable) => shows "—/cap".
    // ============================================================

    private int _activeKiosks = -1;
    public int ActiveKiosks
    {
        get => _activeKiosks;
        set { _activeKiosks = value; OnPropertyChanged(); OnPropertyChanged(nameof(KioskCountText)); }
    }

    private int _kioskCap = 10;
    public int KioskCap
    {
        get => _kioskCap;
        set { _kioskCap = value; OnPropertyChanged(); OnPropertyChanged(nameof(KioskCountText)); }
    }

    public string KioskCountText => ActiveKiosks >= 0 ? $"{ActiveKiosks}/{KioskCap}" : $"—/{KioskCap}";

    /// <summary>Refresh just the kiosk count KPI. Never throws/Errors — on failure it
    /// shows "—/cap" so the rest of the dashboard is unaffected.</summary>
    public async Task LoadKioskCountAsync()
    {
        try
        {
            var (active, cap) = await _api.GetActiveKiosksAsync();
            if (cap > 0) KioskCap = cap;
            ActiveKiosks = active;
        }
        catch
        {
            ActiveKiosks = -1; // unknown -> "—/cap"
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// B7 ADDITIVE view-model item types (small, presentation-only).
public sealed class CategoryLegendItem
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string PercentText { get; set; } = "";
    public Brush ColorBrush { get; set; } = Brushes.Gray;
}

public sealed class RecentBookItem
{
    public string Title { get; set; } = "";
    public string RegNo { get; set; } = "";
    public string Category { get; set; } = "";
    public Brush ColorBrush { get; set; } = Brushes.Gray;
}
