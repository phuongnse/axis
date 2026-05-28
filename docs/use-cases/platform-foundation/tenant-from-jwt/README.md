# Use case — Tenant resolution from JWT

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Resolve the active tenant from the JWT on every request so that downstream code never needs to think about tenant identity.

## Primary actor

- system

## Trigger

- User initiates: resolve the active tenant from the JWT on every request

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Infrastructure-level enforcement ensuring every database query is scoped to the authenticated tenant's schema. No tenant can access another tenant's data — not through the API, not through bugs, not through misconfiguration.

## Acceptance Criteria

*Happy path*
- [ ] JWT contains an `org_id` claim set at login time.
- [ ] Middleware reads `org_id`, resolves the tenant schema name (from Redis cache or DB), and stores it in the scoped `ITenantContext`.
- [ ] All downstream handlers access tenant info through `ITenantContext`; none read `org_id` directly from the JWT.
- [ ] Schema name resolution adds less than 5 ms to request latency (cache hit).

*Validation & errors*
- [ ] A JWT missing the `org_id` claim is rejected with HTTP 401.
- [ ] A JWT with an `org_id` that maps to no known schema is rejected with HTTP 403 (not 404, to avoid enumeration).
- [ ] A JWT with an `org_id` for a deleted or suspended org is rejected with HTTP 403.

*Edge cases*
- [ ] Redis cache miss (cold start, cache eviction): falls back to DB lookup, caches the result for 1 hour, and proceeds normally.
- [ ] Schema name is cached immutably — it never changes after provisioning, so cache invalidation is not a concern.
- [ ] Wolverine background jobs carry tenant context as a message header, resolved before the handler executes.

*Out of scope*
- API key authentication (alternative to JWT) — not in MVP.

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
> - `org_id` claim issued at login (`ConnectEndpoints`)
> - handlers use `ITenantContext` / `ICurrentUser` — schema `tenant_{orgId:N}` derived in `HttpTenantContext` (no separate DB lookup; Redis cache N/A — deterministic derivation satisfies the under-5 ms AC).
> - `TenantOrganizationAccessMiddleware` returns HTTP 403 for missing, archived/deleted, or not-ready orgs on tenant module routes (`/api/models`, workflows, forms, etc.).
> - background jobs use `FixedTenantContext` in provision/cancel handlers (see [patterns.md](../../playbooks/patterns.md)).
>
> **Gaps vs spec:** none for backend cross-tenant access prevention.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-org | [source](../wireframes/settings-org.excalidraw) | [preview](../wireframes/settings-org.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
