using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using LibraryAdminPC.Services;

namespace LibraryAdminPC.ViewModels;

public class ImportViewModel : INotifyPropertyChanged
{
    private readonly ApiClient _api;

    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); }
    }

    private string _resultText = "เลือกไฟล์ (.csv / .xlsx / .db) แล้วกด “อัปโหลด”";
    public string ResultText
    {
        get => _resultText;
        set { _resultText = value; OnPropertyChanged(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public ImportViewModel(ApiClient api)
    {
        _api = api;
    }

    public async Task<bool> ImportAsync()
    {
        var path = (FilePath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("กรุณาเลือกไฟล์ก่อน", "Missing file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            IsBusy = true;
            ResultText = "กำลังอัปโหลดไฟล์...";

            var res = await _api.ImportFileAsync(path);

            var msg = $"นำเข้าแล้ว: {res.Imported:N0} | ข้าม: {res.Skipped:N0}";
            if (!string.IsNullOrWhiteSpace(res.Reason))
                msg += $"\nหมายเหตุ: {res.Reason}";

            ResultText = msg;
            MessageBox.Show(msg, "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            ResultText = ex.Message;
            MessageBox.Show(ex.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ClearAllAsync()
    {
        try
        {
            IsBusy = true;
            ResultText = "กำลังลบข้อมูลหนังสือทั้งหมด...";

            var deleted = await _api.ClearAllBooksAsync();

            var msg = $"ลบข้อมูลแล้ว: {deleted:N0} รายการ";
            ResultText = msg;

            MessageBox.Show(msg, "Clear All", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            ResultText = ex.Message;
            MessageBox.Show(ex.Message, "Clear Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}