namespace MeetingNotes.Models;

public class FolderChatMessage
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public MeetingFolder? Folder { get; set; }
    public ChatRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
