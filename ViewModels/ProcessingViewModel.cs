using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetingNotes.Models;
using MeetingNotes.Services;
using Whisper.net;

namespace MeetingNotes.ViewModels;

public partial class ProcessingViewModel : BaseViewModel
{
    private readonly TranscriptionService _transcription;
    private readonly OllamaService _ollama;
    private readonly DatabaseService _db;
    private readonly AppSettings _settings;
    private readonly AudioCaptureService _audio;

    [ObservableProperty] private string _statusMessage = "Preparing...";
    [ObservableProperty] private string _savingAudioStatus = "Waiting";
    [ObservableProperty] private string _transcribingStatus = "Waiting";
    [ObservableProperty] private string _summarizingStatus = "Waiting";
    [ObservableProperty] private bool _savingAudioActive;
    [ObservableProperty] private bool _transcribingActive;
    [ObservableProperty] private bool _summarizingActive;
    [ObservableProperty] private bool _savingAudioDone;
    [ObservableProperty] private bool _transcribingDone;
    [ObservableProperty] private bool _summarizingDone;
    [ObservableProperty] private string _audioFileName = string.Empty;
    [ObservableProperty] private string _liveTranscriptPreview = string.Empty;

    public event EventHandler<Meeting>? ProcessingComplete;
    public event EventHandler<string>? SegmentTranscribed;
    public event EventHandler<string>? StepChanged;
    public event EventHandler<string>? StatusChanged;

    public ProcessingViewModel(TranscriptionService transcription, OllamaService ollama,
        DatabaseService db, AppSettings settings, AudioCaptureService audio)
    {
        _transcription = transcription;
        _ollama = ollama;
        _db = db;
        _settings = settings;
        _audio = audio;
    }

    /// <param name="appendTranscript">
    /// When true (re-recording on an existing meeting), always transcribe the new audio
    /// and append it to any existing transcript.  When false (first recording or retry),
    /// transcription is skipped if a transcript already exists.
    /// </param>
    public async Task ProcessMeetingAsync(Meeting meeting, bool appendTranscript = false,
        bool runAI = true, CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 0: Wait for the background audio save (mix/convert) to finish.
            // StopRecording() returns immediately and stores the task in AudioSaveTask so
            // the UI can navigate here right away while the file is still being written.
            StatusMessage = "Saving audio...";
            SavingAudioStatus = "In progress";
            SavingAudioActive = true;
            AudioFileName = meeting.AudioFilePath is not null
                ? Path.GetFileName(meeting.AudioFilePath)
                : string.Empty;
            StepChanged?.Invoke(this, "saving-audio");
            StatusChanged?.Invoke(this, "Saving audio...");

            await _audio.AudioSaveTask;

            SavingAudioActive = false;
            SavingAudioDone = true;
            SavingAudioStatus = "Done";
            StepChanged?.Invoke(this, "audio-saved");

            // Capture existing summary before anything changes so we can append to it
            string? existingSummary = meeting.Summary;

            // Timestamp stamped on each recording section
            var timestamp = (meeting.RecordingEnded ?? DateTime.Now)
                                .ToString("M/d/yyyy h:mm:ss tt");

            // Step 1: Transcribe
            // - appendTranscript=true  → always transcribe new audio and append
            // - appendTranscript=false → skip if transcript already exists (retry path)
            bool needsTranscription = appendTranscript || string.IsNullOrWhiteSpace(meeting.Transcript);

            // Track just the new portion so the summary covers only fresh content
            string? newPortionText = null;

            if (needsTranscription)
            {
                StatusMessage = "Transcribing audio...";
                TranscribingStatus = "In progress";
                TranscribingActive = true;
                StepChanged?.Invoke(this, "transcribing");
                StatusChanged?.Invoke(this, "Transcribing audio...");

                _transcription.SegmentTranscribed += (_, line) =>
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        LiveTranscriptPreview = line + "\n" + LiveTranscriptPreview;
                        SegmentTranscribed?.Invoke(this, line);
                    });
                };

                var modelPath = await _transcription.EnsureModelAsync(
                    _settings.WhisperModel, _settings.WhisperCacheFolder);
                _transcription.LoadModel(modelPath);

                var newTranscript = await _transcription.TranscribeFileAsync(
                    meeting.AudioFilePath!, cancellationToken);

                newPortionText = newTranscript;

                // Append to existing transcript when re-recording, otherwise replace
                meeting.Transcript = appendTranscript && !string.IsNullOrWhiteSpace(meeting.Transcript)
                    ? meeting.Transcript + $"\n\n— Continued — {timestamp}\n\n\n" + newTranscript
                    : $"{timestamp}\n\n\n" + newTranscript;

                await _db.UpdateMeetingAsync(meeting);
            }
            else
            {
                // Already transcribed — show existing transcript in preview
                newPortionText = meeting.Transcript;
                LiveTranscriptPreview = meeting.Transcript;
                StatusChanged?.Invoke(this, "Transcript already exists, re-summarizing...");
            }

            TranscribingActive = false;
            TranscribingDone = true;
            TranscribingStatus = "Done";
            StepChanged?.Invoke(this, "transcribed");

            // Step 2: Summarize (skip if the user unchecked "Run AI processing")
            if (!runAI)
            {
                SummarizingDone = true;
                SummarizingStatus = "Skipped";
                StepChanged?.Invoke(this, "summarized");
                meeting.Status = MeetingStatus.Ready;
                await _db.UpdateMeetingAsync(meeting);
                StatusMessage = "Complete!";
                StatusChanged?.Invoke(this, "Complete! (AI processing skipped)");
                ProcessingComplete?.Invoke(this, meeting);
                return;
            }

            // Step 2: Summarize
            // In append mode: summarize only the new portion, then append to the existing
            // summary so the original is never lost.
            // In normal mode: summarize the full transcript (first run or explicit re-process).
            StatusMessage = "Checking Ollama...";
            SummarizingActive = true;
            StepChanged?.Invoke(this, "summarizing");

            string? ollamaWarning = await CheckOllamaAsync();
            if (ollamaWarning != null)
            {
                SummarizingActive = false;
                SummarizingDone = true;
                SummarizingStatus = "Skipped";
                meeting.Status = MeetingStatus.Ready;
                await _db.UpdateMeetingAsync(meeting);
                ProcessingComplete?.Invoke(this, meeting);
                StatusChanged?.Invoke(this, ollamaWarning);
                return;
            }

            StatusMessage = "Generating summary with Ollama...";
            SummarizingStatus = "In progress";
            StatusChanged?.Invoke(this, "Generating summary...");

            var textToSummarize = (appendTranscript && !string.IsNullOrWhiteSpace(existingSummary))
                ? newPortionText!
                : meeting.Transcript!;

            var summary = await _ollama.GenerateSummaryAsync(
                textToSummarize, _settings.SummaryPrompt,
                new Progress<string>(chunk =>
                    App.Current.Dispatcher.Invoke(() => StatusMessage = "Summarizing...")),
                cancellationToken);

            // Preserve original summary when appending; replace it on first run or re-process
            meeting.Summary = (appendTranscript && !string.IsNullOrWhiteSpace(existingSummary))
                ? existingSummary + $"\n\n— Continued — {timestamp}\n\n\n" + summary
                : $"{timestamp}\n\n\n" + summary;
            meeting.Status = MeetingStatus.Ready;
            await _db.UpdateMeetingAsync(meeting);

            if (_settings.DeleteAudioAfterTranscription && meeting.AudioFilePath is not null)
            {
                try { File.Delete(meeting.AudioFilePath); } catch { }
                meeting.AudioFilePath = null;
                await _db.UpdateMeetingAsync(meeting);
            }

            SummarizingActive = false;
            SummarizingDone = true;
            SummarizingStatus = "Done";
            StatusMessage = "Complete!";
            StepChanged?.Invoke(this, "summarized");
            StatusChanged?.Invoke(this, "Complete!");

            ProcessingComplete?.Invoke(this, meeting);
        }
        catch (WhisperModelLoadException)
        {
            // The Whisper native runtime failed to load the model — the file may be
            // corrupt or incomplete. Clear the cached path so the next load retries FromPath,
            // then signal the view to run setup (re-download) before retrying.
            _transcription.UnloadModel();
            WhisperSetupRequired?.Invoke(this, meeting);
        }
        catch (Exception ex)
        {
            // Save whatever transcript we have so retry can skip transcription
            if (!string.IsNullOrWhiteSpace(meeting.Transcript))
                await _db.UpdateMeetingAsync(meeting);

            var message = ex is TaskCanceledException
                ? "Ollama timed out. The meeting may be too long for the current model. Try a faster model in Settings, then retry."
                : $"Processing failed: {ex.Message}";

            StatusChanged?.Invoke(this, message);
            ErrorOccurred?.Invoke(this, (message, meeting));
        }
    }

    public event EventHandler<(string message, Meeting meeting)>? ErrorOccurred;
    public event EventHandler<Meeting>? WhisperSetupRequired;

    /// <summary>
    /// Returns a human-readable warning string if Ollama is not usable, or null if it's fine.
    /// </summary>
    private async Task<string?> CheckOllamaAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.OllamaServerUrl))
            return "⚠ No Ollama server URL is configured. Transcription is saved — open Settings to set up Ollama and use Retry to get a summary.";

        if (string.IsNullOrWhiteSpace(_settings.OllamaDefaultModel))
            return "⚠ No Ollama model is selected. Transcription is saved — open Settings to choose a model and use Retry to get a summary.";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var reachable = await _ollama.IsAvailableAsync();
            if (!reachable)
                return $"⚠ Ollama is not running at {_settings.OllamaServerUrl}. Transcription is saved — start Ollama and use Retry to generate a summary.";
        }
        catch
        {
            return $"⚠ Could not reach Ollama at {_settings.OllamaServerUrl}. Transcription is saved — start Ollama and use Retry to generate a summary.";
        }

        return null;
    }
}
