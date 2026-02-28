using NexusAI.Api.Models;

namespace NexusAI.Api.Services.Impl;

/// <summary>
/// 設備 Service
/// TODO: 替換為真實 OPC-UA / SCADA / MQTT 資料來源
/// </summary>
public class DeviceService : IDeviceService
{
    private readonly ILogger<DeviceService> _logger;

    // ── Mock 設備資料（正式環境改為 OPC-UA 或資料庫查詢）─────────────────
    private static readonly List<DeviceDetail> _mockDevices =
    [
        new()
        {
            Id          = "M-001",
            Name        = "CNC 沖壓機",
            Status      = "running",
            Temperature = 72f,
            Rpm         = 1450f,
            LoadPercent = 84f,
            Uptime      = "18h 32m",
            LastUpdated = DateTime.UtcNow.AddSeconds(-5),
        },
        new()
        {
            Id          = "M-002",
            Name        = "焊接機器人",
            Status      = "idle",
            Temperature = 45f,
            Rpm         = 0f,
            LoadPercent = 0f,
            Uptime      = "0h 00m",
            LastUpdated = DateTime.UtcNow.AddMinutes(-2),
        },
        new()
        {
            Id          = "M-003",
            Name        = "輸送帶 A線",
            Status      = "warning",
            Temperature = 88f,
            Rpm         = 720f,
            LoadPercent = 91f,
            Uptime      = "72h 15m",
            LastUpdated = DateTime.UtcNow.AddSeconds(-1),
        },
        new()
        {
            Id          = "M-004",
            Name        = "檢測站 QC-1",
            Status      = "running",
            Temperature = 38f,
            Rpm         = 0f,
            LoadPercent = 55f,
            Uptime      = "6h 44m",
            LastUpdated = DateTime.UtcNow.AddSeconds(-3),
        },
    ];

    public DeviceService(ILogger<DeviceService> logger)
    {
        _logger = logger;
    }

    public Task<List<DeviceSummary>> GetAllDevicesAsync()
    {
        // TODO: var devices = await _dbContext.Devices.ToListAsync();
        // 或從 OPC-UA Gateway 取得即時狀態
        var summaries = _mockDevices
            .Select(d => new DeviceSummary
            {
                Id          = d.Id,
                Name        = d.Name,
                Status      = d.Status,
                Temperature = d.Temperature,
            })
            .ToList();

        return Task.FromResult(summaries);
    }

    public Task<DeviceDetail?> GetDeviceDetailAsync(string deviceId)
    {
        // TODO: 從 OPC-UA 查詢單一設備即時指標
        var device = _mockDevices.FirstOrDefault(d => d.Id == deviceId);
        return Task.FromResult(device);
    }
}
