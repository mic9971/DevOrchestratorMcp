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
  "task_get",
  "task_get_next",
  "task_start",
  "task_add_evidence",
  "task_submit_review",
  "task_block"
]
```

Do **not** add:

```text
task_create
task_create_batch
review_submit
task_reopen
```

to the Codex implementer allow-list.

That separation is intentional: the coding agent must not create its own acceptance criteria or approve its own implementation.

## Suggested Codex startup instruction

Use `prompts/implementer.md` as the stable workflow instruction, while the target repository's `AGENTS.md` remains the source of coding/architecture rules.

## ChatGPT web note

ChatGPT web and local Codex do not consume the same local `config.toml`. The shared durable state is therefore:

```text
GitHub
+
target AGENTS.md
+
DevOrchestratorMcp database
```

If your ChatGPT workspace supports a remote/plugin MCP connection, expose this server through HTTPS and connect the same MCP instance. Otherwise ChatGPT can still read GitHub and you can use the Architect/Auditor prompts while Codex writes implementation state to MCP.
