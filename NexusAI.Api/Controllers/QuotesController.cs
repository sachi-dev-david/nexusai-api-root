using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using NexusAI.Api.Data;
using NexusAI.Api.Data.Entities;
using NexusAI.Api.Models;
using NexusAI.Api.Services;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 報價選單 + 新增報價（規格版）
/// GET  /api/quotes/options          取得下拉選單
/// GET  /api/quotes/options/version  選單版本（前端快取用）
/// POST /api/quotes                  新增報價（multipart/form-data + SSE 串流過程）
/// </summary>
[ApiController]
[Route("api/quotes")]
[Authorize]
public class QuotesController : ControllerBase
{
    private readonly IDbContextFactory<MlDbContext>  _mlDbFactory;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileService _fileService;
    private readonly IConversationService _convService;
    private readonly Kernel _kernel;
    private readonly IConfiguration _config;
    private readonly ILogger<QuotesController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] AllowedCadExtensions = [".step", ".stp", ".prt"];
    private const long MaxCadFileSizeBytes = 50L * 1024 * 1024; // 50 MB

    public QuotesController(
        IDbContextFactory<MlDbContext>  mlDbFactory,
        IDbContextFactory<AppDbContext> appDbFactory,
        IHttpClientFactory httpClientFactory,
        IFileService fileService,
        IConversationService convService,
        Kernel kernel,
        IConfiguration config,
        ILogger<QuotesController> logger)
    {
        _mlDbFactory       = mlDbFactory;
        _appDbFactory      = appDbFactory;
        _httpClientFactory = httpClientFactory;
        _fileService       = fileService;
        _convService       = convService;
        _kernel            = kernel;
        _config            = config;
        _logger            = logger;
    }

    // ── GET /api/quotes/options ────────────────────────────────────────────

    /// <summary>取得新增報價表單所有下拉選單</summary>
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<QuoteOptionsData>), 200)]
    public async Task<IActionResult> GetOptions(CancellationToken ct)
    {
        await using var db = await _appDbFactory.CreateDbContextAsync(ct);

        var all = await db.BaseParameters
            .AsNoTracking()
            .OrderBy(b => b.No)
            .ToListAsync(ct);

        static QuoteOptionItem ToItem(BaseParameterEntity e) => new()
        {
            Id      = e.Id,
            Label   = e.Name,
            Enabled = true
        };

        var data = new QuoteOptionsData
        {
            Materials          = all.Where(b => b.Type == "MAT").Select(ToItem).ToList(),
            Surfaces           = all.Where(b => b.Type == "SUR").Select(ToItem).ToList(),
            HeatTreats         = all.Where(b => b.Type == "HEAT").Select(ToItem).ToList(),
            Roughnesses        = all.Where(b => b.Type == "SURR").Select(ToItem).ToList(),
            PositionTolerances = all.Where(b => b.Type == "GEO").Select(ToItem).ToList(),
            SizeTolerances     = all.Where(b => b.Type == "DIM").Select(ToItem).ToList(),
        };

        return Ok(ApiResponse<QuoteOptionsData>.Ok(data));
    }

    // ── GET /api/quotes/options/version ───────────────────────────────────

    /// <summary>選單版本（前端快取比對用）</summary>
    [HttpGet("options/version")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public IActionResult GetOptionsVersion()
    {
        return Ok(ApiResponse<object>.Ok(new { version = "2026-04-05.1" }));
    }

    // ── POST /api/quotes ───────────────────────────────────────────────────

    /// <summary>
    /// 新增報價（multipart/form-data，回應為 SSE 串流）
    /// 依序執行：儲存 CAD → 特徵辨識(5051) → 加工費預測(8800) → 成本計算 → 儲存 DB
    /// </summary>
    [HttpPost("")]
    [RequestSizeLimit(MaxCadFileSizeBytes)]
    [Consumes("multipart/form-data")]
    public async Task CreateQuote(
        [FromForm(Name = "material_id")]            string?   materialId,
        [FromForm(Name = "surface_id")]             string?   surfaceId,
        [FromForm(Name = "heat_treat_id")]          string?   heatTreatId,
        [FromForm(Name = "roughness_id")]           string?   roughnessId,
        [FromForm(Name = "position_tolerance_id")]  string?   positionToleranceId,
        [FromForm(Name = "size_tolerance_id")]      string?   sizeToleranceId,
        [FromForm(Name = "company_name")]           string?   companyName,
        [FromForm(Name = "note")]                   string?   note,
        [FromForm(Name = "conversation_id")]        string?   conversationId,
        IFormFile?                                  cad_file,
        CancellationToken                           ct)
    {
        // ── 1. 驗證必填欄位（在啟動 SSE 前，返回標準 JSON 錯誤）──────────
        var errs = new List<object>();

        if (string.IsNullOrWhiteSpace(materialId))
            errs.Add(new { field = "material_id",           message = "required" });
        if (string.IsNullOrWhiteSpace(surfaceId))
            errs.Add(new { field = "surface_id",            message = "required" });
        if (string.IsNullOrWhiteSpace(heatTreatId))
            errs.Add(new { field = "heat_treat_id",         message = "required" });
        if (string.IsNullOrWhiteSpace(roughnessId))
            errs.Add(new { field = "roughness_id",          message = "required" });
        if (string.IsNullOrWhiteSpace(positionToleranceId))
            errs.Add(new { field = "position_tolerance_id", message = "required" });
        if (string.IsNullOrWhiteSpace(sizeToleranceId))
            errs.Add(new { field = "size_tolerance_id",     message = "required" });
        if (string.IsNullOrWhiteSpace(companyName))
            errs.Add(new { field = "company_name",          message = "required" });

        if (errs.Count > 0)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(
                new { success = false, error = "ValidationError", details = errs }, ct);
            return;
        }

        if (cad_file is null || cad_file.Length == 0)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(
                new { success = false, error = "cad_file is required" }, ct);
            return;
        }

        var ext = Path.GetExtension(cad_file.FileName).ToLower();
        if (!AllowedCadExtensions.Contains(ext))
        {
            Response.StatusCode = 415;
            await Response.WriteAsJsonAsync(
                new { success = false, error = $"Unsupported file type. Allowed: .step, .stp, .prt" }, ct);
            return;
        }

        // ── 2. 啟動 SSE 串流 ──────────────────────────────────────────────
        var userId = GetUserId();

        var bufferingFeature = HttpContext.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        Response.ContentType                  = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"]     = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["Connection"]        = "keep-alive";
        await Response.Body.FlushAsync(ct);

        // 儲存使用者訊息（如果有對話）
        var userMsgText = $"新增報價\n公司：{companyName}\nCAD：{cad_file.FileName}";
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            try
            {
                await _convService.AppendMessageAsync(conversationId, new ChatMessage
                {
                    Id        = Guid.NewGuid().ToString("N"),
                    Role      = "user",
                    Text      = userMsgText,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "儲存使用者報價訊息失敗，繼續執行");
            }
        }

        try
        {
            // ── Step 1: 儲存 CAD 檔案 ───────────────────────────────────
            await WriteStepStartAsync("save_file", "正在儲存 CAD 檔案...", ct);

            FileUploadResponse fileInfo;
            try
            {
                fileInfo = await _fileService.UploadAsync(cad_file, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 CAD 檔案失敗");
                await WriteStepErrorAsync("save_file", $"儲存 CAD 檔案失敗：{ex.Message}", ct);
                await WriteErrorAsync("檔案儲存失敗，請稍後再試", ct);
                return;
            }

            await WriteStepDoneAsync("save_file", "檔案儲存完成", new
            {
                file_id   = fileInfo.FileId,
                file_name = fileInfo.FileName,
                file_path = fileInfo.FilePath,
                size_bytes = fileInfo.SizeBytes
            }, ct);

            // ── Step 2: 呼叫 5051 特徵辨識 API ──────────────────────────
            await WriteStepStartAsync("extract", $"正在解析 CAD 特徵：{fileInfo.FileName}...", ct);

            ExtractApiResponse? features;
            try
            {
                var client = _httpClientFactory.CreateClient("FeatureExtract");
                var res = await client.PostAsJsonAsync("/extract",
                    new { file_path = fileInfo.FilePath }, ct);

                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(ct);
                    _logger.LogError("特徵辨識 API 失敗：{Status} {Body}", res.StatusCode, body);
                    await WriteStepErrorAsync("extract", $"特徵辨識失敗（HTTP {(int)res.StatusCode}）", ct);
                    await WriteErrorAsync("特徵辨識服務回應錯誤，請確認 5051 服務正常", ct);
                    return;
                }

                var bodyStr = await res.Content.ReadAsStringAsync(ct);
                features = JsonSerializer.Deserialize<ExtractApiResponse>(bodyStr, JsonOpts);

                if (features?.Success != true)
                {
                    await WriteStepErrorAsync("extract", "特徵辨識回傳失敗狀態", ct);
                    await WriteErrorAsync("CAD 特徵辨識失敗，請確認 STEP 檔案格式正確", ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "呼叫特徵辨識 API 例外");
                await WriteStepErrorAsync("extract", ex.Message, ct);
                await WriteErrorAsync("特徵辨識服務無法連線，請確認 5051 服務已啟動", ct);
                return;
            }

            var feat = features.Features!;
            await WriteStepDoneAsync("extract", "CAD 特徵解析完成", feat, ct);

            // ── Step 3: 呼叫 8800 加工費預測 API ────────────────────────
            await WriteStepStartAsync("predict", "正在預測加工成本...", ct);

            // 從 DB 取得 position_tolerance / size_tolerance 的 en_name 作為公差代碼
            await using var dbCtx    = await _mlDbFactory.CreateDbContextAsync(ct);
            await using var appDbCtx = await _appDbFactory.CreateDbContextAsync(ct);

            var posParam  = await appDbCtx.BaseParameters.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == positionToleranceId, ct);
            var sizeParam = await appDbCtx.BaseParameters.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == sizeToleranceId, ct);

            PredictApiResponse? priceResult;
            try
            {
                var client = _httpClientFactory.CreateClient("Math");
                var predictReq = new
                {
                    shape                  = feat.Shape ?? "Plate",
                    length                 = feat.Length,
                    width                  = feat.Width,
                    height                 = feat.Height,
                    remove_volume          = feat.RemoveVolume,
                    surface_area           = feat.SurfaceArea,
                    blank_surface_area     = feat.BlankSurfaceArea,
                    blank_volume           = feat.BlankVolume,
                    complexity_coefficient = feat.ComplexityCoefficient,
                    circle_hole_count      = feat.CircleHoleCount,
                    non_circle_hole_count  = feat.NonCircleHoleCount,
                    groove_count           = feat.GrooveCount,
                    free_form_surface_area = feat.FreeFormSurfaceArea,
                    machining_directions   = feat.MachiningDirections,
                    processing_type        = "Machining",
                    dimensional_tolerance  = sizeParam?.EnName ?? sizeParam?.Name ?? "",
                    geometrical_tolerance  = posParam?.EnName  ?? posParam?.Name  ?? ""
                };

                var res = await client.PostAsJsonAsync("/predict", predictReq, ct);

                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(ct);
                    _logger.LogError("加工費預測失敗：{Status} {Body}", res.StatusCode, body);
                    await WriteStepErrorAsync("predict", $"加工費預測失敗（HTTP {(int)res.StatusCode}）", ct);
                    await WriteErrorAsync("加工費預測服務回應錯誤，請確認 8800 服務正常", ct);
                    return;
                }

                var bodyStr = await res.Content.ReadAsStringAsync(ct);
                priceResult = JsonSerializer.Deserialize<PredictApiResponse>(bodyStr, JsonOpts);

                if (priceResult?.Success != true)
                {
                    await WriteStepErrorAsync("predict", "加工費預測回傳失敗狀態", ct);
                    await WriteErrorAsync("加工費預測失敗", ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "呼叫加工費預測 API 例外");
                await WriteStepErrorAsync("predict", ex.Message, ct);
                await WriteErrorAsync("加工費預測服務無法連線，請確認 8800 服務已啟動", ct);
                return;
            }

            await WriteStepDoneAsync("predict", "加工成本預測完成", new
            {
                suggested_price  = priceResult.SuggestedPrice,
                range_min        = priceResult.RangeMin,
                range_max        = priceResult.RangeMax,
                predicted_range  = priceResult.PredictedRange,
                currency         = priceResult.Currency
            }, ct);

            // ── Step 4: 從 DB 取得材料、表面處理、熱處理參數計算成本 ───────
            await WriteStepStartAsync("calc", "計算最終報價...", ct);

            var matParam  = await appDbCtx.BaseParameters.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == materialId, ct);
            var surfParam = await appDbCtx.BaseParameters.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == surfaceId, ct);
            var heatParam = await appDbCtx.BaseParameters.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == heatTreatId, ct);

            if (matParam is null)
            {
                await WriteStepErrorAsync("calc", $"找不到材料 ID：{materialId}", ct);
                await WriteErrorAsync("無效的材料選項，請重新選擇", ct);
                return;
            }

            // weight(kg) = length(mm) * width(mm) * height(mm) / 1,000,000,000 * density(kg/m³)
            decimal volume    = feat.Length * feat.Width * feat.Height;
            decimal weightKg  = volume / 1_000_000_000m * matParam.Density;

            decimal materialCost = weightKg * matParam.UnitPrice;
            decimal surfaceCost  = weightKg * (surfParam?.UnitPrice ?? 0m);
            decimal heatCost     = weightKg * (heatParam?.UnitPrice  ?? 0m);
            decimal processCost  = priceResult.SuggestedPrice;
            decimal totalCost    = materialCost + surfaceCost + heatCost + processCost;

            await WriteStepDoneAsync("calc", "報價計算完成", new
            {
                material_cost          = materialCost,
                surface_treatment_cost = surfaceCost,
                heat_treatment_cost    = heatCost,
                processing_cost        = processCost,
                total_cost             = totalCost,
                currency               = priceResult.Currency,
                weight_kg              = weightKg
            }, ct);

            // ── Step 5: 儲存報價單到 DB ──────────────────────────────────
            await WriteStepStartAsync("save", "儲存報價單...", ct);

            int newQuoteId;
            try
            {
                var existingCount = await dbCtx.Quotations.CountAsync(ct);
                var quoteFileName = $"A{(existingCount + 1):D3}";
                var quoteName     = $"Q_{companyName}_{DateTime.UtcNow:yyyyMMdd}";

                var entity = new QuotationEntity
                {
                    QuoteName            = quoteName,
                    QuoteFileName        = quoteFileName,
                    CustomerName         = companyName!,
                    MaterialName         = matParam.Name,
                    SurfaceTreatment     = surfParam?.Name ?? "",
                    HeatTreatment        = heatParam?.Name ?? "",
                    MaterialCost         = materialCost,
                    ProcessingCost       = processCost,
                    SurfaceTreatmentCost = surfaceCost,
                    HeatTreatmentCost    = heatCost,
                    TotalCost            = totalCost,
                    CreatedAt            = DateTime.UtcNow,
                    UpdatedAt            = DateTime.UtcNow
                };

                dbCtx.Quotations.Add(entity);
                await dbCtx.SaveChangesAsync(ct);
                newQuoteId = entity.Id;

                _logger.LogInformation(
                    "報價 ID={Id} 儲存成功，公司={Company}，總計={Total}",
                    newQuoteId, companyName, totalCost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存報價失敗");
                await WriteStepErrorAsync("save", $"儲存失敗：{ex.Message}", ct);
                await WriteErrorAsync("報價資料儲存失敗，請稍後再試", ct);
                return;
            }

            await WriteStepDoneAsync("save", $"報價單 #{newQuoteId} 儲存成功", new
            {
                quote_id = newQuoteId
            }, ct);

            // ── 完成：送出 done 事件 ─────────────────────────────────────
            var doneData = new
            {
                quote_id     = $"qt_{DateTime.UtcNow:yyyyMMdd}_{newQuoteId:D4}",
                quote_db_id  = newQuoteId,
                status       = "completed",
                company_name = companyName,
                note         = note,

                // 1. 特徵辨識結果
                features = new
                {
                    shape                  = feat.Shape,
                    length_mm              = feat.Length,
                    width_mm               = feat.Width,
                    height_mm              = feat.Height,
                    surface_area_mm2       = feat.SurfaceArea,
                    blank_surface_area_mm2 = feat.BlankSurfaceArea,
                    blank_volume_mm3       = feat.BlankVolume,
                    remove_volume_mm3      = feat.RemoveVolume,
                    complexity_coefficient = feat.ComplexityCoefficient,
                    circle_hole_count      = feat.CircleHoleCount,
                    non_circle_hole_count  = feat.NonCircleHoleCount,
                    groove_count           = feat.GrooveCount,
                    free_form_surface_area = feat.FreeFormSurfaceArea,
                    machining_directions   = feat.MachiningDirections,
                    weight_kg              = weightKg
                },

                // 2. 加工費預測
                processing = new
                {
                    suggested_price = processCost,
                    range_min       = priceResult.RangeMin,
                    range_max       = priceResult.RangeMax,
                    predicted_range = priceResult.PredictedRange,
                    currency        = priceResult.Currency
                },

                // 3. 所有費用明細 + 最終報價
                costs = new
                {
                    currency               = priceResult.Currency,
                    material = new
                    {
                        name       = matParam.Name,
                        unit_price = matParam.UnitPrice,
                        density    = matParam.Density,
                        cost       = materialCost
                    },
                    surface_treatment = new
                    {
                        name       = surfParam?.Name ?? "无",
                        unit_price = surfParam?.UnitPrice ?? 0m,
                        cost       = surfaceCost
                    },
                    heat_treatment = new
                    {
                        name       = heatParam?.Name ?? "无",
                        unit_price = heatParam?.UnitPrice ?? 0m,
                        cost       = heatCost
                    },
                    processing_cost        = processCost,
                    total_cost             = totalCost
                },

                file = new
                {
                    file_id    = fileInfo.FileId,
                    file_name  = fileInfo.FileName,
                    size_bytes = fileInfo.SizeBytes
                }
            };

            var doneJson = JsonSerializer.Serialize(new StreamEvent
            {
                Type = StreamEventType.Done, StepData = doneData
            }, JsonOpts);
            await Response.WriteAsync($"data: {doneJson}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            // ── LLM 自然語言整理 ──────────────────────────────────────────
            var llmPrompt = $"""
                以下是一筆新增報價的完整資料，請用繁體中文把結果整理成清晰的報價報告回答給使用者。
                必須包含以下各項：
                1、【零件特徵】：形狀、長寬高（mm）、重量（kg）、加工方向數、複雜度係數、各類孔洞數量
                2、【加工費】：預測建議價及範圍
                3、【報價明細】：材料費、表面處理費、熱處理費、加工費並說明公式
                4、【最終報價】：總計金額（RMB）並加上簡短小結

                報價資料：
                公司：{companyName}
                CAD檔案：{fileInfo.FileName}

                特徵辨識結果：
                - 形狀：{feat.Shape}
                - 長x寬x高：{feat.Length:F1} x {feat.Width:F1} x {feat.Height:F1} mm
                - 重量：{weightKg:F3} kg
                - 加工方向數：{feat.MachiningDirections}
                - 表面積：{feat.SurfaceArea:F1} mm²
                - 複雜度係數：{feat.ComplexityCoefficient:F2}
                - 圓孔數：{feat.CircleHoleCount}，非圓孔：{feat.NonCircleHoleCount}，溝槽：{feat.GrooveCount}

                加工費預測 (8800 AI 模型)：
                - 建議價：{processCost:F2} RMB
                - 預測範圍：{priceResult.RangeMin:F2} ~ {priceResult.RangeMax:F2} RMB

                報價明細：
                - 材料：{matParam.Name}（密度 {matParam.Density} kg/m³，單價 {matParam.UnitPrice} 元/kg）
                  材料費 = {weightKg:F3} kg × {matParam.UnitPrice} = {materialCost:F2} RMB
                - 表面處理：{surfParam?.Name ?? "无"}（單價 {surfParam?.UnitPrice ?? 0} 元/kg）
                  表面處理費 = {weightKg:F3} kg × {surfParam?.UnitPrice ?? 0} = {surfaceCost:F2} RMB
                - 熱處理：{heatParam?.Name ?? "无"}（單價 {heatParam?.UnitPrice ?? 0} 元/kg）
                  熱處理費 = {weightKg:F3} kg × {heatParam?.UnitPrice ?? 0} = {heatCost:F2} RMB
                - 加工費：{processCost:F2} RMB
                - ────────────────────
                - 最終報價：{totalCost:F2} RMB
                """;

            var llmHistory = new ChatHistory();
            llmHistory.AddUserMessage(llmPrompt);

#pragma warning disable SKEXP0001
            var llmSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = null,
                MaxTokens        = 1024,
                Temperature      = 0.3,
            };
#pragma warning restore SKEXP0001

            var chat           = _kernel.GetRequiredService<IChatCompletionService>();
            var aiReplyBuilder = new System.Text.StringBuilder();

            try
            {
                await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                    llmHistory, llmSettings, _kernel, ct))
                {
                    if (string.IsNullOrEmpty(chunk.Content)) continue;
                    aiReplyBuilder.Append(chunk.Content);
                    var tokenJson = JsonSerializer.Serialize(
                        new StreamEvent { Type = StreamEventType.Token, Token = chunk.Content }, JsonOpts);
                    await Response.WriteAsync($"data: {tokenJson}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM 整理報價失敗");
            }

            // ── 儲存 AI 回覆到對話 ─────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(conversationId) && aiReplyBuilder.Length > 0)
            {
                try
                {
                    await _convService.AppendMessageAsync(conversationId, new ChatMessage
                    {
                        Id         = Guid.NewGuid().ToString("N"),
                        Role       = "ai",
                        Text       = aiReplyBuilder.ToString(),
                        SkillCalls = new List<SkillCallRecord>
                        {
                            new() {
                                Name   = "add_quote",
                                Args   = new { company_name = companyName, file = fileInfo.FileName },
                                Result = new { quote_id = newQuoteId, total_cost = totalCost, currency = priceResult.Currency },
                                Done   = true
                            }
                        },
                        Timestamp  = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "儲存 AI 報價回覆失敗，繼續執行");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("使用者 {UserId} 中斷新增報價串流", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新增報價串流發生未預期錯誤");
            try { await WriteErrorAsync($"發生錯誤：{ex.Message}", ct); } catch { }
        }
    }

    // ── SSE 工具方法 ──────────────────────────────────────────────────────

    private Task WriteStepStartAsync(string step, string message, CancellationToken ct)
        => WriteSseAsync(new StreamEvent
        {
            Type        = StreamEventType.StepStart,
            Step        = step,
            StepMessage = message
        }, ct);

    private Task WriteStepDoneAsync(string step, string message, object data, CancellationToken ct)
        => WriteSseAsync(new StreamEvent
        {
            Type        = StreamEventType.StepDone,
            Step        = step,
            StepMessage = message,
            StepData    = data
        }, ct);

    private Task WriteStepErrorAsync(string step, string message, CancellationToken ct)
        => WriteSseAsync(new StreamEvent
        {
            Type        = StreamEventType.StepError,
            Step        = step,
            StepMessage = message
        }, ct);

    private Task WriteErrorAsync(string message, CancellationToken ct)
        => WriteSseAsync(new StreamEvent
        {
            Type  = StreamEventType.Error,
            Token = message
        }, ct);

    private async Task WriteSseAsync(StreamEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, JsonOpts);
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private string GetUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException();

    // ── Internal DTOs（對應 5051 / 8800 API 回應）────────────────────────

    private class ExtractApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("features")]
        public ExtractFeatures? Features { get; set; }
    }

    private class ExtractFeatures
    {
        [JsonPropertyName("length")]
        public decimal Length { get; set; }
        [JsonPropertyName("width")]
        public decimal Width { get; set; }
        [JsonPropertyName("height")]
        public decimal Height { get; set; }
        [JsonPropertyName("surface_area")]
        public decimal SurfaceArea { get; set; }
        [JsonPropertyName("blank_surface_area")]
        public decimal BlankSurfaceArea { get; set; }
        [JsonPropertyName("blank_volume")]
        public decimal BlankVolume { get; set; }
        [JsonPropertyName("remove_volume")]
        public decimal RemoveVolume { get; set; }
        [JsonPropertyName("complexity_coefficient")]
        public decimal ComplexityCoefficient { get; set; }
        [JsonPropertyName("circle_hole_count")]
        public int CircleHoleCount { get; set; }
        [JsonPropertyName("non_circle_hole_count")]
        public int NonCircleHoleCount { get; set; }
        [JsonPropertyName("groove_count")]
        public int GrooveCount { get; set; }
        [JsonPropertyName("free_form_surface_area")]
        public decimal FreeFormSurfaceArea { get; set; }
        [JsonPropertyName("machining_directions")]
        public int MachiningDirections { get; set; }
        [JsonPropertyName("shape")]
        public string? Shape { get; set; }
    }

    private class PredictApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "RMB";
        [JsonPropertyName("suggested_price")]
        public decimal SuggestedPrice { get; set; }
        [JsonPropertyName("range_min")]
        public decimal RangeMin { get; set; }
        [JsonPropertyName("range_max")]
        public decimal RangeMax { get; set; }
        [JsonPropertyName("predicted_range")]
        public string PredictedRange { get; set; } = "";
    }
}
