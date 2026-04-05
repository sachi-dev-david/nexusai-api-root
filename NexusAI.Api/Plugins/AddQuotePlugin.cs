using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using NexusAI.Api.Data;
using NexusAI.Api.Data.Entities;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable SKEXP0001
namespace NexusAI.Api.Plugins;

/// <summary>
/// Add Quote Plugin - 分兩步呼叫外部 API 來計算報價
/// 步驟1：呼叫特徵辨識 API (http://localhost:5051/extract)
/// 步驟2：呼叫數學模型 API (http://localhost:8800/predict)
/// </summary>
public class AddQuotePlugin
{
    private readonly IDbContextFactory<MlDbContext> _dbFactory;
    private readonly ILogger<AddQuotePlugin> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 進度標記前後文（用於 LLM 串流時解析步驟事件）
    public const string ProgressMarkerStart = "[STEP_START:";
    public const string ProgressMarkerEnd   = "[/STEP_START]";
    public const string ProgressMarkerDone  = "[STEP_DONE:";
    public const string ProgressMarkerDoneEnd = "[/STEP_DONE]";
    public const string ProgressMarkerError = "[STEP_ERROR:";
    public const string ProgressMarkerErrorEnd = "[/STEP_ERROR]";

    public AddQuotePlugin(
        IDbContextFactory<MlDbContext> dbFactory,
        ILogger<AddQuotePlugin> logger,
        IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 新增報價單 - 分兩步調用 API 計算成本
    /// 每個步驟都會輸出可解析的進度標記，供 ChatService 轉發為 SSE
    /// </summary>
    [KernelFunction("add_quote")]
    [Description("Create a new quotation for manufacturing parts. Parse STP CAD file to extract geometric features and call ML model to predict manufacturing costs. Use when user says '新增報價' or '請執行新增報價' with material, surface treatment, heat treatment, and customer info.")]
    public async Task<string> AddQuoteAsync(
        [Description("Quote name, format: Q_{project_name}. Extract from context or use default like 'Q_REQUEST'")] string quote_name,
        [Description("Quote file name, format: A001, B001, etc. Use auto-incremented identifier")] string quote_file_name,
        [Description("Customer/Company name. Examples: HZDCIM, SachiTech, HZD. Extract from '公司名稱' or '加工單位' in user input")] string customer_name,
        [Description("Full path to STP CAD file. Example: 'D:\\Models\\part001.stp' or 'D:\\Models\\component.step'")] string file_path,
        [Description("Material name from user input. Examples: '316不鏽鋼', '鋁合金', '鐵', '銅'. Map from '加工材料' parameter")] string? material_name = null,
        [Description("Surface treatment type. Examples: '磨砂', '拋光', '陽極氧化', '無'. Extract from '表面處理' parameter")] string? surface_treatment = null,
        [Description("Heat treatment type. Examples: '退火', '淬火', '回火', '無'. Extract from '熱處理' parameter")] string? heat_treatment = null,
        [Description("Dimensional tolerance. Examples: 'GB/T1804-m'")] string? dimensional_tolerance = null,
        [Description("Geometrical tolerance. Examples: 'GB/T1184-K'")] string? geometrical_tolerance = null)
    {
        _logger.LogInformation(
            "add_quote: quote_name={QN}, file_name={FN}, customer={C}, file_path={FP}",
            quote_name, quote_file_name, customer_name, file_path);

        var steps = new System.Text.StringBuilder();

        try
        {
            // ────────────────────────────────────────────────────────────
            // Step 1: Call feature parsing API (5051 /extract)
            // ────────────────────────────────────────────────────────────
            steps.AppendLine(StepStart("extract", $"正在分析 STEP 檔案：{Path.GetFileName(file_path)}"));

            var parseRequest = new { file_path };
            var parseContent = new StringContent(JsonSerializer.Serialize(parseRequest, JsonOpts),
                System.Text.Encoding.UTF8, "application/json");

            var parseClient = _httpClientFactory.CreateClient("FeatureExtract");
            var parseResponse = await parseClient.PostAsync("/extract", parseContent);
            if (!parseResponse.IsSuccessStatusCode)
            {
                var errBody = await parseResponse.Content.ReadAsStringAsync();
                _logger.LogError("Parse API failed with status {Status}: {Body}", parseResponse.StatusCode, errBody);
                steps.AppendLine(StepError("extract", $"特徵辨識 API 失敗：HTTP {parseResponse.StatusCode}"));
                return steps.ToString();
            }

            var parseBodyStr = await parseResponse.Content.ReadAsStringAsync();
            var parseResp = JsonSerializer.Deserialize<ParseApiResponse>(parseBodyStr, JsonOpts);

            if (parseResp?.Success != true || parseResp.Params == null)
            {
                _logger.LogError("Parse API returned success=false: {Response}", parseBodyStr);
                steps.AppendLine(StepError("extract", "特徵辨識 API 回應失敗"));
                return steps.ToString();
            }

            steps.AppendLine(StepDone("extract", "特徵辨識完成", new
            {
                length = parseResp.Params.Length,
                width = parseResp.Params.Width,
                height = parseResp.Params.Height,
                shape = parseResp.Params.Shape,
                complexity = parseResp.Params.ComplexityCoefficient
            }));

            _logger.LogInformation("Step 1 completed. Features extracted.");

            // ────────────────────────────────────────────────────────────
            // Step 2: Call ML prediction API (8800 /predict)
            // ────────────────────────────────────────────────────────────
            steps.AppendLine(StepStart("predict", "正在預測加工費用..."));

            var predictRequest = parseResp.Params;
            // Add tolerance parameters
            predictRequest.DimensionalTolerance = dimensional_tolerance ?? "GB/T1804-m";
            predictRequest.GeometricalTolerance = geometrical_tolerance ?? "GB/T1184-K";

            var predictContent = new StringContent(JsonSerializer.Serialize(predictRequest, JsonOpts),
                System.Text.Encoding.UTF8, "application/json");

            var predictClient = _httpClientFactory.CreateClient("Math");
            var predictResponse = await predictClient.PostAsync("/predict", predictContent);
            if (!predictResponse.IsSuccessStatusCode)
            {
                var errBody = await predictResponse.Content.ReadAsStringAsync();
                _logger.LogError("Predict API failed with status {Status}: {Body}", predictResponse.StatusCode, errBody);
                steps.AppendLine(StepError("predict", $"加工費預測 API 失敗：HTTP {predictResponse.StatusCode}"));
                return steps.ToString();
            }

            var predictBodyStr = await predictResponse.Content.ReadAsStringAsync();
            var predictResp = JsonSerializer.Deserialize<PredictApiResponse>(predictBodyStr, JsonOpts);

            if (predictResp?.Success != true || predictResp.Prediction == null)
            {
                _logger.LogError("Predict API returned success=false: {Response}", predictBodyStr);
                steps.AppendLine(StepError("predict", "加工費預測 API 回應失敗"));
                return steps.ToString();
            }

            steps.AppendLine(StepDone("predict", "加工費用預測完成", new
            {
                suggestedPrice = predictResp.Prediction.TotalCost ?? predictResp.SuggestedPrice,
                currency = predictResp.Currency ?? "RMB",
                range = predictResp.PredictedRange
            }));

            _logger.LogInformation("Step 2 completed. Prediction results: {Prediction}",
                JsonSerializer.Serialize(predictResp.Prediction, JsonOpts));

            // ────────────────────────────────────────────────────────────
            // Step 3: Save to database
            // ────────────────────────────────────────────────────────────
            steps.AppendLine(StepStart("save", "正在儲存報價資料..."));

            await using var db = await _dbFactory.CreateDbContextAsync();

            var entity = new QuotationEntity
            {
                QuoteName = quote_name,
                QuoteFileName = quote_file_name,
                CustomerName = customer_name,
                MaterialName = material_name,
                SurfaceTreatment = surface_treatment,
                HeatTreatment = heat_treatment,
                MaterialCost = predictResp.Prediction.MaterialCost ?? 0m,
                ProcessingCost = predictResp.Prediction.ProcessingCost ?? 0m,
                SurfaceTreatmentCost = predictResp.Prediction.SurfaceTreatmentCost ?? 0m,
                HeatTreatmentCost = predictResp.Prediction.HeatTreatmentCost ?? 0m,
                TotalCost = (predictResp.Prediction.MaterialCost ?? 0m)
                          + (predictResp.Prediction.ProcessingCost ?? 0m)
                          + (predictResp.Prediction.SurfaceTreatmentCost ?? 0m)
                          + (predictResp.Prediction.HeatTreatmentCost ?? 0m),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Quotations.Add(entity);
            await db.SaveChangesAsync();

            steps.AppendLine(StepDone("save", $"報價已儲存（ID: {entity.Id}）", new
            {
                quoteId = entity.Id,
                totalCost = entity.TotalCost
            }));

            _logger.LogInformation("Quote created with ID={Id}", entity.Id);

            // ────────────────────────────────────────────────────────────
            // Final result
            // ────────────────────────────────────────────────────────────
            steps.AppendLine(); // 空行分隔
            steps.AppendLine($"✅ 報價完成！");
            steps.AppendLine($"   報價單：{entity.QuoteName}");
            steps.AppendLine($"   客戶：{entity.CustomerName}");
            steps.AppendLine($"   總費用：{entity.TotalCost:N2} {predictResp.Currency ?? "RMB"}");

            return steps.ToString();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed");
            steps.AppendLine(StepError("http", $"網路請求失敗：{ex.Message}"));
            return steps.ToString();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing failed");
            steps.AppendLine(StepError("json", $"JSON 解析失敗：{ex.Message}"));
            return steps.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in add_quote");
            steps.AppendLine(StepError("unknown", $"發生錯誤：{ex.Message}"));
            return steps.ToString();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // 進度訊息辅助函数
    // ────────────────────────────────────────────────────────────────────────
    private string StepStart(string step, string message) =>
        $"{ProgressMarkerStart}{step}][]{message}{ProgressMarkerEnd}";

    private string StepDone(string step, string message, object? data = null) =>
        data == null
            ? $"{ProgressMarkerDone}{step}][{message}{ProgressMarkerDoneEnd}"
            : $"{ProgressMarkerDone}{step}][{message}][{JsonSerializer.Serialize(data, JsonOpts)}{ProgressMarkerDoneEnd}";

    private string StepError(string step, string message) =>
        $"{ProgressMarkerError}{step}][{message}{ProgressMarkerErrorEnd}";

    // ────────────────────────────────────────────────────────────────────────
    // DTO Classes for API Responses
    // ────────────────────────────────────────────────────────────────────────

    private class ParseApiResponse
    {
        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        [JsonPropertyName("params")]
        public ParseParams? Params { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    private class ParseParams
    {
        [JsonPropertyName("blank_surface_area")]
        public decimal BlankSurfaceArea { get; set; }

        [JsonPropertyName("blank_volume")]
        public decimal BlankVolume { get; set; }

        [JsonPropertyName("circle_hole_count")]
        public int CircleHoleCount { get; set; }

        [JsonPropertyName("complexity_coefficient")]
        public decimal ComplexityCoefficient { get; set; }

        [JsonPropertyName("dimensional_tolerance")]
        public string? DimensionalTolerance { get; set; }

        [JsonPropertyName("free_form_surface_area")]
        public decimal FreeFormSurfaceArea { get; set; }

        [JsonPropertyName("geometrical_tolerance")]
        public string? GeometricalTolerance { get; set; }

        [JsonPropertyName("groove_count")]
        public int GrooveCount { get; set; }

        [JsonPropertyName("height")]
        public decimal Height { get; set; }

        [JsonPropertyName("length")]
        public decimal Length { get; set; }

        [JsonPropertyName("non_circle_hole_count")]
        public int NonCircleHoleCount { get; set; }

        [JsonPropertyName("remove_volume")]
        public decimal RemoveVolume { get; set; }

        [JsonPropertyName("shape")]
        public string? Shape { get; set; }

        [JsonPropertyName("surface_area")]
        public decimal SurfaceArea { get; set; }

        [JsonPropertyName("width")]
        public decimal Width { get; set; }
    }

    private class PredictApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("prediction")]
        public PredictionResult? Prediction { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("suggested_price")]
        public decimal? SuggestedPrice { get; set; }

        [JsonPropertyName("range_min")]
        public decimal? RangeMin { get; set; }

        [JsonPropertyName("range_max")]
        public decimal? RangeMax { get; set; }

        [JsonPropertyName("predicted_range")]
        public string? PredictedRange { get; set; }

        [JsonPropertyName("probabilities")]
        public List<ProbabilityItem>? Probabilities { get; set; }

        [JsonPropertyName("similar_cases")]
        public List<SimilarCase>? SimilarCases { get; set; }
    }

    private class PredictionResult
    {
        [JsonPropertyName("material_cost")]
        public decimal? MaterialCost { get; set; }

        [JsonPropertyName("processing_cost")]
        public decimal? ProcessingCost { get; set; }

        [JsonPropertyName("surface_treatment_cost")]
        public decimal? SurfaceTreatmentCost { get; set; }

        [JsonPropertyName("heat_treatment_cost")]
        public decimal? HeatTreatmentCost { get; set; }

        [JsonPropertyName("total_cost")]
        public decimal? TotalCost { get; set; }
    }

    private class ProbabilityItem
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("probability")]
        public decimal Probability { get; set; }
    }

    private class SimilarCase
    {
        [JsonPropertyName("similarity")]
        public decimal Similarity { get; set; }

        [JsonPropertyName("length")]
        public decimal Length { get; set; }

        [JsonPropertyName("width")]
        public decimal Width { get; set; }

        [JsonPropertyName("height")]
        public decimal Height { get; set; }

        [JsonPropertyName("complexity_coefficient")]
        public decimal ComplexityCoefficient { get; set; }

        [JsonPropertyName("machining_directions")]
        public int MachiningDirections { get; set; }

        [JsonPropertyName("processing_type")]
        public string ProcessingType { get; set; } = "";

        [JsonPropertyName("result")]
        public decimal Result { get; set; }
    }
}

#pragma warning restore SKEXP0001

