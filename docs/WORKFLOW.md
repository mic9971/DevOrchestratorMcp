# ChatGPT → Codex → ChatGPT workflow

## 1. Architect phase

ChatGPT reads the target repository from GitHub and produces:

- impact analysis;
- implementation design;
- small tasks;
- dependency graph;
- acceptance criteria;
- constraints.

Task size should normally represent one coherent code change that can be implemented and verified independently.

Bad:

```text
Implement the Social module
```

Good:

```text
SOCIAL-001 Create publishing domain contracts
SOCIAL-002 Add TikTok provider
SOCIAL-003 Add publish command/handler
SOCIAL-004 Add API endpoint
SOCIAL-005 Add integration tests
```

Then ChatGPT writes the graph through `task_create_batch`.

## 2. Implementer phase

Codex:

```text
task_get_next
      │
      ▼
task_get
      │
      ▼
read target repo AGENTS.md
      │
      ▼
task_start
      │
      ▼
implement
      │
      ▼
build / test
      │
      ▼
git commit / PR
      │
      ▼
task_add_evidence
      │
      ▼
task_submit_review
      │
      ▼
STOP
```

Codex never calls `review_submit`.

## 3. Audit phase

ChatGPT compares:

```text
Requirement
    +
Acceptance criteria
    +
Task constraints
    +
Git diff / PR
    +
CI / test evidence
```

Outcome:

```text
PASS
  │
  ▼
review_submit(Pass)
  │
  ▼
DONE
  │
  ▼
unlock dependent tasks
```

or:

```text
CHANGES_REQUESTED
        │
        ▼
review_submit(ChangesRequested)
        │
        ▼
CHANGES_REQUESTED
        │
        ▼
Codex task_get_next
        │
        ▼
same task first
```

## Evidence contract

Evidence should be concrete, not prose-only.

Required:

- branch;
- commit SHA;
- changed paths;
- build/test outcomes;
- verification commands.

Recommended:

- PR URL;
- known limitations;
- migration/config changes;
- screenshots or artifact references when the target task requires them.

## Audit contract

A useful review finding contains:

- severity;
- file/symbol;
- violated requirement or rule;
- expected change.

Example:

```text
Medium — src/Media/Application/ImportHandler.cs:
Application directly references Infrastructure concrete provider.
Acceptance criterion requires dependency inversion.
Inject IRemoteVideoProvider instead.
```
