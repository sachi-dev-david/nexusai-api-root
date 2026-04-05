# 新增報價 API 使用指南

## 端點信息

- **方法**: POST
- **路由**: `/api/quotes/add`
- **認證**: 需要 JWT Token (Authorization: Bearer {token})

## 請求體格式

```json
{
  "quoteName": "Q_REQUEST",
  "quoteFileName": "A001",
  "customerName": "HZD",
  "filePath": "D:\\Models\\part001.stp",
  "materialName": "316不鏽鋼",
  "surfaceTreatment": "磨砂",
  "heatTreatment": "無"
}
```

## 參數說明

### 必填參數

| 參數名稱 | 類型 | 說明 | 範例 |
|---------|------|------|------|
| `quoteName` | string | 報價單名稱，格式：Q_{項目名} | `Q_SACH`, `Q_REQUEST` |
| `quoteFileName` | string | 報價文件編號，格式：A001, B001 等 | `A001`, `B002` |
| `customerName` | string | 公司/客戶名稱 | `HZD`, `HZDCIM`, `SachiTech` |
| `filePath` | string | STP 文件完整路徑 | `D:\Models\part001.stp` |

### 可選參數

| 參數名稱 | 類型 | 說明 | 範例 |
|---------|------|------|------|
| `materialName` | string? | 加工材料 | `316不鏽鋼`, `鋁合金`, `銅` |
| `surfaceTreatment` | string? | 表面處理 | `磨砂`, `拋光`, `氧化`, `無` |
| `heatTreatment` | string? | 熱處理 | `退火`, `淬火`, `回火`, `無` |

## 成功響應 (200 OK)

```json
{
  "code": 200,
  "message": "成功",
  "data": "{\"success\":true,\"message\":\"Quotation created successfully\",\"quotation\":{\"id\":123,\"quoteName\":\"Q_REQUEST\",\"quoteFileName\":\"A001\",\"customerName\":\"HZD\",\"materialCost\":1000.00,\"processingCost\":2500.00,\"surfaceTreatmentCost\":300.00,\"heatTreatmentCost\":0.00,\"totalCost\":3800.00,\"createdAt\":\"2026-03-15 14:30:45\"}}"
}
```

## 錯誤響應

### 缺少必填參數 (400 Bad Request)

```json
{
  "code": 400,
  "message": "customer_name 不能為空",
  "data": null
}
```

### 伺服器錯誤 (500 Internal Server Error)

```json
{
  "code": 500,
  "message": "伺服器錯誤: Feature parsing API failed with status 500",
  "data": null
}
```

## cURL 示例

```bash
curl -X POST "http://localhost:5000/api/quotes/add" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
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

## 工作流程

1. **接收請求** - API 接收 POST 請求，驗證所有必填參數
2. **調用插件** - 將參數傳遞給 `AddQuotePlugin.AddQuoteAsync()`
3. **特徵辨識** - 插件調用特徵辨識 API (`http://localhost:8801/parse`)
4. **成本預測** - 插件調用 ML 模型 API (`http://localhost:8800/predict`)
5. **保存數據** - 將報價信息和計算結果保存到數據庫
6. **返回結果** - 返回創建的報價詳細信息

## 調用方式

### 方式 1: 直接 API 調用（推薦）

通過 REST API 直接呼叫此端點。

**優點**:
- 直接、快速
- 容易集成到外部系統
- 適合自動化流程

**缺點**:
- 需要知道 STP 文件路徑
- 需要手動提供所有參數

### 方式 2: 通過 AI 助手（Semantic Kernel）

通過自然語言指令讓 AI 助手呼叫此函數。

**提示詞範例**:
```
請執行 新增報價 - 加工材料: 316不鏽鋼, 表面處理: 磨砂, 熱處理: 無, 公司名稱: HZD
```

**優點**:
- 支持自然語言輸入
- LLM 可以自動推理和填充參數
- 更加用戶友好

**缺點**:
- 需要依賴 LLM 的理解正確性
- 可能需要額外的參數推理邏輯

## 常見問題

### Q: 為什麼報價沒有被創建？
A: 檢查以下幾點：
- STP 文件路徑是否正確
- 特徵辨識 API 是否運行（localhost:8801）
- ML 模型 API 是否運行（localhost:8800）
- 查看伺服器日誌獲取詳細錯誤信息

### Q: 支持哪些文件格式？
A: 目前只支持 STP/STEP 格式。API 路徑應該指向有效的 STP 文件。

### Q: 可以批量新增報價嗎？
A: 目前 API 只支持單個報價創建。需要批量創建時，請循環調用此 API。

## 集成指南

### JavaScript/TypeScript

```javascript
async function addQuote(params) {
  const response = await fetch('/api/quotes/add', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    },
    body: JSON.stringify(params)
  });
  return response.json();
}

// 使用
const result = await addQuote({
  quoteName: 'Q_SACH',
  quoteFileName: 'A001',
  customerName: 'HZD',
  filePath: 'D:\\Models\\part001.stp',
  materialName: '316不鏽鋼',
  surfaceTreatment: '磨砂',
  heatTreatment: '無'
});
```

### Python

```python
import requests

def add_quote(token, params):
    headers = {
        'Content-Type': 'application/json',
        'Authorization': f'Bearer {token}'
    }
    response = requests.post(
        'http://localhost:5000/api/quotes/add',
        json=params,
        headers=headers
    )
    return response.json()

# 使用
result = add_quote(token, {
    'quoteName': 'Q_SACH',
    'quoteFileName': 'A001',
    'customerName': 'HZD',
    'filePath': 'D:\\Models\\part001.stp',
    'materialName': '316不鏽鋼',
    'surfaceTreatment': '磨砂',
    'heatTreatment': '無'
})
```

### C# / .NET

```csharp
using System.Net.Http.Json;

public class AddQuoteRequest
{
    public string QuoteName { get; set; }
    public string QuoteFileName { get; set; }
    public string CustomerName { get; set; }
    public string FilePath { get; set; }
    public string MaterialName { get; set; }
    public string SurfaceTreatment { get; set; }
    public string HeatTreatment { get; set; }
}

var client = new HttpClient();
client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

var request = new AddQuoteRequest
{
    QuoteName = "Q_SACH",
    QuoteFileName = "A001",
    CustomerName = "HZD",
    FilePath = "D:\\Models\\part001.stp",
    MaterialName = "316不鏽鋼",
    SurfaceTreatment = "磨砂",
    HeatTreatment = "無"
};

var response = await client.PostAsJsonAsync(
    "http://localhost:5000/api/quotes/add",
    request
);
```

