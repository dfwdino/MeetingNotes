namespace MeetingNotes.Services;

public interface ILlmService
{
    Task<List<string>> GetAvailableModelsAsync();
    Task<bool> IsAvailableAsync();
    bool IsConfigured();
    Task<string> GenerateSummaryAsync(string transcript, string promptTemplate,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> ChatAsync(string transcript,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> FolderChatAsync(string folderName, string combinedTranscripts,
        IEnumerable<(string role, string content)> history,
        string userMessage,
        CancellationToken cancellationToken = default);
}
