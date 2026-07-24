using MeetingNotes.Models;
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

        // Append version from <Version> in .csproj to the title bar (strip +git-hash suffix)
        var rawVer = (System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)
            ?.InformationalVersion ?? string.Empty;
        var ver = rawVer.Contains('+') ? rawVer[..rawVer.IndexOf('+')] : rawVer;
        if (!string.IsNullOrEmpty(ver))
            Title = $"Meeting Notes  v{ver}";
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

    // Cursor + tooltip feedback for actions blocked while a recording is active.
    // Evaluated on every hover so no recording start/stop event wiring is needed.
    private const string RecordingBlockedTip =
        "Not available while recording.\nStop the recording first.";
    private readonly Dictionary<FrameworkElement, object?> _preRecordingToolTips = [];

    private void BlockedNav_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement fe)
            ApplyBlockedState(fe, System.Windows.Input.Cursors.Hand);
    }

    private void BlockedButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement fe)
            ApplyBlockedState(fe, null);
    }

    private void ApplyBlockedState(FrameworkElement fe, System.Windows.Input.Cursor? normalCursor)
    {
        if (IsRecordingActive())
        {
            fe.Cursor = System.Windows.Input.Cursors.No;
            if (fe.ToolTip as string != RecordingBlockedTip)
            {
                _preRecordingToolTips[fe] = fe.ToolTip; // remember the element's own tooltip (if any)
                fe.ToolTip = RecordingBlockedTip;
                System.Windows.Controls.ToolTipService.SetInitialShowDelay(fe, 200);
            }
        }
        else
        {
            fe.Cursor = normalCursor;
            if (fe.ToolTip as string == RecordingBlockedTip)
            {
                _preRecordingToolTips.Remove(fe, out var original);
                fe.ToolTip = original;
                fe.ClearValue(System.Windows.Controls.ToolTipService.InitialShowDelayProperty);
            }
        }
    }

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
        if (IsRecordingActive()) return;
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
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;

        if (IsRecordingActive())
        {
            // During recording: add to list but don't switch — switching replaces Meetings
            // and SelectedFolder, which breaks OnRecordingStopped's meeting lookup.
            await _vm.CreateFolderAsync(dialog.FolderName);
            return;
        }

        await _vm.AddFolderAsync(dialog.FolderName);
        FolderList.ItemsSource = _vm.Folders;
        MeetingList.ItemsSource = _vm.Meetings;
        NewMeetingButton.Visibility = Visibility.Visible;
        UpdateFolderTitle();
        ShowEmptyState();
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

    private async void EditMeeting_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not MeetingViewModel meeting) return;

        var dialog = new RenameMeetingDialog(meeting.Title) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.NewTitle)) return;

        meeting.Title = dialog.NewTitle;
        var db = App.GetService<Services.DatabaseService>();
        var record = await db.GetMeetingAsync(meeting.Id);
        if (record is not null)
        {
            record.Title = dialog.NewTitle;
            await db.UpdateMeetingAsync(record);
        }

        // Refresh the detail view header if this meeting is open.
        // Skip during recording — navigating away from RecordingView triggers
        // Page_Unloaded which stops the audio capture as a safety net.
        if (_vm.SelectedMeeting?.Id == meeting.Id && !IsRecordingActive())
            ShowMeetingDetail(meeting);
    }

    private async void DeleteMeeting_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent triggering MeetingItem_Click
        if (IsRecordingActive()) return;
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

    // Debounce so we hit the DB once per pause in typing, not once per keystroke.
    private System.Windows.Threading.DispatcherTimer? _searchTimer;

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_searchTimer is null)
        {
            _searchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchTimer.Tick += async (_, _) =>
            {
                _searchTimer.Stop();
                await RunSearchAsync();
            };
        }
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    /// <summary>
    /// Searches title, transcript, summary, and notes across ALL folders.
    /// Empty query restores the current folder's meeting list.
    /// </summary>
    private async Task RunSearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            MeetingList.ItemsSource = _vm.Meetings;
            return;
        }

        var results = await App.GetService<Services.DatabaseService>().SearchMeetingsAsync(query);
        // The user may have kept typing while the query ran — drop stale results.
        if (SearchBox.Text.Trim() != query) return;
        MeetingList.ItemsSource = results.Select(m => new MeetingViewModel(m)).ToList();
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
        if (sender is System.Windows.Controls.TextBox tb && tb.Tag is FolderViewModel folder && folder.IsEditing)
        {
            folder.Name = _folderNameBeforeEdit;
            folder.IsEditing = false;
        }
    }

    // ── Move meeting ───────────────────────────────────────────────────
    private void MoveMeeting_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsRecordingActive()) return;
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

    private MeetingDetailView ShowMeetingDetail(MeetingViewModel meeting)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
        var page = App.GetService<MeetingDetailView>();
        page.MeetingSplit += OnMeetingSplit;
        page.LoadMeeting(meeting);
        ContentFrame.Navigate(page);
        return page;
    }

    private void OnMeetingSplit(object? sender, Meeting newMeeting)
    {
        Dispatcher.Invoke(() =>
        {
            var vm = new MeetingViewModel(newMeeting);
            _vm.Meetings.Insert(0, vm);
            if (_vm.SelectedFolder is not null)
                _vm.SelectedFolder.MeetingCount++;
            MeetingList.ItemsSource = _vm.Meetings;
        });
    }

    public RecordingView ShowRecordingView(MeetingViewModel meeting, bool runAI = true, bool encryptAfter = false)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
        var page = App.GetService<RecordingView>();
        page.SetMeeting(meeting, _vm.SelectedFolder?.Name ?? string.Empty, runAI, encryptAfter);
        page.RecordingStopped += OnRecordingStopped;
        ContentFrame.Navigate(page);
        return page;
    }

    public void ShowProcessingView(MeetingViewModel meetingVm, bool appendTranscript = false,
        bool runAI = true, bool encryptAfter = false, bool forceTranscribe = false)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Visible;
        var page = App.GetService<ProcessingView>();
        page.ProcessingComplete += (_, meeting) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await _vm.RefreshMeetingAsync(meeting.Id);
                MeetingList.ItemsSource = _vm.Meetings;
                var vm = _vm.Meetings.FirstOrDefault(m => m.Id == meeting.Id);
                if (vm is not null)
                {
                    var detailPage = ShowMeetingDetail(vm);
                    if (encryptAfter)
                        await detailPage.EncryptMeetingNowAsync();
                }
            });
        };
        page.StartProcessing(meetingVm.Id, appendTranscript, runAI, forceTranscribe);
        ContentFrame.Navigate(page);
    }

    private async void OnRecordingStopped(object? sender, (int meetingId, bool runAI, bool encryptAfter) args)
    {
        var vm = _vm.Meetings.FirstOrDefault(m => m.Id == args.meetingId);
        if (vm is null) return;
        // Only append when the existing transcript is real plaintext — not ciphertext from an
        // encrypted meeting (which would corrupt the transcript if appended to). Checked
        // against the DB row because list VMs don't carry the transcript column.
        var meeting = await App.GetService<Services.DatabaseService>().GetMeetingAsync(args.meetingId);
        bool append = meeting is not null
            && !string.IsNullOrWhiteSpace(meeting.Transcript)
            && !meeting.IsEncrypted;
        ShowProcessingView(vm, append, args.runAI, args.encryptAfter);
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
        int count = await App.GetService<Services.DatabaseService>().GetTrashCountAsync();
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
        {
            var settings = App.GetService<Models.AppSettings>();
            ShowRecordingView(_vm.SelectedMeeting, settings.RunAiByDefault);
        }
    }

    private void UpdateFolderTitle()
    {
        FolderTitleText.Text = _vm.SelectedFolder?.Name ?? "All Meetings";
    }

    // ── Global record hotkey (Ctrl+Alt+R) ──────────────────────────────
    private const int RecordHotkeyId = 0x4D4E; // "MN"
    private const int WmHotkey = 0x0312;
    private System.Windows.Interop.HwndSource? _hwndSource;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var settings = App.GetService<Models.AppSettings>();
        if (!settings.GlobalRecordHotkeyEnabled) return;

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(HotkeyHook);

        const uint modAlt = 0x0001, modControl = 0x0002;
        const uint vkR = 0x52;
        if (!RegisterHotKey(handle, RecordHotkeyId, modControl | modAlt, vkR))
            _ = App.GetService<Services.IAppLogger>().WarnAsync(
                "Could not register Ctrl+Alt+R global hotkey — another app already uses it.",
                nameof(MainWindow));
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == RecordHotkeyId)
        {
            handled = true;
            _ = Dispatcher.InvokeAsync(ToggleRecordingFromHotkeyAsync);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Ctrl+Alt+R: stops the active recording, or starts a new meeting recording
    /// in the selected (or first) folder — works even when minimized to tray.
    /// </summary>
    private async Task ToggleRecordingFromHotkeyAsync()
    {
        if (IsRecordingActive())
        {
            if (ContentFrame.Content is RecordingView recordingView)
                await recordingView.StopRecordingExternallyAsync();
            return;
        }

        Show();
        Activate();

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
        if (_vm.SelectedMeeting is null) return;

        var settings = App.GetService<Models.AppSettings>();
        var page = ShowRecordingView(_vm.SelectedMeeting, settings.RunAiByDefault);
        await page.StartImmediatelyAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource is not null)
        {
            UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, RecordHotkeyId);
            _hwndSource.RemoveHook(HotkeyHook);
            _hwndSource = null;
        }
        base.OnClosed(e);
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
