# Run DevOrchestratorMcp on macOS and Windows

Tài liệu này hướng dẫn chạy **DevOrchestratorMcp local bằng Docker Desktop** trên macOS và Windows.

Đây là cách chạy khuyến nghị cho development/POC vì `compose.yaml` đã bao gồm:

```text
Docker Compose
  |
  +-- PostgreSQL 17
  +-- db-migrate (one-shot EF Core migrations)
  `-- DevOrchestrator MCP Server
        |
        +-- /control
        +-- /mcp
        +-- /healthz
        +-- /readyz
        +-- /webhooks/github
        +-- /ops/status
        `-- /metrics
```

> `db-migrate` chạy xong rồi exit code `0` là trạng thái **đúng**, không phải lỗi.

---

## 1. Prerequisites

Cài các tool sau trước khi chạy.

### macOS

- Git
- Docker Desktop
- Terminal hoặc iTerm2
- Optional: .NET 8 SDK nếu muốn chạy app không qua Docker

Kiểm tra:

```bash
git --version
docker --version
docker compose version
```

Docker Desktop phải đang ở trạng thái **Running**.

Kiểm tra Docker Engine:

```bash
docker info
```

### Windows

Khuyến nghị:

- Windows 10/11 64-bit
- WSL2 enabled
- Git for Windows
- Docker Desktop dùng WSL2 backend
- PowerShell 7 hoặc Windows PowerShell
- Optional: .NET 8 SDK

Kiểm tra trong PowerShell:

```powershell
git --version
docker --version
docker compose version
docker info
```

Nếu `docker info` lỗi, mở Docker Desktop và chờ Docker Engine start xong.

---

## 2. Clone repository

Repository:

```text
https://github.com/mic9971/DevOrchestratorMcp
```

### macOS

```bash
mkdir -p ~/Projects
cd ~/Projects

git clone https://github.com/mic9971/DevOrchestratorMcp.git
cd DevOrchestratorMcp

git checkout main
git pull
```

### Windows PowerShell

Ví dụ lưu source tại `C:\Projects`:

```powershell
New-Item -ItemType Directory -Force C:\Projects | Out-Null
Set-Location C:\Projects

git clone https://github.com/mic9971/DevOrchestratorMcp.git
Set-Location DevOrchestratorMcp

git checkout main
git pull
```

Kiểm tra branch:

```bash
git status
git log -1 --oneline
```

---

## 3. Create local `.env`

Docker Compose tự đọc file `.env` ở root repository.

File `.env` đã được `.gitignore`, vì vậy **không commit secret này lên GitHub**.

Cần các giá trị:

```text
POSTGRES_PASSWORD
DEVORCHESTRATOR_ARCHITECT_KEY
DEVORCHESTRATOR_IMPLEMENTER_KEY
DEVORCHESTRATOR_AUDITOR_KEY
GITHUB_WEBHOOK_SECRET
```

Mỗi Architect / Implementer / Auditor key phải là secret riêng và đủ mạnh.

### 3.1 Generate secrets on macOS

```bash
openssl rand -hex 32
openssl rand -hex 32
openssl rand -hex 32
openssl rand -hex 32
openssl rand -hex 32
```

Mỗi command trả về một secret khác nhau.

Ví dụ tạo file:

```bash
nano .env
```

Nội dung:

```dotenv
POSTGRES_PASSWORD=REPLACE_WITH_POSTGRES_PASSWORD

DEVORCHESTRATOR_ARCHITECT_KEY=REPLACE_WITH_ARCHITECT_SECRET
DEVORCHESTRATOR_IMPLEMENTER_KEY=REPLACE_WITH_IMPLEMENTER_SECRET
DEVORCHESTRATOR_AUDITOR_KEY=REPLACE_WITH_AUDITOR_SECRET

# Optional GitHub API fallback. Có thể để trống khi chỉ chạy local UI/MCP.
GITHUB_TOKEN=

GITHUB_WEBHOOK_SECRET=REPLACE_WITH_WEBHOOK_SECRET
GITHUB_WEBHOOK_MAX_ATTEMPTS=8

DEVORCHESTRATOR_ALLOWED_HOSTS=localhost;127.0.0.1
```

Save file.

### 3.2 Generate secrets on Windows PowerShell

Có thể dùng helper sau để tạo cryptographically-secure secret:

```powershell
function New-DevOrchestratorSecret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    return ([BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
}

New-DevOrchestratorSecret
```

Chạy `New-DevOrchestratorSecret` nhiều lần để lấy các secret riêng.

Tạo `.env`:

```powershell
notepad .env
```

Nội dung:

```dotenv
POSTGRES_PASSWORD=REPLACE_WITH_POSTGRES_PASSWORD

DEVORCHESTRATOR_ARCHITECT_KEY=REPLACE_WITH_ARCHITECT_SECRET
DEVORCHESTRATOR_IMPLEMENTER_KEY=REPLACE_WITH_IMPLEMENTER_SECRET
DEVORCHESTRATOR_AUDITOR_KEY=REPLACE_WITH_AUDITOR_SECRET

GITHUB_TOKEN=

GITHUB_WEBHOOK_SECRET=REPLACE_WITH_WEBHOOK_SECRET
GITHUB_WEBHOOK_MAX_ATTEMPTS=8

DEVORCHESTRATOR_ALLOWED_HOSTS=localhost;127.0.0.1
```

Save rồi đóng Notepad.

### Important

Không dùng cùng một secret cho:

```text
Architect
Implementer
Auditor
Webhook
```

Không commit `.env`.

Kiểm tra:

```bash
git status
```

`.env` không được xuất hiện trong danh sách file chuẩn bị commit.

---

## 4. Start the complete local stack

Command giống nhau trên macOS Terminal và Windows PowerShell:

```bash
docker compose up --build -d
```

Docker sẽ:

```text
1. Pull postgres:17-alpine
2. Build DevOrchestrator image
3. Start PostgreSQL
4. Wait PostgreSQL healthy
5. Run db-migrate
6. Apply EF Core migrations
7. Start DevOrchestrator MCP Server
```

Xem trạng thái:

```bash
docker compose ps
```

Kỳ vọng gần giống:

```text
NAME                              STATUS
...-postgres-1                    Up (healthy)
...-db-migrate-1                  Exited (0)
...-dev-orchestrator-1            Up (healthy)
```

`db-migrate` = `Exited (0)` là bình thường.

---

## 5. Check logs

Tất cả service:

```bash
docker compose logs
```

App:

```bash
docker compose logs dev-orchestrator
```

Follow realtime:

```bash
docker compose logs -f dev-orchestrator
```

Migration:

```bash
docker compose logs db-migrate
```

PostgreSQL:

```bash
docker compose logs postgres
```

---

## 6. Verify health and readiness

### macOS

```bash
curl http://localhost:5058/healthz
curl http://localhost:5058/readyz
```

### Windows PowerShell

PowerShell:

```powershell
Invoke-RestMethod http://localhost:5058/healthz
Invoke-RestMethod http://localhost:5058/readyz
```

Hoặc nếu máy có `curl.exe`:

```powershell
curl.exe http://localhost:5058/healthz
curl.exe http://localhost:5058/readyz
```

Expected:

```json
{
  "status": "ok",
  "service": "DevOrchestratorMcp"
}
```

và readiness phải trả trạng thái ready.

Nếu `/healthz` OK nhưng `/readyz` fail, kiểm tra migration:

```bash
docker compose logs db-migrate
```

---

## 7. Open the Control Plane

Browser:

```text
http://localhost:5058/control
```

Governance UI:

```text
http://localhost:5058/control/governance.html
```

Auth status:

```text
http://localhost:5058/auth/status
```

Local stack mặc định chưa cần GitHub OAuth để dùng machine Auditor break-glass access.

Dùng giá trị:

```text
DEVORCHESTRATOR_AUDITOR_KEY
```

trong `.env` khi Control Plane yêu cầu Auditor key.

---

## 8. Verify protected operational endpoints

### macOS

Đọc Auditor key từ `.env` thủ công hoặc export trước:

```bash
export DEVORCHESTRATOR_AUDITOR_KEY="YOUR_AUDITOR_KEY"
```

Sau đó:

```bash
curl \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  http://localhost:5058/ops/status
```

Metrics:

```bash
curl \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  http://localhost:5058/metrics
```

### Windows PowerShell

```powershell
$AuditorKey = "YOUR_AUDITOR_KEY"

Invoke-RestMethod `
  -Headers @{ "X-DevOrchestrator-Key" = $AuditorKey } `
  http://localhost:5058/ops/status
```

Metrics:

```powershell
Invoke-WebRequest `
  -Headers @{ "X-DevOrchestrator-Key" = $AuditorKey } `
  http://localhost:5058/metrics
```

Không có key:

```text
GET /metrics -> 401 Unauthorized
```

là behavior đúng.

---

## 9. MCP endpoint

Local MCP endpoint:

```text
http://localhost:5058/mcp
```

Machine roles:

```text
Architect     -> DEVORCHESTRATOR_ARCHITECT_KEY
Implementer   -> DEVORCHESTRATOR_IMPLEMENTER_KEY
Auditor       -> DEVORCHESTRATOR_AUDITOR_KEY
```

Human browser session và machine MCP credential là hai authentication boundary riêng.

```text
Human
  -> /control
  -> GitHub OAuth / browser session

Codex / automation
  -> /mcp
  -> machine credential
```

---

## 10. Connect to PostgreSQL

Không cần cài PostgreSQL trên host.

Dùng `psql` bên trong container:

```bash
docker compose exec postgres psql \
  -U devorchestrator \
  -d devorchestrator
```

Command này chạy được trên macOS và PowerShell nếu shell hỗ trợ multiline khác nhau; command một dòng an toàn nhất:

```bash
docker compose exec postgres psql -U devorchestrator -d devorchestrator
```

Trong `psql`:

```sql
\dt
```

Kiểm tra migrations:

```sql
SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
```

Phase 9 hiện có migration sequence:

```text
202609020001_InitialProductionSchema
202609020002_TaskWorkerLeases
202609020003_DurableWebhookInbox
202609020004_IdentityGovernance
202609030001_WebhookDeadLetter
```

Thoát:

```text
\q
```

---

## 11. Stop the stack

Giữ database volume:

```bash
docker compose down
```

Start lại:

```bash
docker compose up -d
```

Restart riêng app:

```bash
docker compose restart dev-orchestrator
```

---

## 12. Rebuild after pulling new code

```bash
git checkout main
git pull

docker compose down
docker compose up --build -d
```

Migration mới sẽ được `db-migrate` chạy trước khi app start.

Kiểm tra:

```bash
docker compose ps
curl http://localhost:5058/readyz
```

Windows có thể dùng:

```powershell
Invoke-RestMethod http://localhost:5058/readyz
```

---

## 13. Full reset local database

> WARNING: bước này xóa toàn bộ local PostgreSQL data của DevOrchestrator.

Stop + remove volume:

```bash
docker compose down -v
```

Start clean:

```bash
docker compose up --build -d
```

EF migrations sẽ dựng database lại từ đầu.

Không dùng `down -v` nếu cần giữ task/project/audit local hiện tại.

---

## 14. Useful endpoints

| Purpose | URL |
|---|---|
| Control Plane | `http://localhost:5058/control` |
| Governance | `http://localhost:5058/control/governance.html` |
| Auth status | `http://localhost:5058/auth/status` |
| MCP | `http://localhost:5058/mcp` |
| Liveness | `http://localhost:5058/healthz` |
| Readiness | `http://localhost:5058/readyz` |
| GitHub webhook | `http://localhost:5058/webhooks/github` |
| Operations | `http://localhost:5058/ops/status` |
| Metrics | `http://localhost:5058/metrics` |

---

## 15. Optional: expose local Mac/Windows instance to GitHub

GitHub không thể gọi webhook vào:

```text
http://localhost:5058
```

Muốn test **GitHub webhook thật** khi app vẫn chạy trên laptop, cần HTTPS tunnel như Cloudflare Tunnel hoặc ngrok.

Ví dụ với `cloudflared` đã được cài:

```bash
cloudflared tunnel --url http://localhost:5058
```

Bạn sẽ nhận URL public dạng:

```text
https://<random>.trycloudflare.com
```

GitHub webhook URL:

```text
https://<random>.trycloudflare.com/webhooks/github
```

Webhook secret phải đúng giá trị:

```text
GITHUB_WEBHOOK_SECRET
```

Luồng:

```text
GitHub
   |
   | HTTPS webhook
   v
Cloudflare Tunnel
   |
   v
localhost:5058
   |
   v
DevOrchestrator
```

Tunnel URL kiểu quick tunnel có thể thay đổi sau khi restart. Production nên dùng domain/tunnel cố định hoặc VPS.

---

## 16. Optional: GitHub OAuth login local/public

GitHub OAuth human login yêu cầu callback phù hợp với endpoint mà browser truy cập.

Callback application:

```text
/signin-github
```

Ví dụ public tunnel:

```text
https://<public-host>/signin-github
```

Production configuration dùng các settings:

```text
Identity__GitHub__ClientId
Identity__GitHub__ClientSecret
Identity__BootstrapGitHubLogins__0
```

Root `compose.yaml` tập trung vào local MCP/Docker development. Production configuration nằm trong:

```text
deploy/compose.production.yaml
deploy/.env.production.example
docs/PRODUCTION_SETUP.md
```

---

## 17. Optional: run without Docker

Có thể chạy MCP server trực tiếp bằng .NET 8 SDK. Local default persistence khi không chọn PostgreSQL là SQLite.

Kiểm tra .NET:

```bash
dotnet --version
```

Restore/build/test:

```bash
dotnet restore DevOrchestratorMcp.sln
dotnet build DevOrchestratorMcp.sln
dotnet test DevOrchestratorMcp.sln
```

Run migration:

```bash
dotnet run --project src/DevOrchestrator.McpServer -- migrate
```

### macOS

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 \
  dotnet run --project src/DevOrchestrator.McpServer
```

### Windows PowerShell

```powershell
$env:ASPNETCORE_URLS = "http://127.0.0.1:5058"
dotnet run --project src/DevOrchestrator.McpServer
```

Docker Compose vẫn là lựa chọn khuyến nghị vì nó chạy cùng PostgreSQL path gần production hơn.

---

## 18. Troubleshooting

### `port is already allocated` / port 5058 already in use

macOS:

```bash
lsof -i :5058
```

Windows PowerShell:

```powershell
Get-NetTCPConnection -LocalPort 5058 -ErrorAction SilentlyContinue
```

Stop process đang dùng port hoặc thay mapping port trong `compose.yaml` cho local development.

### PostgreSQL unhealthy

```bash
docker compose logs postgres
```

Nếu chỉ là local disposable data:

```bash
docker compose down -v
docker compose up --build -d
```

### Migration fails

```bash
docker compose logs db-migrate
```

Không start app thủ công để bypass migration failure. Sửa migration/config trước.

### `/readyz` not ready

```bash
docker compose ps
docker compose logs db-migrate
docker compose logs dev-orchestrator
```

Readiness cố ý fail khi database có pending migrations.

### `401 Unauthorized`

Kiểm tra đúng role key:

```text
Architect endpoint   -> Architect key
Implementer MCP      -> Implementer key
Auditor / ops        -> Auditor key
```

Keys phải khác nhau.

### `503 identity.github_not_configured`

Đây là expected nếu chưa cấu hình GitHub OAuth App.

MCP và Auditor break-glass vẫn có thể chạy local.

### Windows line ending / shell issue

Ưu tiên chạy Docker Compose commands trong PowerShell.

Nếu dùng WSL2, clone repo vào filesystem Linux như:

```text
~/projects/DevOrchestratorMcp
```

thường có I/O tốt hơn việc thao tác source qua `/mnt/c/...` với workload nhiều file.

### Docker Desktop memory

Nếu build bị kill/out-of-memory, tăng Docker Desktop memory lên khoảng 4 GB hoặc hơn rồi build lại.

---

## 19. Quick start checklist

### macOS

```text
[ ] Docker Desktop Running
[ ] git clone / git pull main
[ ] create .env
[ ] generate 5 independent secrets
[ ] docker compose up --build -d
[ ] docker compose ps
[ ] curl /healthz
[ ] curl /readyz
[ ] open /control
```

### Windows

```text
[ ] Docker Desktop + WSL2 Running
[ ] git clone / git pull main
[ ] create .env
[ ] generate independent secrets
[ ] docker compose up --build -d
[ ] docker compose ps
[ ] Invoke-RestMethod /healthz
[ ] Invoke-RestMethod /readyz
[ ] open /control
```

---

## 20. Minimum commands to remember

Start:

```bash
docker compose up --build -d
```

Status:

```bash
docker compose ps
```

App logs:

```bash
docker compose logs -f dev-orchestrator
```

Stop:

```bash
docker compose down
```

Full destructive reset:

```bash
docker compose down -v
```

Control Plane:

```text
http://localhost:5058/control
```

MCP:

```text
http://localhost:5058/mcp
```

---

## Next step after local startup

Khi local stack đã chạy xanh:

```text
healthz = OK
readyz  = READY
/control opens
```

thì bước tiếp theo để chứng minh flow thực tế là:

```text
Local Docker
   |
HTTPS tunnel
   |
GitHub webhook
   |
Plan Issue
   |
DevOrchestrator task READY
   |
Codex MCP worker
   |
PR
   |
Auditor review
   |
DONE
```

Xem thêm production deployment tại:

```text
docs/PRODUCTION_SETUP.md
```
