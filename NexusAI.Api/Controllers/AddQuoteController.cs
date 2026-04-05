using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Data;
using NexusAI.Api.Models;
using NexusAI.Api.Services;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusAI.Api.Data.Entities;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 新增報價 API（完整流程）
/// POST /api/quotes/add - 新增報價（完整流程）
/// </summary>
[ApiController]
[Route("api/quotes")]
[Authorize]
public class AddQuoteController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<MlDbContext> _dbFactory;
    private readonly IFileService _fileService;
    private readonly IConfiguration _config;
    private readonly ILogger<AddQuoteController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AddQuoteController(
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<MlDbContext> dbFactory,
        IFileService fileService,
        IConfiguration config,
        ILogger<AddQuoteController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbFactory = dbFactory;
        _fileService = fileService;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 新增報價完整流程
    /// </summary>
    /// <remarks>
    /// 流程：
    /// 1. 檢查 STEP 檔案（fileId 或 filePath）
    /// 2. 檢查必要參數（材料、表面處理、熱處理、型位公差、尺寸公差）
    /// 3. 呼叫 5051 特徵辨識 API
    /// 4. 呼叫 8800 加工費預測 API
    /// 5. 計算最終報價（TODO）
    /// </remarks>
    [HttpPost("add")]
    [ProducesResponseType(typeof(ApiResponse<AddQuoteApiResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> AddQuote([FromBody] AddQuoteApiRequest request)
    {
        // ── Step 0: 驗證必要參數 ────────────────────────────────────────
        var validationErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.QuoteName))
            validationErrors.Add("QuoteName（報價單名稱）為必填");
        if (string.IsNullOrWhiteSpace(request.QuoteFileName))
            validationErrors.Add("QuoteFileName（報價檔案名稱）為必填");
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            validationErrors.Add("CustomerName（客戶名稱）為必填");
        if (string.IsNullOrWhiteSpace(request.MaterialName))
            validationErrors.Add("MaterialName（材料）為必填");
        if (string.IsNullOrWhiteSpace(request.SurfaceTreatment))
            validationErrors.Add("SurfaceTreatment（表面處理）為必填");
        if (string.IsNullOrWhiteSpace(request.HeatTreatment))
            validationErrors.Add("HeatTreatment（熱處理）為必填");
        if (string.IsNullOrWhiteSpace(request.DimensionalTolerance))
            validationErrors.Add("DimensionalTolerance（型位公差）為必填");
        if (string.IsNullOrWhiteSpace(request.GeometricalTolerance))
            validationErrors.Add("GeometricalTolerance（尺寸公差）為必填");

        if (validationErrors.Count > 0)
        {
            return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                "請填寫以下必填欄位：" + string.Join("、", validationErrors)));
        }

        // ── Step 1: 解析 STEP 檔案路徑 ──────────────────────────────────
        string? stepFilePath = request.StepFilePath;

        if (!string.IsNullOrWhiteSpace(request.StepFileId))
        {
            // 從 fileId 取得實際檔案路徑
            // TODO: 需要從 Session 或 Claims 取得 userId
            var userId = User.FindFirst("sub")?.Value ?? "1";
            var path = await _fileService.GetFilePathAsync(request.StepFileId, userId);
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                    "找不到指定的 STEP 檔案，請先上傳檔案"));
            }
            stepFilePath = path;
        }

        if (string.IsNullOrWhiteSpace(stepFilePath))
        {
            return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                "請上傳要報價的 STEP 圖檔（透過 /api/files/upload 上傳後，將 fileId 傳入 StepFileId）"));
        }

        // 檢查檔案是否存在
        if (!System.IO.File.Exists(stepFilePath))
        {
            return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                $"STEP 檔案不存在：{stepFilePath}"));
        }

        // ── Step 2: 呼叫 5051 特徵辨識 API ──────────────────────────────
        _logger.LogInformation("Step 2: 呼叫特徵辨識 API，檔案：{Path}", stepFilePath);

        FeatureExtractResponse? features = null;
        try
        {
            var client = _httpClientFactory.CreateClient("FeatureExtract");
            var extractRequest = new { file_path = stepFilePath };
            var response = await client.PostAsJsonAsync("/extract", extractRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("特徵辨識 API 失敗：{Status} - {Body}", response.StatusCode, errBody);
                return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                    $"特徵辨識失敗（HTTP {response.StatusCode}），請確認 5051 服務正常"));
            }

            var bodyStr = await response.Content.ReadAsStringAsync();
            features = JsonSerializer.Deserialize<FeatureExtractResponse>(bodyStr, JsonOpts);

            if (features?.Success != true)
            {
                return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                    "特徵辨識失敗，請確認 STEP 檔案格式正確"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "特徵辨識 API 例外");
            return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                "特徵辨識服務無法連線，請確認 5051 服務已啟動"));
        }

        // ── Step 3: 呼叫 8800 加工費預測 API ───────────────────────────
        _logger.LogInformation("Step 3: 呼叫加工費預測 API");

        MathPriceResponse? priceResult = null;
        try
        {
            var client = _httpClientFactory.CreateClient("Math");
            var predictRequest = new
            {
                shape = features?.Features?.Shape ?? "Plate",
                length = features?.Features?.Length ?? 0,
                width = features?.Features?.Width ?? 0,
                height = features?.Features?.Height ?? 0,
                remove_volume = features?.Features?.RemoveVolume ?? 0,
                surface_area = features?.Features?.SurfaceArea ?? 0,
                blank_surface_area = features?.Features?.BlankSurfaceArea ?? 0,
                blank_volume = features?.Features?.BlankVolume ?? 0,
                complexity_coefficient = features?.Features?.ComplexityCoefficient ?? 0,
                circle_hole_count = features?.Features?.CircleHoleCount ?? 0,
                non_circle_hole_count = features?.Features?.NonCircleHoleCount ?? 0,
                groove_count = features?.Features?.GrooveCount ?? 0,
                free_form_surface_area = features?.Features?.FreeFormSurfaceArea ?? 0,
                machining_directions = features?.Features?.MachiningDirections ?? 0,
                processing_type = "Machining",
                dimensional_tolerance = request.DimensionalTolerance,
                geometrical_tolerance = request.GeometricalTolerance
            };

            var response = await client.PostAsJsonAsync("/predict", predictRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("加工費預測 API 失敗：{Status} - {Body}", response.StatusCode, errBody);
                return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                    $"加工費預測失敗（HTTP {response.StatusCode}），請確認 8800 服務正常"));
            }

            var bodyStr = await response.Content.ReadAsStringAsync();
            priceResult = JsonSerializer.Deserialize<MathPriceResponse>(bodyStr, JsonOpts);

            if (priceResult?.Success != true)
            {
                return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                    "加工費預測失敗"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加工費預測 API 例外");
            return BadRequest(ApiResponse<AddQuoteApiResponse>.Fail(
                "加工費預測服務無法連線，請確認 8800 服務已啟動"));
        }

        // ── Step 4: 儲存到資料庫 ────────────────────────────────────────
        _logger.LogInformation("Step 4: 儲存報價到資料庫");

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var entity = new QuotationEntity
            {
                QuoteName = request.QuoteName,
                QuoteFileName = request.QuoteFileName,
                CustomerName = request.CustomerName,
                MaterialName = request.MaterialName,
                SurfaceTreatment = request.SurfaceTreatment,
                HeatTreatment = request.HeatTreatment,
                MaterialCost = 0m, // TODO: 由 8800 或 Step5 計算
                ProcessingCost = priceResult.SuggestedPrice,
                SurfaceTreatmentCost = 0m, // TODO
                HeatTreatmentCost = 0m, // TODO
                TotalCost = priceResult.SuggestedPrice,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Quotations.Add(entity);
            await db.SaveChangesAsync();

            _logger.LogInformation("報價儲存成功，ID={Id}", entity.Id);

            // ── Step 5: 回傳結果（TODO: 最終報價邏輯）────────────────────
            var result = new AddQuoteApiResponse
            {
                Success = true,
                QuoteId = entity.Id,
                Features = features?.Features != null ? new AddQuoteFeatures
                {
                    Length = features.Features.Length,
                    Width = features.Features.Width,
                    Height = features.Features.Height,
                    SurfaceArea = features.Features.SurfaceArea,
                    BlankSurfaceArea = features.Features.BlankSurfaceArea,
                    BlankVolume = features.Features.BlankVolume,
                    RemoveVolume = features.Features.RemoveVolume,
                    ComplexityCoefficient = features.Features.ComplexityCoefficient,
                    CircleHoleCount = features.Features.CircleHoleCount,
                    NonCircleHoleCount = features.Features.NonCircleHoleCount,
                    GrooveCount = features.Features.GrooveCount,
                    FreeFormSurfaceArea = features.Features.FreeFormSurfaceArea,
                    MachiningDirections = features.Features.MachiningDirections,
                    Shape = features.Features.Shape
                } : null,
                Costs = priceResult != null ? new AddQuoteCosts
                {
                    Currency = priceResult.Currency,
                    SuggestedPrice = priceResult.SuggestedPrice,
                    RangeMin = priceResult.RangeMin,
                    RangeMax = priceResult.RangeMax,
                    PredictedRange = priceResult.PredictedRange,
                    Probabilities = priceResult.Probabilities?.Select(p => new ProbabilityItem
                    {
                        Label = p.Label,
                        Probability = p.Probability
                    }).ToList() ?? [],
                    SimilarCases = priceResult.SimilarCases?.Select(s => new SimilarCase
                    {
                        Similarity = s.Similarity,
                        Length = s.Length,
                        Width = s.Width,
                        Height = s.Height,
                        ComplexityCoefficient = s.ComplexityCoefficient,
                        MachiningDirections = s.MachiningDirections,
                        ProcessingType = s.ProcessingType,
                        Result = s.Result
                    }).ToList() ?? []
                } : null,
                Result = new AddQuoteResult() // TODO
            };

            return Ok(ApiResponse<AddQuoteApiResponse>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存報價失敗");
            return StatusCode(500, ApiResponse<AddQuoteApiResponse>.Fail(
                $"儲存報價失敗：{ex.Message}"));
        }
    }

    // ── Internal DTOs ────────────────────────────────────────────────────

    private class FeatureExtractResponse
    {
        public FeatureExtractFeatures? Features { get; set; }
        public bool Success { get; set; }
    }

    private class FeatureExtractFeatures
    {
        public decimal BlankSurfaceArea { get; set; }
        public decimal BlankVolume { get; set; }
        public int CircleHoleCount { get; set; }
        public decimal ComplexityCoefficient { get; set; }
        public decimal FreeFormSurfaceArea { get; set; }
        public int GrooveCount { get; set; }
        public decimal Height { get; set; }
        public decimal Length { get; set; }
        public int MachiningDirections { get; set; }
        public int NonCircleHoleCount { get; set; }
        public decimal RemoveVolume { get; set; }
        public decimal SurfaceArea { get; set; }
        public string? Shape { get; set; }
        public decimal Width { get; set; }
    }

    private class MathPriceResponse
    {
        public bool Success { get; set; }
        public string Currency { get; set; } = "RMB";
        public decimal SuggestedPrice { get; set; }
        public decimal RangeMin { get; set; }
        public decimal RangeMax { get; set; }
        public string PredictedRange { get; set; } = "";
        public List<MathProbabilityItem>? Probabilities { get; set; }
        public List<MathSimilarCase>? SimilarCases { get; set; }
    }

    private class MathProbabilityItem
    {
        public string Label { get; set; } = "";
        public decimal Probability { get; set; }
    }

    private class MathSimilarCase
    {
        public decimal Similarity { get; set; }
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal ComplexityCoefficient { get; set; }
        public int MachiningDirections { get; set; }
        public string ProcessingType { get; set; } = "";
        public decimal Result { get; set; }
    }
}
