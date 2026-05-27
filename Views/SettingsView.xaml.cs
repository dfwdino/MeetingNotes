using MeetingNotes.Services;
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

        WhisperBeamSizeBox.ItemsSource = (string[])["1 — Fastest", "3 — Balanced", "5 — Accurate (recommended)", "8 — Most accurate"];
        WhisperBeamSizeBox.SelectedIndex = _vm.WhisperBeamSize switch
        {
            1 => 0, 3 => 1, 8 => 3, _ => 2   // default to index 2 = beam 5
        };

        WhisperInitialPromptBox.Text = _vm.WhisperInitialPrompt;

        ChunkIntervalBox.ItemsSource = (string[])["30 seconds", "60 seconds", "2 minutes"];
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

        Mp3BitrateBox.ItemsSource = (string[])["32 kbps", "64 kbps", "128 kbps"];
        Mp3BitrateBox.SelectedIndex = _vm.Mp3Bitrate switch
        {
            32 => 0, 128 => 2, _ => 1
        };

        DeleteAudioBox.IsChecked = _vm.DeleteAudioAfterTranscription;
        RunAiByDefaultBox.IsChecked = _vm.RunAiByDefault;
        AutoStopBox.IsChecked = _vm.AutoStopOnSilenceEnabled;
        AutoStopMinutesBox.ItemsSource = (string[])["2 minutes", "5 minutes", "10 minutes", "15 minutes", "30 minutes"];
        AutoStopMinutesBox.SelectedIndex = _vm.AutoStopSilenceMinutes switch
        {
            2 => 0, 10 => 2, 15 => 3, 30 => 4, _ => 1
        };
        DefaultMeetingTitleBox.Text = _vm.DefaultMeetingTitle;

        LoopbackDeviceBox.ItemsSource = _vm.AvailableLoopbackDevices;
        LoopbackDeviceBox.SelectedItem = _vm.AvailableLoopbackDevices
            .FirstOrDefault(d => d.Id == _vm.LoopbackDeviceId) ?? _vm.AvailableLoopbackDevices.FirstOrDefault();

        MicDeviceBox.ItemsSource = _vm.AvailableMicDevices;
        MicDeviceBox.SelectedItem = _vm.AvailableMicDevices
            .FirstOrDefault(d => d.Id == _vm.MicDeviceId) ?? _vm.AvailableMicDevices.FirstOrDefault();

        RecordingsFolderBox.Text = _vm.RecordingsFolder;

        ThemeBox.ItemsSource = _vm.Themes;
        ThemeBox.SelectedItem = _vm.Theme;

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

    private void WhisperModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelDownloadWarning is null) return;

        var selected = WhisperModelBox.SelectedItem?.ToString() ?? string.Empty;
        var modelFileName = $"ggml-{selected.ToLower()}.bin";
        var cacheFolder = string.IsNullOrWhiteSpace(WhisperFolderBox?.Text)
            ? _vm.WhisperCacheFolder
            : WhisperFolderBox.Text;
        var modelPath = System.IO.Path.Combine(cacheFolder, modelFileName);

        // Show the download warning only if the selected model file doesn't exist yet
        ModelDownloadWarning.Visibility = System.IO.File.Exists(modelPath)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

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
        _vm.WhisperBeamSize = WhisperBeamSizeBox.SelectedIndex switch
        {
            0 => 1, 1 => 3, 3 => 8, _ => 5   // index 2 = "Accurate (recommended)" = 5
        };
        _vm.WhisperInitialPrompt = WhisperInitialPromptBox.Text.Trim();
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
        _vm.RunAiByDefault = RunAiByDefaultBox.IsChecked == true;
        _vm.AutoStopOnSilenceEnabled = AutoStopBox.IsChecked == true;
        _vm.AutoStopSilenceMinutes = AutoStopMinutesBox.SelectedIndex switch
        {
            0 => 2, 2 => 10, 3 => 15, 4 => 30, _ => 5
        };
        _vm.DefaultMeetingTitle = DefaultMeetingTitleBox.Text.Trim();

        if (LoopbackDeviceBox.SelectedItem is AudioDeviceInfo loopbackDevice)
            _vm.LoopbackDeviceId = loopbackDevice.Id;
        if (MicDeviceBox.SelectedItem is AudioDeviceInfo micDevice)
            _vm.MicDeviceId = micDevice.Id;

        _vm.RecordingsFolder = RecordingsFolderBox.Text;
        _vm.Theme = ThemeBox.SelectedItem?.ToString() ?? "Dark";
        _vm.MinimizeToTrayOnClose = MinimizeTrayBox.IsChecked == true;
        _vm.ShowTrayTimer = TrayTimerBox.IsChecked == true;
        _vm.AppWatcherEnabled = AppWatcherEnabledBox.IsChecked == true;
        _vm.WatchedApps = string.Join(",",
            WatchedAppsBox.Text.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        _vm.LogToDatabase = LogToDatabaseBox.IsChecked == true;
        _vm.LogToFile = LogToFileBox.IsChecked == true;
        _vm.LogFolder = LogFolderBox.Text;

        try
        {
            await _vm.SaveAsync();
            App.ApplyTheme(_vm.Theme);
            SaveStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80));
            SaveStatusText.Text = "✓  Settings saved";
        }
        catch (Exception ex)
        {
            SaveStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(196, 43, 28));
            SaveStatusText.Text = $"✗  Save failed: {ex.Message}";
        }

        SaveStatusText.Visibility = Visibility.Visible;
        await Task.Delay(2500);
        SaveStatusText.Visibility = Visibility.Collapsed;
    }
}
