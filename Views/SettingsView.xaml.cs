using MeetingNotes.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MeetingNotes.Views;

public partial class SettingsView : Page
{
    private readonly SettingsViewModel _vm;

    public SettingsView(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        LoadControls();
    }

    private void LoadControls()
    {
        WhisperModelBox.ItemsSource = _vm.WhisperModels;
        WhisperModelBox.SelectedItem = _vm.WhisperModel;

        ChunkIntervalBox.ItemsSource = new[] { "30 seconds", "60 seconds", "2 minutes" };
        ChunkIntervalBox.SelectedIndex = _vm.TranscriptionChunkSeconds switch
        {
            30 => 0, 120 => 2, _ => 1
        };

        OllamaUrlBox.Text = _vm.OllamaServerUrl;
        OllamaModelBox.ItemsSource = _vm.AvailableModels.Count > 0
            ? _vm.AvailableModels : new[] { _vm.OllamaModel };
        OllamaModelBox.SelectedItem = _vm.OllamaModel;

        SummaryPromptBox.Text = _vm.SummaryPrompt;
        WhisperFolderBox.Text = _vm.WhisperCacheFolder;

        AudioFormatBox.ItemsSource = _vm.AudioFormats;
        AudioFormatBox.SelectedItem = _vm.AudioFormat;

        Mp3BitrateBox.ItemsSource = new[] { "32 kbps", "64 kbps", "128 kbps" };
        Mp3BitrateBox.SelectedIndex = _vm.Mp3Bitrate switch
        {
            32 => 0, 128 => 2, _ => 1
        };

        DeleteAudioBox.IsChecked = _vm.DeleteAudioAfterTranscription;
        RecordingsFolderBox.Text = _vm.RecordingsFolder;

        ThemeBox.ItemsSource = _vm.Themes;
        ThemeBox.SelectedItem = _vm.Theme;

        StartupBox.IsChecked = _vm.LaunchAtStartup;
        MinimizeTrayBox.IsChecked = _vm.MinimizeToTrayOnClose;
        TrayTimerBox.IsChecked = _vm.ShowTrayTimer;

        AppWatcherEnabledBox.IsChecked = _vm.AppWatcherEnabled;
        WatchedAppsBox.Text = _vm.WatchedApps.Replace(",", "\n");
    }

    private async void TestOllama_Click(object sender, RoutedEventArgs e)
    {
        _vm.OllamaServerUrl = OllamaUrlBox.Text;
        OllamaStatusText.Text = "Connecting...";
        OllamaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(136, 136, 136));

        await _vm.TestOllamaConnectionAsync();

        OllamaStatusText.Text = _vm.OllamaStatus;
        OllamaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            _vm.OllamaConnected
                ? System.Windows.Media.Color.FromRgb(76, 175, 80)
                : System.Windows.Media.Color.FromRgb(196, 43, 28));

        if (_vm.OllamaConnected)
        {
            OllamaModelBox.ItemsSource = _vm.AvailableModels;
            OllamaModelBox.SelectedItem = _vm.OllamaModel;
        }
    }

    private void BrowseRecordingsFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.BrowseRecordingsFolderCommand.Execute(null);
        RecordingsFolderBox.Text = _vm.RecordingsFolder;
    }

    private void BrowseWhisperFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.BrowseWhisperCacheFolderCommand.Execute(null);
        WhisperFolderBox.Text = _vm.WhisperCacheFolder;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Reload original values — discard any unsaved edits
        LoadControls();

        // Navigate back to the previous content (meeting detail / empty state)
        if (NavigationService?.CanGoBack == true)
            NavigationService.GoBack();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.WhisperModel = WhisperModelBox.SelectedItem?.ToString() ?? "Base";
        _vm.WhisperCacheFolder = WhisperFolderBox.Text;
        _vm.TranscriptionChunkSeconds = ChunkIntervalBox.SelectedIndex switch
        {
            0 => 30, 2 => 120, _ => 60
        };
        _vm.OllamaServerUrl = OllamaUrlBox.Text;
        _vm.OllamaModel = OllamaModelBox.SelectedItem?.ToString() ?? _vm.OllamaModel;
        _vm.SummaryPrompt = SummaryPromptBox.Text;
        _vm.AudioFormat = AudioFormatBox.SelectedItem?.ToString() ?? "MP3";
        _vm.Mp3Bitrate = Mp3BitrateBox.SelectedIndex switch
        {
            0 => 32, 2 => 128, _ => 64
        };
        _vm.DeleteAudioAfterTranscription = DeleteAudioBox.IsChecked == true;
        _vm.RecordingsFolder = RecordingsFolderBox.Text;
        _vm.Theme = ThemeBox.SelectedItem?.ToString() ?? "Dark";
        _vm.LaunchAtStartup = StartupBox.IsChecked == true;
        _vm.MinimizeToTrayOnClose = MinimizeTrayBox.IsChecked == true;
        _vm.ShowTrayTimer = TrayTimerBox.IsChecked == true;
        _vm.AppWatcherEnabled = AppWatcherEnabledBox.IsChecked == true;
        _vm.WatchedApps = string.Join(",",
            WatchedAppsBox.Text.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        await _vm.SaveAsync();

        SaveStatusText.Text = "✓  Settings saved";
        SaveStatusText.Visibility = Visibility.Visible;

        await Task.Delay(2500);
        SaveStatusText.Visibility = Visibility.Collapsed;
    }
}
