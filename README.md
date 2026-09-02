# DevOrchestratorMcp

![CI](https://github.com/mic9971/DevOrchestratorMcp/actions/workflows/ci.yml/badge.svg)

A reusable .NET MCP control plane for an AI software-development loop:

**ChatGPT Architect → GitHub plan contract → Codex Implementer → Git evidence → ChatGPT Auditor → done / changes requested**

The MCP server is deliberately **not** an AI agent. It stores project/task state, enforces task transitions, records implementation evidence, and keeps review history. Git remains the source of truth for code.

## Stack

- .NET 8
- ASP.NET Core
- Official `ModelContextProtocol.AspNetCore` C# SDK
- Streamable HTTP MCP endpoint
- SQLite for zero-infrastructure POC persistence
- Clean separation: Common / Domain / Application / Infrastructure / MCP host
- GitHub REST bridge for ChatGPT Web handoff
- Domain + Application bridge + architecture tests

## Workflow

```text
ChatGPT Web
    |
    | create/update GitHub Plan Issue
    v
GitHub
    |
    | bridge_import_plan_issue
    v
DRAFT -> READY -> IN_PROGRESS -> READY_FOR_REVIEW
                                  |
                     ChatGPT audits Git diff/PR
                                  |
                         GitHub review comment
                                  |
                         bridge_sync_reviews
                           /             \
                          v               v
                        DONE      CHANGES_REQUESTED
                                      |
                                      v
                                    Codex
```

A passing review automatically promotes dependent `Draft` tasks to `Ready` when all dependencies are `Done`.

## MCP tools

Architect / direct MCP clients:

- `project_register`
- `project_get`
- `project_list`
- `task_create`
- `task_create_batch`
- `task_get`
- `task_list`

GitHub Bridge:

- `bridge_import_plan_issue`
- `bridge_sync_reviews`

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

Auditor / direct MCP clients:

- `project_get`
- `task_get`
- `task_list`
- `review_submit`
- `task_reopen`
- `task_resume`

**Codex should not be granted `review_submit`.** The GitHub bridge can apply only review contracts already written to GitHub; Codex does not directly decide review outcomes through MCP.

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

Endpoints:

```text
http://127.0.0.1:5058/mcp
http://127.0.0.1:5058/healthz
```

## Phase 2: GitHub Bridge

Phase 2 supports the case where ChatGPT Web can operate on GitHub but does not directly write to your custom MCP server.

### 1. Register target repo

Use `project_register` once through an MCP-capable client.

Example repository:

```text
https://github.com/mic9971/NovelPlatformArchitecture
```

### 2. ChatGPT creates one Plan Issue

The issue contains a fenced `devorchestrator-plan` JSON contract. See:

- `examples/plan-issue.md`
- `docs/PHASE2_GITHUB_BRIDGE.md`

### 3. Codex imports the plan

```text
bridge_import_plan_issue(projectKey="novel-platform", issueNumber=123)
```

Import is idempotent by task code. Re-running it skips tasks already present in MCP state.

### 4. Codex implements one task

```text
task_get_next
task_start
... code/build/test/commit ...
task_add_evidence
task_submit_review
```

### 5. ChatGPT audits the PR

ChatGPT reads the acceptance criteria, evidence and target GitHub diff/CI, then posts a `devorchestrator-review` comment on the plan issue. See `examples/review-comment.md`.

### 6. Codex synchronizes the audit

```text
bridge_sync_reviews(projectKey="novel-platform", issueNumber=123)
```

Only a review newer than the current task submission can apply. Old review comments are ignored, so a previous approval cannot accidentally approve a later implementation iteration.

## GitHub access

Public repositories can be read without a token. For private repositories or higher API limits, configure:

```bash
export GitHub__Token=<github-token>
```

or `GITHUB_TOKEN`.

Never commit the token to `appsettings.json`, task descriptions, or evidence.

Phase 2 currently supports `github.com` repository URLs.

## Codex configuration

See `docs/CODEX_SETUP.md` and `.codex/config.toml.example`.

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
- add caller authentication/authorization;
- configure exact allowed hosts;
- keep Architect/Auditor direct tools out of the Codex allow-list;
- treat direct `review_submit` as privileged;
- scope the GitHub token to the minimum repository permissions needed;
- do not provide Codex with GitHub Issue write credentials if strict separation of duties is required.

## Design docs

- `docs/ARCHITECTURE.md`
- `docs/WORKFLOW.md`
- `docs/CODEX_SETUP.md`
- `docs/PHASE2_GITHUB_BRIDGE.md`
- `AGENTS.md`
- `prompts/architect.md`
- `prompts/implementer.md`
- `prompts/auditor.md`
