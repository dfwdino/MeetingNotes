using System.Windows;

namespace MeetingNotes.Views;

/// <summary>
/// Modal dialog for entering (and optionally confirming) an encryption password.
/// Set <see cref="IsEncryptMode"/> = true when creating a new password (shows confirm field).
/// </summary>
public partial class EncryptPasswordDialog : Window
{
    public bool IsEncryptMode { get; set; } = true;
    public string EnteredPassword { get; private set; } = string.Empty;

    public EncryptPasswordDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsEncryptMode)
        {
            Title = "Encrypt Meeting";
            TitleText.Text = "Encrypt this meeting";
            SubtitleText.Text =
                "Choose a password to protect the transcript, summary, and notes. " +
                "The audio file will be permanently deleted. " +
                "You will need this password to view the meeting content again.";
            ConfirmPanel.Visibility = Visibility.Visible;
        }
        else
        {
            Title = "Unlock Meeting";
            TitleText.Text = "Unlock encrypted meeting";
            SubtitleText.Text = "Enter the password to view this meeting's content.";
            ConfirmPanel.Visibility = Visibility.Collapsed;
        }
        PasswordBox.Focus();
    }

    private void PasswordBox_Changed(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Please enter a password.");
            return;
        }

        if (IsEncryptMode)
        {
            if (password.Length < 4)
            {
                ShowError("Password must be at least 4 characters.");
                return;
            }
            if (password != ConfirmBox.Password)
            {
                ShowError("Passwords do not match.");
                ConfirmBox.Focus();
                return;
            }
        }

        EnteredPassword = password;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
