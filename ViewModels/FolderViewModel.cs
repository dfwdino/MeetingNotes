using CommunityToolkit.Mvvm.ComponentModel;
using MeetingNotes.Models;

namespace MeetingNotes.ViewModels;

public partial class FolderViewModel : BaseViewModel
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _meetingCount;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing;

    public FolderViewModel(MeetingFolder folder)
    {
        _id = folder.Id;
        _name = folder.Name;
        _meetingCount = folder.Meetings.Count;
    }
}
