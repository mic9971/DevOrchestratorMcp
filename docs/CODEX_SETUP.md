# Codex MCP setup

Run the MCP server locally on a fixed port:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project src/DevOrchestrator.McpServer
```

MCP URL:

```text
http://127.0.0.1:5058/mcp
```

## Codex IDE / desktop setup

In MCP server settings:

1. Add server.
2. Choose **Streamable HTTP**.
3. Name it `dev-orchestrator`.
4. URL: `http://127.0.0.1:5058/mcp`.
5. Configure the Implementer bearer token when Phase 3 authentication is enabled.
6. Restart the extension/app.

## Codex `config.toml`

Use a project-scoped `.codex/config.toml` in the target repository or merge the example into `~/.codex/config.toml`.

Export the implementer key without committing it:

```bash
export DEVORCHESTRATOR_IMPLEMENTER_KEY="<secret>"
```

Recommended implementer configuration:

```toml
[mcp_servers.dev_orchestrator]
url = "http://127.0.0.1:5058/mcp"
enabled = true
required = true
startup_timeout_sec = 20
tool_timeout_sec = 60
default_tools_approval_mode = "writes"
bearer_token_env_var = "DEVORCHESTRATOR_IMPLEMENTER_KEY"

enabled_tools = [
  "project_get",
  "bridge_import_plan_issue",
  "bridge_sync_reviews",
  "task_get",
  "task_get_next",
  "task_start",
  "task_add_evidence",
  "task_submit_review",
  "task_block"
]
```

Do **not** add direct architect/auditor tools such as:

```text
task_create
task_create_batch
review_submit
task_reopen
```

to the Codex implementer allow-list.

Phase 3 also enforces role separation on the server, so an Implementer key cannot call Architect/Auditor tools even if a client configuration is accidentally broadened.

## Phase 2 manual bridge cycle

When webhooks are not configured, Codex can still use the Phase 2 cycle:

1. Call `bridge_import_plan_issue`.
2. Call `bridge_sync_reviews`.
3. Call `task_get_next`.
4. Implement the returned task only.
5. Record real Git evidence and call `task_submit_review`.
6. Stop for independent audit.

## Phase 3 webhook cycle

When the target GitHub repository sends signed `issues` and `issue_comment` events to `/webhooks/github`:

- plan issue creation/edits automatically import missing tasks;
- review comment creation/edits automatically synchronize review decisions;
- `X-GitHub-Delivery` prevents duplicate webhook replay.

Codex therefore normally starts with:

```text
task_get_next
```

and only uses the Phase 2 bridge tools as an explicit recovery/manual-sync path.

## GitHub configuration for the MCP server

For private repositories or higher API rate limits:

```bash
export GitHub__Token="<token>"
```

For webhook verification:

```bash
export GitHub__WebhookSecret="<strong-random-secret>"
```

Configure the same webhook secret in GitHub and subscribe at minimum to:

```text
Issues
Issue comments
```

Webhook URL:

```text
https://<your-host>/webhooks/github
```

Do not commit tokens or webhook secrets.

## Production role keys

When `Security__RequireAuthentication=true`, configure three distinct secrets:

```bash
export Security__ArchitectKey="<architect-secret>"
export Security__ImplementerKey="$DEVORCHESTRATOR_IMPLEMENTER_KEY"
export Security__AuditorKey="<auditor-secret>"
```

Codex receives only the Implementer key. ChatGPT/human operator tooling receives the appropriate Architect or Auditor credential through its deployment environment.

## Suggested Codex startup instruction

Use `prompts/implementer.md` as the stable workflow instruction, while the target repository's `AGENTS.md` remains the source of coding/architecture rules.

## Shared state model

```text
GitHub Plan Issue + review comments
             |
       signed webhooks
             v
DevOrchestratorMcp task database
             |
             v
Codex implementation state
```

GitHub remains the source of truth for code/diff/PR evidence; the MCP database remains the source of truth for task lifecycle state.
