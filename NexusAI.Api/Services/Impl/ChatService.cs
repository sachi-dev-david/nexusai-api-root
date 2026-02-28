using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using NexusAI.Api.Data;
using NexusAI.Api.Models;
using NexusAI.Api.Plugins;
using NexusAI.Api.Services;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NexusAI.Api.Services.Impl;

public class ChatService : IChatService
{
    private readonly Kernel _kernel;
    private readonly IConversationService _convService;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        Kernel kernel,
        IConversationService convService,
        ILogger<ChatService> logger)
    {
        _kernel      = kernel;
        _convService = convService;
        _logger      = logger;
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatStreamRequest request,
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ── 收集容器（不能在 try 裡 yield，所以先收集再 yield）────────────
        var skillEvents  = new List<StreamEvent>();
        var skillRecords = new List<SkillCallRecord>();
        var tokenEvents  = new List<StreamEvent>();
        Exception? error = null;
        string fullReply = string.Empty;

        // ── 1. 對話歷史 ───────────────────────────────────────────────────
        var history     = await _convService.GetMessagesAsync(request.ConversationId, userId) ?? [];
        var chatHistory = BuildChatHistory(history);
        chatHistory.AddUserMessage(request.FileId is not null
            ? $"[Attachment:{request.FileId}]\n{request.Message}"
            : request.Message);

        await _convService.AppendMessageAsync(request.ConversationId, new ChatMessage
        {
            Id        = Guid.NewGuid().ToString("N"),
            Role      = "user",
            Text      = request.Message,
            File      = request.FileId,
            Timestamp = DateTime.UtcNow
        });

        // ── 2. SK 設定（llama3.2 原生 function calling）────────────────────
#pragma warning disable SKEXP0001
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            MaxTokens        = 2048,
            Temperature      = 0.1,
        };
#pragma warning restore SKEXP0001

        try
        {
            // Clone Kernel：每 request 獨立 Filter，避免 Singleton 污染
            var reqKernel    = _kernel.Clone();
            var skillChannel = Channel.CreateUnbounded<StreamEvent>();
            var filter       = new SkillInvocationFilter(skillChannel.Writer, _logger);
            reqKernel.FunctionInvocationFilters.Add(filter);

            var chat = reqKernel.GetRequiredService<IChatCompletionService>();
            _logger.LogInformation("呼叫 llama3.2：{Msg}", request.Message);

            var replyBuilder = new System.Text.StringBuilder();

            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                chatHistory, settings, reqKernel, cancellationToken))
            {
                // 把 Channel 裡的 skill 事件先沖出來
                while (skillChannel.Reader.TryRead(out var se))
                {
                    if (se.Type == StreamEventType.SkillDone && se.SkillName is not null)
                        skillRecords.Add(new SkillCallRecord
                        {
                            Name   = se.SkillName,
                            Args   = se.SkillArgs ?? new Dictionary<string, object?>(),
                            Result = se.SkillResult,
                            Done   = true,
                        });
                    skillEvents.Add(se);
                }

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    replyBuilder.Append(chunk.Content);
                    tokenEvents.Add(new StreamEvent
                        { Type = StreamEventType.Token, Token = chunk.Content });
                }
            }

            // 沖出 Channel 中剩餘的 skill 事件
            skillChannel.Writer.TryComplete();
            await foreach (var se in skillChannel.Reader.ReadAllAsync(CancellationToken.None))
            {
                if (se.Type == StreamEventType.SkillDone && se.SkillName is not null)
                    skillRecords.Add(new SkillCallRecord
                    {
                        Name   = se.SkillName,
                        Args   = se.SkillArgs ?? new Dictionary<string, object?>(),
                        Result = se.SkillResult,
                        Done   = true,
                    });
                skillEvents.Add(se);
            }

            fullReply = replyBuilder.ToString();

            // ── llama3.2:3b 特殊狀況：呼叫了 tool 但沒有輸出整理文字 ───
            // 此時 tokenEvents 是空的，需要再 call 一次 LLM 請它整理結果
            if (skillRecords.Count > 0 && tokenEvents.Count == 0)
            {
                _logger.LogWarning("llama3.2 呼叫了 tool 但沒有輸出回覆，補一次整理請求");

                // tool 結果已被 SK 自動加進 chatHistory，直接再問一次
                chatHistory.AddUserMessage("Please summarize the tool results above in Traditional Chinese (繁體中文).");

                var reqKernel2    = _kernel.Clone();
                var skillChannel2 = Channel.CreateUnbounded<StreamEvent>();
                reqKernel2.FunctionInvocationFilters.Add(
                    new SkillInvocationFilter(skillChannel2.Writer, _logger));

                // 第二次不需要 AutoInvoke，只要整理文字
#pragma warning disable SKEXP0001
                var settingsNoTool = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = null,
                    MaxTokens        = 1024,
                    Temperature      = 0.3,
                };
#pragma warning restore SKEXP0001

                var replyBuilder2 = new System.Text.StringBuilder();
                await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                    chatHistory, settingsNoTool, reqKernel2, cancellationToken))
                {
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        replyBuilder2.Append(chunk.Content);
                        tokenEvents.Add(new StreamEvent
                            { Type = StreamEventType.Token, Token = chunk.Content });
                    }
                }
                fullReply = replyBuilder2.ToString();
            }

            _logger.LogInformation(
                "完成：skillCalled={S}, tokenLen={T}, replyLen={R}",
                skillRecords.Count, tokenEvents.Count, fullReply.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("串流取消");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "無法連線到 Ollama");
            error = ex;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM 錯誤：{T} {M}", ex.GetType().Name, ex.Message);
            error = ex;
        }

        // ── 3. yield（必須在 try/catch 外）──────────────────────────────
        foreach (var e in skillEvents) yield return e;
        foreach (var e in tokenEvents) yield return e;

        if (error is not null)
        {
            yield return new StreamEvent
            {
                Type  = StreamEventType.Error,
                Token = error is HttpRequestException
                    ? $"無法連線到 Ollama：{error.Message}"
                    : $"錯誤：{error.GetType().Name}: {error.Message}"
            };
            yield return new StreamEvent { Type = StreamEventType.Done };
            yield break;
        }

        // ── 4. 儲存 AI 回應 ───────────────────────────────────────────────
        if (fullReply.Length > 0)
        {
            await _convService.AppendMessageAsync(request.ConversationId, new ChatMessage
            {
                Id         = Guid.NewGuid().ToString("N"),
                Role       = "ai",
                Text       = fullReply,
                SkillCalls = skillRecords.Count > 0 ? skillRecords : null,
                Timestamp  = DateTime.UtcNow
            });
        }

        yield return new StreamEvent { Type = StreamEventType.Done };
    }

    // ── System Prompt ──────────────────────────────────────────────────────
    private static ChatHistory BuildChatHistory(List<ChatMessage> history)
    {
        var ch = new ChatHistory("""
            You are NexusAI, a factory assistant. Always reply in Traditional Chinese (繁體中文).

            IMPORTANT RULES:
            - When user asks about quotations, quotes, pricing, costs, or quote records → call query_quote or query_quote_summary tool IMMEDIATELY. Never answer from memory.
            - After tool returns data → present the results clearly in Traditional Chinese using tables or bullet points.
            - Never fabricate numbers or data not returned by a tool.
            """);

        foreach (var msg in history)
        {
            if (msg.Role == "user")    ch.AddUserMessage(msg.Text);
            else if (msg.Role == "ai") ch.AddAssistantMessage(msg.Text);
        }
        return ch;
    }
}

// ── SK Filter：攔截 Plugin 執行事件 ──────────────────────────────────────
public class SkillInvocationFilter : IFunctionInvocationFilter
{
    private readonly ChannelWriter<StreamEvent> _writer;
    private readonly ILogger _logger;

    public SkillInvocationFilter(ChannelWriter<StreamEvent> writer, ILogger logger)
    {
        _writer = writer;
        _logger = logger;
    }

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext ctx, Func<FunctionInvocationContext, Task> next)
    {
        var name = ctx.Function.Name;
        var args = ctx.Arguments
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        _logger.LogInformation("▶ Plugin 開始：{Name}({Args})", name,
            string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}")));

        await _writer.WriteAsync(new StreamEvent
            { Type = StreamEventType.SkillStart, SkillName = name, SkillArgs = args });

        await next(ctx);

        var result = ctx.Result?.GetValue<object>();
        _logger.LogInformation("✓ Plugin 完成：{Name}", name);

        await _writer.WriteAsync(new StreamEvent
        {
            Type        = StreamEventType.SkillDone,
            SkillName   = name,
            SkillArgs   = args,
            SkillResult = result,
        });
    }
}
