using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusAI.Api.Data.Entities;

// ── Users ─────────────────────────────────────────────────────────────────
[Table("users")]
public class UserEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("username"), Required, MaxLength(100)]
    public required string Username { get; set; }

    [Column("password_hash"), Required, MaxLength(256)]
    public required string PasswordHash { get; set; }

    [Column("display_name"), MaxLength(100)]
    public string? DisplayName { get; set; }

    [Column("role"), Required, MaxLength(50)]
    public required string Role { get; set; }   // Admin / Engineer / Operator

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    // Navigation
    public ICollection<ConversationEntity> Conversations { get; set; } = [];
}

// ── Conversations ─────────────────────────────────────────────────────────
[Table("conversations")]
public class ConversationEntity
{
    [Key]
    [Column("id"), MaxLength(64)]
    public required string Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("title"), MaxLength(200)]
    public string Title { get; set; } = "新對話";

    [Column("preview"), MaxLength(500)]
    public string Preview { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("UserId")]
    public UserEntity? User { get; set; }

    public ICollection<MessageEntity> Messages { get; set; } = [];
}

// ── Messages ──────────────────────────────────────────────────────────────
[Table("messages")]
public class MessageEntity
{
    [Key]
    [Column("id"), MaxLength(64)]
    public required string Id { get; set; }

    [Column("conversation_id"), MaxLength(64)]
    public required string ConversationId { get; set; }

    [Column("role"), MaxLength(10)]
    public required string Role { get; set; }   // user | ai

    [Column("text", TypeName = "text")]
    public required string Text { get; set; }

    /// <summary>上傳檔案的 fileId（若有）</summary>
    [Column("file_id"), MaxLength(128)]
    public string? FileId { get; set; }

    /// <summary>Skill 呼叫記錄 JSON 陣列</summary>
    [Column("skill_calls_json", TypeName = "text")]
    public string? SkillCallsJson { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("ConversationId")]
    public ConversationEntity? Conversation { get; set; }
}

// ── UploadedFiles ─────────────────────────────────────────────────────────
[Table("uploaded_files")]
public class UploadedFileEntity
{
    [Key]
    [Column("id"), MaxLength(64)]
    public required string Id { get; set; }         // fileId

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("original_name"), MaxLength(256)]
    public required string OriginalName { get; set; }

    [Column("saved_path"), MaxLength(512)]
    public required string SavedPath { get; set; }

    [Column("content_type"), MaxLength(128)]
    public required string ContentType { get; set; }

    [Column("size_bytes")]
    public long SizeBytes { get; set; }

    [Column("uploaded_at")]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [ForeignKey("UserId")]
    public UserEntity? User { get; set; }
}
