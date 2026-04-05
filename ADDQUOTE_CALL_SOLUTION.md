# AddQuoteAsync 呼叫方案 - 完成總結

## 問題
用戶反映「沒有地方呼叫 AddQuoteAsync」

## 解決方案

已實現 **兩種呼叫方式**：

### 方式 1: Semantic Kernel 插件（通過 AI 助手）

**流程**:
```
用戶提示 (自然語言) 
  ↓
LLM 解析並識別 add_quote 函數
  ↓
Semantic Kernel 自動調用 AddQuotePlugin.AddQuoteAsync()
  ↓
返回報價結果
```

**配置位置**: `Program.cs` 第 150-157 行
```csharp
kernel.Plugins.AddFromObject(
    addQuotePlugin, "AddQuote");
```

**用戶提示詞範例**:
```
請執行 新增報價 - 加工材料: 316不鏽鋼, 表面處理: 磨砂, 熱處理: 無, 公司名稱: HZD
```

---

### 方式 2: REST API 端點（直接調用）

**新建文件**: `NexusAI.Api/Controllers/QuotesController.cs`

**端點信息**:
- 路由: `POST /api/quotes/add`
- 認證: 需要 JWT Token
- 請求體: JSON 格式的報價參數

**請求示例**:
```bash
curl -X POST "http://localhost:5000/api/quotes/add" \
  -H "Authorization: Bearer {JWT_TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "quoteName": "Q_SACH",
    "quoteFileName": "A001",
    "customerName": "HZD",
    "filePath": "D:\\Models\\part001.stp",
    "materialName": "316不鏽鋼",
    "surfaceTreatment": "磨砂",
    "heatTreatment": "無"
  }'
```

**回應示例**:
```json
{
  "code": 200,
  "message": "成功",
  "data": "{\"success\":true,\"quotation\":{\"id\":123,\"totalCost\":3800.00,...}}"
}
```

---

## 實現細節

### 1. 依賴注入配置

**新增到 Program.cs**:
```csharp
// ── PLUGINS AS SERVICES ────────────────────────────────────────────────────
builder.Services.AddScoped(sp =>
{
    var dbFactory = sp.GetRequiredService<IDbContextFactory<MlDbContext>>();
    var logger = sp.GetRequiredService<ILogger<AddQuotePlugin>>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    return new AddQuotePlugin(dbFactory, logger, httpClient);
});
```

**優點**:
- AddQuotePlugin 作為單一服務實例註冊
- 既可被 Controller 注入，也可被 Semantic Kernel 使用
- 避免重複創建實例

### 2. QuotesController 實現

**關鍵特點**:
- 驗證所有必填參數
- 直接調用 `AddQuotePlugin.AddQuoteAsync()`
- 返回統一的 `ApiResponse` 格式
- 完整的錯誤處理和日誌記錄

**參數映射**:
```csharp
[FromBody] AddQuoteRequest request
  ↓
request.QuoteName → quote_name
request.QuoteFileName → quote_file_name
request.CustomerName → customer_name
request.FilePath → file_path
request.MaterialName → material_name
request.SurfaceTreatment → surface_treatment
request.HeatTreatment → heat_treatment
```

### 3. Semantic Kernel 集成

**現有配置保持不變**:
```csharp
var addQuotePlugin = sp.GetRequiredService<AddQuotePlugin>();
kernel.Plugins.AddFromObject(addQuotePlugin, "AddQuote");
```

- 複用同一個 AddQuotePlugin 實例
- LLM 可以通過 Semantic Kernel 自動調用

---

## 文件清單

| 文件 | 變更 | 說明 |
|------|------|------|
| `AddQuotePlugin.cs` | 已存在 | Semantic Kernel 插件實現 |
| `QuotesController.cs` | ✅ 新建 | REST API 控制器 |
| `Program.cs` | ✅ 已修改 | 依賴注入配置 |
| `API_QUOTE_GUIDE.md` | ✅ 新建 | API 使用文檔 |

---

## 使用場景

### 場景 1: 用戶通過 AI 助手
```
用戶輸入: "請執行 新增報價 - ..."
↓
AI 助手通過 Semantic Kernel 調用 add_quote
↓
AddQuotePlugin.AddQuoteAsync() 執行
↓
返回報價結果
```

### 場景 2: 外部系統集成
```
外部系統 → HTTP POST /api/quotes/add
↓
QuotesController.AddQuote() 處理
↓
AddQuotePlugin.AddQuoteAsync() 執行
↓
返回 JSON 響應
```

### 場景 3: 前端應用直接調用
```
前端應用 (JavaScript/React)
↓
fetch() POST /api/quotes/add
↓
QuotesController.AddQuote() 處理
↓
返回報價結果
```

---

## API 速查表

### 端點
```
POST /api/quotes/add
```

### 必填參數
- `quoteName` - 報價單名稱
- `quoteFileName` - 文件編號
- `customerName` - 公司名稱
- `filePath` - STP 文件路徑

### 可選參數
- `materialName` - 加工材料
- `surfaceTreatment` - 表面處理
- `heatTreatment` - 熱處理

### 成功響應 (200)
```json
{
  "code": 200,
  "message": "成功",
  "data": "{\n  \"success\": true,\n  \"quotation\": {...}\n}"
}
```

### 錯誤響應
- 400 - 缺少必填參數
- 500 - 伺服器錯誤 (API 調用失敗等)

---

## 下一步

1. **測試 API**:
   - 使用 Postman 或 curl 測試端點
   - 確保 STP 文件路徑有效
   - 確保特徵辨識 API 和 ML API 運行中

2. **集成前端**:
   - 前端通過 `/api/quotes/add` 調用
   - 處理成功/錯誤響應

3. **優化參數生成**:
   - 可以添加自動增量的 `quoteFileName` 生成邏輯
   - 可以添加 `quote_name` 自動生成規則

---

## 文檔參考

- `API_QUOTE_GUIDE.md` - 完整的 API 文檔和範例
- `add_quote_prompt_guide.md` - 提示詞指南
- `add_quote_prompt_template.txt` - 快速參考卡片

