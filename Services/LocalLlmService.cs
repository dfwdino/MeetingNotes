using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingNotes.Services;

public class LocalLlmService : ILlmService
{
    private string _serverUrl = string.Empty;
    private string _model = string.Empty;
    private string _apiKey = string.Empty;
    private HttpClient? _client;

    public void Configure(string serverUrl, string model, string apiKey = "")
    {
        bool changed = _serverUrl != serverUrl || _apiKey != apiKey;
        _serverUrl = serverUrl;
        _model = model;
        _apiKey = apiKey;
        if (changed) _client = null;
    }

    private HttpClient GetClient()
    {
        if (_client is not null) return _client;
        _client = new HttpClient
        {
            BaseAddress = new Uri(_serverUrl),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        return _client;
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            var response = await GetClient().GetFromJsonAsync<ModelsResponse>("/v1/models");
            return response?.Data?.Select(m => m.Id).ToList() ?? [];
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
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await GetClient().GetAsync("/v1/models", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_serverUrl) && !string.IsNullOrWhiteSpace(_model);

    // Local servers default to a small context window (Ollama ≈ 4k tokens ≈ 16k chars) and
    // silently truncate anything beyond it, so a long meeting's summary would only cover the
    // start. Transcripts over the single-pass limit are summarized per section, then merged.
    private const int MaxSinglePassChars = 12_000;
    private const int SectionTargetChars = 10_000;

    public async Task<string> GenerateSummaryAsync(string transcript, string promptTemplate,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (transcript.Length <= MaxSinglePassChars)
            return await RunSummaryPassAsync(promptTemplate + transcript, progress, cancellationToken);

        var sections = SplitAtLineBoundaries(transcript, SectionTargetChars);
        var sectionSummaries = new List<string>();
        for (int i = 0; i < sections.Count; i++)
        {
            var header = $"(This is section {i + 1} of {sections.Count} of a longer meeting.)\n";
            sectionSummaries.Add(await RunSummaryPassAsync(
                promptTemplate + header + sections[i], progress: null, cancellationToken));
        }

        var combined = string.Join("\n\n", sectionSummaries.Select(
            (s, i) => $"--- Summary of section {i + 1} ---\n{s}"));
        var mergePrompt =
            "The following are AI-generated summaries of consecutive sections of ONE meeting, " +
            "in chronological order. Merge them into a single cohesive summary with the same " +
            "structure, combining duplicate action items, decisions, and follow-ups. " +
            "Do not mention sections or that this was merged.\n\n" + combined;
        return await RunSummaryPassAsync(mergePrompt, progress, cancellationToken);
    }

    private async Task<string> RunSummaryPassAsync(string prompt,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        await foreach (var chunk in StreamChatAsync(
            [new ChatMessage("user", prompt)], cancellationToken))
        {
            result.Append(chunk);
            progress?.Report(chunk);
        }
        return result.ToString();
    }

    private static List<string> SplitAtLineBoundaries(string text, int targetChars)
    {
        List<string> sections = [];
        var current = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > targetChars)
            {
                sections.Add(current.ToString());
                current.Clear();
            }
            current.Append(line).Append('\n');
        }
        if (current.Length > 0)
            sections.Add(current.ToString());
        return sections;
    }

    public async IAsyncEnumerable<string> ChatAsync(string transcript,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system",
                "You are a helpful assistant answering questions about a meeting. " +
                "Use only the information from the transcript below to answer questions. " +
                "Be specific and reference timestamps when relevant.\n\nTranscript:\n" + transcript)
        };
        foreach (var (role, content) in history)
            messages.Add(new ChatMessage(role, content));
        messages.Add(new ChatMessage("user", userMessage));

        await foreach (var chunk in StreamChatAsync(messages, cancellationToken))
            yield return chunk;
    }

    public async IAsyncEnumerable<string> FolderChatAsync(string folderName,
        string combinedTranscripts,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system",
                $"You are a helpful assistant with access to all meeting transcripts in the \"{folderName}\" folder. " +
                "Answer questions using information from any of these meetings. " +
                "When relevant, mention which meeting the information came from by its title and date. " +
                "Be specific and reference timestamps when helpful.\n\n" + combinedTranscripts)
        };
        foreach (var (role, content) in history)
            messages.Add(new ChatMessage(role, content));
        messages.Add(new ChatMessage("user", userMessage));

        await foreach (var chunk in StreamChatAsync(messages, cancellationToken))
            yield return chunk;
    }

    private async IAsyncEnumerable<string> StreamChatAsync(
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model = _model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await GetClient().SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                break; // connection dropped mid-stream; end the enumeration cleanly
            }

            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string? content = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var contentEl))
                    content = contentEl.GetString();
            }
            catch { }

            if (content is not null)
                yield return content;
        }
    }

    private record ChatMessage(string Role, string Content);

    private class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelInfo>? Data { get; set; }
    }

    private class ModelInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
