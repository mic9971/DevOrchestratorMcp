# DevOrchestratorMcp

![CI](https://github.com/mic9971/DevOrchestratorMcp/actions/workflows/ci.yml/badge.svg)

A reusable .NET MCP control plane for an AI software-development loop:

**ChatGPT Architect → task graph → Codex Implementer → Git evidence → ChatGPT Auditor → done / changes requested**

The MCP server is deliberately **not** an AI agent. It stores project/task state, enforces task transitions, records implementation evidence, and keeps review history. Git remains the source of truth for code.

## Stack

- .NET 8
- ASP.NET Core
- Official `ModelContextProtocol.AspNetCore` C# SDK
- Streamable HTTP MCP endpoint
- SQLite for zero-infrastructure POC persistence
- Clean separation: Common / Domain / Application / Infrastructure / MCP host
- Domain state-machine tests + architecture dependency tests

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
  ├── review_submit(ChangesRequested) ──► CHANGES_REQUESTED ──► task_start
  └── review_submit(Pass) ──────────────► DONE
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
- `task_get`
- `task_get_next`
- `task_start`
- `task_add_evidence`
- `task_submit_review`
- `task_block`

Auditor:
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

Default endpoints:

```text
http://localhost:5000/mcp
http://localhost:5000/healthz
```

Kestrel may select another development port if environment variables or launch settings override it. For a fixed port:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5058 dotnet run --project src/DevOrchestrator.McpServer
```

Then use:

```text
http://127.0.0.1:5058/mcp
```

## Codex configuration

See `docs/CODEX_SETUP.md` and `.codex/config.toml.example`.

## First project bootstrap

1. ChatGPT reads the target GitHub repo and creates a plan.
2. Register the target repo with `project_register`.
3. ChatGPT breaks the plan into small dependency-aware tasks with `task_create_batch`.
4. Codex repeatedly calls `task_get_next`, implements one task, records evidence, then calls `task_submit_review`.
5. ChatGPT reads the task + target repo diff/PR and calls `review_submit`.
6. If changes are requested, Codex receives that task before new work.

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
- never place GitHub tokens in task descriptions/evidence.

## Design docs

- `docs/ARCHITECTURE.md`
- `docs/WORKFLOW.md`
- `docs/CODEX_SETUP.md`
- `AGENTS.md`
- `prompts/architect.md`
- `prompts/implementer.md`
- `prompts/auditor.md`
