# Phase 4 — Database-First Hardening

Phase 4 starts with database lifecycle and consistency because the orchestration state is the control plane for ChatGPT, Codex, GitHub reviews, and dependency promotion.

## Goals

1. Replace runtime `EnsureCreated` / ad-hoc DDL with versioned EF Core migrations.
2. Run schema migration explicitly before the MCP server starts.
3. Prove the current model against a real PostgreSQL instance in CI.
4. Make review completion + dependent-task promotion atomic.
5. Preserve Phase 3 SQLite POC databases through safe baseline adoption.

## Deployment workflow

```text
PostgreSQL healthy
      |
      v
DevOrchestrator image: migrate
      |
      | EF Core migrations
      v
schema current
      |
      v
MCP server starts
      |
      v
/readyz verifies no pending migrations
```

The normal MCP process no longer creates or alters schema during startup. Production can therefore use separate migration and runtime database credentials when desired.

## Migration commands

Local SQLite:

```bash
dotnet run --project src/DevOrchestrator.McpServer -- migrate
```

PostgreSQL:

```bash
Database__Provider=postgres \
ConnectionStrings__Orchestrator='Host=localhost;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...' \
dotnet run --project src/DevOrchestrator.McpServer -- migrate
```

## Legacy Phase 3 adoption

A Phase 3 database without `__EFMigrationsHistory` can be adopted only when all known Phase 3 tables exist and `tasks.Revision` is present. Partial schemas fail fast instead of being silently marked current.

## PostgreSQL CI proof

CI starts PostgreSQL 17, executes the real migration entrypoint, then the integration test recreates the database through EF migrations and verifies that no migrations remain pending.

## Transaction boundary

A passing review that marks a task `Done` and promotes dependent tasks now executes inside one database transaction. A failure in dependent promotion rolls back the parent completion as well.

## Next Phase 4 hardening work

After the database-first gate is green:

- summary/detail task query split and cursor pagination;
- authenticated actor binding;
- HTTP MCP/webhook E2E tests;
- OpenTelemetry traces and metrics.
