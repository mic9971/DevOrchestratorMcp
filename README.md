# DevOrchestratorMcp

![CI](https://github.com/mic9971/DevOrchestratorMcp/actions/workflows/ci.yml/badge.svg)

A reusable .NET MCP control plane for an AI software-development loop:

**ChatGPT Architect → GitHub Plan Issue → MCP task graph → Codex Implementer → Git evidence → ChatGPT Auditor → GitHub review contract → done / changes requested**

The MCP server is deliberately **not** an AI agent. It stores project/task state, enforces task transitions, records implementation evidence, and keeps review history. Git remains the source of truth for code.

## Stack

- .NET 8
- ASP.NET Core
- Official `ModelContextProtocol.AspNetCore` C# SDK
- Streamable HTTP MCP endpoint
- SQLite for zero-infrastructure POC persistence
- Clean separation: Common / Domain / Application / Infrastructure / MCP host
- GitHub Issue/comment bridge for ChatGPT Web handoff
- Domain, application/bridge, and architecture tests

## Phase 2 GitHub Bridge

For ChatGPT Web surfaces that cannot invoke write-capable custom MCP tools directly, GitHub becomes the durable handoff contract:

```text
ChatGPT Web
  -> GitHub Plan Issue (`devorchestrator.plan.v1`)
  -> `bridge_import_plan_issue`
  -> Codex implementation/evidence
  -> ChatGPT audit
  -> GitHub review comment (`devorchestrator.review.v1`)
  -> `bridge_sync_reviews`
  -> DONE / CHANGES_REQUESTED
```

See `docs/PHASE2_GITHUB_BRIDGE.md` and `examples/`.

## Workflow

```text
DRAFT
  │ dependencies satisfied
  ▼
READY
  │ task_start
  ▼
IN_PROGRESS
  │ task_add_evidence
  │ task_submit_review
  ▼
READY_FOR_REVIEW
  ├── review / bridge sync: ChangesRequested ──► CHANGES_REQUESTED ──► task_start
  └── review / bridge sync: Pass ──────────────► DONE
```

A passing review automatically promotes dependent `Draft` tasks to `Ready` when all dependencies are `Done`.

## MCP tools

Architect:
- `project_register`
- `project_get`
- `project_list`
- `task_create`
- `task_create_batch`
- `task_get`
- `task_list`

Implementer / Codex:
- `project_get`
- `bridge_import_plan_issue`
- `bridge_sync_reviews`
- `task_get`
- `task_get_next`
- `task_start`
- `task_add_evidence`
- `task_submit_review`
- `task_block`

Auditor / privileged:
- `project_get`
- `task_get`
- `task_list`
- `review_submit`
- `task_reopen`
- `task_resume`

**Codex should not be granted `review_submit`.** This prevents the implementation agent from approving its own work.

## Run locally

Prerequisites: .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DevOrchestrator.McpServer
```

For a fixed port:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project src/DevOrchestrator.McpServer
```

MCP endpoint:

```text
http://127.0.0.1:5058/mcp
```

## Codex configuration

See `docs/CODEX_SETUP.md` and `.codex/config.toml.example`.

## GitHub Bridge quick start

1. Run the MCP server.
2. Register a target repository with `project_register`.
3. ChatGPT creates one GitHub Plan Issue containing a `devorchestrator.plan.v1` fenced JSON block.
4. Codex calls `bridge_import_plan_issue`, then `task_get_next`.
5. Codex implements one task, records real Git evidence, and calls `task_submit_review`.
6. ChatGPT audits the PR and posts a `devorchestrator.review.v1` comment on the Plan Issue.
7. Codex (or an operator) calls `bridge_sync_reviews`.
8. The MCP transitions the task to `Done` or `ChangesRequested`.

## Persistence

The POC uses:

```text
src/DevOrchestrator.McpServer/data/dev-orchestrator.db
```

The `data/` directory is ignored by Git.

The current version uses `EnsureCreated` for a low-friction POC. Before production deployment, replace it with EF Core migrations and move to PostgreSQL if multiple server replicas or operational DB controls are required.

## Security

For local use, `AllowedHosts` is restricted to loopback names. For remote deployment:

- serve MCP behind HTTPS;
- use authentication;
- configure exact allowed hosts;
- keep Architect/Auditor tools out of the Codex allow-list;
- treat `review_submit` as privileged;
- do not grant Codex GitHub Issue-comment write credentials when strict separation of duties is required;
- never place GitHub tokens in task descriptions/evidence.

## Design docs

- `docs/ARCHITECTURE.md`
- `docs/WORKFLOW.md`
- `docs/PHASE2_GITHUB_BRIDGE.md`
- `docs/CODEX_SETUP.md`
- `AGENTS.md`
- `prompts/architect.md`
- `prompts/implementer.md`
- `prompts/auditor.md`
