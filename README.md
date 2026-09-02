# DevOrchestratorMcp

![CI](https://github.com/mic9971/DevOrchestratorMcp/actions/workflows/ci.yml/badge.svg)

A reusable .NET MCP control plane for an AI software-development loop:

**ChatGPT Architect → GitHub Plan Issue → MCP task graph → Codex Implementer → Git evidence → ChatGPT Auditor → GitHub review contract → done / changes requested**

The MCP server is deliberately **not** an AI agent. It stores project/task state, coordinates workers, enforces task transitions, records implementation evidence, and keeps review history. Git remains the source of truth for code.

## Stack

- .NET 8 / ASP.NET Core
- Official `ModelContextProtocol.AspNetCore` C# SDK
- Streamable HTTP MCP endpoint
- SQLite for zero-infrastructure local development
- PostgreSQL for shared/production deployment
- Versioned EF Core migrations with an explicit migration process
- Signed GitHub webhook ingestion with durable database inbox and retry worker
- Server-side Architect / Implementer / Auditor authorization
- Multi-worker task lease, heartbeat, expiry, and reclaim
- OpenTelemetry tracing baseline
- Domain, application, database, security, HTTP, and architecture tests

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

If a worker disappears, its task can be reclaimed after lease expiry. A passing review promotes dependent `Draft` tasks to `Ready` atomically when all dependencies are complete.

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
MCP:       http://127.0.0.1:5058/mcp
Liveness:  http://127.0.0.1:5058/healthz
Readiness: http://127.0.0.1:5058/readyz
Webhook:   http://127.0.0.1:5058/webhooks/github
```

`/readyz` returns not-ready when the database has pending migrations. Normal MCP startup never performs DDL.

## Codex configuration

Copy `.codex/config.toml.example` into the target repository and export only the Implementer key:

```bash
export DEVORCHESTRATOR_IMPLEMENTER_KEY="<secret>"
```

New multi-worker integrations should use `task_claim_next` with a stable worker ID and send `task_heartbeat` periodically. See `docs/CODEX_SETUP.md`.

## Production configuration

Database and MCP security:

```text
Database__Provider=postgres
ConnectionStrings__Orchestrator=Host=postgres;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...
Security__RequireAuthentication=true
Security__ArchitectKey=...
Security__ImplementerKey=...
Security__AuditorKey=...
GitHub__WebhookSecret=...
```

Zero-downtime MCP credential rotation can temporarily use:

```text
Security__ArchitectPreviousKey=...
Security__ImplementerPreviousKey=...
Security__AuditorPreviousKey=...
```

Preferred GitHub production authentication is a GitHub App:

```text
GitHub__AppId=...
GitHub__InstallationId=...
GitHub__PrivateKeyPem=...
```

`GitHub__Token` / `GITHUB_TOKEN` remains a compatibility fallback.

Never commit credentials. Serve remote MCP/webhook endpoints behind HTTPS.

## Persistence and deployment

SQLite remains the local default at:

```text
src/DevOrchestrator.McpServer/data/dev-orchestrator.db
```

PostgreSQL is selected with `Database__Provider=postgres`. `compose.yaml` runs a one-shot `db-migrate` service before MCP startup. CI boots PostgreSQL and executes the real migration path.

GitHub webhook requests are HMAC-verified and durably persisted in `github_webhook_inbox`; a hosted worker leases and retries inbox records. `X-GitHub-Delivery` remains the external idempotency key.

Built-in endpoint limits protect the control plane:
- `/mcp`: 120 requests/minute
- `/webhooks/github`: 300 requests/minute

## Verification

Normal PR CI is hermetic. An explicit `real-github-e2e` workflow is available through `workflow_dispatch` to create a temporary Plan Issue, import it, claim/complete the task lifecycle, post a real review contract, sync it, assert `Done`, and close the Issue.

## Design docs

- `docs/ARCHITECTURE.md`
- `docs/WORKFLOW.md`
- `docs/PHASE2_GITHUB_BRIDGE.md`
- `docs/PHASE3_PRODUCTION_ORCHESTRATION.md`
- `docs/PHASE4_DATABASE_FIRST.md`
- `docs/PHASE5_MULTIWORKER_RUNTIME.md`
- `docs/CODEX_SETUP.md`
- `AGENTS.md`
- `prompts/architect.md`
- `prompts/implementer.md`
- `prompts/auditor.md`
