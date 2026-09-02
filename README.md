# DevOrchestratorMcp

![CI](https://github.com/mic9971/DevOrchestratorMcp/actions/workflows/ci.yml/badge.svg)

A reusable .NET MCP control plane for an AI software-development loop:

**ChatGPT Architect → GitHub Plan Issue → MCP task graph → Codex Implementer → Git evidence → ChatGPT Auditor → GitHub review contract → done / changes requested**

The MCP server is deliberately **not** an AI agent. It stores project/task state, enforces task transitions, records implementation evidence, and keeps review history. Git remains the source of truth for code.

## Stack

- .NET 8
- ASP.NET Core
- Official `ModelContextProtocol.AspNetCore` C# SDK
- Streamable HTTP MCP endpoint
- SQLite for zero-infrastructure local development
- PostgreSQL provider for shared/production deployment
- Signed GitHub webhook automation with delivery idempotency
- Server-side Architect / Implementer / Auditor authorization
- Clean separation: Common / Domain / Application / Infrastructure / MCP host
- Domain, application/bridge, security, and architecture tests

## Phase 2 GitHub Bridge

For ChatGPT Web surfaces that cannot invoke write-capable custom MCP tools directly, GitHub is the durable handoff contract:

```text
ChatGPT Web
  -> GitHub Plan Issue (`devorchestrator.plan.v1`)
  -> `bridge_import_plan_issue`
  -> Codex implementation/evidence
  -> ChatGPT audit
  -> GitHub review comment (`devorchestrator.review.v1`)
  -> `bridge_sync_reviews`
  -> DONE / CHANGES_REQUESTED
```

See `docs/PHASE2_GITHUB_BRIDGE.md` and `examples/`.

## Phase 3 Production Orchestration

Phase 3 removes the normal operational need to call the bridge sync tools manually:

```text
GitHub issues / issue_comment webhook
          |
          | HMAC-SHA256 + X-GitHub-Delivery
          v
POST /webhooks/github
          |
          +--> plan import
          `--> review sync

Codex / ChatGPT MCP client
          |
          | role API key
          v
        /mcp
          |
          +--> Architect tools
          +--> Implementer tools
          `--> Auditor tools
```

Production persistence can use PostgreSQL with `Database__Provider=postgres`. Local development defaults to SQLite.

See `docs/PHASE3_PRODUCTION_ORCHESTRATION.md`.

## Workflow

```text
DRAFT
  │ dependencies satisfied
  ▼
READY
  │ task_start
  ▼
IN_PROGRESS
  │ task_add_evidence
  │ task_submit_review
  ▼
READY_FOR_REVIEW
  ├── review / webhook sync: ChangesRequested ──► CHANGES_REQUESTED ──► task_start
  └── review / webhook sync: Pass ──────────────► DONE
```

A passing review automatically promotes dependent `Draft` tasks to `Ready` when all dependencies are `Done`.

## MCP tools

Architect:
- `project_register`
- `project_get`
- `project_list`
- `task_create`
- `task_create_batch`
- `task_get`
- `task_list`

Implementer / Codex:
- `project_get`
- `bridge_import_plan_issue`
- `bridge_sync_reviews`
- `task_get`
- `task_get_next`
- `task_start`
- `task_add_evidence`
- `task_submit_review`
- `task_block`

Auditor / privileged:
- `project_get`
- `task_get`
- `task_list`
- `review_submit`
- `task_reopen`
- `task_resume`

Client-side tool allow-lists remain useful, but Phase 3 also enforces these roles on the server.

## Run locally

Prerequisites: .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DevOrchestrator.McpServer
```

For a fixed port:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project src/DevOrchestrator.McpServer
```

Endpoints:

```text
MCP:       http://127.0.0.1:5058/mcp
Liveness:  http://127.0.0.1:5058/healthz
Readiness: http://127.0.0.1:5058/readyz
Webhook:   http://127.0.0.1:5058/webhooks/github
```

Local `appsettings.json` keeps `Security:RequireAuthentication=false` for backward-compatible POC use.

## Codex configuration

Copy `.codex/config.toml.example` into the target repository and export an implementer key:

```bash
export DEVORCHESTRATOR_IMPLEMENTER_KEY="<secret>"
```

The example uses `bearer_token_env_var` so Codex authenticates as the Implementer role while still exposing only the implementer tool allow-list.

See `docs/CODEX_SETUP.md`.

## Production configuration

Typical environment variables:

```text
Database__Provider=postgres
ConnectionStrings__Orchestrator=Host=postgres;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...
Security__RequireAuthentication=true
Security__ArchitectKey=...
Security__ImplementerKey=...
Security__AuditorKey=...
GitHub__Token=...
GitHub__WebhookSecret=...
```

Never commit these values.

`compose.yaml` provides PostgreSQL + DevOrchestrator wiring for a production-like local deployment.

## Persistence

SQLite remains the local default:

```text
src/DevOrchestrator.McpServer/data/dev-orchestrator.db
```

PostgreSQL is selected with `Database__Provider=postgres`. Webhook delivery IDs are persisted in `github_webhook_deliveries`, so multiple server instances share replay protection.

Phase 3 retains `EnsureCreated` for compatibility with the current POC schema. Introduce versioned EF Core migrations before future destructive or transforming schema changes.

## Security

For remote deployment:

- serve MCP and webhook endpoints behind HTTPS;
- set `Security__RequireAuthentication=true`;
- configure distinct Architect, Implementer, and Auditor keys;
- set a strong `GitHub__WebhookSecret`;
- configure exact allowed hosts;
- do not give Codex the Architect or Auditor key;
- do not grant Codex GitHub Issue-comment write credentials when strict separation of duties is required;
- never place GitHub/API tokens in task descriptions, evidence, commits, or PRs.

## Design docs

- `docs/ARCHITECTURE.md`
- `docs/WORKFLOW.md`
- `docs/PHASE2_GITHUB_BRIDGE.md`
- `docs/PHASE3_PRODUCTION_ORCHESTRATION.md`
- `docs/CODEX_SETUP.md`
- `AGENTS.md`
- `prompts/architect.md`
- `prompts/implementer.md`
- `prompts/auditor.md`
