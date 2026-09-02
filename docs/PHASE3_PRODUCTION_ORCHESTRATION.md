# Phase 3 — Production Orchestration

## Goal

Move DevOrchestratorMcp from a local/manual Phase 2 bridge to a deployable control plane that can react to GitHub events automatically, run on PostgreSQL, and enforce server-side role separation for MCP callers.

## Scope

Phase 3 delivers five production foundations:

1. PostgreSQL-ready persistence while preserving SQLite for local development.
2. GitHub webhook ingestion for plan/review synchronization.
3. HMAC-SHA256 webhook verification and delivery idempotency.
4. Server-side API-key roles for MCP callers.
5. Readiness/health endpoints and production-like Docker Compose wiring.

RabbitMQ is intentionally not introduced in Phase 3. The webhook workload is small and synchronous; a broker should be added only when throughput, retry isolation, or fan-out justifies it.

## Runtime flow

```text
GitHub Issue opened/edited
        |
        | signed webhook
        v
POST /webhooks/github
        |
        | verify HMAC + delivery id
        v
GitHubWebhookProcessor
        |
        +--> issues -> ImportPlanIssueAsync
        |
        `--> issue_comment -> SyncReviewsAsync

Codex / ChatGPT MCP client
        |
        | Bearer/X-DevOrchestrator-Key
        v
/mcp
        |
        | server-side role guard
        v
Architect / Implementer / Auditor tool surface
```

## Security model

Configured keys are environment secrets and must never be committed.

Roles:

- `Architect`: project registration, task planning, read tools.
- `Implementer`: bridge import/sync, task execution/evidence, read tools.
- `Auditor`: review submission, reopen/resume, read tools.

The MCP host rejects unknown keys before dispatch. Tool methods also perform role checks so a caller cannot bypass the client-side `enabled_tools` allow-list.

For local development, authentication can be disabled explicitly with `Security:RequireAuthentication=false`. Production must set it to true.

## Webhook events

Supported GitHub events:

- `issues`: actions `opened`, `edited`, `reopened` trigger plan import.
- `issue_comment`: actions `created`, `edited` trigger review synchronization.
- `ping`: accepted without orchestration work.

Other event/action combinations return HTTP 202 and are ignored.

### Signature verification

GitHub signs the raw request body with the configured webhook secret. The server compares the `X-Hub-Signature-256` value against an HMAC-SHA256 digest using constant-time comparison.

### Idempotency

`X-GitHub-Delivery` is stored in `github_webhook_deliveries`. Duplicate deliveries return HTTP 202 without applying state transitions again.

## Persistence

Provider selection:

```text
Database:Provider = sqlite | postgres
```

Local default remains SQLite.

Production example:

```text
Database__Provider=postgres
ConnectionStrings__Orchestrator=Host=postgres;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...
```

The current Phase 3 initializer keeps `EnsureCreated` for backward compatibility with the existing POC database. A later schema-evolution phase should introduce versioned EF Core migrations before destructive/transformative model changes are made.

## Configuration

```json
{
  "Database": {
    "Provider": "sqlite"
  },
  "Security": {
    "RequireAuthentication": false,
    "ArchitectKey": "",
    "ImplementerKey": "",
    "AuditorKey": ""
  },
  "GitHub": {
    "Token": "",
    "WebhookSecret": ""
  }
}
```

Production secrets must be supplied through environment variables or a secret manager.

## Definition of done

Phase 3 is complete when:

1. SQLite remains usable for local development.
2. PostgreSQL can be selected by configuration.
3. Signed GitHub `issues` events can import a Plan Issue automatically.
4. Signed `issue_comment` events can synchronize review contracts automatically.
5. Duplicate webhook delivery IDs do not replay orchestration transitions.
6. `/mcp` rejects unknown keys when authentication is required.
7. Privileged tools reject callers with the wrong role.
8. `/healthz` and `/readyz` expose liveness/readiness.
9. Docker Compose includes PostgreSQL and the MCP server wiring.
10. Build, tests, and architecture tests are green in CI.
