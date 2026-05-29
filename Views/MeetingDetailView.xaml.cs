using MeetingNotes.Models;
using MeetingNotes.Services;
using MeetingNotes.ViewModels;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace MeetingNotes.Views;

public partial class MeetingDetailView : Page
{
    private readonly DatabaseService _db;
    private readonly ILlmService _llm;
    private readonly AppSettings _settings;
    private readonly EncryptionService _encryption = new();
    private MeetingViewModel? _meetingVm;
    private bool _notesChanged;
    private bool _suppressNoteChange;
    private bool _suppressEncryptEvent;
    private System.Windows.Threading.DispatcherTimer? _saveTimer;
    // Decrypted content held in memory only (never written back to DB)
    private string? _decryptedTranscript;
    private string? _decryptedSummary;
    private string? _decryptedNotes;

    public MeetingDetailView(DatabaseService db, ILlmService llm, AppSettings settings)
    {
        InitializeComponent();
        _db = db;
        _llm = llm;
        _settings = settings;
        SetupSaveTimer();
    }

    public async void LoadMeeting(MeetingViewModel vm)
    {
        _meetingVm = vm;
        _decryptedTranscript = null;
        _decryptedSummary = null;
        _decryptedNotes = null;

        MetaText.Text = string.IsNullOrEmpty(vm.DurationDisplay)
                            ? vm.DateDisplay
                            : $"{vm.DateDisplay}  ({vm.DurationDisplay})";

        ReprocessRunAICheckBox.IsChecked = _settings.RunAiByDefault;

        if (vm.IsEncrypted)
        {
            ShowEncryptedState();
        }
        else
        {
            ShowUnencryptedState(vm);
            LoadRichText(vm.MyNotes);
            TranscriptText.Text = AddLineSpacing(vm.Transcript);
            SummaryText.Text    = AddLineSpacing(vm.Summary);

            // Show appropriate default tab
            if (vm.Status == MeetingStatus.Ready)
                SwitchTab(string.IsNullOrWhiteSpace(vm.Summary) ? "Transcript" : "Summary");
            else
                SwitchTab("MyNotes");
        }

        await LoadChatHistoryAsync(vm.Id);

        NewMeetingPanel.Visibility = vm.Status == MeetingStatus.New && !vm.IsEncrypted
            ? Visibility.Visible : Visibility.Collapsed;

        var hasContent = vm.Status != MeetingStatus.New;
        ReprocessButton.Visibility = hasContent && !vm.IsEncrypted ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.Visibility    = hasContent && !vm.IsEncrypted ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Encryption state helpers ──────────────────────────────────────────

    private void ShowEncryptedState()
    {
        EncryptMeetingCheckBox.Visibility = Visibility.Collapsed;
        EncryptedBadge.Visibility = Visibility.Visible;
        EncryptedOverlay.Visibility = Visibility.Visible;
        MyNotesPanel.Visibility = Visibility.Collapsed;
        TranscriptPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        ChatPanel.Visibility = Visibility.Collapsed;
        NewMeetingPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowUnencryptedState(MeetingViewModel vm)
    {
        EncryptMeetingCheckBox.Visibility = Visibility.Visible;
        EncryptedBadge.Visibility = Visibility.Collapsed;
        EncryptedOverlay.Visibility = Visibility.Collapsed;

        // Set checkbox from setting without triggering the Checked event handler
        _suppressEncryptEvent = true;
        EncryptMeetingCheckBox.IsChecked = _settings.EncryptMeetingByDefault;
        _suppressEncryptEvent = false;
    }

    // ── Encrypt checkbox ──────────────────────────────────────────────────

    private async void EncryptCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEncryptEvent || _meetingVm is null) return;
        // Only encrypt immediately if the meeting already has content (Ready)
        if (_meetingVm.Status == MeetingStatus.Ready)
            await EncryptMeetingNowAsync();
        // For non-Ready meetings the flag is just stored and picked up when Record is clicked
    }

    private void EncryptCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        // Nothing to do — just a flag for future recording
    }

    /// <summary>Encrypts the current meeting immediately, deletes audio, and updates the UI.</summary>
    public async Task EncryptMeetingNowAsync()
    {
        if (_meetingVm is null) return;

        var dialog = new EncryptPasswordDialog { Owner = Window.GetWindow(this), IsEncryptMode = true };
        if (dialog.ShowDialog() != true)
        {
            // User cancelled — uncheck the box
            EncryptMeetingCheckBox.IsChecked = false;
            return;
        }

        var meeting = await _db.GetMeetingAsync(_meetingVm.Id);
        if (meeting is null) return;

        try
        {
            var salt = _encryption.GenerateSalt();
            var kek = _encryption.DeriveKeyFromPassword(dialog.EnteredPassword, salt);
            var dataKey = _encryption.GenerateDataKey();
            var wrappedKey = _encryption.WrapKey(dataKey, kek);

            meeting.Transcript = _encryption.EncryptText(meeting.Transcript, dataKey);
            meeting.Summary    = _encryption.EncryptText(meeting.Summary, dataKey);
            meeting.MyNotes    = _encryption.EncryptText(meeting.MyNotes, dataKey);

            meeting.IsEncrypted     = true;
            meeting.EncryptionSalt  = Convert.ToBase64String(salt);
            meeting.EncryptedDataKey = Convert.ToBase64String(wrappedKey);

            // Delete audio files — required when encrypting
            DeleteMeetingAudio(meeting);

            await _db.UpdateMeetingAsync(meeting);

            // Update the VM so the list item reflects encrypted status
            _meetingVm.IsEncrypted = true;
            _meetingVm.AudioFilePath = null;
            _meetingVm.Transcript = meeting.Transcript;
            _meetingVm.Summary    = meeting.Summary;
            _meetingVm.MyNotes    = meeting.MyNotes;

            ShowEncryptedState();
            NewMeetingPanel.Visibility = Visibility.Collapsed;
            ReprocessButton.Visibility = Visibility.Collapsed;
            ExportButton.Visibility    = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            EncryptMeetingCheckBox.IsChecked = false;
            System.Windows.MessageBox.Show($"Encryption failed: {ex.Message}", "Encryption Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void DeleteMeetingAudio(Meeting meeting)
    {
        if (!string.IsNullOrEmpty(meeting.AudioFilePaths))
        {
            foreach (var path in meeting.AudioFilePaths.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                TryDeleteFile(path);
        }
        else
        {
            TryDeleteFile(meeting.AudioFilePath);
        }
        meeting.AudioFilePath  = null;
        meeting.AudioFilePaths = null;
    }

    private static void TryDeleteFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            try { File.Delete(path); } catch { }
    }

    // ── Unlock ────────────────────────────────────────────────────────────

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingVm is null) return;

        var dialog = new EncryptPasswordDialog { Owner = Window.GetWindow(this), IsEncryptMode = false };
        if (dialog.ShowDialog() != true) return;

        var meeting = await _db.GetMeetingAsync(_meetingVm.Id);
        if (meeting?.EncryptionSalt is null || meeting.EncryptedDataKey is null) return;

        try
        {
            var salt = Convert.FromBase64String(meeting.EncryptionSalt);
            var kek = _encryption.DeriveKeyFromPassword(dialog.EnteredPassword, salt);
            var wrappedKey = Convert.FromBase64String(meeting.EncryptedDataKey);
            var dataKey = _encryption.UnwrapKey(wrappedKey, kek);

            _decryptedTranscript = _encryption.DecryptText(meeting.Transcript, dataKey);
            _decryptedSummary    = _encryption.DecryptText(meeting.Summary, dataKey);
            _decryptedNotes      = _encryption.DecryptText(meeting.MyNotes, dataKey);

            // Show content in tabs (in-memory only, not saved back to DB)
            EncryptedOverlay.Visibility = Visibility.Collapsed;
            EncryptedBadge.Visibility = Visibility.Collapsed;
            EncryptMeetingCheckBox.Visibility = Visibility.Collapsed;

            // Show a "locked" badge to remind user content is temporarily decrypted
            ShowUnlockedBadge();

            LoadRichText(_decryptedNotes);
            TranscriptText.Text = AddLineSpacing(_decryptedTranscript);
            SummaryText.Text    = AddLineSpacing(_decryptedSummary);

            // Disable editing for notes while unlocked (read-only view)
            MyNotesBox.IsReadOnly = true;

            SwitchTab(string.IsNullOrWhiteSpace(_decryptedSummary) ? "Transcript" : "Summary");
        }
        catch (CryptographicException)
        {
            System.Windows.MessageBox.Show("Incorrect password. Please try again.", "Wrong Password",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to decrypt: {ex.Message}", "Decryption Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowUnlockedBadge()
    {
        EncryptedBadge.Visibility = Visibility.Visible;
        EncryptedBadgeText.Text = "🔓 Unlocked (read-only)";

        // Swap the Unlock button to a Re-lock button
        UnlockButton.Content = "🔒  Re-lock";
        UnlockButton.Click -= UnlockButton_Click;
        UnlockButton.Click += RelockButton_Click;
    }

    private void RelockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingVm is not null)
            LoadMeeting(_meetingVm);
    }

    private async Task LoadChatHistoryAsync(int meetingId)
    {
        ChatMessages.Children.Clear();
        var messages = await _db.GetChatMessagesAsync(meetingId);
        foreach (var msg in messages)
            AddChatBubble(msg.Role == ChatRole.User ? "You" : "AI", msg.Content, msg.Role == ChatRole.User);
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is string tab)
            SwitchTab(tab);
    }

    private void SwitchTab(string tab)
    {
        //_ActiveTab = tab;

        MyNotesPanel.Visibility   = tab == "MyNotes"    ? Visibility.Visible : Visibility.Collapsed;
        TranscriptPanel.Visibility = tab == "Transcript" ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility    = tab == "Summary"    ? Visibility.Visible : Visibility.Collapsed;
        ChatPanel.Visibility       = tab == "Chat"       ? Visibility.Visible : Visibility.Collapsed;
        NewMeetingPanel.Visibility = Visibility.Collapsed;

        // Highlight active tab
        foreach (var btn in new[] { TabMyNotes, TabTranscript, TabSummary, TabChat })
        {
            bool active = btn.Tag?.ToString() == tab;
            btn.Foreground = active
                ? new SolidColorBrush(WpfColor.FromRgb(0, 120, 212))
                : new SolidColorBrush(WpfColor.FromRgb(136, 136, 136));
            btn.BorderBrush = active
                ? new SolidColorBrush(WpfColor.FromRgb(0, 120, 212))
                : WpfBrushes.Transparent;
        }
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingVm is null) return;
        var mainWindow = Window.GetWindow(this) as MainWindow;
        bool runAI = ReprocessRunAICheckBox.IsChecked == true;
        bool encryptAfter = EncryptMeetingCheckBox.IsChecked == true;
        mainWindow?.ShowRecordingView(_meetingVm, runAI, encryptAfter);
    }

    private void ReprocessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingVm is null) return;
        var mainWindow = Window.GetWindow(this) as MainWindow;
        bool runAI = ReprocessRunAICheckBox.IsChecked == true;
        mainWindow?.ShowProcessingView(_meetingVm, runAI: runAI);
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meetingVm is null) return;

        var safeName = string.Concat(_meetingVm.Title
            .Split(Path.GetInvalidFileNameChars()))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Meeting";

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName    = $"{safeName}_{DateTime.Now:yyyyMMdd}",
            DefaultExt  = ".txt",
            Filter      = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            Title       = "Export Meeting Notes"
        };

        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine(_meetingVm.Title);
        sb.AppendLine($"Date: {_meetingVm.DateDisplay}   Duration: {_meetingVm.DurationDisplay}");
        sb.AppendLine(new string('─', 60));

        if (!string.IsNullOrWhiteSpace(_meetingVm.MyNotes))
        {
            sb.AppendLine();
            sb.AppendLine("MY NOTES");
            sb.AppendLine(new string('─', 20));
            sb.AppendLine(_meetingVm.MyNotes);
        }

        if (!string.IsNullOrWhiteSpace(_meetingVm.Summary))
        {
            sb.AppendLine();
            sb.AppendLine("AI SUMMARY");
            sb.AppendLine(new string('─', 20));
            sb.AppendLine(_meetingVm.Summary);
        }

        if (!string.IsNullOrWhiteSpace(_meetingVm.Transcript))
        {
            sb.AppendLine();
            sb.AppendLine("TRANSCRIPT");
            sb.AppendLine(new string('─', 20));
            sb.AppendLine(_meetingVm.Transcript);
        }

        await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);

        // Open the exported file immediately
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName)
        {
            UseShellExecute = true
        });
    }

    private void MyNotes_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressNoteChange) return;
        _notesChanged = true;
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    // ── Formatting toolbar ────────────────────────────────────────────
    private void FormatBold_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, MyNotesBox);
        MyNotesBox.Focus();
    }

    private void FormatItalic_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, MyNotesBox);
        MyNotesBox.Focus();
    }

    private void FormatUnderline_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleUnderline.Execute(null, MyNotesBox);
        MyNotesBox.Focus();
    }

    private void FormatBullets_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBullets.Execute(null, MyNotesBox);
        MyNotesBox.Focus();
    }

    private void FormatNumbered_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleNumbering.Execute(null, MyNotesBox);
        MyNotesBox.Focus();
    }

    // ── RTF helpers ───────────────────────────────────────────────────

    /// <summary>Load plain text or RTF into the notes RichTextBox.</summary>
    private void LoadRichText(string? content)
    {
        _suppressNoteChange = true;
        try
        {
            MyNotesBox.Document.Blocks.Clear();
            if (string.IsNullOrEmpty(content)) return;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var range = new TextRange(MyNotesBox.Document.ContentStart,
                                      MyNotesBox.Document.ContentEnd);
            // RTF content starts with "{\rtf"
            if (content.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
                range.Load(stream, System.Windows.DataFormats.Rtf);
            else
                range.Text = content;   // plain text from quick notes / old records
        }
        catch
        {
            var range = new TextRange(MyNotesBox.Document.ContentStart,
                                      MyNotesBox.Document.ContentEnd);
            range.Text = content ?? string.Empty;
        }
        finally
        {
            _suppressNoteChange = false;
        }
    }

    /// <summary>Serialize the notes RichTextBox to RTF for storage.</summary>
    private string GetRichText()
    {
        using var stream = new MemoryStream();
        var range = new TextRange(MyNotesBox.Document.ContentStart,
                                  MyNotesBox.Document.ContentEnd);
        range.Save(stream, System.Windows.DataFormats.Rtf);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void SetupSaveTimer()
    {
        _saveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await AutoSaveAsync();
        };
    }

    private async Task AutoSaveAsync()
    {
        if (_meetingVm is null || !_notesChanged) return;
        var meeting = await _db.GetMeetingAsync(_meetingVm.Id);
        if (meeting is null) return;
        meeting.Title = _meetingVm.Title;
        meeting.MyNotes = GetRichText();
        await _db.UpdateMeetingAsync(meeting);
        _notesChanged = false;
    }

    // ── Chat ─────────────────────────────────────────────────────────────

    private async void SendChatButton_Click(object sender, RoutedEventArgs e) =>
        await SendChatMessageAsync();

    private async void ChatInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
        {
            e.Handled = true;
            await SendChatMessageAsync();
        }
    }

    private async Task SendChatMessageAsync()
    {
        if (_meetingVm is null || string.IsNullOrWhiteSpace(ChatInputBox.Text)) return;

        var userMessage = ChatInputBox.Text.Trim();
        ChatInputBox.Text = string.Empty;
        ChatInputBox.IsEnabled = false;

        await _db.AddChatMessageAsync(_meetingVm.Id, ChatRole.User, userMessage);
        AddChatBubble("You", userMessage, isUser: true);

        var history = (await _db.GetChatMessagesAsync(_meetingVm.Id))
            .Select(m => (m.Role == ChatRole.User ? "user" : "assistant", m.Content));

        var responseText = string.Empty;
        var responseBubble = AddChatBubble("AI", "...", isUser: false);

        await foreach (var chunk in _llm.ChatAsync(
            _meetingVm.Transcript ?? string.Empty, history, userMessage))
        {
            responseText += chunk;
            Dispatcher.Invoke(() =>
            {
                if (responseBubble is TextBlock tb) tb.Text = responseText;
                ChatScrollViewer.ScrollToBottom();
            });
        }

        await _db.AddChatMessageAsync(_meetingVm.Id, ChatRole.Assistant, responseText);
        ChatInputBox.IsEnabled = true;
        ChatInputBox.Focus();
    }

    /// <summary>
    /// Inserts a blank line between each non-empty line so the read-only TextBox
    /// displays ~5 px of breathing room between transcript / summary segments.
    /// </summary>
    private static string AddLineSpacing(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n\n", lines);
    }

    private UIElement AddChatBubble(string sender, string message, bool isUser)
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            MaxWidth = 440,
            HorizontalAlignment = isUser ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left,
            Background = new SolidColorBrush(isUser
                ? WpfColor.FromRgb(0, 78, 140)
                : WpfColor.FromRgb(37, 37, 37)),
            CornerRadius = new CornerRadius(isUser ? 12 : 4, 12, isUser ? 4 : 12, 12),
            Padding = new Thickness(14, 10, 14, 10)
        };

        var textBlock = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 220, 220)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22
        };

        container.Child = textBlock;
        ChatMessages.Children.Add(container);
        ChatScrollViewer.ScrollToBottom();
        return textBlock;
    }
}
