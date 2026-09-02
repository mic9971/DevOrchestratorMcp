# Phase 7 — Control Plane

## Objective

Phase 7 adds an operational web interface to DevOrchestratorMcp without creating a second frontend application or build pipeline.

The control plane is intentionally a thin presentation/read-model layer over the existing orchestration domain and operational endpoints:

```text
Browser
  |
  | Auditor API key (sessionStorage only)
  v
/control
  |
  +--> /control/api/*   read-only projections
  |
  `--> /ops/*           existing privileged mutations
             |
             v
      PostgreSQL / domain model
```

No React, Node, npm, Vite, or separate deployment artifact is introduced. The static HTML/CSS/JavaScript is published with the ASP.NET Core image.

## Access model

The HTML shell and static assets are public so the browser can render the login screen. Data remains inaccessible until an Auditor credential is supplied.

Protected endpoints:

```text
/control/api/dashboard
/control/api/projects
/control/api/tasks
/control/api/tasks/{projectKey}/{taskCode}
/control/api/workers
/control/api/webhooks
/control/api/audit
```

When `Security__RequireAuthentication=true`:

- missing or invalid key -> `401`
- Architect / Implementer key -> `403`
- Auditor key -> allowed

The browser stores the Auditor key in `sessionStorage`. It is removed when the tab/session is closed or the user presses **Disconnect**. It is sent only in the `X-DevOrchestrator-Key` header and is never placed in a URL, query string, DOM text, or server-side persistence.

Production deployment must continue to expose the control plane only through HTTPS.

## Screens

### Overview

Shows:

- active/paused project count
- total and completed tasks
- active workers and leases
- attention count from blocked tasks, changes requested, expired leases and webhook retries
- task pipeline state distribution
- recent worker lease summary
- project health summary

### Projects

Shows registry information and task counts by project. Auditor can pause or resume a project through the existing `/ops/projects/{projectKey}/pause|resume` endpoints.

### Tasks

Paginated task read model with optional project/status filters. The list projection does not eager-load task history.

Task drill-down loads a single task and its bounded detail data:

- objective and constraints
- acceptance criteria
- dependencies
- latest evidence
- latest reviews
- latest events
- branch / commit / PR / worker lease state

### Workers

Shows currently claimed tasks, worker IDs, heartbeats, lease expiry and branch ownership. Expired leases can be released through the existing operational endpoint.

### Webhooks

Shows durable GitHub webhook inbox state with pending/retrying/completed filters, attempt count and last error. Retrying/completed deliveries can be explicitly queued again through `/ops/webhooks/{deliveryId}/replay`.

### Audit

Paginated task-event history with project and task-code filters.

## Security headers

Responses below `/control` receive:

- Content-Security-Policy
- `X-Frame-Options: DENY`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`

JavaScript is loaded only from the same origin. There are no third-party scripts, fonts, analytics or CDNs.

## Read-model constraints

Control-plane list endpoints use `AsNoTracking`, direct projection and bounded page sizes. Page size is capped at 100.

The task detail endpoint is intentionally bounded to the newest:

- 20 evidence records
- 20 reviews
- 30 task events

This prevents a UI drill-down from recreating the legacy full-history `task_list` scalability problem.

## Operational mutations

Phase 7 does not create a second mutation implementation. The UI calls the Phase 6 operational endpoints:

```text
POST /ops/tasks/{projectKey}/{taskCode}/expire-lease
POST /ops/projects/{projectKey}/pause
POST /ops/projects/{projectKey}/resume
POST /ops/webhooks/{deliveryId}/replay
```

Therefore role enforcement, task invariants and webhook semantics remain centralized.

## Verification

HTTP integration tests verify:

- `/control/index.html` is served
- CSP header is present
- `/control/api/dashboard` returns `401` without credentials
- Implementer credential returns `403`
- Auditor credential returns `200`

The production proof script also boots the real Docker image and verifies:

```text
/control/index.html                    -> UI asset available
/control/api/dashboard without key     -> 401
/control/api/dashboard with Auditor    -> 200
```

The existing runtime restart and PostgreSQL backup/restore proof remains in the same gate.

## Intentional boundaries

Phase 7 is an internal engineering control plane, not a general user-management product. It intentionally does not add:

- username/password accounts
- OAuth/OIDC interactive login
- multi-tenant RBAC administration
- websocket live streaming
- external frontend hosting
- a second API mutation surface

If the control plane later becomes an internet-facing multi-user product, replace the session-scoped API-key login UX with an identity provider and HttpOnly session/cookie authentication while retaining the same server-side role model.
