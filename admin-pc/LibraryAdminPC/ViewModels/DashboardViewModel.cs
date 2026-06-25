using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
