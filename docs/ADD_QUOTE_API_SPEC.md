# 新增報價 API 規格

更新日期: 2026-04-05
目的: 支援前端「新增報價」流程改版

需求對應:
1. 參數選單改為由後端提供
2. 使用者按送出後，才一次送出報價資料與 STEP 檔
3. 前端上傳按鈕文案顯示為「上傳」(純前端 UI，無 API 需求)

---

## 一、資料字典

### 1.1 報價參數欄位
- material_id: string (必填)
- surface_id: string (必填)
- heat_treat_id: string (必填)
- roughness_id: string (必填)
- position_tolerance_id: string (必填)
- size_tolerance_id: string (必填)
- company_name: string (必填)
- note: string (選填)

### 1.2 檔案欄位
- cad_file: file (必填)
- 可接受副檔名: .step, .stp, .prt

---

## 二、選單資料 API

前端開啟「新增報價」視窗時呼叫一次，取得所有下拉選單項目。

### 2.1 取得新增報價表單選項
- Method: GET
- Path: /api/quotes/options
- Auth: Bearer token 必填

#### Response 200
```json
{
  "success": true,
  "data": {
    "materials": [
      { "id": "mat_316", "label": "316不鏽鋼", "enabled": true }
    ],
    "surfaces": [
      { "id": "sf_matte", "label": "磨砂", "enabled": true }
    ],
    "heatTreats": [
      { "id": "ht_none", "label": "無", "enabled": true }
    ],
    "roughnesses": [
      { "id": "ra_1_6", "label": "Ra1.6", "enabled": true }
    ],
    "positionTolerances": [
      { "id": "pt_0_05", "label": "±0.05", "enabled": true }
    ],
    "sizeTolerances": [
      { "id": "st_0_1", "label": "±0.1mm", "enabled": true }
    ]
  }
}
```

#### Response 401
```json
{
  "success": false,
  "error": "Unauthorized"
}
```

#### Response 500
```json
{
  "success": false,
  "error": "Internal Server Error"
}
```

---

## 三、新增報價 (送出時一併上傳檔案)

前端按「確認送出」時才呼叫，不再先上傳檔案。

### 3.1 建立報價並上傳 CAD 檔
- Method: POST
- Path: /api/quotes
- Content-Type: multipart/form-data
- Auth: Bearer token 必填

### 3.1.1 Form Data
- material_id: string
- surface_id: string
- heat_treat_id: string
- roughness_id: string
- position_tolerance_id: string
- size_tolerance_id: string
- company_name: string
- note: string (optional)
- cad_file: binary file (.step/.stp/.prt)

#### Response 201
```json
{
  "success": true,
  "data": {
    "quote_id": "qt_20260405_0001",
    "status": "queued",
    "file": {
      "file_id": "file_xxxxx",
      "file_name": "part001.stp",
      "file_path": "D:\\...\\uploads\\temp\\file_xxxxx.stp",
      "size_bytes": 12345,
      "expires_at": "2026-04-06T10:30:00Z"
    },
    "plugin": {
      "name": "add_quote",
      "called": true,
      "params": {
        "file_path": "D:\\...\\uploads\\temp\\file_xxxxx.stp"
      }
    }
  }
}
```

說明:
- 後端收到 cad_file 後，需先落地到暫存路徑，取得 file_path。
- 後端呼叫 add_quote plugin 時，必須帶入 file_path 參數。

#### Response 400 (欄位錯誤)
```json
{
  "success": false,
  "error": "ValidationError",
  "details": [
    { "field": "material_id", "message": "required" }
  ]
}
```

#### Response 415 (不支援檔案格式)
```json
{
  "success": false,
  "error": "Unsupported file type. Allowed: .step, .stp, .prt"
}
```

#### Response 422 (plugin 執行失敗)
```json
{
  "success": false,
  "error": "add_quote plugin failed",
  "details": {
    "code": "PLUGIN_EXEC_ERROR",
    "message": "..."
  }
}
```

---

## 四、可選: 查詢選單版本 (前端快取用)

### 4.1 取得 options 版本
- Method: GET
- Path: /api/quotes/options/version

#### Response 200
```json
{
  "success": true,
  "data": {
    "version": "2026-04-05.1"
  }
}
```

用途:
- 前端可比對版本，決定是否重新拉取 /api/quotes/options。

---

## 五、通用回應格式

### 成功
```json
{
  "success": true,
  "data": {}
}
```

### 失敗
```json
{
  "success": false,
  "error": "錯誤訊息"
}
```

---

## 六、前後端協作注意事項

- 前端不再於選檔當下呼叫 /api/files/upload。
- 前端僅在使用者按送出後，呼叫一次 POST /api/quotes (含表單 + 檔案)。
- 後端若仍需保留 /api/files/upload，建議標記為舊流程，不給新 UI 使用。
- add_quote plugin 參數命名請統一為 file_path。
