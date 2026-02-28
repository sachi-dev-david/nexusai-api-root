using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusAI.Api.Data;
using NexusAI.Api.Plugins;
using System.Text.RegularExpressions;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 診斷用 Controller（開發階段使用，正式環境可移除）
/// 不需要 JWT，直接從瀏覽器呼叫
/// </summary>
[ApiController]
[Route("api/debug")]
public class DiagnosticController : ControllerBase
{
    private readonly AppDbContext _nexusDb;
    private readonly IDbContextFactory<MlDbContext> _mlFactory;
    private readonly ILoggerFactory _logFactory;

    public DiagnosticController(
        AppDbContext nexusDb,
        IDbContextFactory<MlDbContext> mlFactory,
        ILoggerFactory logFactory)
    {
        _nexusDb    = nexusDb;
        _mlFactory  = mlFactory;
        _logFactory = logFactory;
    }

    // ── GET /api/debug/db ─────────────────────────────────────────────────
    /// <summary>確認兩個 DB 連線與資料筆數</summary>
    [HttpGet("db")]
    public async Task<IActionResult> CheckDb()
    {
        var result = new Dictionary<string, object>();

        // nexusai DB
        try
        {
            var ok  = await _nexusDb.Database.CanConnectAsync();
            var cnt = ok ? await _nexusDb.Users.CountAsync() : -1;
            result["nexusai"] = new { status = "ok", canConnect = ok, userCount = cnt };
        }
        catch (Exception ex)
        {
            result["nexusai"] = new { status = "error", error = ex.Message, inner = ex.InnerException?.Message };
        }

        // mldatabase
        try
        {
            await using var ml  = await _mlFactory.CreateDbContextAsync();
            var ok  = await ml.Database.CanConnectAsync();
            var cnt = ok ? await ml.Quotations.CountAsync() : -1;
            result["mldatabase"] = new { status = "ok", canConnect = ok, quoteCount = cnt };
        }
        catch (Exception ex)
        {
            result["mldatabase"] = new { status = "error", error = ex.Message, inner = ex.InnerException?.Message };
        }

        return Ok(result);
    }

    // ── GET /api/debug/quote ──────────────────────────────────────────────
    /// <summary>
    /// 直接呼叫 QuotePlugin，不經過 LLM
    /// 範例：/api/debug/quote?customer=HZDCIM&amp;from=2026-01-01&amp;to=2026-01-31&amp;limit=5
    /// </summary>
    [HttpGet("quote")]
    public async Task<IActionResult> TestQuotePlugin(
        [FromQuery] string? customer  = null,
        [FromQuery] string? quoteName = null,
        [FromQuery] string? from      = null,
        [FromQuery] string? to        = null,
        [FromQuery] int     limit     = 10)
    {
        try
        {
            var plugin = new QuotePlugin(
                _mlFactory,
                _logFactory.CreateLogger<QuotePlugin>());

            var result = await plugin.QueryQuoteAsync(quoteName, customer, from, to, limit);

            return Ok(new
            {
                status = "ok",
                query  = new { customer, quoteName, from, to, limit },
                result
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                status = "error",
                error  = ex.Message,
                type   = ex.GetType().FullName,
                inner  = ex.InnerException?.Message,
                stack  = ex.StackTrace
            });
        }
    }

    // ── GET /api/debug/quote-summary ──────────────────────────────────────
    /// <summary>
    /// 直接呼叫 QuotePlugin.QueryQuoteSummaryAsync
    /// 範例：/api/debug/quote-summary?customer=HZDCIM&amp;monthFrom=2026-01&amp;monthTo=2026-02
    /// </summary>
    [HttpGet("quote-summary")]
    public async Task<IActionResult> TestQuoteSummary(
        [FromQuery] string? customer  = null,
        [FromQuery] string? monthFrom = null,
        [FromQuery] string? monthTo   = null)
    {
        try
        {
            var plugin = new QuotePlugin(
                _mlFactory,
                _logFactory.CreateLogger<QuotePlugin>());

            var result = await plugin.QueryQuoteSummaryAsync(customer, monthFrom, monthTo);

            return Ok(new { status = "ok", query = new { customer, monthFrom, monthTo }, result });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                status = "error",
                error  = ex.Message,
                type   = ex.GetType().FullName,
                inner  = ex.InnerException?.Message
            });
        }
    }

    // ── GET /api/debug/intent ─────────────────────────────────────────────
    /// <summary>
    /// 測試意圖偵測，確認輸入訊息能否觸發 Plugin
    /// 範例：/api/debug/intent?message=請執行報價查詢2026-02-26~2026-02-26
    /// </summary>
    [HttpGet("intent")]
    public IActionResult TestIntent([FromQuery] string message)
    {
        var lower    = message.ToLower();
        var keywords = new[] { "報價","quote","q_","hzdcim","sachitech","sachi",
                                "材料費","加工費","表面處理費","熱處理費","總計成本","報價單" };

        bool isQuote = keywords.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
        bool isSummary = isQuote && new[]
            { "統計","彙總","合計","總共","幾筆","多少筆","分析","每月","月份" }
            .Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));

        // 日期解析
        string? dateFrom = null, dateTo = null;
        var dates = Regex.Matches(message, @"\d{4}[-/]\d{1,2}[-/]\d{1,2}");
        if (dates.Count >= 2)
        {
            dateFrom = dates[0].Value.Replace("/", "-");
            dateTo   = dates[1].Value.Replace("/", "-");
        }
        else if (dates.Count == 1)
        {
            dateFrom = dates[0].Value.Replace("/", "-");
        }

        // 客戶
        string? customer = lower.Contains("hzdcim")     ? "HZDCIM"
                         : lower.Contains("sachitech") || lower.Contains("sachi") ? "SachiTech"
                         : null;

        // 命中的關鍵字
        var hitKeywords = keywords
            .Where(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Ok(new
        {
            input       = message,
            isQuote,
            isSummary,
            pluginName  = isQuote ? (isSummary ? "query_quote_summary" : "query_quote") : "(none)",
            parsed      = new { customer, dateFrom, dateTo },
            hitKeywords,
            tip         = isQuote
                ? "✅ 會呼叫 Plugin"
                : "❌ 不會呼叫 Plugin，訊息需包含以下其中一個關鍵字：報價、quote、hzdcim、sachitech 等"
        });
    }
}
