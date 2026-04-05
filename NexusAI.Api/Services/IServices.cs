using NexusAI.Api.Models;

namespace NexusAI.Api.Services;

// ── AUTH ──────────────────────────────────────────────────────────────────
public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(string username, string password);
    Task LogoutAsync(string? username);
}

// ── CONVERSATION ──────────────────────────────────────────────────────────
public interface IConversationService
{
    Task<List<ConversationSummary>> GetConversationsAsync(string userId);
    Task<List<ChatMessage>?> GetMessagesAsync(string conversationId, string userId);
    Task<ConversationSummary?> GetConversationAsync(string conversationId, string userId);
    Task<ConversationSummary> CreateConversationAsync(string userId);
    Task<bool> DeleteConversationAsync(string conversationId, string userId);
    /// <summary>供 ChatService 呼叫，新增一則訊息並更新 Conversation 摘要</summary>
    Task AppendMessageAsync(string conversationId, ChatMessage message);
}

// ── CHAT ──────────────────────────────────────────────────────────────────
public interface IChatService
{
    IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatStreamRequest request,
        string userId,
        CancellationToken cancellationToken);
}

// ── DEVICE ────────────────────────────────────────────────────────────────
public interface IDeviceService
{
    Task<List<DeviceSummary>> GetAllDevicesAsync();
    Task<DeviceDetail?> GetDeviceDetailAsync(string deviceId);
}

// ── FILE ──────────────────────────────────────────────────────────────────
public interface IFileService
{
    Task<FileUploadResponse> UploadAsync(IFormFile file, string userId);
    Task<string?> GetFilePathAsync(string fileId, string userId);
}

// ── MODEL STATUS ─────────────────────────────────────────────────────────
public interface IModelService
{
    Task<AllModelsStatus> GetAllModelsStatusAsync();
    Task<ModelStatus> GetOllamaStatusAsync();
    Task<ModelStatus> GetVisionStatusAsync();
    Task<ModelStatus> GetMathStatusAsync();
}
