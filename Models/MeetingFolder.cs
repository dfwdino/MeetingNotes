namespace MeetingNotes.Models;

public class MeetingFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public List<Meeting> Meetings { get; set; } = [];
}
