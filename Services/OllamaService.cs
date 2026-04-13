using OllamaSharp;
using OllamaSharp.Models.Chat;
using OllamaMessage = OllamaSharp.Models.Chat.Message;

namespace MeetingNotes.Services;

public class OllamaService
{
    private OllamaApiClient? _client;
    private string _serverUrl = string.Empty;
    private string _model = string.Empty;

    public void Configure(string serverUrl, string model)
    {
        _serverUrl = serverUrl;
        _model = model;
        _client = CreateClient(serverUrl);
    }

    private static OllamaApiClient CreateClient(string serverUrl)
    {
        // Default HttpClient timeout is 100s — way too short for long meeting summaries.
        // Use 30 minutes so even a 1-hour transcript has time to process.
        var httpClient = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri(serverUrl),
            // No timeout — large models (20b+) can take many minutes to summarize
            // a long meeting. The user can cancel via the app if needed.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        return new OllamaApiClient(httpClient);
    }

    private OllamaApiClient GetClient()
    {
        _client ??= CreateClient(_serverUrl);
        return _client;
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            var client = GetClient();
            var models = await client.ListLocalModelsAsync();
            return models.Select(m => m.Name).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            // Call the API directly — GetAvailableModelsAsync swallows exceptions
            // so it can never tell us the server is unreachable.
            await GetClient().ListLocalModelsAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_serverUrl) && !string.IsNullOrWhiteSpace(_model);

    public async Task<string> GenerateSummaryAsync(string transcript, string promptTemplate,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var fullPrompt = promptTemplate + transcript;
        var response = new System.Text.StringBuilder();

        await foreach (var chunk in client.GenerateAsync(new OllamaSharp.Models.GenerateRequest
        {
            Model = _model,
            Prompt = fullPrompt,
            Stream = true
        }, cancellationToken))
        {
            if (chunk?.Response is not null)
            {
                response.Append(chunk.Response);
                progress?.Report(chunk.Response);
            }
        }

        return response.ToString();
    }

    public async IAsyncEnumerable<string> FolderChatAsync(
        string folderName,
        string combinedTranscripts,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = GetClient();

        var messages = new List<OllamaMessage>
        {
            new OllamaMessage { Role = ChatRole.System, Content =
                $"You are a helpful assistant with access to all meeting transcripts in the \"{folderName}\" folder. " +
                "Answer questions using information from any of these meetings. " +
                "When relevant, mention which meeting the information came from by its title and date. " +
                "Be specific and reference timestamps when helpful.\n\n" +
                combinedTranscripts }
        };

        foreach (var (role, content) in history)
            messages.Add(new OllamaMessage
            {
                Role = role == "user" ? ChatRole.User : ChatRole.Assistant,
                Content = content
            });

        messages.Add(new OllamaMessage { Role = ChatRole.User, Content = userMessage });

        await foreach (var chunk in client.ChatAsync(new OllamaSharp.Models.Chat.ChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = true
        }, cancellationToken))
        {
            if (chunk?.Message?.Content is not null)
                yield return chunk.Message.Content;
        }
    }

    public async IAsyncEnumerable<string> ChatAsync(string transcript,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = GetClient();

        var messages = new List<OllamaMessage>
        {
            new OllamaMessage() { Role = ChatRole.System, Content =
                "You are a helpful assistant answering questions about a meeting. " +
                "Use only the information from the transcript below to answer questions. " +
                "Be specific and reference timestamps when relevant.\n\nTranscript:\n" + transcript }
        };

        foreach (var (role, content) in history)
        {
            messages.Add(new OllamaMessage
            {
                Role = role == "user" ? ChatRole.User : ChatRole.Assistant,
                Content = content
            });
        }

        messages.Add(new OllamaMessage { Role = ChatRole.User, Content = userMessage });

        await foreach (var chunk in client.ChatAsync(new OllamaSharp.Models.Chat.ChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = true
        }, cancellationToken))
        {
            if (chunk?.Message?.Content is not null)
                yield return chunk.Message.Content;
        }
    }
}
