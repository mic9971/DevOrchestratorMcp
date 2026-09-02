# Architecture

## Goal

Provide a shared, durable contract between ChatGPT planning/auditing and Codex implementation without relying on shared conversation memory.

```text
                   ┌─────────────────────┐
                   │    ChatGPT Web      │
                   │ Architect / Auditor │
                   └──────────┬──────────┘
                              │
                              │ MCP / task contract
                              ▼
                   ┌─────────────────────┐
                   │ DevOrchestratorMcp  │
                   │ task state/evidence │
                   └──────────┬──────────┘
                              │
                              │ task_get_next
                              ▼
                   ┌─────────────────────┐
                   │       Codex         │
                   │    Implementer      │
                   └──────────┬──────────┘
                              │
                              │ code / commit / PR
                              ▼
                   ┌─────────────────────┐
                   │       GitHub        │
                   │ source of truth     │
                   └─────────────────────┘
```

## Why MCP stores task state instead of code context

The target Git repository already stores the code. Copying entire repo content into an MCP database creates stale duplicated state.

The MCP stores only:

- target repository identity;
- task specification;
- acceptance criteria;
- dependency graph;
- workflow status;
- implementation evidence;
- review result;
- audit event history.

## Layer responsibilities

### Common

Cross-cutting primitives only:

- `Result<T>`
- `Error`
- `Guard`
- `IClock`

### Domain

Workflow invariants:

- project identity;
- task aggregate;
- task state transitions;
- acceptance criteria;
- dependencies;
- evidence;
- reviews;
- task audit events.

### Application

Use cases and orchestration:

- register target projects;
- create task/task graph;
- resolve dependencies;
- get next implementable task;
- attach evidence;
- submit for review;
- audit/review;
- unlock dependent tasks.

### Infrastructure

- SQLite persistence;
- EF Core mapping;
- repository implementations;
- database initialization.

Future GitHub API/webhook integration also belongs here.

### MCP Server

Thin transport adapter:

- HTTP MCP endpoint;
- MCP tool metadata/schema;
- dependency injection;
- health endpoint.

## Why stateless MCP transport

Application state lives in the database, not in an MCP session. Stateless HTTP:

- is simpler;
- survives client reconnects;
- scales horizontally later;
- does not require session affinity;
- matches the task-control-plane use case.

## Production evolution

POC:

```text
ASP.NET Core MCP
       │
       ▼
    SQLite
```

Production:

```text
        HTTPS / Auth
             │
             ▼
      ASP.NET Core MCP
             │
       ┌─────┴─────┐
       ▼           ▼
 PostgreSQL     GitHub API
                    │
                    ▼
                Webhooks
```

Redis/RabbitMQ are intentionally excluded from v1. Add them only when there is a measured need for distributed caching, asynchronous webhook processing, or high event throughput.
