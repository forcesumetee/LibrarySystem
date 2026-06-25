using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LibraryAdminPC.Services;
using LibraryAdminPC.ViewModels;
using Microsoft.Win32;

namespace LibraryAdminPC.Views;

public partial class ImportView : UserControl
{
    private readonly ImportViewModel _vm;

    public event EventHandler? ImportCompleted;

    public ImportView(ApiClient api)
    {
        InitializeComponent();
        _vm = new ImportViewModel(api);
        DataContext = _vm;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "เลือกไฟล์นำเข้า",
            Filter = "Supported|*.csv;*.xlsx;*.db;*.sqlite|CSV|*.csv|Excel|*.xlsx|SQLite DB|*.db;*.sqlite|All files|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
            _vm.FilePath = dlg.FileName;
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var ok = await _vm.ImportAsync();
        if (ok)
            ImportCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void BtnExportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "บันทึกไฟล์ตัวอย่าง CSV",
            Filter = "CSV|*.csv",
            FileName = "books_template.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("reg_no,title,category,publisher,shelf");
        sb.AppendLine("A-0001,การเขียนโปรแกรมเบื้องต้น,คอมพิวเตอร์,ABC Publishing,ชั้น A1");

        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show("สร้างไฟล์ตัวอย่างแล้ว", "Template", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "ยืนยันลบข้อมูลหนังสือทั้งหมด?",
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (confirm != MessageBoxResult.Yes) return;

        var ok = await _vm.ClearAllAsync();
        if (ok)
            ImportCompleted?.Invoke(this, EventArgs.Empty);
    }
}