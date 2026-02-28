using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Models;
using NexusAI.Api.Services;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 對話管理 API
/// GET    /api/conversations              — 取得對話列表
/// GET    /api/conversations/{id}/messages — 取得對話訊息
/// POST   /api/conversations              — 建立新對話
/// DELETE /api/conversations/{id}         — 刪除對話
/// </summary>
[ApiController]
[Route("api/conversations")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        IConversationService conversationService,
        ILogger<ConversationsController> logger)
    {
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>取得當前使用者的對話歷史列表</summary>
    /// <returns>對話摘要列表，依 UpdatedAt 降冪排列</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ConversationSummary>>), 200)]
    public async Task<IActionResult> GetConversations()
    {
        var userId = GetUserId();
        var list = await _conversationService.GetConversationsAsync(userId);
        return Ok(ApiResponse<List<ConversationSummary>>.Ok(list));
    }

    /// <summary>取得指定對話的完整訊息記錄</summary>
    /// <param name="id">對話 ID</param>
    [HttpGet("{id}/messages")]
    [ProducesResponseType(typeof(ApiResponse<List<ChatMessage>>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetMessages(string id)
    {
        var userId = GetUserId();
        var messages = await _conversationService.GetMessagesAsync(id, userId);

        if (messages is null)
            return NotFound(ApiResponse.Fail($"對話 {id} 不存在"));

        return Ok(ApiResponse<List<ChatMessage>>.Ok(messages));
    }

    /// <summary>建立新對話</summary>
    /// <returns>新對話的 ID</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ConversationSummary>), 201)]
    public async Task<IActionResult> CreateConversation()
    {
        var userId = GetUserId();
        var conversation = await _conversationService.CreateConversationAsync(userId);
        return CreatedAtAction(
            nameof(GetMessages),
            new { id = conversation.Id },
            ApiResponse<ConversationSummary>.Ok(conversation));
    }

    /// <summary>刪除指定對話</summary>
    /// <param name="id">對話 ID</param>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> DeleteConversation(string id)
    {
        var userId = GetUserId();
        var success = await _conversationService.DeleteConversationAsync(id, userId);

        if (!success)
            return NotFound(ApiResponse.Fail($"對話 {id} 不存在或無權限刪除"));

        return Ok(ApiResponse.Ok());
    }

    private string GetUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException();
}
