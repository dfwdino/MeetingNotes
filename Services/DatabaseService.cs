using System.IO;
using Microsoft.EntityFrameworkCore;
using MeetingNotes.Data;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

public class DatabaseService
{
    private readonly AppDbContext _db;

    public DatabaseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();

        //// Safely add columns for existing installs that predate these features
        //try
        //{
        //    await _db.Database.ExecuteSqlRawAsync(@"
        //        IF NOT EXISTS (SELECT * FROM sys.columns
        //                       WHERE object_id = OBJECT_ID(N'Meetings') AND name = N'IsDeleted')
        //            ALTER TABLE Meetings ADD IsDeleted BIT NOT NULL DEFAULT 0;

        //        IF NOT EXISTS (SELECT * FROM sys.columns
        //                       WHERE object_id = OBJECT_ID(N'Meetings') AND name = N'DeletedDate')
        //            ALTER TABLE Meetings ADD DeletedDate DATETIME2 NULL;

        //        IF NOT EXISTS (SELECT * FROM sys.columns
        //                       WHERE object_id = OBJECT_ID(N'Meetings') AND name = N'IsHiddenFromTrash')
        //            ALTER TABLE Meetings ADD IsHiddenFromTrash BIT NOT NULL DEFAULT 0;

        //        IF NOT EXISTS (SELECT * FROM sys.columns
        //                       WHERE object_id = OBJECT_ID(N'Meetings') AND name = N'AudioFilePaths')
        //            ALTER TABLE Meetings ADD AudioFilePaths NVARCHAR(MAX) NULL;
        //    ");
        //}
        //catch { /* columns already exist — ignore */ }
    }

    // ── Folders ──────────────────────────────────────────────────────────
    public async Task<List<MeetingFolder>> GetFoldersAsync() =>
        await _db.Folders.OrderBy(f => f.Name).ToListAsync();

    public async Task<MeetingFolder> CreateFolderAsync(string name)
    {
        var folder = new MeetingFolder { Name = name };
        _db.Folders.Add(folder);
        await _db.SaveChangesAsync();
        return folder;
    }

    public async Task RenameFolderAsync(int id, string newName)
    {
        var folder = await _db.Folders.FindAsync(id);
        if (folder is null) return;
        folder.Name = newName;
        await _db.SaveChangesAsync();
    }

    public async Task MoveMeetingAsync(int meetingId, int targetFolderId)
    {
        var meeting = await _db.Meetings.FindAsync(meetingId);
        if (meeting is null) return;
        meeting.FolderId = targetFolderId;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteFolderAsync(int id)
    {
        // Soft-delete all meetings in the folder first
        var meetings = await _db.Meetings.Where(m => m.FolderId == id).ToListAsync();
        foreach (var m in meetings)
            await SoftDeleteMeetingAsync(m);

        var folder = await _db.Folders.FindAsync(id);
        if (folder is null) return;
        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync();
    }

    // ── Meetings ─────────────────────────────────────────────────────────
    public async Task<List<Meeting>> GetMeetingsForFolderAsync(int folderId) =>
        await _db.Meetings
            .Where(m => m.FolderId == folderId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedDate)
            .ToListAsync();

    public async Task<List<Meeting>> GetDeletedMeetingsAsync() =>
        await _db.Meetings
            .Where(m => m.IsDeleted && !m.IsHiddenFromTrash)
            .OrderByDescending(m => m.DeletedDate)
            .ToListAsync();

    public async Task<Meeting?> GetMeetingAsync(int id) =>
        await _db.Meetings.FindAsync(id);

    public async Task<Meeting> CreateMeetingAsync(int folderId, string title)
    {
        var meeting = new Meeting { FolderId = folderId, Title = title };
        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync();
        return meeting;
    }

    public async Task UpdateMeetingAsync(Meeting meeting)
    {
        _db.Meetings.Update(meeting);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Soft delete — marks the meeting as deleted and removes audio files from disk.
    /// All text (transcript, summary, notes, chat) stays in the database.
    /// </summary>
    public async Task DeleteMeetingAsync(int id)
    {
        var meeting = await _db.Meetings.FindAsync(id);
        if (meeting is null) return;
        await SoftDeleteMeetingAsync(meeting);
    }

    private async Task SoftDeleteMeetingAsync(Meeting meeting)
    {
        // Delete every audio file ever recorded for this meeting
        if (!string.IsNullOrEmpty(meeting.AudioFilePaths))
        {
            foreach (var path in meeting.AudioFilePaths.Split(';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                DeleteFileIfExists(path);
        }
        else
        {
            // Fallback for meetings recorded before AudioFilePaths was added
            DeleteFileIfExists(meeting.AudioFilePath);
        }

        meeting.IsDeleted     = true;
        meeting.DeletedDate   = DateTime.Now;
        meeting.AudioFilePath  = null;
        meeting.AudioFilePaths = null;
        await UpdateMeetingAsync(meeting);
    }

    /// <summary>
    /// Restore a soft-deleted meeting back to its folder.
    /// </summary>
    public async Task RestoreMeetingAsync(int id)
    {
        var meeting = await _db.Meetings.FindAsync(id);
        if (meeting is null) return;
        meeting.IsDeleted   = false;
        meeting.DeletedDate = null;
        await UpdateMeetingAsync(meeting);
    }

    /// <summary>
    /// Hides a meeting from the Trash view without removing it from the database.
    /// The record is preserved indefinitely; only manual SQL can remove it.
    /// </summary>
    public async Task HideFromTrashAsync(int id)
    {
        var meeting = await _db.Meetings.FindAsync(id);
        if (meeting is null) return;
        meeting.IsHiddenFromTrash = true;
        await UpdateMeetingAsync(meeting);
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            try { File.Delete(path); } catch { /* locked or already gone */ }
    }

    // Chat
    public async Task<List<ChatMessage>> GetChatMessagesAsync(int meetingId) =>
        await _db.ChatMessages
            .Where(c => c.MeetingId == meetingId)
            .OrderBy(c => c.Timestamp)
            .ToListAsync();

    public async Task<ChatMessage> AddChatMessageAsync(int meetingId, ChatRole role, string content)
    {
        var msg = new ChatMessage { MeetingId = meetingId, Role = role, Content = content };
        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync();
        return msg;
    }

    // Settings
    public async Task<AppSettings> GetSettingsAsync() =>
        await _db.Settings.FirstAsync();

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        _db.Settings.Update(settings);
        await _db.SaveChangesAsync();
    }
}
