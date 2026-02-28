using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using NexusAI.Api.Models;
using NexusAI.Api.Services;
using System.Text.Json;

namespace NexusAI.Api.Controllers;

/// <summary>
/// AI 對話串流 API
/// POST /api/chat/stream — SSE 串流回傳 AI 回應
/// </summary>
[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IConversationService _conversationService;
    private readonly ILogger<ChatController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(
        IChatService chatService,
        IConversationService conversationService,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>
    /// 核心 AI 對話串流端點（SSE）
    /// </summary>
    /// <remarks>
    /// 使用 Server-Sent Events 串流回傳，前端使用 fetch + ReadableStream 接收。
    ///
    /// SSE 事件格式：
    ///   data: {"type":"skill_start","skillName":"get_machine_status","skillArgs":{...}}
    ///   data: {"type":"skill_done","skillName":"get_machine_status","skillResult":{...}}
    ///   data: {"type":"token","token":"回應文字片段"}
    ///   data: {"type":"done"}
    ///   data: {"type":"error","token":"錯誤訊息"}
    /// </remarks>
    [HttpPost("stream")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task StreamChat([FromBody] ChatStreamRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        // 確認對話存在
        var conversation = await _conversationService.GetConversationAsync(request.ConversationId, userId);
        if (conversation is null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(ApiResponse.Fail($"對話 {request.ConversationId} 不存在"), cancellationToken);
            return;
        }

        // ── 關鍵：在任何 await 串流之前，先停用回應緩衝並設定 Headers ──────
        // IHttpResponseBodyFeature.DisableBuffering() 確保每次 FlushAsync 都即時送出
        var bufferingFeature = HttpContext.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        // 直接對 ContentType / Headers 賦值（不用 Append，避免重複 key 錯誤）
        Response.ContentType    = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"]    = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";   // 停用 Nginx / reverse proxy 緩衝
        Response.Headers["Connection"]       = "keep-alive";

        // 立即送出 Headers（狀態碼 200），讓前端知道串流已開始
        await Response.Body.FlushAsync(cancellationToken);

        try
        {
            await foreach (var evt in _chatService.StreamAsync(request, userId, cancellationToken))
            {
                var json = JsonSerializer.Serialize(evt, JsonOpts);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 前端斷線，正常結束，不需要寫入錯誤
            _logger.LogInformation("使用者 {UserId} 中斷串流", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "串流發生錯誤");
            try
            {
                var errJson = JsonSerializer.Serialize(
                    new StreamEvent { Type = StreamEventType.Error, Token = "AI 回應發生錯誤，請稍後再試" },
                    JsonOpts);
                await Response.WriteAsync($"data: {errJson}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch { /* 連錯誤訊息都寫不出去，靜默放棄 */ }
        }
    }

    private string GetUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException();
}
