# Codex MCP setup

Run the MCP server locally on a fixed port:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project src/DevOrchestrator.McpServer
```

MCP URL: `http://127.0.0.1:5058/mcp`.

## Codex configuration

Copy `.codex/config.toml.example` into the target repository or merge it into the user-level Codex configuration. Export only the Implementer credential:

```bash
export DEVORCHESTRATOR_IMPLEMENTER_KEY="<secret>"
```

The Implementer allow-list includes `task_claim_next` and `task_heartbeat` but excludes Architect/Auditor write tools.

## Phase 5 multi-worker cycle

Each Codex process/session should generate or persist a stable worker id such as:

```text
codex:<hostname>:<process-or-session-id>
```

Normal execution:

```text
1. Read target AGENTS.md.
2. Call bridge_sync_reviews only when explicit recovery/manual sync is needed.
3. Call task_claim_next(projectKey, workerId, branch).
4. Work on exactly the returned task.
5. Call task_heartbeat with the same workerId while long work is active.
6. Run required build/tests.
7. Attach real Git evidence with task_add_evidence.
8. Call task_submit_review.
9. Stop for independent audit.
```

The task lease is 10 minutes. A practical heartbeat interval is about 5 minutes. If a worker disappears, another worker can reclaim the task after lease expiry.

`task_get_next` and `task_start` remain compatibility paths; new multi-worker integrations should prefer `task_claim_next`.

## GitHub webhook cycle

Signed GitHub `issues` and `issue_comment` events are HMAC-verified and durably queued. The HTTP request returns after enqueue; a hosted worker performs plan import/review synchronization and retries transient failures.

The Phase 2 bridge tools remain available as explicit recovery/manual-sync operations.

## GitHub authentication

Preferred production mode is a GitHub App:

```bash
export GitHub__AppId="<app-id>"
export GitHub__InstallationId="<installation-id>"
export GitHub__PrivateKeyPem="$(cat app-private-key.pem)"
export GitHub__WebhookSecret="<strong-random-secret>"
```

Compatibility token mode remains available:

```bash
export GitHub__Token="<token>"
```

Never commit tokens, app private keys, or webhook secrets.

## Production role keys and rotation

When `Security__RequireAuthentication=true`, configure distinct current keys:

```bash
export Security__ArchitectKey="<architect-secret>"
export Security__ImplementerKey="$DEVORCHESTRATOR_IMPLEMENTER_KEY"
export Security__AuditorKey="<auditor-secret>"
```

For zero-downtime rotation, temporarily configure the outgoing key as the corresponding `PreviousKey`, deploy the new current key, migrate clients, then remove the previous key.

Codex receives only the Implementer key. ChatGPT/human operator tooling receives Architect/Auditor credentials through its deployment environment.

## Manual real GitHub E2E

The `real-github-e2e` GitHub Actions workflow is manual (`workflow_dispatch`) because it intentionally creates a temporary Issue and review comment in the repository. It closes the Issue after verifying the full plan/review contract reaches `Done`.

## Separation of duties

Do **not** expose these to Codex implementers:

```text
task_create
task_create_batch
review_submit
task_reopen
```

Server-side role enforcement remains the authoritative security boundary; the client tool allow-list is defense in depth.
