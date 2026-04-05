namespace NexusAI.Api.Models;

// ── AUTH ──────────────────────────────────────────────────────────────────

public class LoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class LoginResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public required string Role { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// ── CONVERSATION ──────────────────────────────────────────────────────────

public class ConversationSummary
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Preview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChatMessage
{
    public required string Id { get; set; }
    public required string Role { get; set; }   // "user" | "ai"
    public required string Text { get; set; }
    public string? File { get; set; }
    public List<SkillCallRecord>? SkillCalls { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SkillCallRecord
{
    public required string Name { get; set; }
    public required object Args { get; set; }
    public object? Result { get; set; }
    public bool Done { get; set; }
}

// ── CHAT STREAM ───────────────────────────────────────────────────────────

public class ChatStreamRequest
{
    public required string ConversationId { get; set; }
    public required string Message { get; set; }
    public string? FileId { get; set; }         // 上傳後取得的 fileId
}

/// <summary>SSE 事件類型</summary>
public static class StreamEventType
{
    public const string SkillStart = "skill_start";
    public const string SkillDone  = "skill_done";
    public const string Token      = "token";
    public const string Done       = "done";
    public const string Error      = "error";
    public const string StepStart  = "step_start";     // 外部 API 步驟開始
    public const string StepDone   = "step_done";      // 外部 API 步驟完成
    public const string StepError  = "step_error";    // 外部 API 步驟失敗
    public const string StepData   = "step_data";     // 外部 API 步驟資料
}

public class StreamEvent
{
    public required string Type { get; set; }   // StreamEventType
    public string? SkillName { get; set; }
    public object? SkillArgs { get; set; }
    public object? SkillResult { get; set; }
    public string? Token { get; set; }
    /// <summary>步驟名稱（如 step_1_extract, step_2_predict）</summary>
    public string? Step { get; set; }
    /// <summary>步驟訊息（如「正在呼叫特徵辨識 API...」）</summary>
    public string? StepMessage { get; set; }
    /// <summary>步驟結果資料</summary>
    public object? StepData { get; set; }
}

// ── DEVICE ────────────────────────────────────────────────────────────────

public class DeviceSummary
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; } // "running" | "idle" | "warning"
    public float Temperature { get; set; }
}

public class DeviceDetail : DeviceSummary
{
    public float Rpm { get; set; }
    public float LoadPercent { get; set; }
    public required string Uptime { get; set; }
    public DateTime LastUpdated { get; set; }
}

// ── MODEL STATUS ─────────────────────────────────────────────────────────

public class ModelStatus
{
    public required string Name { get; set; }
    public required string Status { get; set; } // "online" | "offline"
    public string? Message { get; set; }
}

public class AllModelsStatus
{
    public required ModelStatus Ollama { get; set; }
    public required ModelStatus Vision { get; set; }
    public required ModelStatus Math { get; set; }
}

// ── SKILL ─────────────────────────────────────────────────────────────────

public class SkillItem
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Label { get; set; }
    public required string Icon { get; set; }
    public required string Color { get; set; }
}

// ── FILE UPLOAD ───────────────────────────────────────────────────────────

public class FileUploadResponse
{
    public required string FileId { get; set; }
    public required string FileName { get; set; }
    /// <summary>伺服器上的實際檔案路徑，新增報價時作為 file_path 使用</summary>
    public required string FilePath { get; set; }
    public long SizeBytes { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// ── ADD QUOTE ─────────────────────────────────────────────────────────────

/// <summary>新增報價請求（API 使用）</summary>
public class AddQuoteApiRequest
{
    public required string QuoteName { get; set; }
    public required string QuoteFileName { get; set; }
    public required string CustomerName { get; set; }
    /// <summary>STEP 檔案的 fileId（從 /api/files/upload 取得）</summary>
    public string? StepFileId { get; set; }
    /// <summary>STEP 檔案直接路徑（兩者二選一）</summary>
    public string? StepFilePath { get; set; }
    /// <summary>材料名稱</summary>
    public string? MaterialName { get; set; }
    /// <summary>表面處理</summary>
    public string? SurfaceTreatment { get; set; }
    /// <summary>熱處理</summary>
    public string? HeatTreatment { get; set; }
    /// <summary>型位公差</summary>
    public string? DimensionalTolerance { get; set; }
    /// <summary>尺寸公差</summary>
    public string? GeometricalTolerance { get; set; }
}

/// <summary>新增報價回應</summary>
public class AddQuoteApiResponse
{
    public bool Success { get; set; }
    public int? QuoteId { get; set; }
    public string? Error { get; set; }
    public AddQuoteFeatures? Features { get; set; }
    public AddQuoteCosts? Costs { get; set; }
    public AddQuoteResult? Result { get; set; }
}

/// <summary>從 STEP 檔案取出幾何特徵</summary>
public class AddQuoteFeatures
{
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal SurfaceArea { get; set; }
    public decimal BlankSurfaceArea { get; set; }
    public decimal BlankVolume { get; set; }
    public decimal RemoveVolume { get; set; }
    public decimal ComplexityCoefficient { get; set; }
    public int CircleHoleCount { get; set; }
    public int NonCircleHoleCount { get; set; }
    public int GrooveCount { get; set; }
    public decimal FreeFormSurfaceArea { get; set; }
    public int MachiningDirections { get; set; }
    public string? Shape { get; set; }
}

/// <summary>加工費用預測結果</summary>
public class AddQuoteCosts
{
    public string Currency { get; set; } = "RMB";
    public decimal SuggestedPrice { get; set; }
    public decimal RangeMin { get; set; }
    public decimal RangeMax { get; set; }
    public string PredictedRange { get; set; } = "";
    public List<ProbabilityItem> Probabilities { get; set; } = [];
    public List<SimilarCase> SimilarCases { get; set; } = [];
}

public class ProbabilityItem
{
    public string Label { get; set; } = "";
    public decimal Probability { get; set; }
}

public class SimilarCase
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

/// <summary>最終報價結果</summary>
public class AddQuoteResult
{
    public decimal MaterialCost { get; set; }
    public decimal SurfaceTreatmentCost { get; set; }
    public decimal HeatTreatmentCost { get; set; }
    public decimal ProcessingCost { get; set; }
    public decimal TotalCost { get; set; }
    public string Currency { get; set; } = "RMB";
}

// ── QUOTE FORM OPTIONS ────────────────────────────────────────────────────

/// <summary>單一選單項目（id + 顯示文字）</summary>
public class QuoteOptionItem
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>GET /api/quotes/options 回案資料</summary>
public class QuoteOptionsData
{
    public List<QuoteOptionItem> Materials { get; set; } = [];
    public List<QuoteOptionItem> Surfaces { get; set; } = [];
    public List<QuoteOptionItem> HeatTreats { get; set; } = [];
    public List<QuoteOptionItem> Roughnesses { get; set; } = [];
    public List<QuoteOptionItem> PositionTolerances { get; set; } = [];
    public List<QuoteOptionItem> SizeTolerances { get; set; } = [];
}
