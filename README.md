# DevOrchestratorMcp

A .NET 8 Model Context Protocol (MCP) control plane for an AI-assisted software development workflow where ChatGPT acts as architect/auditor and Codex acts as implementer.

## Goals

- Keep GitHub as the source of truth for code.
- Keep task state, dependencies, acceptance criteria, implementation evidence, and audit history in one MCP service.
- Enforce separation of duties so an implementer cannot approve its own work.
- Stay small enough for a POC while keeping clean architecture boundaries.

## Architecture

```text
ChatGPT Web (Architect / Auditor)
            |
            | Streamable HTTP MCP
            v
DevOrchestrator.McpServer
            |
            v
DevOrchestrator.Application
            |
     +------+------+
     |             |
     v             v
Domain        Common abstractions
     |
     v
Infrastructure -> SQLite

Codex (Implementer) -- Streamable HTTP MCP --> DevOrchestrator.McpServer
```

## Projects

```text
src/
  DevOrchestrator.McpServer
  DevOrchestrator.Application
  DevOrchestrator.Domain
  DevOrchestrator.Infrastructure
  Shared/DevOrchestrator.Common

tests/
  DevOrchestrator.Domain.Tests
  DevOrchestrator.Architecture.Tests
```

## Task lifecycle

```text
Draft -> Ready -> InProgress -> ReadyForReview
                         ^          |
                         |          +-> ChangesRequested
                         |                      |
                         +----------------------+

ReadyForReview -> Passed -> Done

Any active task can be Blocked or Cancelled where allowed by the domain rules.
```

Only review logic can move a submitted task to `Passed` / `Done` or back to `ChangesRequested`.

## MCP tools

Architect / project owner:

- `project_register`
- `project_get`
- `task_create`
- `task_create_batch`
- `task_get`
- `task_list`

Implementer (Codex):

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

## Run locally

Requirements:

- .NET 8 SDK

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet run --project src/DevOrchestrator.McpServer
```

Default MCP endpoint:

```text
http://localhost:5080/mcp
```

Health endpoint:

```text
http://localhost:5080/health
```

The POC uses SQLite at `data/dev-orchestrator.db` by default.

## Run with Docker

```bash
docker compose up --build
```

## Configure Codex

Copy `.codex/config.toml.example` to the target repository as `.codex/config.toml` and adjust the URL if required.

The example intentionally enables only implementer-safe tools. Do not add `review_submit`, `task_create`, or `task_create_batch` to Codex unless you deliberately want to remove separation of duties.

See [docs/CODEX_SETUP.md](docs/CODEX_SETUP.md).

## Workflow

1. ChatGPT reads a target GitHub repository and creates a project record.
2. ChatGPT breaks a requirement into small tasks with acceptance criteria and dependency links.
3. Codex requests `task_get_next` and starts the returned task.
4. Codex implements the code, runs build/tests, commits/pushes changes, and records evidence.
5. Codex calls `task_submit_review`.
6. ChatGPT reads the task plus GitHub diff/CI output and calls `review_submit`.
7. Approval completes the task and unlocks dependent tasks; requested changes return it to Codex.

See [docs/WORKFLOW.md](docs/WORKFLOW.md).

## Security model for v1

The MCP server exposes all tools at the server level. Client-side tool allowlists enforce the initial role split. For production, add authentication/authorization at the MCP server and map caller identity to server-side tool policies.

## Persistence

SQLite is selected for the first POC to remove infrastructure friction. The repository/application boundary keeps migration to PostgreSQL straightforward when concurrent users and hosted deployment become necessary.

## CI

`.github/workflows/ci.yml` runs restore, release build, domain tests, and architecture tests on pushes and pull requests.

## Next production steps

- OAuth/OIDC or API-key authentication for MCP callers.
- Server-side actor roles and authorization policies.
- EF Core migrations and PostgreSQL provider.
- GitHub App integration/webhooks for commit, PR and CI evidence.
- Concurrency tokens/optimistic locking for task state transitions.
- OpenTelemetry traces/metrics.
- Integration tests with a real HTTP MCP client.
