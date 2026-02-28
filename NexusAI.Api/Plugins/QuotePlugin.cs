using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using NexusAI.Api.Data;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

#pragma warning disable SKEXP0001
namespace NexusAI.Api.Plugins;

public class QuotePlugin
{
    private readonly IDbContextFactory<MlDbContext> _dbFactory;
    private readonly ILogger<QuotePlugin> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
        Encoder              = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public QuotePlugin(IDbContextFactory<MlDbContext> dbFactory, ILogger<QuotePlugin> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    /// <summary>
    /// llama3.2:3b 的 Description 要用英文且非常簡短，
    /// 中文 description 在 3b 模型容易被忽略
    /// </summary>
    [KernelFunction("query_quote")]
    [Description("Search quotation records from database. Use this when user asks about quotes, pricing, costs, or quotation data. Filters: customer name, quote name, date range.")]
    public async Task<string> QueryQuoteAsync(
        [Description("Quote name to search (fuzzy). Example: 'HZDCIM' or 'Q_SACH'")] string? quote_name = null,
        [Description("Customer name to filter. Known customers: HZDCIM, SachiTech")] string? customer_name = null,
        [Description("Start date (inclusive), format yyyy-MM-dd. Example: '2026-01-01'")] string? date_from = null,
        [Description("End date (inclusive), format yyyy-MM-dd. Example: '2026-02-28'")] string? date_to = null,
        [Description("Max results to return, default 20, max 100")] int limit = 20)
    {
        _logger.LogInformation(
            "query_quote: customer={C}, from={F}, to={T}, quote={Q}, limit={L}",
            customer_name, date_from, date_to, quote_name, limit);

        limit = Math.Clamp(limit, 1, 100);

        DateTime? from = TryParseDate(date_from);
        DateTime? to   = TryParseDate(date_to)?.AddDays(1);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Quotations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(quote_name))    query = query.Where(q => q.QuoteName.Contains(quote_name));
        if (!string.IsNullOrWhiteSpace(customer_name)) query = query.Where(q => q.CustomerName.Contains(customer_name));
        if (from.HasValue) query = query.Where(q => q.CreatedAt >= from.Value);
        if (to.HasValue)   query = query.Where(q => q.CreatedAt < to.Value);

        var total = await query.CountAsync();
        var rows  = await query
            .OrderByDescending(q => q.CreatedAt)
            .Take(limit)
            .Select(q => new
            {
                q.Id, q.QuoteName, q.QuoteFileName, q.CustomerName,
                q.MaterialName, q.SurfaceTreatment, q.HeatTreatment,
                MaterialCost         = q.MaterialCost,
                ProcessingCost       = q.ProcessingCost,
                SurfaceTreatmentCost = q.SurfaceTreatmentCost,
                HeatTreatmentCost    = q.HeatTreatmentCost,
                TotalCost            = q.TotalCost,
                CreatedAt            = q.CreatedAt.ToString("yyyy-MM-dd"),
                UpdatedAt            = q.UpdatedAt.ToString("yyyy-MM-dd"),
            })
            .ToListAsync();

        if (rows.Count == 0) return "No quotation records found for the given criteria.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {total} records, showing {rows.Count}.");
        sb.AppendLine($"Total cost sum: {rows.Sum(r => r.TotalCost):N0} | Average: {rows.Average(r => r.TotalCost):N0}");
        sb.AppendLine(JsonSerializer.Serialize(new { total, shown = rows.Count, rows }, JsonOpts));
        return sb.ToString();
    }

    [KernelFunction("query_quote_summary")]
    [Description("Get aggregated quotation statistics grouped by customer and month. Use this when user asks for totals, counts, or monthly summaries of quotes.")]
    public async Task<string> QueryQuoteSummaryAsync(
        [Description("Customer name to filter. Known customers: HZDCIM, SachiTech. Leave empty for all.")] string? customer_name = null,
        [Description("Start month, format yyyy-MM. Example: '2026-01'")] string? month_from = null,
        [Description("End month, format yyyy-MM. Example: '2026-02'")] string? month_to = null)
    {
        _logger.LogInformation(
            "query_quote_summary: customer={C}, from={F}, to={T}",
            customer_name, month_from, month_to);

        DateTime? from = month_from.HasContent() ? TryParseDate(month_from + "-01") : null;
        DateTime? to   = month_to.HasContent()   ? TryParseDate(month_to   + "-01")?.AddMonths(1) : null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Quotations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(customer_name)) query = query.Where(q => q.CustomerName.Contains(customer_name));
        if (from.HasValue) query = query.Where(q => q.CreatedAt >= from.Value);
        if (to.HasValue)   query = query.Where(q => q.CreatedAt < to.Value);

        var grouped = await query
            .GroupBy(q => new { q.CustomerName, Year = q.CreatedAt.Year, Month = q.CreatedAt.Month })
            .Select(g => new
            {
                g.Key.CustomerName,
                g.Key.Year,
                g.Key.Month,
                Count     = g.Count(),
                TotalCost = g.Sum(q => q.TotalCost),
                AvgCost   = g.Average(q => q.TotalCost),
            })
            .OrderBy(g => g.CustomerName).ThenBy(g => g.Year).ThenBy(g => g.Month)
            .ToListAsync();

        if (grouped.Count == 0) return "No summary data found for the given criteria.";

        return JsonSerializer.Serialize(new
        {
            groups     = grouped.Count,
            summary    = grouped,
            grandTotal = grouped.Sum(g => g.TotalCost)
        }, JsonOpts);
    }

    private static DateTime? TryParseDate(string? s)
        => string.IsNullOrWhiteSpace(s) ? null
         : DateTime.TryParse(s, out var dt) ? dt : null;
}

internal static class StringExtensions
{
    public static bool HasContent(this string? s) => !string.IsNullOrWhiteSpace(s);
}
