namespace MeetingNotes.Models;

public class LogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? ExceptionText { get; set; }
}
