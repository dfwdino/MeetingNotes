using MeetingNotes.Models;

namespace MeetingNotes.Services;

public class LlmServiceRouter : ILlmService
{
    private readonly LocalLlmService _llm;
    private readonly AppSettings _settings;

    public LlmServiceRouter(LocalLlmService llm, AppSettings settings)
    {
        _llm = llm;
        _settings = settings;
    }

    private LocalLlmService Configured()
    {
        if (_settings.LlmProvider == "LmStudio")
            _llm.Configure(_settings.LmStudioServerUrl, _settings.LmStudioDefaultModel, _settings.LmStudioApiKey);
        else
            _llm.Configure(_settings.OllamaServerUrl, _settings.OllamaDefaultModel);
        return _llm;
    }

    public Task<List<string>> GetAvailableModelsAsync() => Configured().GetAvailableModelsAsync();
    public Task<bool> IsAvailableAsync() => Configured().IsAvailableAsync();
    public bool IsConfigured() => Configured().IsConfigured();

    public Task<string> GenerateSummaryAsync(string transcript, string promptTemplate,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        Configured().GenerateSummaryAsync(transcript, promptTemplate, progress, cancellationToken);

    public IAsyncEnumerable<string> ChatAsync(string transcript,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        CancellationToken cancellationToken = default) =>
        Configured().ChatAsync(transcript, history, userMessage, cancellationToken);

    public IAsyncEnumerable<string> FolderChatAsync(string folderName, string combinedTranscripts,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        CancellationToken cancellationToken = default) =>
        Configured().FolderChatAsync(folderName, combinedTranscripts, history, userMessage, cancellationToken);
}
