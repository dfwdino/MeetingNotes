using MeetingNotes.Models;
using MeetingNotes.Services;
using System.Windows;

namespace MeetingNotes.Views;

public partial class SetupView : Window
{
    private readonly TranscriptionService _transcription;
    private readonly AppSettings _settings;

    public SetupView(TranscriptionService transcription, AppSettings settings)
    {
        InitializeComponent();
        _transcription = transcription;
        _settings = settings;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            long totalBytes = 148_000_000; // approximate base model size

            var progress = new Progress<long>(bytesRead =>
            {
                Dispatcher.Invoke(() =>
                {
                    var pct = Math.Min(100, (int)(bytesRead * 100 / totalBytes));
                    ProgressPercent.Text = $"{pct}%";
                    ProgressLabel.Text = $"Downloading...  {bytesRead / 1_000_000} MB";
                    ProgressBar.Width = Math.Max(0, ProgressBarTrack.ActualWidth * pct / 100.0);
                });
            });

            var modelPath = await _transcription.EnsureModelAsync(
                _settings.WhisperModel, _settings.WhisperCacheFolder, progress);

            // Verify the downloaded model actually loads before declaring success
            ProgressLabel.Text = "Verifying model...";
            ProgressPercent.Text = "";
            await Task.Run(() => _transcription.LoadModel(modelPath));

            StatusText.Text = "✓  Ready to go!";
            StatusText.Visibility = Visibility.Visible;
            DownloadButton.Content = "Open Meeting Notes";
            DownloadButton.IsEnabled = true;
            DownloadButton.Click -= DownloadButton_Click;
            DownloadButton.Click += (_, _) => DialogResult = true;
        }
        catch (Exception ex)
        {
            // Clean up any partial/corrupt file so the user can retry cleanly
            var modelPath = System.IO.Path.Combine(
                _settings.WhisperCacheFolder,
                $"ggml-{_settings.WhisperModel.ToLower()}.bin");
            try { if (System.IO.File.Exists(modelPath)) System.IO.File.Delete(modelPath); } catch { }

            ProgressPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Error: {ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Visibility = Visibility.Visible;
            DownloadButton.Content = "Download & Get Started";
            DownloadButton.IsEnabled = true;
        }
    }
}
