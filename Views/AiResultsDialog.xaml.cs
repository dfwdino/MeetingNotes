using System.Windows;

namespace MeetingNotes.Views;

public partial class AiResultsDialog : Window
{
    public AiResultsDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
