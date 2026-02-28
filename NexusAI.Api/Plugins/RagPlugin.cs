using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

#pragma warning disable SKEXP0001, SKEXP0004
namespace NexusAI.Api.Plugins;

/// <summary>
/// SK Plugin：知識庫查詢（RAG）
/// 對應前端 Skill：知識庫查詢
/// 串接向量資料庫（Qdrant）進行語意搜尋
/// </summary>
public class RagPlugin
{
    private readonly ILogger<RagPlugin> _logger;
    // TODO: 注入向量資料庫 client
    // private readonly QdrantClient _qdrantClient;
    // private readonly ITextEmbeddingGenerationService _embeddingService;

    public RagPlugin(ILogger<RagPlugin> logger)
    {
        _logger = logger;
    }

    [KernelFunction("query_rag")]
    [Description("搜尋工廠知識庫，包含 SOP 文件、設備手冊、維修記錄和操作規範，用於回答技術問題")]
    public async Task<string> QueryRagAsync(
        [Description("搜尋關鍵字或自然語言問題")] string query,
        [Description("要回傳的最大文件數量，預設 3")] int top_k = 3,
        [Description("文件類別篩選：'sop'=標準程序, 'manual'=設備手冊, 'all'=全部")] string category = "all")
    {
        _logger.LogInformation("RAG 查詢：query={Query}, category={Category}", query, category);

        // TODO: 替換為真實 Qdrant 向量搜尋
        // var embedding = await _embeddingService.GenerateEmbeddingAsync(query);
        // var results   = await _qdrantClient.SearchAsync(
        //     collectionName: "factory_docs",
        //     vector:         embedding.ToArray(),
        //     limit:          (ulong)top_k,
        //     filter:         category != "all"
        //                     ? new Filter { Must = [new FieldCondition { Key = "category", Match = new MatchValue { Value = category } }] }
        //                     : null);
        // return FormatResults(results);

        // ── Mock RAG 結果 ───────────────────────────────────────────────
        var mockDocs = new[]
        {
            new
            {
                doc_id     = "DOC-SOP-001",
                title      = "M-003 輸送帶 A線 維護 SOP",
                category   = "sop",
                content    = "步驟 1：停機前確認安全鎖定（LOTO）\n步驟 2：清潔輸送帶表面\n步驟 3：檢查張力（標準值 850N ± 50N）\n步驟 4：注入潤滑油（ISO VG 68）\n步驟 5：試運行並確認溫度 < 80°C",
                score      = 0.94,
                updated_at = "2025-01-10"
            },
            new
            {
                doc_id     = "DOC-MAN-003",
                title      = "CNC 沖壓機 M-001 操作手冊 v2.3",
                category   = "manual",
                content    = "正常操作溫度範圍：40-80°C\n最大負載：90%\n建議每 200 小時進行一次預防性保養\n潤滑點位置：主軸承（前後各一）、導軌（四處）",
                score      = 0.87,
                updated_at = "2024-12-01"
            },
            new
            {
                doc_id     = "DOC-SOP-005",
                title      = "設備異常處理標準程序",
                category   = "sop",
                content    = "溫度超標（> 85°C）：立即降低負載至 70% 以下，通知維護人員\n轉速異常：停機檢查傳動系統\n負載過高（> 90%）：排查卡料或機械阻力問題",
                score      = 0.82,
                updated_at = "2025-01-05"
            },
        };

        // 簡易關鍵字過濾（模擬語意搜尋）
        var filtered = mockDocs
            .Where(d => category == "all" || d.category == category)
            .Where(d =>
                d.title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                query.Contains("SOP") || query.Contains("維護") || query.Contains("設備"))
            .Take(top_k)
            .ToArray();

        if (filtered.Length == 0)
            filtered = mockDocs.Take(top_k).ToArray(); // fallback
        // ────────────────────────────────────────────────────────────────

        await Task.CompletedTask;

        return JsonSerializer.Serialize(new
        {
            query,
            found = filtered.Length,
            documents = filtered
        });
    }
}
