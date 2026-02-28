namespace NexusAI.Api.Models;

// ── AUTH ──────────────────────────────────────────────────────────────────

public class LoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class LoginResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public required string Role { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// ── CONVERSATION ──────────────────────────────────────────────────────────

public class ConversationSummary
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Preview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChatMessage
{
    public required string Id { get; set; }
    public required string Role { get; set; }   // "user" | "ai"
    public required string Text { get; set; }
    public string? File { get; set; }
    public List<SkillCallRecord>? SkillCalls { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SkillCallRecord
{
    public required string Name { get; set; }
    public required object Args { get; set; }
    public object? Result { get; set; }
    public bool Done { get; set; }
}

// ── CHAT STREAM ───────────────────────────────────────────────────────────

public class ChatStreamRequest
{
    public required string ConversationId { get; set; }
    public required string Message { get; set; }
    public string? FileId { get; set; }         // 上傳後取得的 fileId
}

/// <summary>SSE 事件類型</summary>
public static class StreamEventType
{
    public const string SkillStart = "skill_start";
    public const string SkillDone  = "skill_done";
    public const string Token      = "token";
    public const string Done       = "done";
    public const string Error      = "error";
}

public class StreamEvent
{
    public required string Type { get; set; }   // StreamEventType
    public string? SkillName { get; set; }
    public object? SkillArgs { get; set; }
    public object? SkillResult { get; set; }
    public string? Token { get; set; }
}

// ── DEVICE ────────────────────────────────────────────────────────────────

public class DeviceSummary
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; } // "running" | "idle" | "warning"
    public float Temperature { get; set; }
}

public class DeviceDetail : DeviceSummary
{
    public float Rpm { get; set; }
    public float LoadPercent { get; set; }
    public required string Uptime { get; set; }
    public DateTime LastUpdated { get; set; }
}

// ── SKILL ─────────────────────────────────────────────────────────────────

public class SkillItem
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Label { get; set; }
    public required string Icon { get; set; }
    public required string Color { get; set; }
}

// ── FILE UPLOAD ───────────────────────────────────────────────────────────

public class FileUploadResponse
{
    public required string FileId { get; set; }
    public required string FileName { get; set; }
    public long SizeBytes { get; set; }
    public DateTime ExpiresAt { get; set; }
}
