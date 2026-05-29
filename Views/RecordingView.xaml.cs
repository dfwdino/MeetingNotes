using System.IO;
using System.Text;
using System.Windows.Documents;
using MeetingNotes.Models;
using MeetingNotes.Services;
using MeetingNotes.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfColor = System.Windows.Media.Color;
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

    public event EventHandler<(int meetingId, bool runAI, bool encryptAfter)>? RecordingStopped;
    private bool _runAI = true;
    private bool _encryptAfter;

    public RecordingView(AudioCaptureService audio, DatabaseService db, AppSettings settings)
    {
        InitializeComponent();
        _audio = audio;
        _db = db;
        _settings = settings;
        _audio.AudioLevelChanged  += OnAudioLevel;
        _audio.AutoStopRequested  += OnAutoStopRequested;
        WaveformDisplay.ItemsSource = _waveformData;
    }

    public async void SetMeeting(MeetingViewModel vm, string folderName, bool runAI = true, bool encryptAfter = false)
    {
        _runAI = runAI;
        _encryptAfter = encryptAfter;
        _meetingVm = vm;
        MeetingTitleText.Text = vm.Title;
        FolderBadgeText.Text = $"📁 {folderName}";

        _meeting = await _db.GetMeetingAsync(vm.Id);

        var loopbackDevices = AudioCaptureService.GetLoopbackDevices();
        var micDevices      = AudioCaptureService.GetMicDevices();

        PreLoopbackDeviceBox.ItemsSource  = loopbackDevices;
        PreLoopbackDeviceBox.SelectedItem = loopbackDevices
            .FirstOrDefault(d => d.Id == _settings.LoopbackDeviceId)
            ?? loopbackDevices.First();

        PreMicDeviceBox.ItemsSource  = micDevices;
        PreMicDeviceBox.SelectedItem = micDevices
            .FirstOrDefault(d => d.Id == _settings.MicDeviceId)
            ?? micDevices.First();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meeting is null) return;

        StartButton.IsEnabled = false;

        var loopbackId = (PreLoopbackDeviceBox.SelectedItem as AudioDeviceInfo)?.Id;
        var micId      = (PreMicDeviceBox.SelectedItem as AudioDeviceInfo)?.Id;

        PreRecordPanel.Visibility  = Visibility.Collapsed;
        RecordingPanel.Visibility  = Visibility.Visible;
        InitializeDotAnimation();

        await StartRecordingAsync(loopbackId, micId);
    }

    private async Task StartRecordingAsync(string? loopbackDeviceId = null, string? micDeviceId = null)
    {
        if (_meeting is null) return;

        var ext = _settings.AudioFormat == "MP3" ? "mp3" : "wav";
        var fileName = $"{_meeting.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        var outputPath = Path.Combine(_settings.RecordingsFolder, fileName);

        _audio.AutoStopEnabled        = _settings.AutoStopOnSilenceEnabled;
        _audio.AutoStopSilenceTimeout = TimeSpan.FromMinutes(_settings.AutoStopSilenceMinutes);
        _audio.StartRecording(outputPath, _settings.AudioFormat, _settings.Mp3Bitrate,
            loopbackDeviceId, micDeviceId);

        // Show the selected device name in the loopback badge
        if (PreLoopbackDeviceBox.SelectedItem is AudioDeviceInfo ld && !string.IsNullOrEmpty(ld.Id))
            LoopbackLabel.Text = ld.Name;

        _meeting.Status           = MeetingStatus.Recording;
        _meeting.RecordingStarted = DateTime.Now;
        _meeting.AudioFilePath    = outputPath;

        // Accumulate every file path so soft-delete can remove all of them
        _meeting.AudioFilePaths = string.IsNullOrEmpty(_meeting.AudioFilePaths)
            ? outputPath
            : $"{_meeting.AudioFilePaths};{outputPath}";

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

    private async void StopButton_Click(object sender, RoutedEventArgs e) => await DoStopAsync();

    private void OnAutoStopRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(async () => await DoStopAsync());

    private async Task DoStopAsync()
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
        RecordingStopped?.Invoke(this, (_meeting.Id, _runAI, _encryptAfter));
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
        TextBlock label, string baseName, bool muted)
    {
        if (muted)
        {
            border.Background  = new SolidColorBrush(WpfColor.FromRgb(30, 20, 20));
            border.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 30, 30));
            dot.Fill           = new SolidColorBrush(WpfColor.FromRgb(150, 50, 50));
            label.Text         = baseName + " (muted)";
            label.Foreground   = new SolidColorBrush(WpfColor.FromRgb(150, 50, 50));
        }
        else
        {
            border.Background  = new SolidColorBrush(WpfColor.FromRgb(21, 31, 21));
            border.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(26, 61, 26));
            dot.Fill           = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80));
            label.Text         = baseName;
            label.Foreground   = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80));
        }
    }

    private void OnAudioLevel(object? sender, float level)
    {
        Dispatcher.Invoke(() =>
        {
            _waveformData.AsSpan(1).CopyTo(_waveformData);
            _waveformData[^1] = Math.Max(4, Math.Min(56, level * 100));
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
        _audio.AutoStopRequested -= OnAutoStopRequested;
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
