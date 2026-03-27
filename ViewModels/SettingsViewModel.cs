using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingNotes.Models;
using MeetingNotes.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MeetingNotes.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly OllamaService _ollama;
    private AppSettings _settings;

    // Whisper
    [ObservableProperty] private string _whisperModel = "Base";
    [ObservableProperty] private string _whisperCacheFolder = string.Empty;
    [ObservableProperty] private int _transcriptionChunkSeconds = 60;

    // Ollama
    [ObservableProperty] private string _ollamaServerUrl = "http://localhost:11434";
    [ObservableProperty] private string _ollamaModel = "llama3.2:3b";
    [ObservableProperty] private string _summaryPrompt = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _availableModels = [];
    [ObservableProperty] private string _ollamaStatus = "Not checked";
    [ObservableProperty] private bool _ollamaConnected;

    // Recording
    [ObservableProperty] private string _audioFormat = "MP3";
    [ObservableProperty] private int _mp3Bitrate = 64;
    [ObservableProperty] private bool _deleteAudioAfterTranscription;

    // Storage
    [ObservableProperty] private string _recordingsFolder = string.Empty;
    [ObservableProperty] private string _databaseFolder = string.Empty;

    // General
    [ObservableProperty] private string _theme = "Dark";
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private bool _minimizeToTrayOnClose = true;
    [ObservableProperty] private bool _showTrayTimer = true;

    // App Watcher
    [ObservableProperty] private bool _appWatcherEnabled;
    [ObservableProperty] private string _watchedApps = string.Empty;

    public string[] WhisperModels { get; } = ["Tiny", "Base", "Small", "Medium"];
    public string[] AudioFormats { get; } = ["MP3", "WAV"];
    public int[] Mp3Bitrates { get; } = [32, 64, 128];
    public int[] ChunkIntervals { get; } = [30, 60, 120];
    public string[] Themes { get; } = ["Dark", "Light", "System"];

    public SettingsViewModel(OllamaService ollama, AppSettings settings)
    {
        _ollama = ollama;
        _settings = settings;
        LoadFromSettings(settings);
    }

    private void LoadFromSettings(AppSettings s)
    {
        WhisperModel = s.WhisperModel;
        WhisperCacheFolder = s.WhisperCacheFolder;
        TranscriptionChunkSeconds = s.TranscriptionChunkSeconds;
        OllamaServerUrl = s.OllamaServerUrl;
        OllamaModel = s.OllamaModel;
        SummaryPrompt = s.SummaryPrompt;
        AudioFormat = s.AudioFormat;
        Mp3Bitrate = s.Mp3Bitrate;
        DeleteAudioAfterTranscription = s.DeleteAudioAfterTranscription;
        RecordingsFolder = s.RecordingsFolder;
        DatabaseFolder = s.DatabaseFolder;
        Theme = s.Theme;
        LaunchAtStartup = s.LaunchAtStartup;
        MinimizeToTrayOnClose = s.MinimizeToTrayOnClose;
        ShowTrayTimer = s.ShowTrayTimer;
        AppWatcherEnabled = s.AppWatcherEnabled;
        WatchedApps = s.WatchedApps;
    }

    [RelayCommand]
    public async Task TestOllamaConnectionAsync()
    {
        OllamaStatus = "Connecting...";
        OllamaConnected = false;
        _ollama.Configure(OllamaServerUrl, OllamaModel);
        var models = await _ollama.GetAvailableModelsAsync();

        if (models.Count > 0)
        {
            AvailableModels = new ObservableCollection<string>(models);
            OllamaStatus = $"Connected — {models.Count} model(s) found";
            OllamaConnected = true;
            if (!models.Contains(OllamaModel) && models.Count > 0)
                OllamaModel = models[0];
        }
        else
        {
            OllamaStatus = "Could not connect. Is Ollama running?";
        }
    }

    [RelayCommand]
    public void BrowseRecordingsFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select recordings folder",
            SelectedPath = RecordingsFolder
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            RecordingsFolder = dialog.SelectedPath;
    }

    [RelayCommand]
    public void BrowseWhisperCacheFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Whisper model cache folder",
            SelectedPath = WhisperCacheFolder
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            WhisperCacheFolder = dialog.SelectedPath;
    }

    [RelayCommand]
    public Task SaveAsync()
    {
        _settings.WhisperModel = WhisperModel;
        _settings.WhisperCacheFolder = WhisperCacheFolder;
        _settings.TranscriptionChunkSeconds = TranscriptionChunkSeconds;
        _settings.OllamaServerUrl = OllamaServerUrl;
        _settings.OllamaModel = OllamaModel;
        _settings.SummaryPrompt = SummaryPrompt;
        _settings.AudioFormat = AudioFormat;
        _settings.Mp3Bitrate = Mp3Bitrate;
        _settings.DeleteAudioAfterTranscription = DeleteAudioAfterTranscription;
        _settings.RecordingsFolder = RecordingsFolder;
        _settings.DatabaseFolder = DatabaseFolder;
        _settings.Theme = Theme;
        _settings.LaunchAtStartup = LaunchAtStartup;
        _settings.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        _settings.ShowTrayTimer = ShowTrayTimer;
        _settings.AppWatcherEnabled = AppWatcherEnabled;
        _settings.WatchedApps = WatchedApps;

        SettingsService.Save(_settings);
        return Task.CompletedTask;
    }
}
