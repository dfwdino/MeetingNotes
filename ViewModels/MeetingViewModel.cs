using CommunityToolkit.Mvvm.ComponentModel;
using MeetingNotes.Models;

namespace MeetingNotes.ViewModels;

public partial class MeetingViewModel : BaseViewModel
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private int _folderId;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateTime _createdDate;
    [ObservableProperty] private string _durationDisplay = string.Empty;
    [ObservableProperty] private MeetingStatus _status;
    [ObservableProperty] private string? _transcript;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private string? _myNotes;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isDeleted;
    [ObservableProperty] private DateTime? _deletedDate;

    public MeetingViewModel(Meeting meeting)
    {
        _id = meeting.Id;
        _folderId = meeting.FolderId;
        _title = meeting.Title;
        _createdDate = meeting.CreatedDate;
        _durationDisplay = meeting.DurationDisplay;
        _status = meeting.Status;
        _transcript = meeting.Transcript;
        _summary = meeting.Summary;
        _myNotes = meeting.MyNotes;
        _isDeleted = meeting.IsDeleted;
        _deletedDate = meeting.DeletedDate;
    }

    public string StatusDisplay => Status switch
    {
        MeetingStatus.Recording   => "Recording",
        MeetingStatus.Processing  => "Processing...",
        MeetingStatus.Ready       => "Ready",
        _                         => "New"
    };

    public string DateDisplay => CreatedDate.ToString("MMM d, yyyy");
}
