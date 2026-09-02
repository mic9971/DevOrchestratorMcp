# DevOrchestratorMcp

![CI](https://github.com/mic9971/DevOrchestratorMcp/actions/workflows/ci.yml/badge.svg)

A reusable .NET MCP control plane for an AI software-development loop:

**ChatGPT Architect → GitHub Plan Issue → MCP task graph → Codex Implementer → Git evidence → ChatGPT Auditor → GitHub review contract → done / changes requested**

The MCP server is deliberately **not** an AI agent. It stores project/task state, coordinates workers, enforces task transitions, records implementation evidence, and keeps review history. Git remains the source of truth for code.

## Stack

- .NET 8 / ASP.NET Core
- Official `ModelContextProtocol.AspNetCore` C# SDK
- Streamable HTTP MCP endpoint
- Built-in dependency-free web control plane for operators
- GitHub OAuth 2.0 human sign-in with secure server-side cookie session
- Persisted Admin / Architect / Auditor / Implementer human roles
- Revocable/expiring database-managed machine credentials with hash-at-rest
- SQLite for zero-infrastructure local development
- PostgreSQL for shared/production deployment
- Versioned EF Core migrations with an explicit migration process
- Signed GitHub webhook ingestion with durable database inbox and retry worker
- Server-side machine Architect / Implementer / Auditor authorization
- Multi-worker task lease, heartbeat, expiry, reclaim and manual recovery
- Identity-aware security audit for privileged operations
- OpenTelemetry tracing baseline + authenticated Prometheus operational gauges
- Immutable GHCR release image and vendor-neutral production Compose
- PostgreSQL logical backup/restore recovery drill
- Domain, application, database, security, HTTP, runtime and architecture tests

## Orchestration flow

```text
ChatGPT Architect
      |
      v
GitHub Plan Issue (`devorchestrator.plan.v1`)
      |
      v
signed GitHub webhook -> durable inbox -> plan import
      |
      v
READY task
      |
      | task_claim_next(workerId)
      v
Codex worker -> IN_PROGRESS lease
      |
      +--> task_heartbeat
      +--> Git evidence
      `--> task_submit_review
                    |
                    v
             ChatGPT Auditor
                    |
                    v
GitHub review contract (`devorchestrator.review.v1`)
                    |
                    v
        durable inbox -> review sync
             /                 \
            v                   v
          DONE         CHANGES_REQUESTED
```

If a worker disappears, its task can be reclaimed after lease expiry. An Auditor can also expire a stuck lease immediately without deleting ownership/history. A passing review promotes dependent `Draft` tasks to `Ready` atomically when all dependencies are complete.

## Identity boundary

Human browser identity and machine MCP identity are intentionally separate:

```text
Human operator
  -> GitHub OAuth
  -> HttpOnly secure cookie
  -> /control + role-based operations

Codex / automation
  -> machine credential
  -> /mcp
```

A human session never authorizes `/mcp`. Database-managed machine credentials can be individually expired, revoked and rotated; plaintext secrets are returned once and only their SHA-256 hashes are persisted. Static configured role keys remain available as bootstrap/break-glass credentials.

## MCP tools

Architect:
- `project_register`
- `project_get`
- `project_list`
- `task_create`
- `task_create_batch`
- `task_get`
- `task_list`
- `task_list_page`

Implementer / Codex:
- `project_get`
- `bridge_import_plan_issue`
- `bridge_sync_reviews`
- `task_get`
- `task_list_page`
- `task_get_next` (preview/compatibility)
- `task_claim_next`
- `task_heartbeat`
- `task_start` (compatibility)
- `task_add_evidence`
- `task_submit_review`
- `task_block`

Auditor / privileged:
- `project_get`
- `task_get`
- `task_list`
- `task_list_page`
- `review_submit`
- `task_reopen`
- `task_resume`

Client-side tool allow-lists are defense in depth; server-side role enforcement is authoritative.

## Run locally

Prerequisites: .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet test

dotnet run --project src/DevOrchestrator.McpServer -- migrate
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project src/DevOrchestrator.McpServer
```

Endpoints:

```text
Control:      http://127.0.0.1:5058/control
Governance:   http://127.0.0.1:5058/control/governance.html
Auth status:  http://127.0.0.1:5058/auth/status
MCP:          http://127.0.0.1:5058/mcp
Liveness:     http://127.0.0.1:5058/healthz
Readiness:    http://127.0.0.1:5058/readyz
Webhook:      http://127.0.0.1:5058/webhooks/github
Ops:          http://127.0.0.1:5058/ops/status
Metrics:      http://127.0.0.1:5058/metrics
```

`/readyz` returns not-ready when the database has pending migrations. Normal MCP startup never performs DDL.

When authentication is enabled, `/control/api/*` accepts an authenticated human with an assigned role for read access. Human `Admin` or `Auditor` can invoke privileged `/ops/*` and `/metrics`. Machine access to those endpoints still requires the Auditor machine role. Admin governance APIs require a real human `Admin`; an Auditor machine key cannot administer users or credentials.

The `/control` shell itself is static so a browser can render the login screen. Human sign-in uses a secure HttpOnly cookie. The legacy Auditor key remains available from the UI as break-glass access and is stored only in the browser tab's `sessionStorage` when used.

## Codex configuration

Copy `.codex/config.toml.example` into the target repository and export an Implementer machine credential:

```bash
export DEVORCHESTRATOR_IMPLEMENTER_KEY="<secret>"
```

New multi-worker integrations should use `task_claim_next` with a stable worker ID and send `task_heartbeat` periodically. See `docs/CODEX_SETUP.md`.

## Production configuration

Database and break-glass MCP security:

```text
Database__Provider=postgres
ConnectionStrings__Orchestrator=Host=postgres;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...
Security__RequireAuthentication=true
Security__ArchitectKey=...
Security__ImplementerKey=...
Security__AuditorKey=...
GitHub__WebhookSecret=...
```

Static credential rotation overlap remains supported:

```text
Security__ArchitectPreviousKey=...
Security__ImplementerPreviousKey=...
Security__AuditorPreviousKey=...
```

For day-to-day machine access, prefer Governance-created database credentials because they support per-credential expiry, last-used tracking, rotation and immediate revocation without a service restart.

### Human GitHub login

Create a GitHub OAuth App with callback:

```text
https://<your-host>/signin-github
```

Configure:

```text
Identity__GitHub__ClientId=...
Identity__GitHub__ClientSecret=...
Identity__BootstrapGitHubLogins__0=mic9971
```

The explicit bootstrap login receives `Admin` on successful sign-in if needed. Other GitHub users are persisted but receive no role automatically until an Admin grants one. Human login requires HTTPS in production because the session cookie is always Secure.

Preferred GitHub repository-automation authentication remains a GitHub App:

```text
GitHub__AppId=...
GitHub__InstallationId=...
GitHub__PrivateKeyPem=...
```

`GitHub__Token` / `GITHUB_TOKEN` remains a compatibility fallback.

Never commit credentials. Serve remote MCP/webhook/control-plane endpoints behind HTTPS.

## Persistence and deployment

SQLite remains the local default at:

```text
src/DevOrchestrator.McpServer/data/dev-orchestrator.db
```

PostgreSQL is selected with `Database__Provider=postgres`. `compose.yaml` runs a one-shot `db-migrate` service before MCP startup. CI boots PostgreSQL and executes the real migration path.

Current migration sequence:

```text
202609020001_InitialProductionSchema
202609020002_TaskWorkerLeases
202609020003_DurableWebhookInbox
202609020004_IdentityGovernance
```

For production, `.github/workflows/release-image.yml` publishes immutable `ghcr.io/<owner>/devorchestratormcp:sha-<commit>` images. `deploy/compose.production.yaml` consumes that immutable image and an external/managed PostgreSQL connection string; it does not bundle the production database.

GitHub webhook requests are HMAC-verified and durably persisted in `github_webhook_inbox`; a hosted worker leases and retries inbox records. `X-GitHub-Delivery` remains the external idempotency key.

Built-in endpoint limits protect the control plane:
- `/mcp`: 120 requests/minute
- `/webhooks/github`: 300 requests/minute

## Web control plane

The built-in `/control` UI has operator views:

```text
Overview -> Projects -> Tasks -> Workers -> Webhooks -> Audit -> Governance
```

Task inspection includes acceptance criteria, dependencies, evidence, reviews, recent events, Git branch/commit/PR metadata and lease ownership. List APIs are no-tracking, directly projected and paginated; task detail history is bounded.

`/control/governance.html` is a human Admin surface for:

```text
Users / roles / enable-disable
Machine credential create / rotate / revoke
Identity and privileged-operation security audit
```

The UI deliberately reuses existing privileged `/ops/*` mutations instead of introducing a parallel task/business write model.

## Production operations

Auditor-authorized operational endpoints:

```text
GET  /ops/status
GET  /metrics
POST /ops/tasks/{projectKey}/{taskCode}/expire-lease
POST /ops/projects/{projectKey}/pause
POST /ops/projects/{projectKey}/resume
POST /ops/webhooks/{deliveryId}/replay
```

Privileged mutations record identity-aware security events such as `github:<login>`, `credential:<id>` or `config:auditor`, including before/after context where appropriate.

The metrics surface exports active workers, active/expired task leases and pending/retrying webhook inbox gauges. PostgreSQL dump/restore helpers live in `scripts/backup-postgres.sh` and `scripts/restore-postgres.sh`.

## Verification

Normal PR CI proves more than compilation:

```text
.NET restore/build/test
PostgreSQL 17 migrations
SQLite migration compatibility
machine credential hash/revoke security tests
Docker image build and real startup
health/readiness
control-plane + governance static assets
human-login-disabled behavior when OAuth is not configured
Auditor machine auth + Admin separation
service restart recovery
PostgreSQL pg_dump -> fresh database pg_restore
migration-history verification
```

An explicit `real-github-e2e` workflow creates real GitHub Issue/comment contracts and proves the plan/review lifecycle. After a public HTTPS deployment exists, `live-production-proof` verifies the live endpoint and signed webhook path without changing application code. A real GitHub human login additionally requires a deployed HTTPS host and OAuth App secrets, so that external proof is intentionally not coupled to hermetic PR CI.

## Design docs

- `docs/ARCHITECTURE.md`
- `docs/WORKFLOW.md`
- `docs/PHASE2_GITHUB_BRIDGE.md`
- `docs/PHASE3_PRODUCTION_ORCHESTRATION.md`
- `docs/PHASE4_DATABASE_FIRST.md`
- `docs/PHASE5_MULTIWORKER_RUNTIME.md`
- `docs/PHASE6_PRODUCTION_PROOF.md`
- `docs/PHASE7_CONTROL_PLANE.md`
- `docs/PHASE8_IDENTITY_GOVERNANCE.md`
- `docs/CODEX_SETUP.md`
- `AGENTS.md`
- `prompts/architect.md`
- `prompts/implementer.md`
- `prompts/auditor.md`
