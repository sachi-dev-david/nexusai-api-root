using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Models;
using NexusAI.Api.Services;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 設備管理 API
/// GET /api/devices          — 取得所有設備列表
/// GET /api/devices/{id}/status — 取得單一設備即時狀態
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(IDeviceService deviceService, ILogger<DevicesController> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>取得所有設備列表（含狀態燈號與溫度）</summary>
    /// <returns>
    /// status 值：
    ///   "running" — 運作中
    ///   "idle"    — 待機
    ///   "warning" — 異常
    ///   "offline" — 離線
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<DeviceSummary>>), 200)]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        return Ok(ApiResponse<List<DeviceSummary>>.Ok(devices));
    }

    /// <summary>取得單一設備即時詳細狀態與指標</summary>
    /// <param name="id">設備編號，例如 M-001</param>
    /// <returns>溫度、轉速、負載率、連續運行時間</returns>
    [HttpGet("{id}/status")]
    [ProducesResponseType(typeof(ApiResponse<DeviceDetail>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetDeviceStatus(string id)
    {
        var detail = await _deviceService.GetDeviceDetailAsync(id);

        if (detail is null)
            return NotFound(ApiResponse.Fail($"設備 {id} 不存在"));

        return Ok(ApiResponse<DeviceDetail>.Ok(detail));
    }
}
