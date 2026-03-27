using MeetingNotes.Models;
using MeetingNotes.Services;
using MeetingNotes.ViewModels;
using System.Windows.Controls;

namespace MeetingNotes.Views;

public partial class ProcessingView : Page
{
    private readonly ProcessingViewModel _vm;

    public event EventHandler<Meeting>? ProcessingComplete;

    public ProcessingView(ProcessingViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    private MeetingNotes.Models.Meeting? _currentMeeting;

    private bool _appendTranscript;

    public async void StartProcessing(int meetingId, bool appendTranscript = false)
    {
        _appendTranscript = appendTranscript;

        var db = App.GetService<DatabaseService>();
        _currentMeeting = await db.GetMeetingAsync(meetingId);
        if (_currentMeeting is null) return;

        _vm.SegmentTranscribed += (_, line) =>
            Dispatcher.Invoke(() => LivePreviewText.Text = line + "\n" + LivePreviewText.Text);

        _vm.StepChanged   += (_, step) => Dispatcher.Invoke(() => UpdateStepUI(step));
        _vm.StatusChanged += (_, msg)  => Dispatcher.Invoke(() => StatusText.Text = msg);
        _vm.ProcessingComplete += (_, m) => ProcessingComplete?.Invoke(this, m);
        _vm.ErrorOccurred += (_, err)  => Dispatcher.Invoke(() => ShowError(err.message));

        await _vm.ProcessMeetingAsync(_currentMeeting, _appendTranscript);
    }

    private void ShowError(string message)
    {
        ErrorText.Text    = message;
        ErrorPanel.Visibility = System.Windows.Visibility.Visible;
    }

    private async void RetryButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_currentMeeting is null) return;
        ErrorPanel.Visibility = System.Windows.Visibility.Collapsed;
        LivePreviewText.Text  = string.Empty;
        await _vm.ProcessMeetingAsync(_currentMeeting);
    }

    private void UpdateStepUI(string step)
    {
        switch (step)
        {
            case "transcribing":
                Step1Icon.Foreground = System.Windows.Media.Brushes.White;
                TranscribeStatusText.Text = "In progress...";
                TranscribeStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212));
                break;
            case "transcribed":
                Step1Check.Visibility = System.Windows.Visibility.Visible;
                TranscribeStatusText.Text = "Done";
                TranscribeStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(76, 175, 80));
                break;
            case "summarizing":
                Step2Icon.Foreground = System.Windows.Media.Brushes.White;
                SummarizeStatusText.Text = "In progress...";
                SummarizeStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212));
                break;
            case "summarized":
                Step2Check.Visibility = System.Windows.Visibility.Visible;
                SummarizeStatusText.Text = "Done";
                SummarizeStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(76, 175, 80));
                break;
        }
    }
}
