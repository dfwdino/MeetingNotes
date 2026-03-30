using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingNotes.Models;
using MeetingNotes.Services;
using System.Collections.ObjectModel;

namespace MeetingNotes.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly DatabaseService _db;
    private readonly AppSettings _settings;

    [ObservableProperty] private ObservableCollection<FolderViewModel> _folders = [];
    [ObservableProperty] private ObservableCollection<MeetingViewModel> _meetings = [];
    [ObservableProperty] private FolderViewModel? _selectedFolder;
    [ObservableProperty] private MeetingViewModel? _selectedMeeting;
    [ObservableProperty] private string _currentView = "MeetingList"; // MeetingList, Recording, Processing, Settings
    [ObservableProperty] private bool _isLoading;

    public MainViewModel(DatabaseService db, AppSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        var folders = await _db.GetFoldersAsync();
        Folders = new ObservableCollection<FolderViewModel>(
            folders.Select(f => new FolderViewModel(f)));

        if (Folders.Count > 0)
            await SelectFolderAsync(Folders[0]);

        IsLoading = false;
    }

    public async Task SelectFolderAsync(FolderViewModel folder)
    {
        foreach (var f in Folders) f.IsSelected = false;
        folder.IsSelected = true;
        SelectedFolder = folder;
        SelectedMeeting = null;

        var meetings = await _db.GetMeetingsForFolderAsync(folder.Id);
        Meetings = new ObservableCollection<MeetingViewModel>(
            meetings.Select(m => new MeetingViewModel(m)));
    }

    [RelayCommand]
    public async Task AddFolderAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var folder = await _db.CreateFolderAsync(name);
        var vm = new FolderViewModel(folder);
        Folders.Add(vm);
        await SelectFolderAsync(vm);
    }

    public async Task RenameFolderAsync(FolderViewModel folder, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        await _db.RenameFolderAsync(folder.Id, newName);
        folder.Name = newName;
        folder.IsEditing = false;
    }

    public async Task MoveMeetingAsync(MeetingViewModel meeting, int targetFolderId)
    {
        await _db.MoveMeetingAsync(meeting.Id, targetFolderId);
        Meetings.Remove(meeting);

        // Update meeting counts on both folders
        var target = Folders.FirstOrDefault(f => f.Id == targetFolderId);
        if (target is not null) target.MeetingCount++;
        if (SelectedFolder is not null)
            SelectedFolder.MeetingCount = Math.Max(0, SelectedFolder.MeetingCount - 1);

        if (SelectedMeeting?.Id == meeting.Id)
            SelectedMeeting = null;
    }

    [RelayCommand]
    public async Task DeleteFolderAsync(FolderViewModel folder)
    {
        await _db.DeleteFolderAsync(folder.Id);
        Folders.Remove(folder);
        if (SelectedFolder == folder)
        {
            SelectedFolder = null;
            Meetings.Clear();
        }
    }

    [RelayCommand]
    public async Task AddMeetingAsync()
    {
        if (SelectedFolder is null) return;
        var title = $"Meeting — {DateTime.Now:MMM d, yyyy}";
        var meeting = await _db.CreateMeetingAsync(SelectedFolder.Id, title);
        var vm = new MeetingViewModel(meeting);
        Meetings.Insert(0, vm);
        SelectMeeting(vm);
        SelectedFolder.MeetingCount++;
    }

    public void SelectMeeting(MeetingViewModel meeting)
    {
        foreach (var m in Meetings) m.IsSelected = false;
        meeting.IsSelected = true;
        SelectedMeeting = meeting;
    }

    public async Task RefreshMeetingAsync(int meetingId)
    {
        var meeting = await _db.GetMeetingAsync(meetingId);
        if (meeting is null) return;
        var vm = Meetings.FirstOrDefault(m => m.Id == meetingId);
        if (vm is null) return;
        vm.Status = meeting.Status;
        vm.Transcript = meeting.Transcript;
        vm.Summary = meeting.Summary;
        vm.MyNotes = meeting.MyNotes;
        vm.DurationDisplay = meeting.DurationDisplay;
    }

    [RelayCommand]
    public async Task DeleteMeetingAsync(MeetingViewModel meeting)
    {
        // Soft delete — audio files removed, text kept in DB
        await _db.DeleteMeetingAsync(meeting.Id);
        Meetings.Remove(meeting);
        if (SelectedMeeting?.Id == meeting.Id)
            SelectedMeeting = null;
        // Keep the folder badge count in sync
        var folder = Folders.FirstOrDefault(f => f.Id == meeting.FolderId);
        if (folder is not null)
            folder.MeetingCount = Math.Max(0, folder.MeetingCount - 1);
    }

    public async Task LoadTrashAsync()
    {
        var deleted = await _db.GetDeletedMeetingsAsync();
        Meetings = new ObservableCollection<MeetingViewModel>(
            deleted.Select(m => new MeetingViewModel(m)));
        SelectedMeeting = null;
    }

    public async Task RestoreMeetingAsync(MeetingViewModel meeting)
    {
        await _db.RestoreMeetingAsync(meeting.Id);
        Meetings.Remove(meeting);
        if (SelectedMeeting?.Id == meeting.Id)
            SelectedMeeting = null;
    }

    public async Task HideFromTrashAsync(MeetingViewModel meeting)
    {
        await _db.HideFromTrashAsync(meeting.Id);
        Meetings.Remove(meeting);
        if (SelectedMeeting?.Id == meeting.Id)
            SelectedMeeting = null;
    }

    public async Task UpdateFolderCountAsync(int folderId)
    {
        var meetings = await _db.GetMeetingsForFolderAsync(folderId);
        var folder = Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder is not null)
            folder.MeetingCount = meetings.Count;
    }
}
