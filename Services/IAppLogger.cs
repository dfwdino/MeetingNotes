namespace MeetingNotes.Services;

public interface IAppLogger
{
    Task InfoAsync(string message, string? source = null);
    Task WarnAsync(string message, string? source = null);
    Task ErrorAsync(string message, Exception? ex = null, string? source = null);
}
