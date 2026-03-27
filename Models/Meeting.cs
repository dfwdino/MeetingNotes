namespace MeetingNotes.Models;

public enum MeetingStatus
{
    New,
    Recording,
    Processing,
    Ready
}

public class Meeting
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public MeetingFolder? Folder { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? RecordingStarted { get; set; }
    public DateTime? RecordingEnded { get; set; }

    public string? AudioFilePath { get; set; }

    /// <summary>
    /// Semicolon-separated list of every audio file ever recorded for this meeting.
    /// Used to ensure all files are deleted on soft-delete, even after multiple re-recordings.
    /// </summary>
    public string? AudioFilePaths { get; set; }

    public string? TranscriptFilePath { get; set; }

    public string? Transcript { get; set; }
    public string? Summary { get; set; }
    public string? MyNotes { get; set; }

    public MeetingStatus Status { get; set; } = MeetingStatus.New;

    // Soft delete — text stays in DB, only audio files are removed
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedDate { get; set; }

    // Hidden from Trash — item no longer shows in the Trash view but stays in DB forever
    public bool IsHiddenFromTrash { get; set; } = false;

    public TimeSpan? Duration => RecordingStarted.HasValue && RecordingEnded.HasValue
        ? RecordingEnded.Value - RecordingStarted.Value
        : null;

    public string DurationDisplay => Duration.HasValue && Duration.Value.TotalSeconds > 0
        ? Duration.Value.TotalHours >= 1
            ? $"{(int)Duration.Value.TotalHours}h {Duration.Value.Minutes}min"
            : $"{(int)Duration.Value.TotalMinutes} min"
        : string.Empty;
}
