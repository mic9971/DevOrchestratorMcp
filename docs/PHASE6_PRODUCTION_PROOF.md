# Phase 6 — Production Deployment & Operational Proof

## Objective

Phase 6 moves DevOrchestrator from production-ready implementation to a deployable, observable and recoverable control plane. It intentionally stays cloud-vendor neutral: the runtime is one immutable container plus managed PostgreSQL, so Azure Container Apps, ECS, Kubernetes or a hardened VM can host the same artifact.

## Acceptance gates

1. CI builds the .NET solution with zero errors and runs all tests.
2. PostgreSQL migrations run against PostgreSQL 17.
3. CI boots the real Docker image with PostgreSQL and proves `/healthz` and `/readyz`.
4. Operational endpoints require the Auditor key.
5. CI restarts the service and proves database-backed readiness returns.
6. CI performs `pg_dump` and restores into a fresh database, then verifies migration history.
7. `main` publishes immutable `ghcr.io/<owner>/devorchestratormcp:sha-<commit>` images.
8. A manual live workflow proves a public deployment with health/readiness, authenticated metrics and a signed GitHub webhook ping.
9. Multi-worker lease tests prove a dead worker can be manually expired and another worker can reclaim immediately.

## Production topology

```text
GitHub App/Webhooks
        |
        v
HTTPS / reverse proxy / cloud ingress
        |
        v
DevOrchestratorMcp container(s)
        |
        +---- OTLP collector (optional)
        |
        v
Managed PostgreSQL
```

Do not place PostgreSQL on the public internet. Use private networking/firewall allow-lists and TLS.

## Image release

`.github/workflows/release-image.yml` publishes:

```text
ghcr.io/<owner>/devorchestratormcp:sha-<GITHUB_SHA>
ghcr.io/<owner>/devorchestratormcp:latest
```

Git tags additionally publish the tag name. Production deployments should pin the immutable `sha-*` tag, not `latest`.

The runtime image runs as the non-root `app` user and contains only the ASP.NET runtime plus curl for container health checks.

## Production deployment

Copy `deploy/.env.production.example` to an out-of-repository secret environment and configure it. Then run:

```bash
docker compose --env-file /secure/path/devorchestrator.env \
  -f deploy/compose.production.yaml pull

docker compose --env-file /secure/path/devorchestrator.env \
  -f deploy/compose.production.yaml up -d
```

`db-migrate` must finish successfully before the application starts. The application process does not need schema-DDL permissions after migration.

## Operational endpoints

`/healthz` and `/readyz` are intentionally unauthenticated for load balancers.

The following endpoints require the **Auditor** API key when authentication is enabled:

```text
GET  /ops/status
GET  /metrics
POST /ops/tasks/{projectKey}/{taskCode}/expire-lease
POST /ops/projects/{projectKey}/pause
POST /ops/projects/{projectKey}/resume
POST /ops/webhooks/{deliveryId}/replay
```

Example:

```bash
curl -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  https://orchestrator.example.com/ops/status
```

`expire-lease` does not erase ownership/history. It sets the current lease expiry to `now`, making the existing InProgress task reclaimable through the normal `task_claim_next` path and preserving an audit event.

## Metrics

`GET /metrics` exposes Prometheus text for the current operational gauges:

```text
devorchestrator_active_workers
devorchestrator_active_task_leases
devorchestrator_expired_task_leases
devorchestrator_webhook_inbox_pending
devorchestrator_webhook_inbox_retrying
```

OpenTelemetry tracing remains available through `OTEL_EXPORTER_OTLP_ENDPOINT` for HTTP and outbound GitHub calls.

## Backup and restore

Backup uses a PostgreSQL URI because `pg_dump` consumes libpq connection strings:

```bash
export DEVORCHESTRATOR_PG_URL='postgresql://user:password@db:5432/devorchestrator?sslmode=require'
bash scripts/backup-postgres.sh
```

Restore must target a deliberately selected database:

```bash
export DEVORCHESTRATOR_RESTORE_PG_URL='postgresql://user:password@restore-db:5432/devorchestrator_restore?sslmode=require'
bash scripts/restore-postgres.sh backups/devorchestrator-....dump
```

The CI production proof restores into a separate database and verifies `__EFMigrationsHistory`. Production should also schedule provider-native PITR/snapshots; logical dumps are the portable recovery layer, not the only backup layer.

## Live production proof

After deploying a public HTTPS endpoint, configure repository secrets:

```text
DEVORCHESTRATOR_AUDITOR_KEY
GITHUB_WEBHOOK_SECRET
```

Run the `live-production-proof` workflow and provide the public base URL. The workflow verifies health, readiness, Auditor-only operations/metrics and a correctly HMAC-signed GitHub `ping` webhook.

The existing `real-github-e2e` workflow separately creates real GitHub Issue/comment contracts and proves the plan/review workflow against GitHub.

## Cloud-provider boundary

No real external cloud deployment is claimed until credentials, DNS/TLS and a target runtime are supplied. Phase 6 provides the immutable image, deployment contract, recovery scripts and live proof gate so that the external deployment is configuration work rather than a code redesign.
