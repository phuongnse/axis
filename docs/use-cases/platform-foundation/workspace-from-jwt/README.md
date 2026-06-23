# Use case — Workspace resolution from JWT

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Resolve the active workspace from the JWT on every request so that downstream code never needs to think about workspace identity.

## Primary actor

- system

## Trigger

- User initiates: resolve the active workspace from the JWT on every request

## Main flow

1. Actor starts the — Workspace resolution from JWT flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Infrastructure-level enforcement ensuring every database query is scoped to the authenticated workspace's schema. No workspace can access another workspace's data — not through the API, not through bugs, not through misconfiguration.

## Acceptance Criteria

*Happy path*
- [ ] JWT contains an `workspace_id` claim set at login time.
- [ ] Middleware reads `workspace_id`, resolves the workspace schema name (from Redis cache or DB), and stores it in the scoped `IWorkspaceContext`.
- [ ] All downstream handlers access workspace info through `IWorkspaceContext`; none read `workspace_id` directly from the JWT.
- [ ] Schema name resolution adds less than 5 ms to request latency (cache hit).

*Validation & errors*
- [ ] A JWT missing the `workspace_id` claim is rejected with HTTP 401.
- [ ] A JWT with an `workspace_id` that maps to no known schema is rejected with HTTP 403 (not 404, to avoid enumeration).
- [ ] A JWT with an `workspace_id` for a deleted or suspended workspace is rejected with HTTP 403.

*Edge cases*
- [ ] Redis cache miss (cold start, cache eviction): falls back to DB lookup, caches the result for 1 hour, and proceeds normally.
- [ ] Schema name is cached immutably — it never changes after provisioning, so cache invalidation is not a concern.
- [ ] Wolverine background jobs carry workspace context as a message header, resolved before the handler executes.

*Out of scope*
- API key authentication (alternative to JWT).

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | N/A |
>
> **Done:**
> - `workspace_id` claim issued at login (`ConnectEndpoints`)
> - handlers use `IWorkspaceContext` / `ICurrentUser` — schema `workspace_{WorkspaceId:N}` derived in `HttpWorkspaceContext` (no separate DB lookup; Redis cache N/A — deterministic derivation satisfies the under-5 ms AC).
> - `WorkspaceAccessMiddleware` returns HTTP 403 for missing, archived/deleted, or not-ready workspaces on workspace module routes (`/api/models`, workflows, forms, etc.).
> - background jobs use `FixedWorkspaceContext` in provision/cancel handlers (see [runtime patterns](../../../playbooks/runtime-patterns.md) and [Wolverine patterns](../../../playbooks/wolverine-patterns.md)).
>
> **Gaps vs spec:** none for backend cross-workspace access prevention.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
