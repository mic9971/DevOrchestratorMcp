# Phase 9 — Live Production Deployment & End-to-End Proof

## Goal

Phase 9 closes the gap between **production-like proof in CI** and **production proof against a public HTTPS deployment**.

The phase does not redefine the domain model or introduce a message broker. It hardens the existing PostgreSQL-backed runtime, adds poison-webhook recovery, provides a vendor-neutral deployment path, and creates an opt-in live harness that exercises real GitHub resources through the public MCP endpoint.

## Proof boundary

Three evidence levels are intentionally separated:

1. **Hermetic PR CI** — build, migrations, tests, Docker boot, restart, backup and restore. This runs on every PR and does not need public infrastructure.
2. **Live synthetic MCP proof** — a remote MCP client behaves like an Implementer worker and creates a real GitHub Issue, branch, marker commit and PR. This proves the protocol/orchestration path, but it is not an external Codex executable.
3. **External Codex proof** — a separately launched Codex worker connects to `/mcp`, changes a real target repository and submits evidence. Phase 9 prepares this path but does not claim it unless it is actually executed.

Never report level 2 as level 3.

## Runtime topology

```text
GitHub OAuth / GitHub App / Codex / ChatGPT
                  |
               HTTPS 443
                  |
                Caddy
                  |
          127.0.0.1:5058
                  |
          DevOrchestratorMcp
                  |
          managed PostgreSQL
```

`deploy/compose.production.yaml` binds the application only to loopback. A reverse proxy is therefore the intended public ingress and clients cannot bypass TLS by reaching port 5058 directly.

`deploy/Caddyfile.example` is a minimal HTTPS reverse-proxy example. Other proxies/load balancers are valid if they preserve HTTPS and forward requests normally.

## Webhook dead-letter lifecycle

Before Phase 9, a durable GitHub webhook could retry indefinitely. Phase 9 adds a bounded retry policy:

```text
received
  -> leased
  -> processed -----------------------> completed
       |
       +-> transient failure
              |
              +-> retry with backoff
                       |
                       +-> attempt < max -> pending retry
                       |
                       `-> attempt >= max -> dead-lettered
                                               |
                                      Auditor replay
                                               |
                                               `-> pending
```

Configuration:

```text
GitHub__WebhookMaxAttempts=8
```

Production Compose environment alias:

```text
GITHUB_WEBHOOK_MAX_ATTEMPTS=8
```

The configured value is clamped to 1–100 by the runtime. The default is 8.

Dead-lettered rows retain `AttemptCount` and `LastError`. They are excluded from worker leasing until an Auditor explicitly replays them. Replay clears the dead-letter timestamp/error and makes the delivery eligible immediately.

Migration:

```text
202609030001_WebhookDeadLetter
```

The migration adds `DeadLetteredAtUtc` and updates the inbox scheduling index.

## Repository routing safety

A GitHub repository must map to at most one active DevOrchestrator project.

If no active project matches, the webhook is treated as `unregistered_repository` and completed. If multiple active projects match, processing fails with `webhook.repository_ambiguous`; the inbox retries and eventually dead-letters the delivery instead of choosing a project nondeterministically.

This makes configuration mistakes visible to operations.

## Operational surfaces

`/ops/status` reports:

- task state counts
- active and expired task leases
- active worker IDs
- pending webhook count
- retrying webhook count
- dead-lettered webhook count

`/metrics` exports:

```text
devorchestrator_active_workers
devorchestrator_active_task_leases
devorchestrator_expired_task_leases
devorchestrator_webhook_inbox_pending
devorchestrator_webhook_inbox_retrying
devorchestrator_webhook_dead_lettered
devorchestrator_webhook_retry_total
devorchestrator_task_reclaim_total
devorchestrator_manual_lease_expiry_total
```

The retry/reclaim/lease-expiry values are derived from durable database state/history rather than an in-memory counter, so a service restart does not reset operational evidence.

The Control Plane Webhooks view supports:

```text
Pending
Retrying
Dead-lettered
Completed
All
```

Auditor replay continues to use:

```text
POST /ops/webhooks/{deliveryId}/replay
```

## Live production workflow

`.github/workflows/live-production-proof.yml` is intentionally manual. It requires a public HTTPS deployment and production secrets.

Inputs:

```text
base_url       required public HTTPS DevOrchestrator URL
repository_url optional live GitHub target; defaults to current repository
project_key    optional pre-registered project key
```

Required repository secrets:

```text
DEVORCHESTRATOR_ARCHITECT_KEY
DEVORCHESTRATOR_IMPLEMENTER_KEY
DEVORCHESTRATOR_AUDITOR_KEY
GITHUB_WEBHOOK_SECRET
```

For a target repository that cannot be mutated by the workflow `github.token`, also configure:

```text
DEVORCHESTRATOR_LIVE_GITHUB_TOKEN
```

That token needs only the repository permissions required by the live proof: Issues read/write, Contents read/write and Pull Requests read/write.

The workflow first proves:

```text
HTTPS
/healthz
/readyz
GitHub OAuth challenge -> 302
unauthenticated /metrics -> 401
Auditor /ops/status
Phase 9 metrics
signed webhook ingress
```

It then runs `LiveProductionWorkflowTests`.

### Live lifecycle test

```text
create real GitHub Plan Issue
        |
        | real GitHub webhook
        v
wait until task = Ready
        |
        | remote Streamable HTTP MCP
        v
synthetic worker task_claim_next
        |
        +-> task_heartbeat
        |
        +-> real GitHub branch
        +-> real marker commit
        +-> real pull request
        |
        +-> task_add_evidence
        `-> task_submit_review
                  |
                  v
        GitHub review contract comment
                  |
                  | real GitHub webhook
                  v
                Done
```

The temporary PR is closed and its temporary branch is deleted during cleanup. The Plan Issue is also closed.

### Multi-worker live test

The second test opens three separate MCP Implementer clients and claims three imported tasks concurrently. It asserts unique task and worker ownership. One lease is then manually expired by an Auditor and a fourth MCP worker must reclaim that exact task.

This proves the lease/recovery behavior over the real public MCP transport rather than only through application-service tests.

## Continuous production monitor

`.github/workflows/production-monitor.yml` runs hourly and can also be dispatched manually.

Scheduled monitoring activates after these secrets are configured:

```text
DEVORCHESTRATOR_PRODUCTION_BASE_URL
DEVORCHESTRATOR_AUDITOR_KEY
```

The job fails when:

- liveness/readiness fails
- GitHub OAuth no longer challenges correctly
- authenticated ops access fails
- any webhook is dead-lettered
- any task lease is expired
- webhook pending backlog exceeds 100

A failing scheduled GitHub Action becomes a simple initial alert channel without introducing Prometheus Alertmanager or another service. An external monitoring platform can replace or complement it later.

## Generic immutable deploy workflow

`.github/workflows/deploy-production.yml` supports a simple SSH-hosted deployment while keeping the runtime vendor-neutral.

It accepts only an image whose tag matches:

```text
ghcr.io/...:sha-<commit>
```

The remote host must already contain:

```text
<deploy_path>/DevOrchestratorMcp checkout
<deploy_path>/deploy/.env.production
Docker + Docker Compose
Caddy or another HTTPS reverse proxy
```

Required secrets:

```text
DEVORCHESTRATOR_PRODUCTION_SSH_HOST
DEVORCHESTRATOR_PRODUCTION_SSH_USER
DEVORCHESTRATOR_PRODUCTION_SSH_PRIVATE_KEY
DEVORCHESTRATOR_PRODUCTION_SSH_KNOWN_HOSTS
DEVORCHESTRATOR_AUDITOR_KEY
```

The workflow performs:

```text
git pull --ff-only main
pull immutable image
run db-migrate
up -d
local /readyz
public /healthz + /readyz
Auditor /ops/status
```

The production `.env` is deliberately not transferred by CI and must already exist on the host with restricted permissions.

## Migration and recovery gate

Normal CI continues to run the real PostgreSQL migration path and production-like Docker proof. Phase 9 raises restore verification to at least five migrations:

```text
001 InitialProductionSchema
002 TaskWorkerLeases
003 DurableWebhookInbox
004 IdentityGovernance
005 WebhookDeadLetter
```

A backup is restored into a fresh PostgreSQL database and `__EFMigrationsHistory` is verified after restore.

## Phase 9 definition of done

Repository/CI DoD:

- build has zero warnings/errors
- migration 005 works on PostgreSQL and SQLite
- DLQ persistence/replay tests pass
- ambiguous repository routing test passes
- control-plane DLQ/metrics HTTP tests pass
- production Compose validates
- real Docker runtime/restart/backup/restore proof passes
- PR CI is green and merged to `main`
- immutable GHCR image for the merge commit is published

External live DoD is separate and must not be inferred from repository CI:

- public HTTPS endpoint deployed
- managed PostgreSQL connected
- GitHub OAuth App configured and human login tested
- GitHub App/webhook configured for Issues + Issue comments
- `live-production-proof` workflow passes against that endpoint
- optionally, an external Codex executable is launched and proven independently

Until those external gates run, the accurate status is **production-ready and live-proof-capable**, not **public production verified**.
