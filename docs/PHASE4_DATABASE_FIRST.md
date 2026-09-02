# Phase 4 — Database-First Hardening

Phase 4 starts with database lifecycle because the database is the control plane for ChatGPT planning, Codex execution, GitHub reviews, dependency promotion, and audit history. After that gate, Phase 4 hardens read scalability, caller identity, HTTP proof, and observability.

## Delivered

### 1. Versioned database lifecycle

- Replaced runtime `EnsureCreated` / ad-hoc schema DDL with a versioned EF Core migration baseline.
- Added an explicit `migrate` process mode.
- Normal MCP startup does not alter schema.
- `/readyz` reports `503` when migrations are pending.
- Complete Phase 3 SQLite/PostgreSQL schemas without migration history can be safely adopted; partial legacy schemas fail fast.

Deployment order:

```text
PostgreSQL healthy
      |
      v
DevOrchestrator image: migrate
      |
      v
schema current
      |
      v
MCP server starts
      |
      v
/readyz verifies no pending migrations
```

Local SQLite migration:

```bash
dotnet run --project src/DevOrchestrator.McpServer -- migrate
```

PostgreSQL migration:

```bash
Database__Provider=postgres \
ConnectionStrings__Orchestrator='Host=localhost;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...' \
dotnet run --project src/DevOrchestrator.McpServer -- migrate
```

`compose.yaml` runs a one-shot `db-migrate` service and starts the MCP server only after migration succeeds.

### 2. Real PostgreSQL CI proof

CI starts PostgreSQL 17 and executes the real migration entrypoint before tests. The integration suite verifies that the current migration can recreate the model and leaves no migrations pending.

### 3. Atomic review completion

A passing review that transitions a task to `Done` and promotes dependent tasks executes inside one database transaction. Failure during dependent promotion rolls the parent completion back as well.

### 4. Bounded task read model

`task_list_page` provides a compact, `AsNoTracking` read model with offset pagination and a hard page-size bound of 100. It does not load acceptance criteria, evidence, reviews, or task events.

`task_list` remains for compatibility and full-detail workflows. Normal browsing should use `task_list_page`.

### 5. Authenticated actor binding

When MCP authentication is enabled, write-operation actor identity is no longer trusted from caller input. The server derives it from the authenticated role:

```text
Architect   -> mcp:architect
Implementer -> mcp:implementer
Auditor     -> mcp:auditor
```

Authentication-disabled local POC mode preserves the caller-provided actor for backward compatibility.

### 6. HTTP integration proof

The test host exercises the real ASP.NET Core pipeline and verifies:

- `/healthz` succeeds;
- `/readyz` succeeds only after migration;
- `/mcp` rejects unauthenticated requests when auth is required;
- `/webhooks/github` rejects an invalid HMAC signature.

### 7. OpenTelemetry baseline

The MCP host records ASP.NET Core inbound traces and outbound `HttpClient` traces. Health polling is excluded from inbound tracing. OTLP export is enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured.

## Intentionally not added

RabbitMQ and Redis remain out of scope. The current orchestration control plane does not have enough message throughput to justify the extra operational surface.

## Remaining technical debt after Phase 4

- legacy full-detail `task_list` still has a heavier query shape than `task_list_page`;
- webhook processing is synchronous rather than durable background dispatch;
- webhook delivery retention/cleanup is not automated;
- GitHub authentication is still token-centric rather than GitHub App installation tokens;
- OTEL has a baseline but no SLO dashboard/alert policy yet;
- production PostgreSQL backup/restore/PITR is deployment-specific and not automated in this repository.
