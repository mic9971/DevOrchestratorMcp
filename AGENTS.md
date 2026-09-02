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

1. Domain must not reference Application, Infrastructure, ASP.NET Core, MCP SDK, EF Core, or GitHub HTTP concerns.
2. Application must not reference Infrastructure or MCP SDK.
3. MCP tool classes are adapters only. Business/state transition logic belongs in Domain/Application.
4. Infrastructure implements persistence and external-provider abstractions declared by Application.
5. Common contains only genuinely cross-cutting primitives. No task/review/GitHub bridge business contracts in Common.
6. GitHub integration belongs in Infrastructure behind Application abstractions; GitHub contract semantics belong in Application.
7. Do not add RabbitMQ, Redis, or microservices unless an explicit scaling requirement exists.

## GitHub Bridge invariants

- A Plan Issue must contain exactly one `devorchestrator-plan` fenced block.
- Only schema `devorchestrator.plan.v1` is accepted in Phase 2.
- Plan `projectKey` must match the registered target project.
- Import is idempotent by normalized task code.
- A review comment must use schema `devorchestrator.review.v1`.
- Review sync only applies to tasks currently `ReadyForReview`.
- A review older than the current task submission must never apply.
- Ordinary GitHub comments must not be treated as invalid reviews.
- Codex must not receive direct `review_submit` permission.

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

1. If a GitHub Plan Issue is supplied, call `bridge_import_plan_issue` and `bridge_sync_reviews` first.
2. Read the selected task completely.
3. Do not expand scope.
4. Follow target repository `AGENTS.md`.
5. Run the narrowest relevant build/tests first, then required broader checks.
6. Attach real evidence: branch, commit SHA, changed files, commands, and test results.
7. Call `task_submit_review`.
8. Stop. Do not call reviewer tools.

## Definition of done

A code change is done only when:

- acceptance criteria are met;
- build/tests relevant to the change pass;
- architecture rules still pass;
- evidence points to actual Git state;
- an independent review passes.
