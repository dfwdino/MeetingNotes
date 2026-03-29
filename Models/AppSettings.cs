using System.IO;
namespace MeetingNotes.Models;

public class AppSettings
{
    public int Id { get; set; }

    // Whisper
    public string WhisperModel { get; set; } = "Base";
    public string WhisperCacheFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "whisper.net");
    public int TranscriptionChunkSeconds { get; set; } = 60;

    // Ollama
    public string OllamaServerUrl { get; set; } = "http://localhost:11434";
    public string OllamaDefaultModel { get; set; } = "llama3.2:3b";
    public string SummaryPrompt { get; set; } =
        "You are a meeting assistant. Based on the following transcript, provide:\n" +
        "1. A brief overview (2-3 sentences)\n" +
        "2. Action items (who does what)\n" +
        "3. Key decisions made\n" +
        "4. Follow-up items\n\n" +
        "5. How was everones attitude.\n\n" +
        "Be concise and specific. Use the actual names from the transcript.\n\nTranscript:\n";

    // Storage — paths relative to the app folder so the whole thing is portable
    public string RecordingsFolder { get; set; } = Path.Combine(
        AppContext.BaseDirectory, "Data", "Audio");
    public string DatabaseFolder { get; set; } = Path.Combine(
        AppContext.BaseDirectory, "Data", "DB");

    // Recording
    public string AudioFormat { get; set; } = "MP3";
    public int Mp3Bitrate { get; set; } = 64;
    public bool DeleteAudioAfterTranscription { get; set; } = false;

    // General
    public string Theme { get; set; } = "Dark";
    public bool LaunchAtStartup { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool ShowTrayTimer { get; set; } = true;

    // App Watcher
    public bool AppWatcherEnabled { get; set; } = false;
    /// <summary>Comma-separated list of process names (partial match) to watch.</summary>
    public string WatchedApps { get; set; } = string.Empty;
}
