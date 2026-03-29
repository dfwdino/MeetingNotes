using MeetingNotes.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace MeetingNotes.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _listPanelCollapsed;
    private bool _sidebarCollapsed;
    private bool _searchVisible;
    private string _folderNameBeforeEdit = string.Empty;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        FolderList.ItemsSource = _vm.Folders;
        MeetingList.ItemsSource = _vm.Meetings;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.LoadAsync();
        FolderList.ItemsSource = _vm.Folders;
        MeetingList.ItemsSource = _vm.Meetings;
        UpdateFolderTitle();
        await RefreshTrashCountAsync();
        // Show New button only when a folder is selected (set by LoadAsync → SelectFolderAsync)
        NewMeetingButton.Visibility = _vm.SelectedFolder is not null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsRecordingActive() =>
        App.GetService<Services.AudioCaptureService>().IsRecording;

    private async void FolderItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsRecordingActive()) return; // don't navigate away during recording

        if (sender is System.Windows.Controls.Border border &&
            border.Tag is FolderViewModel folder)
        {
            await _vm.SelectFolderAsync(folder);
            MeetingList.ItemsSource = _vm.Meetings;
            NewMeetingButton.Visibility = Visibility.Visible;
            UpdateFolderTitle();
            ShowEmptyState();
        }
    }

    private async void AllMeetings_Click(object sender, MouseButtonEventArgs e)
    {
        foreach (var f in _vm.Folders) f.IsSelected = false;
        FolderTitleText.Text = "All Meetings";
        NewMeetingButton.Visibility = Visibility.Collapsed;
        ShowEmptyState();
    }

    private async void Trash_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsRecordingActive()) return;
        foreach (var f in _vm.Folders) f.IsSelected = false;
        FolderTitleText.Text = "🗑  Trash";
        NewMeetingButton.Visibility = Visibility.Collapsed;
        await _vm.LoadTrashAsync();
        MeetingList.ItemsSource = _vm.Meetings;
        ShowEmptyState();
    }

    private async void HideFromTrash_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button btn && btn.Tag is MeetingViewModel meeting)
        {
            await _vm.HideFromTrashAsync(meeting);
            MeetingList.ItemsSource = _vm.Meetings;
            await RefreshTrashCountAsync();
            ShowEmptyState();
        }
    }

    private async void RestoreMeeting_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button btn && btn.Tag is MeetingViewModel meeting)
        {
            var result = System.Windows.MessageBox.Show(
                $"Restore \"{meeting.Title}\"?\n\nOnly the notes, transcript, and AI summary will be restored.\nThe original audio recording was permanently deleted and cannot be recovered.",
                "Restore Meeting",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _vm.RestoreMeetingAsync(meeting);
                MeetingList.ItemsSource = _vm.Meetings;
                await RefreshTrashCountAsync();
                ShowEmptyState();
            }
        }
    }

    private void MeetingItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsRecordingActive()) return; // don't navigate away during recording

        if (sender is System.Windows.Controls.Border border &&
            border.Tag is MeetingViewModel meeting)
        {
            _vm.SelectMeeting(meeting);
            ShowMeetingDetail(meeting);
        }
    }

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewFolderDialog { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            await _vm.AddFolderAsync(dialog.FolderName);
            FolderList.ItemsSource = _vm.Folders;
            MeetingList.ItemsSource = _vm.Meetings;
            NewMeetingButton.Visibility = Visibility.Visible; // new folder auto-selects
            UpdateFolderTitle();
        }
    }

    private async void NewMeetingButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingActive())
        {
            System.Windows.MessageBox.Show(
                "A recording is in progress.\n\nPlease stop the recording before creating a new meeting.",
                "Recording in Progress",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (_vm.SelectedFolder is null)
        {
            System.Windows.MessageBox.Show("Please select a folder first.", "No folder selected",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        await _vm.AddMeetingAsync();
        MeetingList.ItemsSource = _vm.Meetings;
        if (_vm.SelectedMeeting is not null)
            ShowMeetingDetail(_vm.SelectedMeeting);
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button btn &&
            btn.Tag is FolderViewModel folder)
        {
            var result = System.Windows.MessageBox.Show(
                $"Delete folder \"{folder.Name}\" and all its meetings?\nThis cannot be undone.",
                "Delete Folder",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _vm.DeleteFolderAsync(folder);
                FolderList.ItemsSource = _vm.Folders;
                MeetingList.ItemsSource = _vm.Meetings;
                NewMeetingButton.Visibility = Visibility.Collapsed;
                await RefreshTrashCountAsync();
                ShowEmptyState();
            }
        }
    }

    private async void DeleteMeeting_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent triggering MeetingItem_Click
        if (sender is System.Windows.Controls.Button btn &&
            btn.Tag is MeetingViewModel meeting)
        {
            var result = System.Windows.MessageBox.Show(
                $"Delete \"{meeting.Title}\"?\n\nThe audio file will be permanently deleted and cannot be recovered.\nNotes, transcript, and AI summary will be kept in Trash and can be restored at any time.",
                "Delete Meeting",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _vm.DeleteMeetingAsync(meeting);
                MeetingList.ItemsSource = _vm.Meetings;
                await RefreshTrashCountAsync();
                ShowEmptyState();
            }
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        new AboutDialog { Owner = this }.ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Block settings navigation during an active recording
        var audio = App.GetService<Services.AudioCaptureService>();
        if (audio.IsRecording)
        {
            System.Windows.MessageBox.Show(
                "A recording is in progress.\n\nPlease stop the recording before opening Settings.",
                "Recording in Progress",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var page = App.GetService<SettingsView>();
        ContentFrame.Navigate(page);
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
    }

    // ── Search toggle ─────────────────────────────────────────────────
    private void SearchToggle_Click(object sender, RoutedEventArgs e)
    {
        _searchVisible = !_searchVisible;
        SearchRow.Visibility = _searchVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_searchVisible)
            SearchBox.Focus();
        else
        {
            SearchBox.Text = string.Empty;
            MeetingList.ItemsSource = _vm.Meetings;
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        MeetingList.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _vm.Meetings
            : _vm.Meetings.Where(m => m.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ── Folder rename ──────────────────────────────────────────────────
    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button btn && btn.Tag is FolderViewModel folder)
        {
            _folderNameBeforeEdit = folder.Name;
            folder.IsEditing = true;
        }
    }

    private void FolderNameBox_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb && (bool)e.NewValue)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private async void FolderNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb && tb.Tag is FolderViewModel folder)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                await _vm.RenameFolderAsync(folder, tb.Text);
                if (_vm.SelectedFolder?.Id == folder.Id)
                    UpdateFolderTitle();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                folder.Name = _folderNameBeforeEdit;
                folder.IsEditing = false;
            }
        }
    }

    private void FolderNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb && tb.Tag is FolderViewModel folder)
        {
            folder.Name = _folderNameBeforeEdit;
            folder.IsEditing = false;
        }
    }

    // ── Move meeting ───────────────────────────────────────────────────
    private void MoveMeeting_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.Button btn ||
            btn.Tag is not MeetingViewModel meeting) return;

        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var folder in _vm.Folders.Where(f => f.Id != _vm.SelectedFolder?.Id))
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = folder.Name,
                Tag    = (meeting, folder)
            };
            item.Click += async (_, _) =>
            {
                var (m, f) = ((MeetingViewModel, FolderViewModel))item.Tag!;
                await _vm.MoveMeetingAsync(m, f.Id);
                MeetingList.ItemsSource = _vm.Meetings;
                ShowEmptyState();
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem
                { Header = "(No other folders)", IsEnabled = false });
        }

        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowMeetingDetail(MeetingViewModel meeting)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
        var page = App.GetService<MeetingDetailView>();
        page.LoadMeeting(meeting);
        ContentFrame.Navigate(page);
    }

    public void ShowRecordingView(MeetingViewModel meeting)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
        var page = App.GetService<RecordingView>();
        page.SetMeeting(meeting, _vm.SelectedFolder?.Name ?? string.Empty);
        page.RecordingStopped += OnRecordingStopped;
        ContentFrame.Navigate(page);
    }

    public void ShowProcessingView(MeetingViewModel meetingVm, bool appendTranscript = false)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
        var page = App.GetService<ProcessingView>();
        page.ProcessingComplete += async (_, meeting) =>
        {
            await _vm.RefreshMeetingAsync(meeting.Id);
            MeetingList.ItemsSource = _vm.Meetings;
            var vm = _vm.Meetings.FirstOrDefault(m => m.Id == meeting.Id);
            if (vm is not null) ShowMeetingDetail(vm);
        };
        page.StartProcessing(meetingVm.Id, appendTranscript);
        ContentFrame.Navigate(page);
    }

    private void OnRecordingStopped(object? sender, int meetingId)
    {
        var vm = _vm.Meetings.FirstOrDefault(m => m.Id == meetingId);
        if (vm is null) return;
        // If the meeting already had a transcript, this is a re-recording → append
        bool append = !string.IsNullOrWhiteSpace(vm.Transcript);
        ShowProcessingView(vm, append);
    }

    private void CollapseListButton_Click(object sender, RoutedEventArgs e)
        => SetListPanelCollapsed(true);

    private void ExpandListButton_Click(object sender, RoutedEventArgs e)
        => SetListPanelCollapsed(false);

    private void CollapseSidebarButton_Click(object sender, RoutedEventArgs e)
        => SetSidebarCollapsed(true);

    private void ExpandSidebarButton_Click(object sender, RoutedEventArgs e)
        => SetSidebarCollapsed(false);

    private void SetListPanelCollapsed(bool collapse)
    {
        _listPanelCollapsed = collapse;
        if (MeetingListPanel.Parent is System.Windows.Controls.Grid mainGrid)
        {
            mainGrid.ColumnDefinitions[2].Width = collapse ? new GridLength(0) : new GridLength(300);
            mainGrid.ColumnDefinitions[3].Width = collapse ? new GridLength(0) : new GridLength(1);
        }
        // Collapse button lives inside the panel — hide it when panel hides
        MeetingListPanel.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        // Expand button lives OUTSIDE the panel — always reachable
        ExpandListButton.Visibility = collapse ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSidebarCollapsed(bool collapse)
    {
        _sidebarCollapsed = collapse;
        if (MeetingListPanel.Parent is System.Windows.Controls.Grid mainGrid)
        {
            // Collapse to a thin 32px strip so the expand button stays visible
            // without overlapping the meeting list panel at all
            mainGrid.ColumnDefinitions[0].Width = collapse ? new GridLength(32) : new GridLength(220);
            mainGrid.ColumnDefinitions[1].Width = new GridLength(1);
        }
        SidebarContent.Visibility        = collapse ? Visibility.Collapsed : Visibility.Visible;
        SidebarCollapsedStrip.Visibility = collapse ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        EmptyState.Visibility = Visibility.Visible;
        ContentFrame.Visibility = Visibility.Collapsed;
    }

    /// <summary>Called by SettingsView Cancel when there's no Frame back-history.</summary>
    internal void GoBackFromSettings() => ShowEmptyState();

    private async Task RefreshTrashCountAsync()
    {
        var deleted = await App.GetService<Services.DatabaseService>().GetDeletedMeetingsAsync();
        int count = deleted.Count;
        TrashCountText.Text = count.ToString();
        TrashCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Called by the App Watcher toast when the user clicks "Record".
    /// Brings the window forward, picks a folder if needed, creates a meeting,
    /// and navigates straight to the recording view.
    /// </summary>
    public async Task StartRecordingForWatcher(string appName)
    {
        Show();
        Activate();

        if (IsRecordingActive()) return;

        // Ensure a folder is selected
        if (_vm.SelectedFolder is null && _vm.Folders.Count > 0)
        {
            await _vm.SelectFolderAsync(_vm.Folders[0]);
            MeetingList.ItemsSource = _vm.Meetings;
            NewMeetingButton.Visibility = Visibility.Visible;
            UpdateFolderTitle();
        }

        if (_vm.SelectedFolder is null) return;

        await _vm.AddMeetingAsync();
        MeetingList.ItemsSource = _vm.Meetings;

        if (_vm.SelectedMeeting is not null)
            ShowRecordingView(_vm.SelectedMeeting);
    }

    private void UpdateFolderTitle()
    {
        FolderTitleText.Text = _vm.SelectedFolder?.Name ?? "All Meetings";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var settings = App.GetService<Models.AppSettings>();
        if (settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            App.TrayIcon?.ShowBalloonTip(2000, "Meeting Notes",
                "App is still running in the tray.", System.Windows.Forms.ToolTipIcon.Info);
        }
        else
        {
            base.OnClosing(e);
        }
    }
}
