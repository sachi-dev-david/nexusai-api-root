using Microsoft.EntityFrameworkCore;
using NexusAI.Api.Data.Entities;

namespace NexusAI.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UserEntity>         Users         => Set<UserEntity>();
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity>      Messages      => Set<MessageEntity>();
    public DbSet<UploadedFileEntity> UploadedFiles => Set<UploadedFileEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ── Users ──────────────────────────────────────────────────────
        model.Entity<UserEntity>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.CreatedAt).HasDefaultValueSql("UTC_TIMESTAMP()");
        });

        // ── Conversations ──────────────────────────────────────────────
        model.Entity<ConversationEntity>(e =>
        {
            e.HasIndex(c => c.UserId);
            e.HasIndex(c => c.UpdatedAt);
            e.Property(c => c.CreatedAt).HasDefaultValueSql("UTC_TIMESTAMP()");
            e.Property(c => c.UpdatedAt).HasDefaultValueSql("UTC_TIMESTAMP()");

            e.HasOne(c => c.User)
             .WithMany(u => u.Conversations)
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Messages ───────────────────────────────────────────────────
        model.Entity<MessageEntity>(e =>
        {
            e.HasIndex(m => m.ConversationId);
            e.HasIndex(m => m.Timestamp);
            e.Property(m => m.Timestamp).HasDefaultValueSql("UTC_TIMESTAMP()");

            e.HasOne(m => m.Conversation)
             .WithMany(c => c.Messages)
             .HasForeignKey(m => m.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── UploadedFiles ──────────────────────────────────────────────
        model.Entity<UploadedFileEntity>(e =>
        {
            e.HasIndex(f => f.UserId);
            e.HasIndex(f => f.ExpiresAt);   // 方便定時清除過期檔案
            e.Property(f => f.UploadedAt).HasDefaultValueSql("UTC_TIMESTAMP()");
        });
    }
}
