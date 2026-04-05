using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Models;
using NexusAI.Api.Services;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 模型狀態 API
/// GET /api/models          — 取得所有模型狀態
/// GET /api/models/ollama   — 取得 Ollama LLM 模型狀態
/// GET /api/models/vision   — 取得特徵辨識模型狀態
/// GET /api/models/math     — 取得數學模型狀態
/// </summary>
[ApiController]
[Route("api/models")]
[Authorize]
public class ModelsController : ControllerBase
{
    private readonly IModelService _modelService;
    private readonly ILogger<ModelsController> _logger;

    public ModelsController(IModelService modelService, ILogger<ModelsController> logger)
    {
        _modelService = modelService;
        _logger = logger;
    }

    /// <summary>取得所有模型狀態</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AllModelsStatus>), 200)]
    public async Task<IActionResult> GetAllModels()
    {
        try
        {
            var result = await _modelService.GetAllModelsStatusAsync();
            return Ok(ApiResponse<AllModelsStatus>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得所有模型狀態失敗");
            return StatusCode(500, ApiResponse.Fail("取得模型狀態失敗"));
        }
    }

    /// <summary>取得 Ollama LLM 模型狀態</summary>
    [HttpGet("ollama")]
    [ProducesResponseType(typeof(ApiResponse<ModelStatus>), 200)]
    public async Task<IActionResult> GetOllamaStatus()
    {
        try
        {
            var result = await _modelService.GetOllamaStatusAsync();
            return Ok(ApiResponse<ModelStatus>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得 Ollama 模型狀態失敗");
            return StatusCode(500, ApiResponse.Fail("取得 Ollama 模型狀態失敗"));
        }
    }

    /// <summary>取得特徵辨識模型狀態</summary>
    [HttpGet("vision")]
    [ProducesResponseType(typeof(ApiResponse<ModelStatus>), 200)]
    public async Task<IActionResult> GetVisionStatus()
    {
        try
        {
            var result = await _modelService.GetVisionStatusAsync();
            return Ok(ApiResponse<ModelStatus>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得 Vision 模型狀態失敗");
            return StatusCode(500, ApiResponse.Fail("取得 Vision 模型狀態失敗"));
        }
    }

    /// <summary>取得數學模型狀態</summary>
    [HttpGet("math")]
    [ProducesResponseType(typeof(ApiResponse<ModelStatus>), 200)]
    public async Task<IActionResult> GetMathStatus()
    {
        try
        {
            var result = await _modelService.GetMathStatusAsync();
            return Ok(ApiResponse<ModelStatus>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取得 Math 模型狀態失敗");
            return StatusCode(500, ApiResponse.Fail("取得 Math 模型狀態失敗"));
        }
    }
}
