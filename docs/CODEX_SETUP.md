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
5. Restart the extension/app.

## Codex `config.toml`

Use a project-scoped `.codex/config.toml` in the target repository or merge the example into `~/.codex/config.toml`.

Recommended implementer allow-list:

```toml
[mcp_servers.dev_orchestrator]
url = "http://127.0.0.1:5058/mcp"
enabled = true
required = true
startup_timeout_sec = 20
tool_timeout_sec = 60
default_tools_approval_mode = "writes"

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

`bridge_import_plan_issue` is allowed because it consumes a plan already authored in GitHub instead of allowing Codex to invent its own task contract.

`bridge_sync_reviews` is allowed because it consumes a review already authored in GitHub instead of allowing Codex to call `review_submit` directly.

For strict separation of duties, Codex should have Git push/PR capabilities but should not receive a GitHub token with Issue comment write permission.

## GitHub Bridge startup cycle

When a Plan Issue number is supplied to Codex:

1. Call `bridge_import_plan_issue` once. It is safe to call again because existing task codes are skipped.
2. Call `bridge_sync_reviews` to consume any new auditor comment from the previous implementation cycle.
3. Call `task_get_next`.
4. Implement the returned task only.
5. Record real Git evidence and call `task_submit_review`.
6. Stop and wait for an independent ChatGPT audit.

On the next implementation cycle, repeat steps 1–6.

## GitHub token for the MCP server

The MCP GitHub Bridge reads the registered target repository through GitHub REST.

Public repositories need no token for a basic POC. For private repositories or higher rate limits:

```bash
export GitHub__Token=<token>
```

`GITHUB_TOKEN` is also recognized.

Do not commit tokens into the target repo or DevOrchestratorMcp.

## Suggested Codex startup instruction

Use `prompts/implementer.md` as the stable workflow instruction, while the target repository's `AGENTS.md` remains the source of coding/architecture rules.

## Shared state model

ChatGPT Web and local Codex do not need to share one local config file. The durable handoff is:

```text
GitHub Plan Issue + review comments
             |
             v
DevOrchestratorMcp task database
             |
             v
Codex implementation state
```

GitHub remains the source of truth for code/diff/PR evidence; the MCP database remains the source of truth for task lifecycle state.
