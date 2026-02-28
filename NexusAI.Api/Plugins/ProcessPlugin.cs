using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

#pragma warning disable SKEXP0001, SKEXP0004
namespace NexusAI.Api.Plugins;

/// <summary>
/// SK Plugin：製程查詢
/// 對應前端 Skill：製程查詢
/// </summary>
public class ProcessPlugin
{
    private readonly ILogger<ProcessPlugin> _logger;

    public ProcessPlugin(ILogger<ProcessPlugin> logger)
    {
        _logger = logger;
    }

    [KernelFunction("query_process")]
    [Description("查詢產品製程資訊，包含製程步驟、標準工時、使用設備和品質標準")]
    public async Task<string> QueryProcessAsync(
        [Description("產品料號或製程編號")] string part_or_process_id,
        [Description("查詢類型：'steps'=製程步驟, 'time'=標準工時, 'quality'=品質標準, 'all'=全部")] string query_type = "all")
    {
        _logger.LogInformation(
            "製程查詢：id={Id}, type={Type}", part_or_process_id, query_type);

        // TODO: 替換為真實 MES / ERP 系統查詢
        // var process = await _mesClient.GetProcessAsync(part_or_process_id);

        // ── Mock 資料 ──────────────────────────────────────────────────
        var mockProcesses = new Dictionary<string, object>
        {
            ["PN-A102"] = new
            {
                part_number    = "PN-A102",
                process_name   = "精密沖壓件 A102",
                steps = new[]
                {
                    new { seq = 1, name = "備料",     machine = "倉儲",   std_time_min = 5,  quality_check = false },
                    new { seq = 2, name = "CNC 沖壓", machine = "M-001",  std_time_min = 12, quality_check = true  },
                    new { seq = 3, name = "去毛刺",   machine = "手工站", std_time_min = 8,  quality_check = false },
                    new { seq = 4, name = "外觀檢測", machine = "M-004",  std_time_min = 5,  quality_check = true  },
                    new { seq = 5, name = "包裝出貨", machine = "包裝站", std_time_min = 3,  quality_check = false },
                },
                total_std_time_min = 33,
                quality_standard   = "尺寸公差 ±0.05mm，表面粗糙度 Ra ≤ 1.6μm"
            },
            ["PN-B204"] = new
            {
                part_number  = "PN-B204",
                process_name = "焊接組件 B204",
                steps = new[]
                {
                    new { seq = 1, name = "零件備料",  machine = "倉儲",  std_time_min = 8,  quality_check = false },
                    new { seq = 2, name = "點焊組裝",  machine = "M-002", std_time_min = 20, quality_check = true  },
                    new { seq = 3, name = "氣密測試",  machine = "測試站", std_time_min = 10, quality_check = true  },
                    new { seq = 4, name = "防鏽處理",  machine = "塗裝站", std_time_min = 15, quality_check = false },
                    new { seq = 5, name = "最終檢驗",  machine = "M-004", std_time_min = 7,  quality_check = true  },
                },
                total_std_time_min = 60,
                quality_standard   = "焊縫強度 ≥ 250MPa，氣密測試壓力 0.5MPa 維持 30 秒"
            },
        };

        var key = part_or_process_id.ToUpper();
        if (!mockProcesses.TryGetValue(key, out var processData))
            return $"查無料號或製程編號 {part_or_process_id} 的資料，請確認後重試";
        // ────────────────────────────────────────────────────────────────

        await Task.CompletedTask;
        return JsonSerializer.Serialize(processData);
    }
}
