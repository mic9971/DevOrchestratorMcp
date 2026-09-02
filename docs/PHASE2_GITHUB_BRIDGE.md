# Phase 2 — GitHub Bridge for ChatGPT Web + Codex

## Goal

Enable the workflow to work when ChatGPT Web can read/write GitHub but cannot directly invoke custom write-capable MCP tools.

GitHub becomes the handoff contract while DevOrchestratorMcp remains the source of truth for task state.

```text
ChatGPT Web
    |
    | create/update one GitHub Plan Issue
    v
GitHub Issue
    |
    | bridge_import_plan_issue
    v
DevOrchestratorMcp
    |
    | task_get_next / task_start
    v
Codex
    |
    | code + tests + PR + evidence
    v
READY_FOR_REVIEW
    |
    | ChatGPT reviews PR and posts review contract comment
    v
GitHub Issue comment
    |
    | bridge_sync_reviews
    v
DevOrchestratorMcp
    +--> DONE
    `--> CHANGES_REQUESTED --> Codex
```

## Scope

### P2.1 Contract format

Use one planning issue per orchestration run. The issue body contains one fenced JSON block:

````markdown
```devorchestrator-plan
{
  "schema": "devorchestrator.plan.v1",
  "projectKey": "novel-platform",
  "tasks": [
    {
      "code": "P2-001",
      "title": "Example task",
      "objective": "Small implementation objective",
      "acceptanceCriteria": ["Build passes", "Tests pass"],
      "constraints": ["Do not change unrelated behavior"],
      "dependencies": [],
      "priority": "Normal"
    }
  ]
}
```
````

A review is a GitHub issue comment containing:

````markdown
```devorchestrator-review
{
  "schema": "devorchestrator.review.v1",
  "taskCode": "P2-001",
  "decision": "Pass",
  "summary": "Acceptance criteria verified.",
  "findings": [],
  "completeOnPass": true
}
```
````

`decision` supports `Pass` and `ChangesRequested`.

### P2.2 GitHub API adapter

Add an Application abstraction and Infrastructure implementation using `HttpClient`.

Responsibilities:

- parse `https://github.com/{owner}/{repo}` from the registered target project;
- fetch one issue body;
- fetch all issue comments;
- support public repositories without a token;
- use `GitHub:Token` or `GITHUB_TOKEN` for private repositories/rate-limit headroom;
- set GitHub API version and user-agent headers;
- keep GitHub DTOs inside Infrastructure.

### P2.3 Contract parser

The Application layer owns contract semantics.

Rules:

- require the expected fenced block exactly once;
- require a supported schema version;
- require issue `projectKey` to match the registered project;
- normalize task codes/dependencies through the existing task service;
- reject malformed JSON with a deterministic validation error;
- ignore ordinary prose around the fenced contract.

### P2.4 Idempotent plan import

`bridge_import_plan_issue`:

1. Resolve the registered target project.
2. Fetch the GitHub issue.
3. Parse the plan contract.
4. List existing orchestrator tasks.
5. Skip task codes already present.
6. Import only missing tasks through `ITaskService.CreateBatchAsync`.
7. Return created count + skipped task codes + source issue URL.

This allows the same GitHub issue to be imported repeatedly without duplicating tasks.

### P2.5 Review synchronization

`bridge_sync_reviews`:

1. Fetch issue comments.
2. Parse valid review contracts.
3. Consider only tasks currently `ReadyForReview`.
4. Ignore comments older than the task's latest `UpdatedAtUtc` (prevents an old review from applying after a new implementation iteration).
5. Use the latest applicable review comment per task.
6. Apply it through `IReviewService` using `github:{comment-author}` as actor.
7. Return applied / ignored / invalid counts.

This makes repeated sync calls naturally idempotent without adding a new persistence table in Phase 2.

### P2.6 MCP tools

Add:

- `bridge_import_plan_issue(projectKey, issueNumber)`
- `bridge_sync_reviews(projectKey, issueNumber)`

Both tools are safe for the Codex allow-list because they cannot invent an orchestrator plan/review themselves; they only consume GitHub-authored contracts.

### P2.7 Configuration

```json
{
  "GitHub": {
    "Token": ""
  }
}
```

Prefer environment configuration in real use:

```text
GitHub__Token=<token>
```

Never commit a token.

## Task breakdown

| Task | Module | Deliverable | Dependency |
|---|---|---|---|
| P2-001 | Docs/Contract | Phase 2 contract and workflow | - |
| P2-002 | Application | GitHub bridge abstractions + contracts | P2-001 |
| P2-003 | Infrastructure | GitHub REST client + DI | P2-002 |
| P2-004 | Application | Plan parser/import service | P2-002,P2-003 |
| P2-005 | Application | Review comment sync | P2-004 |
| P2-006 | MCP Host | Bridge MCP tools | P2-004,P2-005 |
| P2-007 | Codex | Add safe bridge tools to allow-list | P2-006 |
| P2-008 | Tests | Parser/service tests + architecture guard | P2-004,P2-005 |
| P2-009 | CI/Audit | Release build + all tests green | P2-008 |

## Non-goals for Phase 2

- GitHub App webhook listener.
- Background polling.
- GitHub OAuth installation flow.
- Automatic PR creation.
- Automatically commenting back to GitHub from the MCP server.
- PostgreSQL migration.
- Multi-tenant authorization.

Those belong in Phase 3 after the bridge POC is proven.

## Definition of done

Phase 2 is done when:

1. ChatGPT can create a valid plan issue on a target repo.
2. Codex can import the issue through MCP and receive the first ready task.
3. Re-importing the same plan does not duplicate tasks.
4. Codex can submit implementation evidence and move a task to `ReadyForReview`.
5. A valid GitHub review contract can move the task to `Done` or `ChangesRequested`.
6. Re-syncing the same comments has no duplicate state transition.
7. `dotnet build -c Release` succeeds with zero errors.
8. All domain, application/bridge, and architecture tests pass in GitHub Actions.
