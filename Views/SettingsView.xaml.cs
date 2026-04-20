using MeetingNotes.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfMsgBox = System.Windows.MessageBox;

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

        ProviderBox.ItemsSource = _vm.LlmProviders;
        ProviderBox.SelectedItem = _vm.LlmProvider == "LmStudio" ? "LM Studio" : "Ollama";
        ApplyProviderVisibility(_vm.LlmProvider);

        OllamaUrlBox.Text = _vm.OllamaServerUrl;
        OllamaModelBox.ItemsSource = _vm.AvailableModels.Count > 0
            ? _vm.AvailableModels : new[] { _vm.OllamaDefaultModel };
        OllamaModelBox.SelectedItem = _vm.OllamaDefaultModel;
        UpdateDefaultModelDisplay();

        LmStudioUrlBox.Text = _vm.LmStudioServerUrl;
        LmStudioApiKeyBox.Text = _vm.LmStudioApiKey;
        LmStudioModelBox.ItemsSource = _vm.LmStudioAvailableModels.Count > 0
            ? (System.Collections.IEnumerable)_vm.LmStudioAvailableModels
            : new[] { _vm.LmStudioDefaultModel };
        LmStudioModelBox.SelectedItem = _vm.LmStudioDefaultModel;
        UpdateLmStudioDefaultModelDisplay();

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

        LogToDatabaseBox.IsChecked = _vm.LogToDatabase;
        LogToFileBox.IsChecked = _vm.LogToFile;
        LogFolderBox.Text = _vm.LogFolder;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vm.LlmProvider == "LmStudio")
            await FetchLmStudioModelsAsync(showStatus: false);
        else
            await FetchOllamaModelsAsync(showStatus: false);
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ProviderBox.SelectedItem?.ToString();
        ApplyProviderVisibility(selected == "LM Studio" ? "LmStudio" : "Ollama");
    }

    private void ApplyProviderVisibility(string provider)
    {
        if (OllamaPanel is null || LmStudioPanel is null) return;
        OllamaPanel.Visibility = provider == "LmStudio" ? Visibility.Collapsed : Visibility.Visible;
        LmStudioPanel.Visibility = provider == "LmStudio" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Ollama ────────────────────────────────────────────────────────

    private async void TestOllama_Click(object sender, RoutedEventArgs e)
    {
        await FetchOllamaModelsAsync(showStatus: true);
    }

    private async Task FetchOllamaModelsAsync(bool showStatus)
    {
        _vm.OllamaServerUrl = OllamaUrlBox.Text;

        if (showStatus)
        {
            OllamaStatusText.Text = "Connecting...";
            OllamaStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(136, 136, 136));
        }

        await _vm.TestOllamaConnectionAsync();

        if (showStatus)
        {
            OllamaStatusText.Text = _vm.OllamaStatus;
            OllamaStatusText.Foreground = new SolidColorBrush(
                _vm.OllamaConnected
                    ? WpfColor.FromRgb(76, 175, 80)
                    : WpfColor.FromRgb(196, 43, 28));
        }

        if (_vm.OllamaConnected)
        {
            OllamaModelBox.ItemsSource = _vm.AvailableModels;
            OllamaModelBox.SelectedItem = _vm.OllamaDefaultModel;
            InstalledModelsPanel.Visibility = Visibility.Visible;
            ManualModelPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            InstalledModelsPanel.Visibility = Visibility.Collapsed;
            ManualModelPanel.Visibility = Visibility.Visible;
            ManualModelBox.Text = _vm.OllamaDefaultModel;
        }
        UpdateDefaultModelDisplay();
    }

    private void SetDefaultModelManual_Click(object sender, RoutedEventArgs e)
    {
        var name = ManualModelBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            WpfMsgBox.Show("Please enter a model name (e.g. llama3.2:3b).",
                "No Model Entered", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.OllamaDefaultModel = name;
        UpdateDefaultModelDisplay();
    }

    private void SetDefaultModel_Click(object sender, RoutedEventArgs e)
    {
        if (OllamaModelBox.SelectedItem is not string selected)
        {
            WpfMsgBox.Show("Please select a model from the Installed Models list first.",
                "No Model Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_vm.AvailableModels.Count > 0 && !_vm.AvailableModels.Contains(selected))
        {
            WpfMsgBox.Show($"\"{selected}\" is not in your installed Ollama models.\n\n" +
                "Click Test Connection to refresh the model list, then select a valid model.",
                "Model Not Installed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.OllamaDefaultModel = selected;
        UpdateDefaultModelDisplay();
    }

    private void UpdateDefaultModelDisplay()
    {
        DefaultModelLabel.Text = _vm.OllamaDefaultModel;

        bool modelsKnown = _vm.AvailableModels.Count > 0;
        bool isInstalled = _vm.AvailableModels.Contains(_vm.OllamaDefaultModel);

        if (!modelsKnown)
        {
            DefaultModelStatus.Text = "(connect to verify)";
            DefaultModelStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(136, 136, 136));
            DefaultModelWarning.Visibility = Visibility.Collapsed;
        }
        else if (isInstalled)
        {
            DefaultModelStatus.Text = "✓ Installed";
            DefaultModelStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80));
            DefaultModelWarning.Visibility = Visibility.Collapsed;
        }
        else
        {
            DefaultModelStatus.Text = "⚠ Not found";
            DefaultModelStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(239, 83, 80));
            DefaultModelWarning.Visibility = Visibility.Visible;
        }
    }

    // ── LM Studio ─────────────────────────────────────────────────────

    private async void TestLmStudio_Click(object sender, RoutedEventArgs e)
    {
        await FetchLmStudioModelsAsync(showStatus: true);
    }

    private async Task FetchLmStudioModelsAsync(bool showStatus)
    {
        _vm.LmStudioServerUrl = LmStudioUrlBox.Text;
        _vm.LmStudioApiKey = LmStudioApiKeyBox.Text;

        if (showStatus)
        {
            LmStudioStatusText.Text = "Connecting...";
            LmStudioStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(136, 136, 136));
        }

        await _vm.TestLmStudioConnectionAsync();

        if (showStatus)
        {
            LmStudioStatusText.Text = _vm.LmStudioStatus;
            LmStudioStatusText.Foreground = new SolidColorBrush(
                _vm.LmStudioConnected
                    ? WpfColor.FromRgb(76, 175, 80)
                    : WpfColor.FromRgb(196, 43, 28));
        }

        if (_vm.LmStudioConnected)
        {
            LmStudioModelBox.ItemsSource = _vm.LmStudioAvailableModels;
            LmStudioModelBox.SelectedItem = _vm.LmStudioDefaultModel;
            LmStudioModelsPanel.Visibility = Visibility.Visible;
        }

        UpdateLmStudioDefaultModelDisplay();
    }

    private void SetLmStudioDefaultModel_Click(object sender, RoutedEventArgs e)
    {
        if (LmStudioModelBox.SelectedItem is not string selected)
        {
            WpfMsgBox.Show("Please select a model from the Available Models list first.",
                "No Model Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.LmStudioDefaultModel = selected;
        UpdateLmStudioDefaultModelDisplay();
    }

    private void UpdateLmStudioDefaultModelDisplay()
    {
        LmStudioDefaultModelLabel.Text = string.IsNullOrWhiteSpace(_vm.LmStudioDefaultModel)
            ? "(none selected)" : _vm.LmStudioDefaultModel;

        bool hasModel = !string.IsNullOrWhiteSpace(_vm.LmStudioDefaultModel);
        bool modelsKnown = _vm.LmStudioAvailableModels.Count > 0;

        if (!hasModel)
        {
            LmStudioDefaultModelStatus.Text = "(connect to select)";
            LmStudioDefaultModelStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(136, 136, 136));
        }
        else if (!modelsKnown || _vm.LmStudioAvailableModels.Contains(_vm.LmStudioDefaultModel))
        {
            LmStudioDefaultModelStatus.Text = hasModel ? "✓ Selected" : string.Empty;
            LmStudioDefaultModelStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80));
        }
        else
        {
            LmStudioDefaultModelStatus.Text = "⚠ Not found";
            LmStudioDefaultModelStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(239, 83, 80));
        }
    }

    // ── Browse / folder ───────────────────────────────────────────────

    private void BrowseRecordingsFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.BrowseRecordingsFolderCommand.Execute(null);
        RecordingsFolderBox.Text = _vm.RecordingsFolder;
    }

    private void BrowseLogFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.BrowseLogFolderCommand.Execute(null);
        LogFolderBox.Text = _vm.LogFolder;
    }

    private void BrowseWhisperFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.BrowseWhisperCacheFolderCommand.Execute(null);
        WhisperFolderBox.Text = _vm.WhisperCacheFolder;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        LoadControls();

        if (NavigationService?.CanGoBack == true)
            NavigationService.GoBack();
        else if (System.Windows.Window.GetWindow(this) is MainWindow mw)
            mw.GoBackFromSettings();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.WhisperModel = WhisperModelBox.SelectedItem?.ToString() ?? "Base";
        _vm.WhisperCacheFolder = WhisperFolderBox.Text;
        _vm.TranscriptionChunkSeconds = ChunkIntervalBox.SelectedIndex switch
        {
            0 => 30, 2 => 120, _ => 60
        };

        var selectedProvider = ProviderBox.SelectedItem?.ToString();
        _vm.LlmProvider = selectedProvider == "LM Studio" ? "LmStudio" : "Ollama";

        _vm.OllamaServerUrl = OllamaUrlBox.Text;
        _vm.SummaryPrompt = SummaryPromptBox.Text;

        _vm.LmStudioServerUrl = LmStudioUrlBox.Text;
        _vm.LmStudioApiKey = LmStudioApiKeyBox.Text;

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
        _vm.LogToDatabase = LogToDatabaseBox.IsChecked == true;
        _vm.LogToFile = LogToFileBox.IsChecked == true;
        _vm.LogFolder = LogFolderBox.Text;

        await _vm.SaveAsync();
        App.ApplyTheme(_vm.Theme);

        SaveStatusText.Text = "✓  Settings saved";
        SaveStatusText.Visibility = Visibility.Visible;

        await Task.Delay(2500);
        SaveStatusText.Visibility = Visibility.Collapsed;
    }
}
