# Phase 8 — Identity & Governance

Phase 8 separates **human identity** from **machine identity** while preserving the existing MCP role boundaries.

## Goals

- replace browser API-key handling with a real human login path
- persist human users and role grants
- keep Codex/automation on machine credentials
- make machine credentials expirable, revocable and rotatable
- attribute privileged operations to a concrete human or machine actor
- retain static role keys only as bootstrap / break-glass credentials

## Identity model

### Human identity

The first human provider is GitHub OAuth 2.0.

```text
Browser
  -> /auth/login
  -> GitHub OAuth authorization
  -> /signin-github
  -> identity_users + identity_user_roles
  -> secure ASP.NET Core cookie
  -> /control
```

The session cookie is:

- `HttpOnly`
- `Secure`
- `SameSite=Strict`
- host-only (`__Host-DevOrchestrator.Session`)
- 8-hour sliding lifetime

Role grants are re-read from PostgreSQL/SQLite when the cookie principal is validated, so disabling a user or changing roles takes effect without minting a new API key.

### Machine identity

MCP and automation never inherit the human browser session.

```text
Codex / automation
  -> X-DevOrchestrator-Key or Bearer credential
  -> static break-glass key OR machine_credentials lookup
  -> Architect / Implementer / Auditor
  -> /mcp or operational endpoint
```

Database-managed machine secrets are returned once. Only a SHA-256 hash and a short display prefix are stored.

A machine credential records:

- name
- role
- SHA-256 key hash
- display prefix
- created time / creator
- expiry
- last-used time
- revocation time

Machine credentials cannot receive the `Admin` human role.

## Human roles

| Role | Control read | Privileged ops | Governance | MCP human access |
|---|---:|---:|---:|---:|
| Admin | yes | yes | yes | no |
| Auditor | yes | yes | no | no |
| Architect | yes | no | no | no |
| Implementer | yes | no | no | no |

`/mcp` always requires a machine credential. A logged-in human session cannot call MCP by virtue of its browser identity.

## Bootstrap administrator

Production configuration may specify an explicit GitHub login:

```text
Identity__BootstrapGitHubLogins__0=mic9971
```

On that login's first successful GitHub authentication, the user receives `Admin` if it is not already present. No other GitHub account receives a role automatically.

After bootstrap, manage users and roles in `/control/governance.html` and keep the bootstrap allow-list narrow.

## GitHub OAuth App setup

Create a GitHub OAuth App with:

```text
Homepage URL:              https://orchestrator.example.com/
Authorization callback:   https://orchestrator.example.com/signin-github
```

Set:

```text
Identity__GitHub__ClientId=<client id>
Identity__GitHub__ClientSecret=<client secret>
Identity__BootstrapGitHubLogins__0=<initial admin login>
```

Human login is intentionally disabled when the client ID/secret are absent. `/auth/login` returns `503 identity.github_not_configured` while break-glass machine access remains available.

Human sign-in is a production HTTPS feature. If TLS terminates at a reverse proxy, ensure ASP.NET Core receives the original HTTPS scheme through trusted forwarded headers / platform integration.

## Governance UI

`/control/governance.html` is Admin-only and exposes:

```text
Users
  - roles
  - enable / disable

Credentials
  - create
  - rotate
  - revoke
  - expiry
  - last used

Security Audit
  - actor
  - actor type
  - action
  - resource
  - reason
  - timestamp
```

New secrets are shown once and must be copied into the caller's secret store.

## Security audit

Privileged mutations now emit `security_audit_events` with identity-aware actors, for example:

```text
github:mic9971
credential:7f8b...
config:auditor
```

Audited operations include:

- human login/logout
- role grants/replacements
- user enable/disable
- machine credential create/rotate/revoke
- project pause/resume
- task lease force-expiry
- webhook replay

Where appropriate, the event also records reason, before/after JSON and source IP.

## Database migration

Phase 8 adds:

```text
202609020004_IdentityGovernance
```

Tables:

```text
identity_users
identity_user_roles
machine_credentials
security_audit_events
```

Normal startup still performs no DDL. Run the explicit migration process before application startup.

## Compatibility boundary

Existing configured role keys remain valid:

```text
Security__ArchitectKey
Security__ImplementerKey
Security__AuditorKey
```

and their `PreviousKey` overlap values remain supported. They are intended for bootstrap and emergency access. Day-to-day machine identities should migrate to database-managed credentials because those can be expired/revoked individually without restarting the service.

## Production acceptance criteria

Phase 8 is complete when CI proves:

- build has zero warnings/errors
- migrations `001 -> 004` apply to PostgreSQL 17
- SQLite local migration path remains current
- dynamic machine Auditor can access operational endpoints
- machine credentials are stored only as hashes
- revoked machine credential receives `401`
- machine Auditor cannot access Admin governance APIs
- static Auditor break-glass remains functional
- governance UI is included in the published Docker image
- GitHub login is cleanly disabled when OAuth configuration is absent
- runtime restart + PostgreSQL backup/restore still pass with four migrations

Live GitHub OAuth login itself requires a deployed HTTPS host and a real OAuth App secret, so that is an external production configuration proof rather than a hermetic PR-CI dependency.
