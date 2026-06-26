using System.Windows;

namespace LibraryAdminPC.Views;

// C2 UX: themed confirm dialog (replaces the gray system MessageBox) — card +
// warning icon + Danger confirm / Secondary cancel, matching the B0 design tokens.
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText = "ลบ")
    {
        InitializeComponent();
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        BtnConfirm.Content = confirmText;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    // Convenience: show modally and return whether the user confirmed.
    public static bool Ask(Window? owner, string title, string message, string confirmText = "ลบ")
    {
        var dlg = new ConfirmDialog(title, message, confirmText) { Owner = owner };
        return dlg.ShowDialog() == true;
    }
}
