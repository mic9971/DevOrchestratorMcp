# Phase 5 — Multi-Worker Runtime Hardening

Phase 5 hardens the control plane for multiple Codex workers and removes synchronous GitHub webhook work from the request path.

## 1. Task lease and heartbeat

`task_get_next` remains a read-only preview for backward compatibility. Multi-worker Codex should use:

```text
task_claim_next(projectKey, workerId, branch)
        |
        v
IN_PROGRESS + LeaseOwner + LeaseExpiresAtUtc
        |
        +--> task_heartbeat every few minutes
        |
        +--> evidence + submit review
        |
        `--> worker crash -> lease expires -> another worker can reclaim
```

The production lease is 10 minutes. Heartbeats renew the lease. A non-owner cannot heartbeat another worker's active lease. Optimistic concurrency ensures that two workers racing for the same candidate cannot both successfully claim the same task.

Legacy `task_start` remains as a compatibility path, but new Codex configurations should prefer `task_claim_next`.

## 2. Duplicate task-create race normalization

The database unique index remains the final consistency boundary. PostgreSQL and SQLite unique-key exceptions are translated into the deterministic application error:

```text
task.already_exists
```

The preliminary existence check remains useful for normal UX, but correctness no longer depends on check-then-insert being race-free.

## 3. Durable GitHub webhook inbox

The HTTP webhook endpoint now performs only:

1. HMAC verification;
2. event/header validation;
3. durable inbox enqueue;
4. `202 Accepted`.

A hosted background worker leases inbox records and invokes the existing GitHub bridge processor. Failed work is released with exponential retry backoff. `X-GitHub-Delivery` is still the idempotency key.

This removes GitHub/API latency from the webhook request path without introducing RabbitMQ or Redis.

## 4. GitHub App authentication baseline

Preferred production configuration:

```text
GitHub__AppId
GitHub__InstallationId
GitHub__PrivateKeyPem
```

The server signs a short-lived RS256 GitHub App JWT, requests an installation access token, and caches that token until shortly before expiration.

`GitHub__Token` / `GITHUB_TOKEN` remains a compatibility fallback when GitHub App configuration is absent.

## 5. MCP credential rotation and rate limiting

Each MCP role can have a current and previous key during a rotation window:

```text
Security__ArchitectKey
Security__ArchitectPreviousKey
Security__ImplementerKey
Security__ImplementerPreviousKey
Security__AuditorKey
Security__AuditorPreviousKey
```

All configured role keys must be strong and globally distinct. The previous key can be removed after clients have moved to the new key.

Built-in fixed-window limits protect the control-plane endpoints:

- `/mcp`: 120 requests/minute;
- `/webhooks/github`: 300 requests/minute.

## 6. Real GitHub contract E2E

Normal PR CI remains hermetic and does not create GitHub Issues.

The manual `real-github-e2e` workflow explicitly exercises the live GitHub handoff contract:

```text
create real Plan Issue
  -> register temporary local project
  -> import plan through GitHubBridgeClient
  -> claim task
  -> add evidence
  -> submit for review
  -> post real review-contract comment
  -> sync review
  -> assert DONE
  -> close temporary Issue
```

This proves the real GitHub Plan/Review bridge. It does not launch an external Codex executable; Codex implementation behavior remains independently proven by MCP task/tool contracts and target-repository CI.

## Intentionally not added

RabbitMQ and Redis remain out of scope. PostgreSQL provides the durable control-plane state, lease coordination, and webhook inbox required at the current scale.

## Remaining debt after Phase 5

- evidence/submit/block compatibility tools do not yet require the worker id on every mutation; the claim/heartbeat boundary prevents duplicate assignment, while stricter per-mutation lease ownership can be added if untrusted implementer workers are introduced;
- inbox/event/evidence retention cleanup is not automated;
- production Postgres backup/PITR and restore drills remain deployment responsibilities;
- OTEL tracing exists, but SLO dashboards and alert policies are not bundled;
- the full-detail compatibility `task_list` is still heavier than `task_list_page`;
- GitHub Actions `checkout@v4` / `setup-dotnet@v4` currently emit the upstream Node runtime deprecation warning.
