using System.IO;
using System.Text;
using System.Windows.Documents;
using MeetingNotes.Models;
using MeetingNotes.Services;
using MeetingNotes.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfMsgBox = System.Windows.MessageBox;

namespace MeetingNotes.Views;

public partial class RecordingView : Page
{
    private readonly AudioCaptureService _audio;
    private readonly DatabaseService _db;
    private readonly AppSettings _settings;
    private MeetingViewModel? _meetingVm;
    private Meeting? _meeting;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _dotTimer;
    private DateTime _startTime;
    private bool _isRecording;
    private readonly double[] _waveformData = new double[40];

    public event EventHandler<(int meetingId, bool runAI)>? RecordingStopped;
    private bool _runAI = true;

    public RecordingView(AudioCaptureService audio, DatabaseService db, AppSettings settings)
    {
        InitializeComponent();
        _audio = audio;
        _db = db;
        _settings = settings;
        _audio.AudioLevelChanged += OnAudioLevel;
        WaveformDisplay.ItemsSource = _waveformData;
        InitializeDotAnimation();
    }

    public async void SetMeeting(MeetingViewModel vm, string folderName, bool runAI = true)
    {
        _runAI = runAI;
        _meetingVm = vm;
        MeetingTitleText.Text = vm.Title;
        FolderBadgeText.Text = $"📁 {folderName}";

        _meeting = await _db.GetMeetingAsync(vm.Id);
        if (_meeting is not null)
            await StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        if (_meeting is null) return;

        var ext = _settings.AudioFormat == "MP3" ? "mp3" : "wav";
        var fileName = $"{_meeting.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        var outputPath = Path.Combine(_settings.RecordingsFolder, fileName);

        _audio.StartRecording(outputPath, _settings.AudioFormat, _settings.Mp3Bitrate);

        _meeting.Status           = MeetingStatus.Recording;
        _meeting.RecordingStarted = DateTime.Now;
        _meeting.AudioFilePath    = outputPath;

        // Accumulate every file path so soft-delete can remove all of them
        _meeting.AudioFilePaths = string.IsNullOrEmpty(_meeting.AudioFilePaths)
            ? outputPath
            : _meeting.AudioFilePaths + ";" + outputPath;

        await _db.UpdateMeetingAsync(_meeting);

        _startTime = DateTime.Now;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _startTime;
            TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
        };
        _timer.Start();
        _isRecording = true;
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording || _meeting is null) return;

        StopButton.IsEnabled = false;
        _timer?.Stop();
        _dotTimer?.Stop();

        // StopRecording stops capture immediately and starts conversion on a background
        // thread (AudioSaveTask). ProcessingViewModel awaits that task as its first step.
        _audio.StopRecording();

        _meeting.RecordingEnded = DateTime.Now;
        _meeting.Status = MeetingStatus.Processing;

        if (!string.IsNullOrWhiteSpace(QuickNotesBox.Text))
            _meeting.MyNotes = AppendQuickNotes(_meeting.MyNotes, QuickNotesBox.Text);

        await _db.UpdateMeetingAsync(_meeting);

        _isRecording = false;
        RecordingStopped?.Invoke(this, (_meeting.Id, _runAI));
    }

    private void LoopbackBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isRecording) return;

        bool currentlyMuted = _audio.IsLoopbackMuted;
        if (!currentlyMuted)
        {
            var result = WpfMsgBox.Show(
                "Mute system audio (loopback) for the rest of this recording?\n\n" +
                "The recording will continue — system audio will be replaced with silence from this point on.",
                "Mute System Audio?",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        _audio.SetLoopbackMuted(!currentlyMuted);
        UpdateBadgeState(LoopbackBorder, LoopbackDot, LoopbackLabel,
            "System Audio", _audio.IsLoopbackMuted);
    }

    private void MicBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isRecording) return;

        bool currentlyMuted = _audio.IsMicMuted;
        if (!currentlyMuted)
        {
            var result = WpfMsgBox.Show(
                "Mute the microphone for the rest of this recording?\n\n" +
                "The recording will continue — microphone audio will be replaced with silence from this point on.",
                "Mute Microphone?",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        _audio.SetMicMuted(!currentlyMuted);
        UpdateBadgeState(MicBorder, MicDot, MicLabel,
            "Microphone", _audio.IsMicMuted);
    }

    private static void UpdateBadgeState(Border border, System.Windows.Shapes.Ellipse dot,
        System.Windows.Controls.TextBlock label, string baseName, bool muted)
    {
        if (muted)
        {
            border.Background  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 20, 20));
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(80, 30, 30));
            dot.Fill   = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(150, 50, 50));
            label.Text       = baseName + " (muted)";
            label.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(150, 50, 50));
        }
        else
        {
            border.Background  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(21, 31, 21));
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(26, 61, 26));
            dot.Fill   = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(76, 175, 80));
            label.Text       = baseName;
            label.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(76, 175, 80));
        }
    }

    private void OnAudioLevel(object? sender, float level)
    {
        Dispatcher.Invoke(() =>
        {
            // Shift waveform left and add new value
            for (int i = 0; i < _waveformData.Length - 1; i++)
                _waveformData[i] = _waveformData[i + 1];
            _waveformData[^1] = Math.Max(4, level * 100);
            WaveformDisplay.Items.Refresh();
        });
    }

    private void InitializeDotAnimation()
    {
        _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        bool visible = true;
        _dotTimer.Tick += (_, _) =>
        {
            RecDot.Opacity = visible ? 1 : 0;
            visible = !visible;
        };
        _dotTimer.Start();
    }

    /// <summary>
    /// Appends plain quick-notes text to existing notes (which may be RTF or plain text).
    /// Returns RTF when existing content is RTF; plain text otherwise.
    /// </summary>
    private static string AppendQuickNotes(string? existing, string quickNotes)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return quickNotes;

        // Existing notes are plain text — simple concatenation
        if (!existing.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
            return existing + "\n\n" + quickNotes;

        // Existing notes are RTF — load into a FlowDocument, append, re-serialize
        try
        {
            var doc = new FlowDocument();
            using (var inStream = new MemoryStream(Encoding.UTF8.GetBytes(existing)))
            {
                var inRange = new TextRange(doc.ContentStart, doc.ContentEnd);
                inRange.Load(inStream, System.Windows.DataFormats.Rtf);
            }
            doc.Blocks.Add(new Paragraph(new Run(quickNotes)));

            using var outStream = new MemoryStream();
            var outRange = new TextRange(doc.ContentStart, doc.ContentEnd);
            outRange.Save(outStream, System.Windows.DataFormats.Rtf);
            return Encoding.UTF8.GetString(outStream.ToArray());
        }
        catch
        {
            // If RTF manipulation fails fall back to simple concat
            return existing + "\n\n" + quickNotes;
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _audio.AudioLevelChanged -= OnAudioLevel;
        _timer?.Stop();
        _dotTimer?.Stop();

        // Safety net: if the frame was navigated away while a recording was still
        // active (e.g. WPF back/forward journal), stop the audio service so
        // IsRecording never gets stuck as true.
        if (_isRecording)
        {
            _isRecording = false;
            Task.Run(() => _audio.StopRecording());
        }
    }
}
