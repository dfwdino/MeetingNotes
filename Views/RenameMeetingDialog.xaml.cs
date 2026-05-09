using System.Windows;
using System.Windows.Input;

namespace MeetingNotes.Views;

public partial class RenameMeetingDialog : Window
{
    public string NewTitle => TitleBox.Text.Trim();

    public RenameMeetingDialog(string currentTitle)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleBox.Text = currentTitle;
            TitleBox.SelectAll();
            TitleBox.Focus();
        };
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void TitleBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RenameButton_Click(sender, e);
        if (e.Key == Key.Escape) CancelButton_Click(sender, e);
    }
}
