# Use case — Automatic workspace scoping on every request

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Every database query to be automatically scoped to the requesting workspace so that data isolation is enforced at the infrastructure level and not left to developer discipline.

## Primary actor

- platform operator

## Trigger

- User initiates: every database query to be automatically scoped to the requesting workspace

## Main flow

1. Actor starts the — Automatic workspace scoping on every request flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Infrastructure-level enforcement ensuring every database query is scoped to the authenticated workspace's schema. No workspace can access another workspace's data — not through the API, not through bugs, not through misconfiguration.

## Acceptance Criteria

*Happy path*
- [ ] Every authenticated API request sets the PostgreSQL `search_path` to the workspace's schema before any query executes.
- [ ] EF Core's `DbContext` is scoped per HTTP request and uses the resolved workspace schema for all queries.
- [ ] Integration tests confirm that records created by Workspace A are not visible to Workspace B under any query path.

*Validation & errors*
- [ ] A request with a valid JWT but an `workspace_id` that references a non-existent or deleted workspace returns HTTP 403.
- [ ] A request that somehow bypasses workspace resolution (e.g., direct DB query without schema set) cannot read another workspace's data because schema isolation prevents it at the DB level.
- [ ] Any query that targets the `public` schema for workspace-scoped data (models, records, workflows) fails with an application-level exception, not silently returning empty results.

*Edge cases*
- [ ] Concurrent requests from two different workspaces do not interfere with each other's `search_path` context (connection-level isolation verified with load tests).
- [ ] A connection returned to the pool has its `search_path` reset to a neutral value before reuse.
- [ ] Background jobs (Wolverine handlers) that operate on behalf of a workspace correctly inject workspace context without an HTTP request present.

*Out of scope*
- Cross-workspace data sharing features.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | ✅ (`IWorkspaceContext`) |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | N/A |
>
> **Done:**
> - `WorkspaceSchemaInterceptor` sets PostgreSQL `search_path` per connection for module `DbContext`s
> - `WorkspaceSchemaInterceptorTests` (two schemas, no cross-read). `HttpWorkspaceContext` on `Axis.Api`
> - `FixedWorkspaceContext` in Wolverine provision handlers.
> - cross-workspace API integration tests (`WorkspaceIsolationEndpointTests` — DataModeling list/get by id and archived-workspace 403)
> - connection-pool safety documented in [patterns.md](../../../playbooks/patterns.md) (`search_path` set on every `ConnectionOpened`, including pooled reconnects).
>
> **Gaps vs spec:** none for backend schema-per-workspace isolation. Workspace-scoped data never lives in `public` by design (module tables only in `workspace_{WorkspaceId:N}`); no separate runtime guard beyond schema isolation.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

