# AGENTS.md — DevOrchestratorMcp

## Purpose

This repository implements a deterministic MCP task-control plane for AI-assisted software development.

It does not implement product-specific business logic and it does not directly edit target repositories.

## Architecture rules

Dependency direction:

```text
DevOrchestrator.McpServer
          │
          ▼
DevOrchestrator.Application
          │
          ▼
DevOrchestrator.Domain
          │
          ▼
DevOrchestrator.Common

DevOrchestrator.Infrastructure
          │
          ├──► Application abstractions
          ├──► Domain
          └──► Common
```

Rules:

1. Domain must not reference Application, Infrastructure, ASP.NET Core, MCP SDK, or EF Core.
2. Application must not reference Infrastructure or MCP SDK.
3. MCP tool classes are adapters only. Business/state transition logic belongs in Domain/Application.
4. Infrastructure implements persistence abstractions declared by Application.
5. Common contains only genuinely cross-cutting primitives. No task/review business entities in Common.
6. Git provider integration, if added later, belongs in Infrastructure behind Application abstractions.
7. Do not add RabbitMQ, Redis, or microservices unless an explicit scaling requirement exists.

## Task-state invariants

- New task starts `Draft`.
- It becomes `Ready` only when all dependencies are `Done`.
- `Ready` or `ChangesRequested` may become `InProgress`.
- Evidence may be added only while `InProgress`.
- A task cannot become `ReadyForReview` without evidence.
- Only the reviewer flow can produce `Done`.
- `ChangesRequested` is returned by `task_get_next` before new `Ready` work.
- A passing review unlocks dependent tasks whose dependency set is fully `Done`.

## Codex behavior

When implementing this repository or when this MCP is used by Codex on a target repository:

1. Read the task completely.
2. Do not expand scope.
3. Follow target repository `AGENTS.md`.
4. Run the narrowest relevant build/tests first, then required broader checks.
5. Attach real evidence: branch, commit SHA, changed files, commands, and test results.
6. Call `task_submit_review`.
7. Stop. Do not call reviewer tools.

## Definition of done

A code change is done only when:

- acceptance criteria are met;
- build/tests relevant to the change pass;
- architecture rules still pass;
- evidence points to actual Git state;
- an independent review passes.
