using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetingNotes.Models;
using MeetingNotes.Services;

namespace MeetingNotes.ViewModels;

public partial class ProcessingViewModel : BaseViewModel
{
    private readonly TranscriptionService _transcription;
    private readonly OllamaService _ollama;
    private readonly DatabaseService _db;
    private readonly AppSettings _settings;

    [ObservableProperty] private string _statusMessage = "Preparing...";
    [ObservableProperty] private string _transcribingStatus = "Waiting";
    [ObservableProperty] private string _summarizingStatus = "Waiting";
    [ObservableProperty] private bool _transcribingActive;
    [ObservableProperty] private bool _summarizingActive;
    [ObservableProperty] private bool _transcribingDone;
    [ObservableProperty] private bool _summarizingDone;
    [ObservableProperty] private string _liveTranscriptPreview = string.Empty;

    public event EventHandler<Meeting>? ProcessingComplete;
    public event EventHandler<string>? SegmentTranscribed;
    public event EventHandler<string>? StepChanged;
    public event EventHandler<string>? StatusChanged;

    public ProcessingViewModel(TranscriptionService transcription, OllamaService ollama,
        DatabaseService db, AppSettings settings)
    {
        _transcription = transcription;
        _ollama = ollama;
        _db = db;
        _settings = settings;
    }

    /// <param name="appendTranscript">
    /// When true (re-recording on an existing meeting), always transcribe the new audio
    /// and append it to any existing transcript.  When false (first recording or retry),
    /// transcription is skipped if a transcript already exists.
    /// </param>
    public async Task ProcessMeetingAsync(Meeting meeting, bool appendTranscript = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
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

            // Step 2: Summarize
            // In append mode: summarize only the new portion, then append to the existing
            // summary so the original is never lost.
            // In normal mode: summarize the full transcript (first run or explicit re-process).
            StatusMessage = "Generating summary with Ollama...";
            SummarizingStatus = "In progress";
            SummarizingActive = true;
            StepChanged?.Invoke(this, "summarizing");
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
}
