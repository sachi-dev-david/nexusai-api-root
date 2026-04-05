# 待開發 API 文件

本文檔記錄後端需要開發的 API，供前端串接使用。

---

## 1. 模型狀態 API

### 1.1 取得所有模型狀態
```
GET /api/models
```

**Response:**
```json
{
  "success": true,
  "data": {
    "ollama": { "name": "llama3.2:latest", "status": "online" },
    "vision": { "name": "Vision", "status": "online" },
    "math": { "name": "Math", "status": "online" }
  }
}
```

**說明：**
- `status` 可為 `online` 或 `offline`
- 前端每 30 秒輪詢此 API 更新狀態

---

### 1.2 取得 Ollama LLM 模型狀態
```
GET /api/models/ollama
```

**Response:**
```json
{
  "success": true,
  "data": {
    "name": "llama3.2:latest",
    "status": "online",
    "message": "模型運行中"
  }
}
```

---

### 1.3 取得特徵辨識模型狀態
```
GET /api/models/vision
```

**Response:**
```json
{
  "success": true,
  "data": {
    "name": "Vision",
    "status": "online",
    "message": "特徵辨識模型運行中"
  }
}
```

---

### 1.4 取得數學模型狀態
```
GET /api/models/math
```

**Response:**
```json
{
  "success": true,
  "data": {
    "name": "Math",
    "status": "online",
    "message": "數學模型運行中"
  }
}
```

---

## 2. 設備狀態 API（已存在但需確認）

現有 API `/api/devices` 已存在，設備狀態區塊已從 UI 隱藏。

如未來需要重新啟用，請確保以下格式：
```
GET /api/devices
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": "M-001",
      "name": "CNC 機台 1",
      "status": "running",
      "temperature": 45
    }
  ]
}
```

---

## 3. API 響應格式規範

所有 API 應遵循以下格式：

**成功響應：**
```json
{
  "success": true,
  "data": { ... }
}
```

**錯誤響應：**
```json
{
  "success": false,
  "error": "錯誤訊息"
}
```

---

## 4. 認證

所有 API 需要 JWT Token 認證，透過 `Authorization: Bearer <token>` Header 傳遞。

---
