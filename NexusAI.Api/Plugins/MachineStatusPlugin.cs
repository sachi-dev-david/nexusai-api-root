using Microsoft.SemanticKernel;
using System.ComponentModel;

#pragma warning disable SKEXP0001, SKEXP0004
namespace NexusAI.Api.Plugins;

/// <summary>
/// SK Plugin：設備狀態查詢
/// 對應前端 Skill：設備狀態查詢
/// </summary>
public class MachineStatusPlugin
{
    private readonly ILogger<MachineStatusPlugin> _logger;

    public MachineStatusPlugin(ILogger<MachineStatusPlugin> logger)
    {
        _logger = logger;
    }

    [KernelFunction("get_machine_status")]
    [Description("查詢工廠設備的即時運作狀態與指標數據，包含溫度、轉速、負載率和連續運行時間")]
    public async Task<string> GetMachineStatusAsync(
        [Description("設備編號，例如 M-001、M-002")] string machine_id,
        [Description("是否包含溫度、轉速等詳細數值指標，預設為 true")] bool include_metrics = true)
    {
        _logger.LogInformation("查詢設備狀態：{MachineId}", machine_id);

        // TODO: 替換為真實 OPC-UA / SCADA 查詢
        // var status = await _opcUaClient.ReadNodeAsync($"ns=2;s={machine_id}");

        // ── Mock 資料 ──────────────────────────────────────────────────
        var mockData = new Dictionary<string, object>
        {
            ["M-001"] = new { status = "running", temp = 72, rpm = 1450, load = 84, uptime = "18h 32m" },
            ["M-002"] = new { status = "idle",    temp = 45, rpm = 0,    load = 0,  uptime = "0h 00m"  },
            ["M-003"] = new { status = "warning", temp = 88, rpm = 720,  load = 91, uptime = "72h 15m" },
            ["M-004"] = new { status = "running", temp = 38, rpm = 0,    load = 55, uptime = "6h 44m"  },
        };

        if (!mockData.TryGetValue(machine_id.ToUpper(), out var data))
            return $"找不到設備 {machine_id}，請確認設備編號是否正確";
        // ────────────────────────────────────────────────────────────────

        await Task.CompletedTask;

        if (!include_metrics)
            return System.Text.Json.JsonSerializer.Serialize(new { machine_id, status = "running" });

        return System.Text.Json.JsonSerializer.Serialize(new { machine_id, data });
    }
}
