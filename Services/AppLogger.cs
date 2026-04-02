using System.IO;
using Microsoft.EntityFrameworkCore;
using MeetingNotes.Data;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

public class AppLogger : IAppLogger
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public AppLogger(IDbContextFactory<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    public Task InfoAsync(string message, string? source = null) =>
        WriteAsync("Info", message, source);

    public Task WarnAsync(string message, string? source = null) =>
        WriteAsync("Warn", message, source);

    public Task ErrorAsync(string message, Exception? ex = null, string? source = null) =>
        WriteAsync("Error", message, source, ex);

    private async Task WriteAsync(string level, string message, string? source, Exception? ex = null)
    {
        var entry = new LogEntry
        {
            Timestamp     = DateTime.Now,
            Level         = level,
            Message       = message,
            Source        = source,
            ExceptionText = ex?.ToString()
        };

        if (_settings.LogToDatabase)
            await WriteToDatabaseAsync(entry);

        if (_settings.LogToFile)
            await WriteToFileAsync(entry);
    }

    private async Task WriteToDatabaseAsync(LogEntry entry)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.LogEntries.Add(entry);
            await db.SaveChangesAsync();
        }
        catch { /* logging must never throw */ }
    }

    private async Task WriteToFileAsync(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_settings.LogFolder);

            var fileName = $"MeetingNotes_{DateTime.Now:yyyy-MM-dd}.log";
            var filePath = Path.Combine(_settings.LogFolder, fileName);
            var line     = FormatLine(entry);

            await _fileLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(filePath, line + Environment.NewLine);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch { /* logging must never throw */ }
    }

    private static string FormatLine(LogEntry entry)
    {
        var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level,-5}]";

        if (!string.IsNullOrEmpty(entry.Source))
            line += $" [{entry.Source}]";

        line += $" {entry.Message}";

        if (!string.IsNullOrEmpty(entry.ExceptionText))
            line += $"{Environment.NewLine}  Exception: {entry.ExceptionText}";

        return line;
    }
}
