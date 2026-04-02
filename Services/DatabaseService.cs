using System.IO;
using Microsoft.EntityFrameworkCore;
using MeetingNotes.Data;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

public class DatabaseService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DatabaseService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task InitializeAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    // ── Folders ──────────────────────────────────────────────────────────
    public async Task<List<MeetingFolder>> GetFoldersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Folders
            .OrderBy(f => f.Name)
            .Include(f => f.Meetings.Where(m => !m.IsDeleted))
            .ToListAsync();
    }

    public async Task<MeetingFolder> CreateFolderAsync(string name)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var folder = new MeetingFolder { Name = name };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    public async Task RenameFolderAsync(int id, string newName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var folder = await db.Folders.FindAsync(id);
        if (folder is null) return;
        folder.Name = newName;
        await db.SaveChangesAsync();
    }

    public async Task MoveMeetingAsync(int meetingId, int targetFolderId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(meetingId);
        if (meeting is null) return;
        meeting.FolderId = targetFolderId;
        await db.SaveChangesAsync();
    }

    public async Task DeleteFolderAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var meetings = await db.Meetings.Where(m => m.FolderId == id).ToListAsync();
        foreach (var m in meetings)
            SoftDeleteFiles(m);

        var folder = await db.Folders.FindAsync(id);
        if (folder is null) return;
        db.Folders.Remove(folder);
        await db.SaveChangesAsync();
    }

    // ── Meetings ─────────────────────────────────────────────────────────
    public async Task<List<Meeting>> GetMeetingsForFolderAsync(int folderId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings
            .Where(m => m.FolderId == folderId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedDate)
            .ToListAsync();
    }

    public async Task<List<Meeting>> GetDeletedMeetingsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings
            .Where(m => m.IsDeleted && !m.IsHiddenFromTrash)
            .OrderByDescending(m => m.DeletedDate)
            .ToListAsync();
    }

    public async Task<Meeting?> GetMeetingAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings.FindAsync(id);
    }

    public async Task<Meeting> CreateMeetingAsync(int folderId, string title)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = new Meeting { FolderId = folderId, Title = title };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        return meeting;
    }

    public async Task UpdateMeetingAsync(Meeting meeting)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Meetings.Update(meeting);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Soft delete — marks the meeting as deleted and removes audio files from disk.
    /// All text (transcript, summary, notes, chat) stays in the database.
    /// </summary>
    public async Task DeleteMeetingAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting is null) return;

        SoftDeleteFiles(meeting);
        meeting.IsDeleted      = true;
        meeting.DeletedDate    = DateTime.Now;
        meeting.AudioFilePath  = null;
        meeting.AudioFilePaths = null;
        db.Meetings.Update(meeting);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Restore a soft-deleted meeting back to its folder.
    /// </summary>
    public async Task RestoreMeetingAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting is null) return;
        meeting.IsDeleted   = false;
        meeting.DeletedDate = null;
        db.Meetings.Update(meeting);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hides a meeting from the Trash view without removing it from the database.
    /// </summary>
    public async Task HideFromTrashAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting is null) return;
        meeting.IsHiddenFromTrash = true;
        db.Meetings.Update(meeting);
        await db.SaveChangesAsync();
    }

    private static void SoftDeleteFiles(Meeting meeting)
    {
        if (!string.IsNullOrEmpty(meeting.AudioFilePaths))
        {
            foreach (var path in meeting.AudioFilePaths.Split(';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                DeleteFileIfExists(path);
        }
        else
        {
            DeleteFileIfExists(meeting.AudioFilePath);
        }
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            try { File.Delete(path); } catch { /* locked or already gone */ }
    }

    // ── Chat ─────────────────────────────────────────────────────────────
    public async Task<List<ChatMessage>> GetChatMessagesAsync(int meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ChatMessages
            .Where(c => c.MeetingId == meetingId)
            .OrderBy(c => c.Timestamp)
            .ToListAsync();
    }

    public async Task<ChatMessage> AddChatMessageAsync(int meetingId, ChatRole role, string content)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var msg = new ChatMessage { MeetingId = meetingId, Role = role, Content = content };
        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync();
        return msg;
    }

    // ── Settings ─────────────────────────────────────────────────────────
    public async Task<AppSettings> GetSettingsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Settings.FirstAsync();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Settings.Update(settings);
        await db.SaveChangesAsync();
    }
}
