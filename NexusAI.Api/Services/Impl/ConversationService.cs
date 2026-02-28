using Microsoft.EntityFrameworkCore;
using NexusAI.Api.Data;
using NexusAI.Api.Data.Entities;
using NexusAI.Api.Models;
using NexusAI.Api.Services;
using System.Text.Json;

namespace NexusAI.Api.Services.Impl;

public class ConversationService : IConversationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ConversationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConversationService(AppDbContext db, ILogger<ConversationService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── IConversationService ──────────────────────────────────────────────

    public async Task<List<ConversationSummary>> GetConversationsAsync(string userId)
    {
        var uid = int.Parse(userId);
        var list = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.UserId == uid)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ConversationSummary
            {
                Id        = c.Id,
                Title     = c.Title,
                Preview   = c.Preview,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
            })
            .ToListAsync();

        return list;
    }

    public async Task<List<ChatMessage>?> GetMessagesAsync(string conversationId, string userId)
    {
        var uid = int.Parse(userId);
        var conv = await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == uid);

        if (conv is null) return null;

        var entities = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<ConversationSummary?> GetConversationAsync(string conversationId, string userId)
    {
        var uid = int.Parse(userId);
        var conv = await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == uid);

        if (conv is null) return null;

        return new ConversationSummary
        {
            Id        = conv.Id,
            Title     = conv.Title,
            Preview   = conv.Preview,
            CreatedAt = conv.CreatedAt,
            UpdatedAt = conv.UpdatedAt,
        };
    }

    public async Task<ConversationSummary> CreateConversationAsync(string userId)
    {
        var uid  = int.Parse(userId);
        var now  = DateTime.UtcNow;
        var id   = $"conv_{Guid.NewGuid():N}";

        var entity = new ConversationEntity
        {
            Id        = id,
            UserId    = uid,
            Title     = "新對話",
            Preview   = "",
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Conversations.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("建立新對話 {ConvId} for userId={UserId}", id, userId);

        return new ConversationSummary
        {
            Id        = id,
            Title     = "新對話",
            Preview   = "",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public async Task<bool> DeleteConversationAsync(string conversationId, string userId)
    {
        var uid    = int.Parse(userId);
        var deleted = await _db.Conversations
            .Where(c => c.Id == conversationId && c.UserId == uid)
            .ExecuteDeleteAsync();

        if (deleted > 0)
            _logger.LogInformation("刪除對話 {ConvId}", conversationId);

        return deleted > 0;
    }

    // ── 供 ChatService 呼叫 ───────────────────────────────────────────────

    public async Task AppendMessageAsync(string conversationId, ChatMessage message)
    {
        var skillJson = message.SkillCalls?.Count > 0
            ? JsonSerializer.Serialize(message.SkillCalls, JsonOpts)
            : null;

        var entity = new MessageEntity
        {
            Id             = message.Id,
            ConversationId = conversationId,
            Role           = message.Role,
            Text           = message.Text,
            FileId         = message.File,
            SkillCallsJson = skillJson,
            Timestamp      = message.Timestamp,
        };

        _db.Messages.Add(entity);

        // 同步更新 conversation 的 Preview 與 UpdatedAt
        var preview = message.Text.Length > 100
            ? message.Text[..100] + "..."
            : message.Text;

        await _db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(c => c
                .SetProperty(x => x.UpdatedAt, message.Timestamp)
                .SetProperty(x => x.Preview, preview));

        // 若是第一則 user 訊息，用來當 title
        var existingCount = await _db.Messages
            .CountAsync(m => m.ConversationId == conversationId && m.Role == "user");

        if (existingCount == 0 && message.Role == "user")
        {
            var title = message.Text.Length > 30
                ? message.Text[..30] + "..."
                : message.Text;

            await _db.Conversations
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Title, title));
        }

        await _db.SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ChatMessage MapToModel(MessageEntity e)
    {
        List<SkillCallRecord>? skillCalls = null;
        if (!string.IsNullOrEmpty(e.SkillCallsJson))
        {
            try
            {
                skillCalls = JsonSerializer.Deserialize<List<SkillCallRecord>>(e.SkillCallsJson, JsonOpts);
            }
            catch { /* JSON 解析失敗，忽略 */ }
        }

        return new ChatMessage
        {
            Id         = e.Id,
            Role       = e.Role,
            Text       = e.Text,
            File       = e.FileId,
            SkillCalls = skillCalls,
            Timestamp  = e.Timestamp,
        };
    }
}
