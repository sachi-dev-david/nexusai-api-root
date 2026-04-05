# NexusAI 新增報價 API — 前端串接文件

更新日期：2026-04-05  
後端維護：NexusAI Backend Team

---

## 目錄

1. [概覽與流程](#1-概覽與流程)
2. [通用規格](#2-通用規格)
3. [API 1 — 取得下拉選單（GET /api/quotes/options）](#3-api-1--取得下拉選單)
4. [API 2 — 選單版本（GET /api/quotes/options/version）](#4-api-2--選單版本)
5. [API 3 — 新增報價（POST /api/quotes）](#5-api-3--新增報價)
6. [SSE 串流事件詳解](#6-sse-串流事件詳解)
7. [錯誤處理](#7-錯誤處理)
8. [前端實作範例（JavaScript）](#8-前端實作範例javascript)
9. [報價費用計算公式說明](#9-報價費用計算公式說明)
10. [常見問題](#10-常見問題)

---

## 1. 概覽與流程

### 1.1 整體流程

```
前端開啟「新增報價」視窗
  │
  ├─► GET /api/quotes/options          ← 拉取下拉選單資料（一次，可快取）
  │
使用者填寫表單 + 選取 CAD 檔案（.step / .stp / .prt）
  │
使用者按「確認送出」
  │
  └─► POST /api/quotes  (multipart/form-data)
        │  Content-Type: multipart/form-data
        │  回應: text/event-stream (SSE)
        │
        ├── [SSE] step_start: save_file  → 後端儲存 CAD 到暫存目錄
        ├── [SSE] step_done:  save_file  → 回傳 file_id / file_path
        ├── [SSE] step_start: extract    → 呼叫特徵辨識 (port 5051)
        ├── [SSE] step_done:  extract    → 回傳幾何特徵數值
        ├── [SSE] step_start: predict    → 呼叫加工費預測 (port 8800)
        ├── [SSE] step_done:  predict    → 回傳加工費建議價格
        ├── [SSE] step_start: calc       → 計算材料費 / 表面處理費 / 熱處理費
        ├── [SSE] step_done:  calc       → 回傳各項費用明細
        ├── [SSE] step_start: save       → 寫入資料庫
        └── [SSE] done                   → 回傳最終報價單完整資訊
```

### 1.2 重要說明

- 前端**不需要**先呼叫 `/api/files/upload`，CAD 檔案隨表單一起 POST
- POST /api/quotes 回應是 **SSE 串流（text/event-stream）**，不是一般 JSON 回應
- 在送出前驗證錯誤時（HTTP 400 / 415），回應是**普通 JSON**（非 SSE）
- 所有 SSE 事件格式統一為：`data: {JSON}\n\n`

---

## 2. 通用規格

### 2.1 Base URL

```
http://localhost:5050
```

### 2.2 認證

所有報價 API 都需要 JWT Token：

```
Authorization: Bearer <token>
```

Token 透過 `POST /api/auth/login` 取得。

### 2.3 通用 JSON 回應格式（非串流端點）

**成功：**
```json
{
  "success": true,
  "data": { ... }
}
```

**失敗：**
```json
{
  "success": false,
  "error": "錯誤訊息"
}
```

---

## 3. API 1 — 取得下拉選單

### 端點

```
GET /api/quotes/options
Authorization: Bearer <token>
```

### 說明

前端開啟「新增報價」視窗時呼叫一次。取得材料、表面處理、熱處理、表面粗糙度、型位公差、尺寸公差的選單清單。

回傳的每個項目包含 `id`（送出時使用）和 `label`（顯示用）。

### Response 200

```json
{
  "success": true,
  "data": {
    "materials": [
      { "id": "4e998812-6995-11ee-ab2d-ac82472e843d", "label": "45钢",         "enabled": true },
      { "id": "4ea407e6-6995-11ee-ab2d-ac82472e843d", "label": "SUS304棒材",   "enabled": true },
      { "id": "7f8a18c2-6995-11ee-ab2d-ac82472e843d", "label": "AL6061-圆棒",  "enabled": true }
    ],
    "surfaces": [
      { "id": "ad1f7d41-6996-11ee-ab2d-ac82472e843d", "label": "无",           "enabled": true },
      { "id": "ad20775f-6996-11ee-ab2d-ac82472e843d", "label": "发黑",         "enabled": true },
      { "id": "ad22e8d1-6996-11ee-ab2d-ac82472e843d", "label": "阳极氧化",     "enabled": true }
    ],
    "heatTreats": [
      { "id": "5fb501c1-7ec8-11ee-91a2-2ec64bbaf159", "label": "无",           "enabled": true },
      { "id": "5fb5c5f1-7ec8-11ee-91a2-2ec64bbaf159", "label": "调质",         "enabled": true },
      { "id": "5fb683b0-7ec8-11ee-91a2-2ec64bbaf159", "label": "淬火",         "enabled": true }
    ],
    "roughnesses": [
      { "id": "8caa3b1e-c50b-11ee-ac9a-ac82472e843d", "label": "无",           "enabled": true },
      { "id": "8cabb748-c50b-11ee-ac9a-ac82472e843d", "label": "0.4",          "enabled": true },
      { "id": "8cac6808-c50b-11ee-ac9a-ac82472e843d", "label": "1.6",          "enabled": true }
    ],
    "positionTolerances": [
      { "id": "cf23febd-51e3-4863-8f90-c5ee99e3bc40", "label": "无",           "enabled": true },
      { "id": "a316f3e4-806d-42e6-b644-ab0d8240d1a9", "label": "GB/T1184-H",  "enabled": true },
      { "id": "e388e6bd-f560-4f24-8b48-10539b038515", "label": "GB/T1184-K",  "enabled": true }
    ],
    "sizeTolerances": [
      { "id": "900ee59d-d926-4691-b00e-ddf4a8238f30", "label": "无",           "enabled": true },
      { "id": "d93d175d-c6ea-4d3b-92ea-530a4dbfe851", "label": "GB/T1804-m",  "enabled": true },
      { "id": "25749bd3-98d8-4b0f-98ad-a62578f0e9af", "label": "GB/T1804-c",  "enabled": true }
    ]
  }
}
```

### 欄位說明

| 欄位 | 說明 |
|------|------|
| `materials` | 材料選單，`id` 對應 `material_id` 欄位 |
| `surfaces` | 表面處理選單，`id` 對應 `surface_id` 欄位 |
| `heatTreats` | 熱處理選單，`id` 對應 `heat_treat_id` 欄位 |
| `roughnesses` | 表面粗糙度選單，`id` 對應 `roughness_id` 欄位 |
| `positionTolerances` | 型位公差選單，`id` 對應 `position_tolerance_id` 欄位 |
| `sizeTolerances` | 尺寸公差選單，`id` 對應 `size_tolerance_id` 欄位 |
| `enabled` | 目前固定為 `true`，保留供後續停用選項用 |

### Response 401

```json
{ "success": false, "error": "Unauthorized" }
```

### Response 500

```json
{ "success": false, "error": "Internal Server Error" }
```

---

## 4. API 2 — 選單版本

### 端點

```
GET /api/quotes/options/version
Authorization: Bearer <token>
```

### 說明

前端可在每次進入頁面時先拉版本號，與前次快取比對。版本不同再重新呼叫 `/api/quotes/options`，減少不必要請求。

### Response 200

```json
{
  "success": true,
  "data": {
    "version": "2026-04-05.1"
  }
}
```

---

## 5. API 3 — 新增報價

### 端點

```
POST /api/quotes
Authorization: Bearer <token>
Content-Type: multipart/form-data
```

> ⚠️ **回應為 SSE 串流（text/event-stream），不是一般 JSON。**  
> 請用 `fetch` + `ReadableStream` 或 `EventSource`（需 POST 版本，建議用 fetch）處理。

### Request — Form Fields

| 欄位名稱 | 類型 | 必填 | 說明 |
|---------|------|------|------|
| `material_id` | string | ✅ | 材料 ID（從 options.materials[].id 取得） |
| `surface_id` | string | ✅ | 表面處理 ID（從 options.surfaces[].id 取得） |
| `heat_treat_id` | string | ✅ | 熱處理 ID（從 options.heatTreats[].id 取得） |
| `roughness_id` | string | ✅ | 表面粗糙度 ID（從 options.roughnesses[].id 取得） |
| `position_tolerance_id` | string | ✅ | 型位公差 ID（從 options.positionTolerances[].id 取得） |
| `size_tolerance_id` | string | ✅ | 尺寸公差 ID（從 options.sizeTolerances[].id 取得） |
| `company_name` | string | ✅ | 客戶 / 公司名稱 |
| `note` | string | ❌ | 備註（可空） |
| `cad_file` | file | ✅ | CAD 檔案（僅接受 `.step` / `.stp` / `.prt`，最大 50MB） |

### 驗證失敗（HTTP 400）— 純 JSON，非 SSE

欄位缺失：
```json
{
  "success": false,
  "error": "ValidationError",
  "details": [
    { "field": "material_id",   "message": "required" },
    { "field": "company_name",  "message": "required" }
  ]
}
```

CAD 檔為空：
```json
{
  "success": false,
  "error": "cad_file is required"
}
```

### 不支援的檔案格式（HTTP 415）— 純 JSON，非 SSE

```json
{
  "success": false,
  "error": "Unsupported file type. Allowed: .step, .stp, .prt"
}
```

---

## 6. SSE 串流事件詳解

通過驗證後，後端切換為 SSE 串流，依序送出以下事件。

### SSE 通用格式

每個 SSE 事件格式為：

```
data: {JSON物件}\n\n
```

JSON 物件結構：

```ts
interface SseEvent {
  type: string;          // 事件類型，見下方說明
  step?: string;         // 步驟代碼（step_start / step_done / step_error 有此欄位）
  stepMessage?: string;  // 步驟說明文字（進度條用）
  stepData?: object;     // 步驟資料（step_done / done 才有）
  token?: string;        // 錯誤訊息文字（error 事件才有）
}
```

### 6.1 事件類型總覽

| `type` 值 | 說明 | 一定有 `step` | 一定有 `stepData` |
|-----------|------|:---:|:---:|
| `step_start` | 某個步驟開始執行 | ✅ | ❌ |
| `step_done` | 某個步驟成功完成 | ✅ | ✅ |
| `step_error` | 某個步驟執行失敗 | ✅ | ❌ |
| `error` | 整體流程中斷錯誤 | ❌ | ❌ |
| `done` | 全部完成（最後一個事件） | ❌ | ✅ |

---

### 6.2 步驟代碼（step）說明

| `step` 值 | 說明 |
|-----------|------|
| `save_file` | 步驟 1：儲存 CAD 檔案到伺服器暫存目錄 |
| `extract` | 步驟 2：呼叫特徵辨識 API（5051），解析幾何特徵 |
| `predict` | 步驟 3：呼叫加工費預測 API（8800），得出建議加工費 |
| `calc` | 步驟 4：計算材料費、表面處理費、熱處理費，合計 |
| `save` | 步驟 5：將報價單寫入資料庫 |

---

### 6.3 各步驟事件詳細說明

#### Step 1：儲存 CAD 檔案

**開始：**
```json
{
  "type": "step_start",
  "step": "save_file",
  "stepMessage": "正在儲存 CAD 檔案..."
}
```

**完成：**
```json
{
  "type": "step_done",
  "step": "save_file",
  "stepMessage": "檔案儲存完成",
  "stepData": {
    "fileId": "file_a1b2c3d4e5f6...",
    "fileName": "part001.stp",
    "filePath": "D:\\uploads\\temp\\file_a1b2c3d4e5f6.stp",
    "sizeBytes": 245760
  }
}
```

| 欄位 | 型別 | 說明 |
|------|------|------|
| `fileId` | string | 檔案 ID |
| `fileName` | string | 原始檔名 |
| `filePath` | string | 伺服器實際儲存路徑（供 debug 用） |
| `sizeBytes` | number | 檔案大小（bytes） |

---

#### Step 2：CAD 特徵辨識（5051）

**開始：**
```json
{
  "type": "step_start",
  "step": "extract",
  "stepMessage": "正在解析 CAD 特徵：part001.stp..."
}
```

**完成：**
```json
{
  "type": "step_done",
  "step": "extract",
  "stepMessage": "CAD 特徵解析完成",
  "stepData": {
    "length": 150.5,
    "width": 80.3,
    "height": 25.0,
    "surfaceArea": 48620.4,
    "blankSurfaceArea": 52100.0,
    "blankVolume": 302606.5,
    "removeVolume": 18450.2,
    "complexityCoefficient": 1.25,
    "circleHoleCount": 4,
    "nonCircleHoleCount": 2,
    "grooveCount": 1,
    "freeFormSurfaceArea": 0.0,
    "machiningDirections": 3,
    "shape": "Plate"
  }
}
```

| 欄位 | 型別 | 單位 | 說明 |
|------|------|------|------|
| `length` | number | mm | 零件長度 |
| `width` | number | mm | 零件寬度 |
| `height` | number | mm | 零件高度 |
| `surfaceArea` | number | mm² | 加工表面積 |
| `blankSurfaceArea` | number | mm² | 毛坯表面積 |
| `blankVolume` | number | mm³ | 毛坯體積 |
| `removeVolume` | number | mm³ | 去除材料體積 |
| `complexityCoefficient` | number | — | 複雜度係數 |
| `circleHoleCount` | number | — | 圓孔數量 |
| `nonCircleHoleCount` | number | — | 非圓孔數量 |
| `grooveCount` | number | — | 溝槽數量 |
| `freeFormSurfaceArea` | number | mm² | 自由曲面面積 |
| `machiningDirections` | number | — | 加工方向數 |
| `shape` | string | — | 形狀分類（Plate / Cylindrical / Complex 等）|

---

#### Step 3：加工費預測（8800）

**開始：**
```json
{
  "type": "step_start",
  "step": "predict",
  "stepMessage": "正在預測加工成本..."
}
```

**完成：**
```json
{
  "type": "step_done",
  "step": "predict",
  "stepMessage": "加工成本預測完成",
  "stepData": {
    "suggestedPrice": 320.5,
    "rangeMin": 280.0,
    "rangeMax": 380.0,
    "predictedRange": "300-350",
    "currency": "RMB"
  }
}
```

| 欄位 | 型別 | 說明 |
|------|------|------|
| `suggestedPrice` | number | 建議加工費（用於最終報價計算） |
| `rangeMin` | number | 預測範圍下限 |
| `rangeMax` | number | 預測範圍上限 |
| `predictedRange` | string | 範圍文字描述 |
| `currency` | string | 幣別（固定 `"RMB"`）|

---

#### Step 4：成本計算

**開始：**
```json
{
  "type": "step_start",
  "step": "calc",
  "stepMessage": "計算最終報價..."
}
```

**完成：**
```json
{
  "type": "step_done",
  "step": "calc",
  "stepMessage": "報價計算完成",
  "stepData": {
    "materialCost": 218.4,
    "surfaceTreatmentCost": 12.5,
    "heatTreatmentCost": 8.2,
    "processingCost": 320.5,
    "totalCost": 559.6,
    "currency": "RMB",
    "weightKg": 2.73
  }
}
```

| 欄位 | 型別 | 說明 |
|------|------|------|
| `materialCost` | number | 材料費（元） |
| `surfaceTreatmentCost` | number | 表面處理費（元） |
| `heatTreatmentCost` | number | 熱處理費（元） |
| `processingCost` | number | 加工費（來自 8800 的 suggestedPrice） |
| `totalCost` | number | 總報價 = 上四項合計 |
| `currency` | string | 幣別 |
| `weightKg` | number | 計算出的零件重量（kg），供參考 |

---

#### Step 5：儲存報價單

**開始：**
```json
{
  "type": "step_start",
  "step": "save",
  "stepMessage": "儲存報價單..."
}
```

> 此步驟無 `step_done` 事件，成功後直接發出 `done` 事件。

---

#### 完成事件（done）

這是**最後一個事件**，包含完整報價資訊。

```json
{
  "type": "done",
  "stepData": {
    "quoteId": "qt_20260405_0001",
    "status": "completed",
    "file": {
      "fileId": "file_a1b2c3d4e5f6...",
      "fileName": "part001.stp",
      "filePath": "D:\\uploads\\temp\\file_a1b2c3d4e5f6.stp",
      "sizeBytes": 245760
    },
    "costs": {
      "materialCost": 218.4,
      "surfaceTreatmentCost": 12.5,
      "heatTreatmentCost": 8.2,
      "processingCost": 320.5,
      "totalCost": 559.6,
      "currency": "RMB"
    },
    "material": "SUS304棒材",
    "surface": "阳极氧化",
    "heatTreat": "无",
    "companyName": "HZDCIM",
    "note": null
  }
}
```

| 欄位 | 說明 |
|------|------|
| `stepData.quoteId` | 報價單編號，格式 `qt_YYYYMMDD_XXXX` |
| `stepData.status` | 固定 `"completed"` |
| `stepData.file` | CAD 檔案資訊 |
| `stepData.costs` | 各項費用明細 + 總計 |
| `stepData.material` | 選用材料名稱（中文顯示名）|
| `stepData.surface` | 表面處理名稱 |
| `stepData.heatTreat` | 熱處理名稱 |
| `stepData.companyName` | 公司名稱 |
| `stepData.note` | 備註（可為 null）|

---

#### 步驟失敗事件（step_error）

```json
{
  "type": "step_error",
  "step": "extract",
  "stepMessage": "特徵辨識失敗（HTTP 500）"
}
```

> 發生 `step_error` 後，緊接著會送出 `error` 事件，串流結束。

#### 整體錯誤事件（error）

```json
{
  "type": "error",
  "token": "特徵辨識服務無法連線，請確認 5051 服務已啟動"
}
```

> `error` 事件送出後串流即結束（不會有 `done` 事件）。

---

## 7. 錯誤處理

### 7.1 錯誤發生時機與格式對照

| 情境 | HTTP 狀態碼 | 回應格式 |
|------|:-----------:|---------|
| Header 未帶 token | 401 | 純 JSON |
| 必填欄位缺失 | 400 | 純 JSON（含 details 陣列） |
| CAD 檔為空 | 400 | 純 JSON |
| 不支援的副檔名 | 415 | 純 JSON |
| 儲存 CAD 失敗 | 200（SSE） | step_error + error 事件 |
| 5051 無法連線 | 200（SSE） | step_error + error 事件 |
| 5051 回傳失敗 | 200（SSE） | step_error + error 事件 |
| 8800 無法連線 | 200（SSE） | step_error + error 事件 |
| 材料 ID 無效 | 200（SSE） | step_error + error 事件 |
| 資料庫寫入失敗 | 200（SSE） | step_error + error 事件 |

> **注意**：SSE 串流回應的 HTTP 狀態碼固定為 200，錯誤狀態由事件 type 判斷。

### 7.2 建議前端處理邏輯

```
收到事件 type == "step_error"  → 顯示步驟失敗 UI，等待 error 事件
收到事件 type == "error"       → 顯示錯誤訊息（token 欄位），關閉串流
收到事件 type == "done"        → 顯示報價結果，關閉串流
連線中斷（網路 timeout 等）   → 顯示「連線中斷，請重試」
```

---

## 8. 前端實作範例（JavaScript）

### 8.1 完整串流請求實作

```javascript
async function createQuote(formData) {
  // formData 是 FormData 物件，包含所有 form fields + cad_file
  const token = localStorage.getItem('access_token');

  const response = await fetch('http://localhost:5050/api/quotes', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      // 注意：不要手動設 Content-Type，讓瀏覽器自動帶 boundary
    },
    body: formData,
  });

  // --- 驗證錯誤（非 SSE）---
  if (!response.ok) {
    const errBody = await response.json();
    if (errBody.error === 'ValidationError') {
      // 欄位驗證失敗，errBody.details 是 [{field, message}] 陣列
      handleValidationError(errBody.details);
    } else {
      handleError(errBody.error);
    }
    return;
  }

  // --- SSE 串流處理 ---
  const reader = response.body.getReader();
  const decoder = new TextDecoder('utf-8');
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // 按 \n\n 切分 SSE 事件
    const parts = buffer.split('\n\n');
    buffer = parts.pop(); // 最後一段可能不完整，留著等下次

    for (const part of parts) {
      if (!part.startsWith('data: ')) continue;
      const jsonStr = part.slice(6).trim(); // 去掉 "data: " 前綴
      if (!jsonStr) continue;

      let evt;
      try {
        evt = JSON.parse(jsonStr);
      } catch {
        continue; // 解析失敗跳過
      }

      handleSseEvent(evt);

      // 完成或錯誤後中止讀取
      if (evt.type === 'done' || evt.type === 'error') {
        reader.cancel();
        return;
      }
    }
  }
}

function handleSseEvent(evt) {
  switch (evt.type) {
    case 'step_start':
      updateProgress(evt.step, 'running', evt.stepMessage);
      break;

    case 'step_done':
      updateProgress(evt.step, 'done', evt.stepMessage);
      // 可展示中間資料（如特徵數值、預測金額）
      if (evt.step === 'calc') {
        previewCosts(evt.stepData);  // 顯示費用預覽
      }
      break;

    case 'step_error':
      updateProgress(evt.step, 'error', evt.stepMessage);
      break;

    case 'error':
      showErrorToast(evt.token);    // 顯示錯誤通知
      closeProgressDialog();
      break;

    case 'done':
      showQuoteResult(evt.stepData); // 顯示報價結果
      closeProgressDialog();
      break;
  }
}
```

---

### 8.2 建構 FormData 範例

```javascript
function buildFormData({ materialId, surfaceId, heatTreatId, roughnessId,
  positionToleranceId, sizeToleranceId, companyName, note, cadFile }) {
  const fd = new FormData();
  fd.append('material_id',            materialId);
  fd.append('surface_id',             surfaceId);
  fd.append('heat_treat_id',          heatTreatId);
  fd.append('roughness_id',           roughnessId);
  fd.append('position_tolerance_id',  positionToleranceId);
  fd.append('size_tolerance_id',      sizeToleranceId);
  fd.append('company_name',           companyName);
  if (note) fd.append('note', note);
  fd.append('cad_file', cadFile); // cadFile 是 File 物件（來自 <input type="file">）
  return fd;
}
```

---

### 8.3 取得下拉選單範例

```javascript
let cachedOptions = null;
let cachedVersion = null;

async function getQuoteOptions() {
  const token = localStorage.getItem('access_token');
  const headers = { 'Authorization': `Bearer ${token}` };

  // 檢查版本
  const verRes = await fetch('http://localhost:5050/api/quotes/options/version', { headers });
  const { data: { version } } = await verRes.json();

  if (cachedOptions && cachedVersion === version) {
    return cachedOptions; // 命中快取
  }

  // 重新拉取
  const optRes = await fetch('http://localhost:5050/api/quotes/options', { headers });
  const { data } = await optRes.json();
  cachedOptions = data;
  cachedVersion = version;
  return data;
}
```

---

### 8.4 進度列步驟對應 UI 建議

```
[ ✓ ] 步驟 1：上傳檔案     (step = "save_file")
[ ⟳ ] 步驟 2：特徵辨識     (step = "extract")
[ — ] 步驟 3：加工費預測   (step = "predict")
[ — ] 步驟 4：計算報價     (step = "calc")
[ — ] 步驟 5：儲存報價單   (step = "save")
```

- 收到 `step_start` → 該步驟轉為「執行中」⟳
- 收到 `step_done` → 該步驟轉為「完成」✓
- 收到 `step_error` → 該步驟轉為「失敗」✗
- 收到 `done` → 全部完成，顯示報價結果 Modal

---

## 9. 報價費用計算公式說明

後端計算邏輯如下，前端可用於顯示計算說明或驗算：

### 9.1 重量計算

$$\text{weight (kg)} = \frac{L_{\text{mm}} \times W_{\text{mm}} \times H_{\text{mm}}}{1{,}000{,}000{,}000} \times \text{density} \ (\text{kg/m}^3)$$

長寬高單位為 mm，密度來自 baseparameters 表中對應材料的 `density`。

### 9.2 各項費用計算

| 費用項目 | 計算公式 |
|---------|---------|
| 材料費 | `weight × material.unitPrice` |
| 表面處理費 | `weight × surface.unitPrice` |
| 熱處理費 | `weight × heatTreat.unitPrice` |
| 加工費 | 由 8800 AI 模型預測（`suggestedPrice`） |
| **總計** | `材料費 + 表面處理費 + 熱處理費 + 加工費` |

> `unitPrice` 單位為 元/kg，`weight` 單位為 kg，結果為人民幣元（RMB）。

---

## 10. 常見問題

### Q1. 為什麼 POST /api/quotes 的 HTTP 狀態碼是 200，但實際上失敗了？

SSE 回應必須在送出第一個 byte 前鎖定 HTTP 狀態碼。後端驗證成功後就切換成串流模式（HTTP 200），後續的執行錯誤（5051 連線失敗、DB 寫入失敗等）以 `type: "error"` 事件傳遞，不會改變 HTTP 狀態碼。

### Q2. 為什麼不能設定 Content-Type: multipart/form-data？

讓瀏覽器自動設定 Content-Type，它會同時帶上 `boundary`（例如 `multipart/form-data; boundary=----xyz`）。手動設定只會寫 `multipart/form-data`，導致後端無法解析。

### Q3. EventSource 可以使用嗎？

`EventSource` 僅支援 GET，不支援 POST 與自訂 Header（無法帶 Bearer Token）。請使用 `fetch` + `ReadableStream`，如 8.1 範例。

### Q4. CAD 檔上傳後有保留期限嗎？

是的。CAD 檔儲存在暫存目錄，預設保留 **24 小時**，到期後自動清除。報價資料已存入資料庫，與暫存檔無關。

### Q5. 如果在步驟中途使用者關閉頁面，會發生什麼？

後端偵測到連線中斷（`OperationCanceledException`）後會靜默停止，不會寫入不完整的報價單（已有 DB transaction 保護）。

### Q6. roughness_id 在報價計算中有用到嗎？

目前 `roughness_id` 儲存表單紀錄用，計算費用時未直接使用（表面粗糙度影響由 8800 ML 模型透過 `surface_area` 等特徵間接考量）。

---

*文件結束*
