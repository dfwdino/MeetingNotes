using System.IO;
using Microsoft.EntityFrameworkCore;
using MeetingNotes.Data;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

public class DatabaseService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private IAppLogger _logger;

    public DatabaseService(IDbContextFactory<AppDbContext> dbFactory, IAppLogger logger )
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _ = _logger.InfoAsync("Initializing database...");
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        // Drop unused tables for existing installs
        try { await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS FolderChatMessages"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS AppSettings"); } catch { }

        // Add per-meeting encryption columns for existing installs
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meetings ADD COLUMN IsEncrypted INTEGER NOT NULL DEFAULT 0");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meetings ADD COLUMN EncryptionSalt TEXT");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meetings ADD COLUMN EncryptedDataKey TEXT");
        }
        catch { }

        // Per-meeting Whisper prompt terms (participants/acronyms) for existing installs
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meetings ADD COLUMN WhisperPromptTerms TEXT");
        }
        catch { }

        // Drop unused columns and tables for existing installs
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meetings DROP COLUMN TranscriptFilePath");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS AppSettings");
        }
        catch { }
    }

    // ── Folders ──────────────────────────────────────────────────────────
    /// <summary>
    /// Full folder load INCLUDING every meeting's transcript/summary/notes.
    /// Only use when full content is actually needed (e.g. export-all);
    /// the sidebar uses <see cref="GetFolderListAsync"/> instead.
    /// </summary>
    public async Task<List<MeetingFolder>> GetFoldersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Folders
            .OrderBy(f => f.Name)
            .Include(f => f.Meetings.Where(m => !m.IsDeleted))
            .ToListAsync();
    }

    /// <summary>
    /// Lightweight folder list for the sidebar: folders plus meeting counts,
    /// without pulling any meeting rows (or their large text columns) into memory.
    /// </summary>
    public async Task<List<(MeetingFolder Folder, int MeetingCount)>> GetFolderListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Folders
            .OrderBy(f => f.Name)
            .Select(f => new
            {
                Folder = f,
                Count = f.Meetings.Count(m => !m.IsDeleted)
            })
            .ToListAsync();
        return rows.Select(r => (r.Folder, r.Count)).ToList();
    }

    public async Task<int> GetMeetingCountAsync(int folderId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings.CountAsync(m => m.FolderId == folderId && !m.IsDeleted);
    }

    public async Task<int> GetTrashCountAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings.CountAsync(m => m.IsDeleted && !m.IsHiddenFromTrash);
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
    /// <summary>
    /// List projection for the meeting list panel. Transcript, Summary, and MyNotes
    /// are NOT loaded (left null) — callers that need them must fetch the full row
    /// with <see cref="GetMeetingAsync"/> first (MeetingDetailView.LoadMeeting does this).
    /// </summary>
    public async Task<List<Meeting>> GetMeetingsForFolderAsync(int folderId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Meetings
            .Where(m => m.FolderId == folderId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedDate)
            .Select(ListColumns)
            .ToListAsync();
        return rows.Select(ToListMeeting).ToList();
    }

    /// <summary>Trash list — same lightweight projection as <see cref="GetMeetingsForFolderAsync"/>.</summary>
    public async Task<List<Meeting>> GetDeletedMeetingsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Meetings
            .Where(m => m.IsDeleted && !m.IsHiddenFromTrash)
            .OrderByDescending(m => m.DeletedDate)
            .Select(ListColumns)
            .ToListAsync();
        return rows.Select(ToListMeeting).ToList();
    }

    // EF Core cannot project directly to an entity type inside a query, so list queries
    // go through this intermediate row shape and are mapped back to Meeting in memory.
    private sealed record MeetingListRow(int Id, int FolderId, string Title,
        DateTime CreatedDate, DateTime? RecordingStarted, DateTime? RecordingEnded,
        string? AudioFilePath, MeetingStatus Status, bool IsEncrypted,
        bool IsDeleted, DateTime? DeletedDate);

    private static readonly System.Linq.Expressions.Expression<Func<Meeting, MeetingListRow>>
        ListColumns = m => new MeetingListRow(m.Id, m.FolderId, m.Title,
            m.CreatedDate, m.RecordingStarted, m.RecordingEnded,
            m.AudioFilePath, m.Status, m.IsEncrypted,
            m.IsDeleted, m.DeletedDate);

    /// <summary>
    /// Searches all non-deleted meetings by title, transcript, summary, and notes
    /// (case-insensitive for ASCII — SQLite LIKE semantics). Encrypted meetings match on
    /// title only since their text columns hold ciphertext. Returns the same lightweight
    /// projection as the other list queries.
    /// </summary>
    public async Task<List<Meeting>> SearchMeetingsAsync(string query)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var pattern = $"%{EscapeLike(query)}%";
        var rows = await db.Meetings
            .Where(m => !m.IsDeleted && (
                EF.Functions.Like(m.Title, pattern, "\\") ||
                (!m.IsEncrypted && (
                    EF.Functions.Like(m.Transcript!, pattern, "\\") ||
                    EF.Functions.Like(m.Summary!, pattern, "\\") ||
                    EF.Functions.Like(m.MyNotes!, pattern, "\\")))))
            .OrderByDescending(m => m.CreatedDate)
            .Select(ListColumns)
            .ToListAsync();
        return rows.Select(ToListMeeting).ToList();
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    private static Meeting ToListMeeting(MeetingListRow r) => new()
    {
        Id               = r.Id,
        FolderId         = r.FolderId,
        Title            = r.Title,
        CreatedDate      = r.CreatedDate,
        RecordingStarted = r.RecordingStarted,
        RecordingEnded   = r.RecordingEnded,
        AudioFilePath    = r.AudioFilePath,
        Status           = r.Status,
        IsEncrypted      = r.IsEncrypted,
        IsDeleted        = r.IsDeleted,
        DeletedDate      = r.DeletedDate,
    };

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

    public async Task UpdateChatMessageAsync(ChatMessage msg)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.ChatMessages.Update(msg);
        await db.SaveChangesAsync();
    }

}
