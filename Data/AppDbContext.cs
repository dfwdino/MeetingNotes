using Microsoft.EntityFrameworkCore;
using MeetingNotes.Models;

namespace MeetingNotes.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MeetingFolder> Folders { get; set; }
    public DbSet<Meeting> Meetings { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<AppSettings> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meeting>()
            .HasOne(m => m.Folder)
            .WithMany(f => f.Meetings)
            .HasForeignKey(m => m.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatMessage>()
            .HasOne(c => c.Meeting)
            .WithMany()
            .HasForeignKey(c => c.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Meeting>()
            .Property(m => m.Status)
            .HasConversion<string>();

        modelBuilder.Entity<ChatMessage>()
            .Property(c => c.Role)
            .HasConversion<string>();
    }
}
