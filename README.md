# NexusAI Backend

工廠智能助手後端 — ASP.NET Core 9 + Semantic Kernel + llama3.2:latest + MariaDB

---

## 技術棧

| 元件 | 版本 / 說明 |
|------|------------|
| .NET | 9.0 |
| Semantic Kernel | 1.x（含 OpenAI-compatible connector） |
| LLM | llama3.2:latest（透過 Ollama，原生 Function Calling） |
| ORM | EF Core 9 + Pomelo.EntityFrameworkCore.MySql |
| 主資料庫 | MariaDB `nexusai`（使用者、對話、訊息、上傳檔案） |
| 報價資料庫 | MariaDB `mldatabase`（`machinelearning_quatation`） |
| 認證 | JWT Bearer Token + BCrypt 密碼雜湊（cost=12） |

---

## 環境需求

| 工具 | 版本 | 下載 |
|------|------|------|
| .NET SDK | 9.0+ | https://dotnet.microsoft.com/download |
| Ollama | 最新版 | https://ollama.com |
| MariaDB | 10.6+ | https://mariadb.org |

---

## 快速啟動

### 1. 啟動 Ollama 並下載模型

```bash
ollama serve

# llama3.2:latest 支援原生 Function Calling
ollama pull llama3.2:latest
```

### 2. 初始化資料庫

```bash
# nexusai 主資料庫（資料表 + 3 個測試帳號）
mysql -u root -p < init_db.sql

# mldatabase 報價資料庫（資料表 + 720 筆假資料）
mysql -u root -p < mldatabase_init.sql
```

### 3. 設定連線字串

編輯 `NexusAI.Api/appsettings.json`：

```json
"ConnectionStrings": {
  "MariaDB":    "Server=localhost;Port=3306;Database=nexusai;User=root;Password=YOUR_PW;CharSet=utf8mb4;",
  "MlDatabase": "Server=localhost;Port=3306;Database=mldatabase;User=root;Password=YOUR_PW;CharSet=utf8mb4;"
},
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "Model":   "llama3.2:latest"
}
```

### 4. 執行後端

```bash
cd NexusAI.Api
dotnet restore
dotnet run
```

- **Swagger UI**：http://localhost:5050/swagger
- **健康診斷**：http://localhost:5050/api/debug/db

---

## 測試帳號

| 帳號 | 密碼 | 角色 |
|------|------|------|
| admin | admin123 | Admin |
| engineer | eng123 | Engineer |
| operator | op123 | Operator |

---

## API 端點

### 認證
```
POST /api/auth/login     登入，回傳 JWT Token
POST /api/auth/logout    登出
```

### 對話管理
```
GET    /api/conversations                取得對話列表
GET    /api/conversations/{id}/messages  取得對話訊息
POST   /api/conversations                建立新對話
DELETE /api/conversations/{id}           刪除對話
```

### AI 串流（SSE）
```
POST /api/chat/stream
```

**Request body：**
```json
{
  "conversationId": "conv_xxx",
  "message": "查詢 HZDCIM 2026年1月的報價",
  "fileId": null
}
```

**SSE 事件流：**
```
data: {"type":"skill_start","skillName":"query_quote","skillArgs":{"customer_name":"HZDCIM","date_from":"2026-01-01","date_to":"2026-01-31"}}
data: {"type":"skill_done","skillName":"query_quote","skillResult":"Found 228 records..."}
data: {"type":"token","token":"以下是 HZDCIM 2026年1月的報價記錄："}
data: {"type":"token","token":" 共 228 筆..."}
data: {"type":"done"}
```

### 設備
```
GET /api/devices             取得所有設備列表
GET /api/devices/{id}/status 取得單一設備詳細狀態
```

### Skills
```
GET /api/skills    取得可用 Skill 列表
```

### 檔案上傳
```
POST /api/files/upload    multipart/form-data，欄位名稱：file
```

### 診斷（不需 JWT，開發用）
```
GET /api/debug/db                   確認兩個 DB 連線與資料筆數
GET /api/debug/quote                直接呼叫 QuotePlugin（繞過 LLM）
GET /api/debug/quote-summary        直接呼叫彙總統計 Plugin
GET /api/debug/intent?message=...   測試意圖偵測解析
GET /api/health/ollama              確認 Ollama 連線狀態
```

**debug 範例：**
```
GET /api/debug/db
GET /api/debug/quote?customer=HZDCIM&from=2026-01-01&to=2026-01-31&limit=5
GET /api/debug/intent?message=請查詢HZDCIM報價
```

---

## Function Calling 架構

llama3.2:latest 支援原生 Function Calling，整合 SK `AutoInvokeKernelFunctions`：

```
使用者輸入
    ↓
SK AutoInvokeKernelFunctions（llama3.2:latest 原生 tool call）
    ↓
llama3.2:latest 判斷意圖 → 選擇 Plugin + 解析參數
    ↓
SK 自動呼叫 Plugin → 查詢 MariaDB（mldatabase）
    ↓
SkillInvocationFilter 攔截 → 推送 skill_start / skill_done SSE 事件
    ↓
llama3.2:latest 整理查詢結果 → 繁體中文回覆串流輸出
```

> **容錯機制**：若 llama3.2:latest 呼叫完 tool 後沒有輸出整理文字，後端自動補一次不帶 tool 的 LLM 請求，強制整理結果後輸出。

> **Plugin Description 使用英文**：實測 llama3.2:latest 對英文 description 的 function calling 觸發率明顯高於中文。

---

## Plugins

| Plugin 類別 | Function | 資料來源 | 狀態 |
|-------------|----------|----------|------|
| `QuotePlugin` | `query_quote` | mldatabase（真實 DB） | ✅ 完成 |
| `QuotePlugin` | `query_quote_summary` | mldatabase（真實 DB） | ✅ 完成 |
| `MachineStatusPlugin` | `get_machine_status` | Mock | 🔧 待接 OPC-UA |
| `ProcessPlugin` | `query_process` | Mock | 🔧 待接 MES |
| `RagPlugin` | `query_rag` | Mock | 🔧 待接 Qdrant |

---

## 資料庫結構

### nexusai（主 DB）

| 資料表 | 欄位說明 |
|--------|---------|
| `users` | id, username, password_hash（BCrypt）, role, last_login_at |
| `conversations` | id, user_id, title, summary, created_at |
| `messages` | id, conversation_id, role, text, skill_calls（JSON）, created_at |
| `uploaded_files` | id, file_name, file_path, content_type, expires_at |

### mldatabase（報價 DB）

| 資料表 | 欄位說明 |
|--------|---------|
| `machinelearning_quatation` | id, quote_name, quote_file_name, customer_name, material_cost, processing_cost, surface_treatment_cost, heat_treatment_cost, material_name, surface_treatment, heat_treatment, total_cost, created_at, updated_at |

假資料：2026/01 ~ 02，客戶 HZDCIM + SachiTech，720 筆。

---

## 專案結構

```
NexusAI.Api/
├── Controllers/
│   ├── AuthController.cs
│   ├── ChatController.cs              SSE 串流端點
│   ├── ConversationsController.cs
│   ├── DevicesController.cs
│   ├── FilesController.cs
│   ├── SkillsController.cs
│   └── DiagnosticController.cs        開發診斷（不需 JWT）
├── Data/
│   ├── AppDbContext.cs                nexusai 主 DB
│   ├── MlDbContext.cs                 mldatabase 報價 DB（IDbContextFactory）
│   └── Entities/
│       ├── Entities.cs                User / Conversation / Message / UploadedFile
│       └── QuotationEntity.cs         machinelearning_quatation
├── Models/
│   ├── ApiResponse.cs
│   └── Models.cs                      所有 DTO + StreamEvent + StreamEventType
├── Plugins/
│   ├── QuotePlugin.cs                 ★ 報價查詢（連接 mldatabase）
│   ├── MachineStatusPlugin.cs         設備狀態（Mock）
│   ├── ProcessPlugin.cs               製程查詢（Mock）
│   └── RagPlugin.cs                   知識庫 RAG（Mock，待接 Qdrant）
├── Services/
│   ├── IServices.cs
│   └── Impl/
│       ├── AuthService.cs             JWT 簽發 + BCrypt 驗證
│       ├── ChatService.cs             ★ SK AutoInvoke 串流核心 + 3b 容錯
│       ├── ConversationService.cs
│       ├── DeviceService.cs
│       └── FileService.cs
├── Program.cs                         DI 注冊（DB 優先於 Kernel Singleton）
├── appsettings.json
└── appsettings.Development.json

init_db.sql            nexusai 建表 + seed 資料
mldatabase_init.sql    mldatabase 建表 + 720 筆報價假資料
seed.sql               僅 users seed（補充用）
```

---

## curl 快速測試

```bash
# 1. 登入取得 Token
TOKEN=$(curl -s -X POST http://localhost:5050/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['token'])")

# 2. 建立對話
CONV_ID=$(curl -s -X POST http://localhost:5050/api/conversations \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title":"報價查詢測試"}' | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['id'])")

# 3. 串流查詢（觀察 skill_start / skill_done / token 事件）
curl -X POST http://localhost:5050/api/chat/stream \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"conversationId\":\"$CONV_ID\",\"message\":\"查詢 HZDCIM 2026年1月的報價\"}" \
  --no-buffer
```

---

## 待辦（Phase 02）

- [ ] MachineStatusPlugin → OPC-UA / MQTT 真實資料
- [ ] RagPlugin → Qdrant 向量資料庫 + nomic-embed-text 嵌入
- [ ] ProcessPlugin → ERP / MES API
- [ ] Redis Token 黑名單（logout 立即失效）
- [ ] Rate Limiting（每分鐘請求上限）
- [ ] Serilog + Seq 結構化日誌
- [ ] Docker Compose 一鍵部署
- [ ] 報價 CRUD（新增 / 修改 / 刪除）
