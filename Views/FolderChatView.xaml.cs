using MeetingNotes.Models;
using MeetingNotes.Services;
using MeetingNotes.ViewModels;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MeetingNotes.Views;

public partial class FolderChatView : Page
{
    private readonly DatabaseService _db;
    private readonly ILlmService _llm;
    private readonly AppSettings _settings;

    private FolderViewModel? _folder;
    // In-memory history for Ollama context — rebuilt from DB on each SetFolder call
    private readonly List<(string role, string content)> _history = [];

    public FolderChatView(DatabaseService db, ILlmService llm, AppSettings settings)
    {
        InitializeComponent();
        _db = db;
        _llm = llm;
        _settings = settings;
    }

    public async void SetFolder(FolderViewModel folder)
    {
        _folder = folder;
        _history.Clear();
        ChatMessages.Children.Clear();
        ChatScrollViewer.Visibility = Visibility.Collapsed;
        ChatHint.Visibility = Visibility.Visible;

        FolderNameText.Text = $"📁  {folder.Name}";

        // Count meetings with transcripts
        var meetings = await _db.GetMeetingsForFolderAsync(folder.Id);
        var withTranscript = meetings.Count(m => !string.IsNullOrWhiteSpace(m.Transcript));

        if (folder.MeetingCount == 0)
        {
            SubtitleText.Text = "No meetings yet";
            HintText.Text = "Create a meeting and record it — then come back to ask questions about it.";
            ChatInputBox.IsEnabled = false;
        }
        else if (withTranscript == 0)
        {
            SubtitleText.Text = $"{folder.MeetingCount} meeting{(folder.MeetingCount == 1 ? "" : "s")} · No transcripts yet";
            HintText.Text = "Record and process a meeting first — the AI needs transcripts to answer questions.";
            ChatInputBox.IsEnabled = false;
        }
        else
        {
            SubtitleText.Text = $"{folder.MeetingCount} meeting{(folder.MeetingCount == 1 ? "" : "s")} · {withTranscript} with transcript{(withTranscript == 1 ? "" : "s")} · AI can search all of them";
            HintText.Text = $"Ask anything about your meetings in \"{folder.Name}\".\nThe AI will search across all {withTranscript} transcript{(withTranscript == 1 ? "" : "s")}.";
            ChatInputBox.IsEnabled = true;
        }

        // Restore persisted chat history from database
        var saved = await _db.GetFolderChatMessagesAsync(folder.Id);
        if (saved.Count > 0)
        {
            ChatHint.Visibility = Visibility.Collapsed;
            ChatScrollViewer.Visibility = Visibility.Visible;
            foreach (var msg in saved)
            {
                bool isUser = msg.Role == ChatRole.User;
                AddBubble(isUser ? "You" : "AI", msg.Content, isUser);
                _history.Add((isUser ? "user" : "assistant", msg.Content));
            }
            ChatScrollViewer.ScrollToBottom();
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void ChatInputBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async void ClearChat_Click(object sender, RoutedEventArgs e)
    {
        if (_folder is null) return;

        // Clear from database
        await _db.ClearFolderChatAsync(_folder.Id);

        // Clear in-memory state
        _history.Clear();
        ChatMessages.Children.Clear();
        ChatScrollViewer.Visibility = Visibility.Collapsed;
        ChatHint.Visibility = Visibility.Visible;
    }

    private async Task SendAsync()
    {
        if (_folder is null || string.IsNullOrWhiteSpace(ChatInputBox.Text)) return;

        var userMessage = ChatInputBox.Text.Trim();
        ChatInputBox.Text = string.Empty;
        ChatInputBox.IsEnabled = false;

        // Show messages area, hide hint
        ChatHint.Visibility = Visibility.Collapsed;
        ChatScrollViewer.Visibility = Visibility.Visible;

        AddBubble("You", userMessage, isUser: true);

        // Persist user message immediately
        await _db.AddFolderChatMessageAsync(_folder.Id, ChatRole.User, userMessage);

        // Build combined transcript context
        var combinedContext = await BuildFolderContextAsync(_folder.Id);

        var responseText = string.Empty;
        var responseBubble = AddBubble("AI", "…", isUser: false);

        try
        {
            await foreach (var chunk in _llm.FolderChatAsync(
                _folder.Name, combinedContext, _history, userMessage))
            {
                responseText += chunk;
                Dispatcher.Invoke(() =>
                {
                    if (responseBubble is TextBlock tb) tb.Text = responseText;
                    ChatScrollViewer.ScrollToBottom();
                });
            }
        }
        catch (Exception ex)
        {
            responseText = $"Error: {ex.Message}";
            if (responseBubble is TextBlock tb) tb.Text = responseText;
        }

        // Persist AI response and update in-memory history
        await _db.AddFolderChatMessageAsync(_folder.Id, ChatRole.Assistant, responseText);
        _history.Add(("user", userMessage));
        _history.Add(("assistant", responseText));

        ChatInputBox.IsEnabled = true;
        ChatInputBox.Focus();
    }

    private async Task<string> BuildFolderContextAsync(int folderId)
    {
        var meetings = await _db.GetMeetingsForFolderAsync(folderId);
        var sb = new StringBuilder();
        foreach (var m in meetings.Where(m => !string.IsNullOrWhiteSpace(m.Transcript)))
        {
            sb.AppendLine($"=== Meeting: \"{m.Title}\"  ({m.CreatedDate:MMM d, yyyy}) ===");
            sb.AppendLine(m.Transcript);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private UIElement AddBubble(string sender, string message, bool isUser)
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            MaxWidth = 480,
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
