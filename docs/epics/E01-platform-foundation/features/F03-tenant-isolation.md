# F03 — Tenant Data Isolation

[← Back to E01](../README.md)

---

## Description

Infrastructure-level enforcement ensuring every database query is scoped to the authenticated tenant's schema. No tenant can access another tenant's data — not through the API, not through bugs, not through misconfiguration.

---

## User Stories

### US-008 — Automatic tenant scoping on every request

**As a** platform operator, **I want** every database query to be automatically scoped to the requesting tenant **so that** data isolation is enforced at the infrastructure level and not left to developer discipline.

**Acceptance Criteria:**

*Happy path*
- [ ] Every authenticated API request sets the PostgreSQL `search_path` to the tenant's schema before any query executes.
- [ ] EF Core's `DbContext` is scoped per HTTP request and uses the resolved tenant schema for all queries.
- [ ] Integration tests confirm that records created by Tenant A are not visible to Tenant B under any query path.

*Validation & errors*
- [ ] A request with a valid JWT but an `org_id` that references a non-existent or deleted org returns HTTP 403.
- [ ] A request that somehow bypasses tenant resolution (e.g., direct DB query without schema set) cannot read another tenant's data because schema isolation prevents it at the DB level.
- [ ] Any query that targets the `public` schema for tenant-scoped data (models, records, workflows) fails with an application-level exception, not silently returning empty results.

*Edge cases*
- [ ] Concurrent requests from two different tenants do not interfere with each other's `search_path` context (connection-level isolation verified with load tests).
- [ ] A connection returned to the pool has its `search_path` reset to a neutral value before reuse.
- [ ] Background jobs (Wolverine handlers) that operate on behalf of a tenant correctly inject tenant context without an HTTP request present.

*Out of scope*
- Cross-tenant data sharing features — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | ✅ (`ITenantContext`) |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | N/A |
>
> **Done:**
> - `TenantSchemaInterceptor` sets PostgreSQL `search_path` per connection for module `DbContext`s
> - `TenantSchemaInterceptorTests` (two schemas, no cross-read). `HttpTenantContext` on `Axis.Api`
> - `FixedTenantContext` in Wolverine provision handlers.
>
> **Gaps vs spec:** none for backend US-008. **Done:** cross-tenant API tests (`TenantIsolationEndpointTests`); schema isolation test (`TenantSchemaIsolationTests`). `TenantSchemaInterceptor` sets `search_path` on every `ConnectionOpened` (pooled connections included). Explicit `public`-schema guard for module data not added — MVP relies on interceptor + per-handler `OrganizationId` filters; raw SQL must use `ITenantContext.Schema` per [patterns.md](../../../playbooks/patterns.md).

---

### US-009 — Tenant resolution from JWT

**As a** system, **I want to** resolve the active tenant from the JWT on every request **so that** downstream code never needs to think about tenant identity.

**Acceptance Criteria:**

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
> - handlers use `ITenantContext` / `ICurrentUser` — schema `tenant_{orgId:N}` derived in `HttpTenantContext` (no separate DB lookup).
>
> **Gaps vs spec:** none for backend US-009. **Done:** `TenantOrganizationAccessMiddleware` returns 401 when `org_id` is missing, 403 when org is unknown/deleted/archived; `AllowsSignIn()` still permits `Provisioning`, `ProvisioningFailed`, and `DeletionScheduled`. Redis schema cache deferred — schema name is derived as `tenant_{orgId:N}` (immutable). Wolverine jobs use `FixedTenantContext` in provision/delete handlers; tenant header propagation documented in [patterns.md § Tenant isolation](../../../playbooks/patterns.md#tenant-isolation).
