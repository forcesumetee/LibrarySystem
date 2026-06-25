using System.Windows;

namespace LibraryAdminPC.Views;

public partial class LicenseKeyDialog : Window
{
    public string? ProductKey { get; private set; }

    public LicenseKeyDialog()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ProductKey = (TxtKey.Text ?? "").Trim();
        DialogResult = true;
        Close();
    }
}