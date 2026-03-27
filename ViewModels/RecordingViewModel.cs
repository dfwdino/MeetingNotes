using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingNotes.Models;
using MeetingNotes.Services;
using System.Collections.ObjectModel;
using System.Timers;

namespace MeetingNotes.ViewModels;

public partial class RecordingViewModel : BaseViewModel, IDisposable
{
    private readonly AudioCaptureService _audio;
    private readonly DatabaseService _db;
    private readonly AppSettings _settings;
    private System.Timers.Timer? _timer;
    private DateTime _startTime;
    private Meeting? _meeting;

    [ObservableProperty] private string _timerDisplay = "00:00:00";
    [ObservableProperty] private string _meetingTitle = string.Empty;
    [ObservableProperty] private string _folderName = string.Empty;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private float _audioLevel;
    [ObservableProperty] private string _quickNotes = string.Empty;
    [ObservableProperty] private bool _systemAudioActive;
    [ObservableProperty] private bool _micActive;

    public ObservableCollection<float> WaveformLevels { get; } = new(Enumerable.Repeat(0.1f, 40));

    public event EventHandler? RecordingStopped;

    public RecordingViewModel(AudioCaptureService audio, DatabaseService db, AppSettings settings)
    {
        _audio = audio;
        _db = db;
        _settings = settings;
        _audio.AudioLevelChanged += OnAudioLevelChanged;
    }

    public void SetMeeting(Meeting meeting, string folderName)
    {
        _meeting = meeting;
        MeetingTitle = meeting.Title;
        FolderName = folderName;
    }

    [RelayCommand]
    public async Task StartRecordingAsync()
    {
        if (_meeting is null) return;

        var ext = _settings.AudioFormat == "MP3" ? "mp3" : "wav";
        var fileName = $"{_meeting.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        var outputPath = Path.Combine(_settings.RecordingsFolder, fileName);

        _audio.StartRecording(outputPath, _settings.AudioFormat, _settings.Mp3Bitrate);

        _meeting.Status = MeetingStatus.Recording;
        _meeting.RecordingStarted = DateTime.Now;
        _meeting.AudioFilePath = outputPath;
        await _db.UpdateMeetingAsync(_meeting);

        _startTime = DateTime.Now;
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTimerTick;
        _timer.Start();

        IsRecording = true;
        SystemAudioActive = true;
        MicActive = true;
    }

    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (!IsRecording || _meeting is null) return;

        _timer?.Stop();
        _timer?.Dispose();

        var audioPath = _audio.StopRecording();

        _meeting.RecordingEnded = DateTime.Now;
        _meeting.Status = MeetingStatus.Processing;

        if (!string.IsNullOrWhiteSpace(QuickNotes))
            _meeting.MyNotes = QuickNotes;

        await _db.UpdateMeetingAsync(_meeting);

        IsRecording = false;
        RecordingStopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimerTick(object? sender, ElapsedEventArgs e)
    {
        var elapsed = DateTime.Now - _startTime;
        App.Current.Dispatcher.Invoke(() =>
            TimerDisplay = elapsed.ToString(@"hh\:mm\:ss"));
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        AudioLevel = level;
        App.Current.Dispatcher.Invoke(() =>
        {
            WaveformLevels.RemoveAt(0);
            WaveformLevels.Add(level);
        });
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _audio.AudioLevelChanged -= OnAudioLevelChanged;
    }
}
