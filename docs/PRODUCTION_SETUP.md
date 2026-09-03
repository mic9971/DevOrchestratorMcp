# Production Setup Runbook

This runbook starts from a Mac with Docker Desktop, proves the stack locally, then describes the minimum public HTTPS deployment needed for GitHub OAuth/webhooks and the Phase 9 live workflow.

## 1. Local production-like proof on macOS

Prerequisites:

```text
Docker Desktop
Git
curl
openssl
```

Clone and use the stable branch you intend to run:

```bash
git clone https://github.com/mic9971/DevOrchestratorMcp.git
cd DevOrchestratorMcp
git checkout main
git pull --ff-only
```

Create unique local secrets:

```bash
export POSTGRES_PASSWORD="$(openssl rand -hex 32)"
export DEVORCHESTRATOR_ARCHITECT_KEY="$(openssl rand -hex 32)"
export DEVORCHESTRATOR_IMPLEMENTER_KEY="$(openssl rand -hex 32)"
export DEVORCHESTRATOR_AUDITOR_KEY="$(openssl rand -hex 32)"
export GITHUB_WEBHOOK_SECRET="$(openssl rand -hex 32)"
```

Start the local PostgreSQL + migration + application stack:

```bash
docker compose up --build -d
docker compose ps
```

Expected service state:

```text
postgres          running/healthy
db-migrate        exited 0
dev-orchestrator  running
```

Verify:

```bash
curl --fail http://127.0.0.1:5058/healthz
curl --fail http://127.0.0.1:5058/readyz
```

Open:

```text
http://127.0.0.1:5058/control
```

Use the value of `DEVORCHESTRATOR_AUDITOR_KEY` for local break-glass Control Plane access.

Verify Auditor operations:

```bash
curl --fail \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  http://127.0.0.1:5058/ops/status

curl --fail \
  -H "X-DevOrchestrator-Key: $DEVORCHESTRATOR_AUDITOR_KEY" \
  http://127.0.0.1:5058/metrics
```

Stop and remove local data when desired:

```bash
docker compose down -v
```

## 2. Why localhost is not enough for the live proof

GitHub cannot deliver a webhook to your Mac's `localhost`. Human GitHub OAuth also needs a callback URL reachable by the browser/GitHub service.

For short-lived testing on the Mac, put an HTTPS tunnel in front of port 5058:

```text
GitHub
  -> public HTTPS tunnel
  -> http://127.0.0.1:5058
```

Cloudflare Tunnel or ngrok are suitable for this temporary test. Use the generated HTTPS URL consistently for both OAuth callback and GitHub webhook.

For 24/7 use, prefer a small Linux host/VPS plus a managed PostgreSQL database.

## 3. Production server layout

Recommended minimal layout:

```text
Internet :443
    |
  Caddy
    |
127.0.0.1:5058
    |
DevOrchestrator container
    |
managed PostgreSQL
```

The production Compose file binds 5058 only to loopback, so the reverse proxy is the public ingress.

Server prerequisites:

```text
Linux
Docker Engine
Docker Compose plugin
Git
Caddy (or equivalent reverse proxy)
```

Checkout location used by the example deploy workflow:

```bash
sudo mkdir -p /opt/devorchestrator
sudo chown "$USER":"$USER" /opt/devorchestrator
git clone https://github.com/mic9971/DevOrchestratorMcp.git /opt/devorchestrator
cd /opt/devorchestrator
```

## 4. DNS and HTTPS

Create an `A` or `AAAA` record such as:

```text
orchestrator.example.com -> production server IP
```

Copy the example Caddy configuration:

```bash
sudo cp deploy/Caddyfile.example /etc/caddy/Caddyfile
sudo nano /etc/caddy/Caddyfile
```

Replace `orchestrator.example.com`, then reload Caddy:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

The public URL used everywhere below must be the same canonical HTTPS origin.

## 5. Managed PostgreSQL

Create a PostgreSQL database/user and obtain an SSL connection string. Example shape:

```text
Host=db.example.com;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=...;SSL Mode=Require
```

Do not expose PostgreSQL publicly unless the provider requires it; prefer provider firewall/private-network controls.

## 6. Production environment file

On the server:

```bash
cd /opt/devorchestrator
cp deploy/.env.production.example deploy/.env.production
chmod 600 deploy/.env.production
nano deploy/.env.production
```

At minimum configure:

```text
DEVORCHESTRATOR_IMAGE=ghcr.io/mic9971/devorchestratormcp:sha-<verified-main-commit>
DEVORCHESTRATOR_ALLOWED_HOSTS=orchestrator.example.com
DEVORCHESTRATOR_DATABASE_URL=<managed-postgresql-connection-string>

DEVORCHESTRATOR_ARCHITECT_KEY=<unique-32-byte-or-stronger-secret>
DEVORCHESTRATOR_IMPLEMENTER_KEY=<different-secret>
DEVORCHESTRATOR_AUDITOR_KEY=<different-secret>

GITHUB_WEBHOOK_SECRET=<different-secret>
GITHUB_WEBHOOK_MAX_ATTEMPTS=8
```

Generate secrets, for example:

```bash
openssl rand -hex 32
```

Never use one secret for multiple roles.

## 7. GitHub OAuth App for human Control Plane login

Create a GitHub OAuth App with:

```text
Homepage URL:
https://orchestrator.example.com

Authorization callback URL:
https://orchestrator.example.com/signin-github
```

Then configure:

```text
DEVORCHESTRATOR_GITHUB_OAUTH_CLIENT_ID=...
DEVORCHESTRATOR_GITHUB_OAUTH_CLIENT_SECRET=...
DEVORCHESTRATOR_BOOTSTRAP_GITHUB_LOGIN=mic9971
```

The bootstrap login receives Admin on its first successful sign-in. Keep the bootstrap list explicit and narrow.

## 8. GitHub App for repository automation/webhooks

The OAuth App above is human authentication. Repository automation should use a GitHub App.

For the complete Phase 9 synthetic live proof the installed GitHub App/credential needs access appropriate to:

```text
Metadata       Read
Issues         Read & Write
Contents       Read & Write
Pull Requests  Read & Write
```

The DevOrchestrator runtime itself primarily reads Issues/comments through the bridge; Contents/Pull Requests are needed by the synthetic live proof token that creates a marker branch/commit/PR.

Set GitHub App webhook URL:

```text
https://orchestrator.example.com/webhooks/github
```

Set the webhook secret equal to `GITHUB_WEBHOOK_SECRET` in the production environment file.

Subscribe at least to:

```text
Issues
Issue comments
```

Install the GitHub App on every target repository that should drive DevOrchestrator.

Configure the runtime GitHub App credentials:

```text
GITHUB_APP_ID=...
GITHUB_INSTALLATION_ID=...
GITHUB_APP_PRIVATE_KEY_PEM=-----BEGIN ...
```

`GITHUB_TOKEN` remains only a fallback.

## 9. First immutable deployment

Pull and run the exact immutable image:

```bash
cd /opt/devorchestrator

DEVORCHESTRATOR_IMAGE="ghcr.io/mic9971/devorchestratormcp:sha-<commit>" \
  docker compose \
  --env-file deploy/.env.production \
  -f deploy/compose.production.yaml \
  pull
```

Run migrations explicitly:

```bash
DEVORCHESTRATOR_IMAGE="ghcr.io/mic9971/devorchestratormcp:sha-<commit>" \
  docker compose \
  --env-file deploy/.env.production \
  -f deploy/compose.production.yaml \
  run --rm db-migrate
```

Start:

```bash
DEVORCHESTRATOR_IMAGE="ghcr.io/mic9971/devorchestratormcp:sha-<commit>" \
  docker compose \
  --env-file deploy/.env.production \
  -f deploy/compose.production.yaml \
  up -d
```

Check locally on the server:

```bash
curl --fail http://127.0.0.1:5058/healthz
curl --fail http://127.0.0.1:5058/readyz
```

Check publicly:

```bash
curl --fail https://orchestrator.example.com/healthz
curl --fail https://orchestrator.example.com/readyz
```

## 10. Verify the database schema

The Phase 9 migration sequence is:

```text
202609020001_InitialProductionSchema
202609020002_TaskWorkerLeases
202609020003_DurableWebhookInbox
202609020004_IdentityGovernance
202609030001_WebhookDeadLetter
```

`/readyz` must be `200` after deployment. Pending migrations intentionally make readiness fail.

## 11. Verify human login

Open:

```text
https://orchestrator.example.com/control
```

Click **Sign in with GitHub**. After callback, verify the bootstrap user can open Governance.

Command-line challenge check:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' \
  'https://orchestrator.example.com/auth/login?returnUrl=/control'
```

Expected before following redirects:

```text
302
```

## 12. Register a target project

Connect an Architect MCP client to:

```text
https://orchestrator.example.com/mcp
```

Authenticate with an Architect machine credential and call `project_register`, for example:

```json
{
  "projectKey": "novel-platform",
  "name": "Novel Platform Architecture",
  "repositoryUrl": "https://github.com/mic9971/NovelPlatformArchitecture",
  "defaultBranch": "main",
  "actor": "architect"
}
```

Only one active project should point to a given GitHub repository.

## 13. Verify a real GitHub webhook import

Create a GitHub Issue in the target repository containing:

````text
```devorchestrator-plan
{
  "schema": "devorchestrator.plan.v1",
  "projectKey": "novel-platform",
  "tasks": [
    {
      "code": "POC-001",
      "title": "Production orchestrator proof",
      "objective": "Verify the real GitHub webhook path.",
      "acceptanceCriteria": [
        "Task appears as Ready in DevOrchestrator"
      ]
    }
  ]
}
```
````

Then open `/control` -> Tasks and verify `POC-001` becomes `Ready` without manually calling the bridge import tool.

If it does not, inspect:

```text
/control -> Webhooks
/ops/status
/metrics
```

Dead-lettered events are retained with their error and can be replayed by an Auditor after the configuration problem is fixed.

## 14. Run the Phase 9 live-production-proof workflow

Repository secrets required:

```text
DEVORCHESTRATOR_ARCHITECT_KEY
DEVORCHESTRATOR_IMPLEMENTER_KEY
DEVORCHESTRATOR_AUDITOR_KEY
GITHUB_WEBHOOK_SECRET
DEVORCHESTRATOR_LIVE_GITHUB_TOKEN   # required when github.token cannot mutate target repo
```

Dispatch `.github/workflows/live-production-proof.yml` with:

```text
base_url=https://orchestrator.example.com
repository_url=https://github.com/mic9971/DevOrchestratorMcp
project_key=<optional>
```

The workflow performs real Issue/webhook/MCP/branch/commit/PR/review/webhook operations and cleans up its temporary GitHub resources.

Passing this workflow is the Phase 9 public synthetic MCP evidence gate.

## 15. Enable scheduled production monitoring

Set repository secrets:

```text
DEVORCHESTRATOR_PRODUCTION_BASE_URL=https://orchestrator.example.com
DEVORCHESTRATOR_AUDITOR_KEY=...
```

`.github/workflows/production-monitor.yml` runs hourly. It fails on readiness/auth problems, dead-lettered webhooks, expired leases, or an excessive inbox backlog.

## 16. Optional SSH deploy workflow

The generic `.github/workflows/deploy-production.yml` expects these repository secrets:

```text
DEVORCHESTRATOR_PRODUCTION_SSH_HOST
DEVORCHESTRATOR_PRODUCTION_SSH_USER
DEVORCHESTRATOR_PRODUCTION_SSH_PRIVATE_KEY
DEVORCHESTRATOR_PRODUCTION_SSH_KNOWN_HOSTS
DEVORCHESTRATOR_AUDITOR_KEY
```

The remote server must already have `/opt/devorchestrator/deploy/.env.production`. CI intentionally does not transmit the production environment file.

Dispatch with an immutable image such as:

```text
ghcr.io/mic9971/devorchestratormcp:sha-<main-commit>
```

## 17. Backup/restore

Use the existing helpers and test restore regularly:

```bash
export DEVORCHESTRATOR_PG_URL='postgresql://user:password@host:5432/devorchestrator'
bash scripts/backup-postgres.sh ./backups/prod.dump
```

Restore only into an intended target connection string:

```bash
export DEVORCHESTRATOR_RESTORE_PG_URL='postgresql://user:password@host:5432/devorchestrator_restore'
bash scripts/restore-postgres.sh ./backups/prod.dump
```

A backup that has never been restored is not sufficient recovery evidence.

## Final production checklist

```text
[ ] immutable main image deployed
[ ] PostgreSQL migration 005 applied
[ ] /healthz public 200
[ ] /readyz public 200
[ ] direct port 5058 is not public
[ ] GitHub OAuth login succeeds
[ ] bootstrap Admin verified
[ ] GitHub App installed on target repo
[ ] Issues + issue_comment webhooks reach the server
[ ] Plan Issue auto-imports without manual bridge call
[ ] Auditor /ops/status and /metrics succeed
[ ] dead-letter count = 0
[ ] live-production-proof workflow passes
[ ] production-monitor workflow configured
[ ] backup/restore drill succeeds
```

Do not mark public production as proven until the live workflow has actually run successfully against the deployed HTTPS endpoint.
