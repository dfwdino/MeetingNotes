using System.Windows;
using System.Windows.Input;

namespace MeetingNotes.Views;

public partial class NewFolderDialog : Window
{
    public string FolderName => FolderNameBox.Text.Trim();

    public NewFolderDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => FolderNameBox.Focus();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderNameBox.Text)) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void FolderNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CreateButton_Click(sender, e);
        if (e.Key == Key.Escape) CancelButton_Click(sender, e);
    }
}
