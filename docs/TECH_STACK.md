# Tech Stack

[← Back to Docs Home](./README.md)

---

## Backend

| Technology | Version | Role | Rationale |
|---|---|---|---|
| **C# / .NET 8** | 8.x LTS | Language & runtime | Mature, high-performance, strong ecosystem |
| **ASP.NET Core** | 8.x | Web API & SignalR | Industry standard for .NET APIs |
| **Modular Monolith + DDD** | — | Architecture pattern | Team velocity + domain clarity, easier to extract microservices later |
| **CQRS via MediatR** | 12.x | Command/Query separation | Clean separation of read/write paths, fits DDD |
| **Entity Framework Core** | 9.x | ORM | Best-in-class ORM for .NET, great migration support |
| **Npgsql** | 9.x | PostgreSQL driver | Official EF Core provider for PostgreSQL |
| **Wolverine** | 5.x | Background jobs + messaging | Handles background jobs, scheduling, intra- and inter-module domain event dispatch. Persistence layout: see [ADR-009](#adr-009-wolverine-durable-inboxoutbox-in-a-dedicated-wolverine-schema). Not Hangfire. |
| **OpenIddict** | 5.x | Auth (OAuth2/OIDC) | Standards-compliant OAuth2/OIDC server. Authorization Code + PKCE for the SPA; Client Credentials for external system integrations (e.g. triggering workflows via API). |
| **SignalR** | (built-in) | Real-time updates | Capability available in ASP.NET Core; no `*Hub.cs` currently registered in `src/`. Status: [PROGRESS.md](./PROGRESS.md), epic acceptance: [E06](./epics/E06-workflow-engine/README.md). |
| **FluentValidation** | 11.x | Input validation | Declarative, testable validation |
| **Serilog** | 3.x | Structured logging | JSON logs, easy to ship to any log aggregator |
| **Swashbuckle.AspNetCore** | 6.9.0 | OpenAPI metadata | `AddSwaggerGen` + `UseSwagger` generates the Swagger JSON document; wired with Bearer auth definition |
| **Scalar.AspNetCore** | 2.x | API reference UI | Modern OpenAPI UI; reads `/swagger/v1/swagger.json`. Enabled in Development and Staging only. |

---

## Frontend

| Technology | Version | Role | Rationale |
|---|---|---|---|
| **React** | 18.x | UI framework | Richest ecosystem for complex builder UIs |
| **TypeScript** | 6.x | Type safety | Catches errors early, essential for a large codebase. `strict: true` + `noUnusedLocals/Parameters` enforced. |
| **Vite** | 6.x | Build tool | Fast dev server and build |
| **@vitejs/plugin-react** | 5.x | React Vite plugin | Babel-based React transform; v5 supports vite 4–7 |
| **TanStack Query** | 5.x | Server state / data fetching | Caching, background refetch, request deduplication |
| **TanStack Router** | 1.x | Routing | Type-safe routing with search param management and built-in prefetching |
| **Zustand** | 5.x | Client state management | Lightweight, minimal boilerplate |
| **Fetch API** | (native) | HTTP client | Native browser API for network requests; custom wrapper (`fetchApi`) for auth, timeout, and error normalisation |
| **shadcn/ui + @base-ui/react** | latest | UI components | shadcn/ui for pre-built patterns; @base-ui/react for lower-level unstyled primitives |
| **Tailwind CSS** | 3.x | Styling | Utility-first, fast iteration |
| **@xyflow/react** (React Flow) | 12.x | Workflow canvas | Best-in-class drag & drop node-based diagram editor. ⏳ Not yet installed — add when workflow canvas is first implemented. |
| **dnd-kit** | 6.x | Page builder drag & drop | Flexible, accessible DnD for UI builder. ⏳ Not yet installed — add when page builder is first implemented. |
| **Zod** | 3.x | Schema validation | Runtime validation; source of truth for form types via `z.infer`. |
| **react-hook-form** | 7.x | Form state management | Performant form handling; always paired with Zod via `@hookform/resolvers/zod`. |
| **@hookform/resolvers** | 3.x | Form validation bridge | Connects Zod schemas to react-hook-form via `zodResolver`. |
| **Biome** | 2.x | Linter + formatter | Replaces ESLint + Prettier. Single tool for linting, formatting, and import sorting. See ADR-008. |
| **Vitest** | 3.x | Frontend test runner | Fast Vite-native test runner; v3 deduplicates cleanly with vite 6 (v4 installs a nested vite 8, breaking `npm ci`). |
| **@testing-library/react** | 16.x | Component testing | Behaviour-driven component tests; always paired with Vitest |

---

## Infrastructure & Data

| Technology | Role | Rationale |
|---|---|---|
| **PostgreSQL 16** | Primary database | Schema-per-tenant isolation, JSONB for dynamic fields, strong .NET support |
| **Redis 7** | Cache + distributed lock | Session cache, prevent duplicate job execution |
| **AWS S3** | File storage | Stores uploaded files (attachments, exports). Accessed via AWSSDK.S3 + AWSSDK.Extensions.NETCore.Setup. |
| **Docker / Docker Compose** | Local development | Reproducible dev environment |

---

## Architecture Decisions

### ADR-001: Modular Monolith over Microservices
**Decision:** Start as a modular monolith.
**Reason:** Microservices add operational overhead that is not justified at the start. The modular structure allows splitting into services later without rewriting domain logic.

### ADR-002: Schema-per-Tenant
**Decision:** Each organization gets its own PostgreSQL schema.
**Reason:** Strong data isolation, no risk of data leakage between tenants. Simplifies backups and restores per tenant. Performance is acceptable at target scale.

### ADR-003: Wolverine over Hangfire
**Decision:** Use Wolverine for background jobs and intra-module messaging.
**Reason:** Wolverine unifies job processing and domain event dispatching. It integrates naturally with DDD patterns and eliminates the need for a separate message bus for internal communication.

### ADR-004: OpenIddict as OAuth2/OIDC Server
**Decision:** Use OpenIddict 5.x as the in-process OAuth2/OIDC authorization server.
**Reason:** Axis will support external systems triggering workflows via API — this requires a standards-compliant OAuth2 Client Credentials flow so third-party tools can authenticate without user interaction. OpenIddict also enables Authorization Code + PKCE for the SPA (more secure than custom JWT for browser clients) and positions Axis to support enterprise SSO in the future. Keeping the auth server in-process avoids external service dependencies.

### ADR-005: React over Vue/Svelte for Frontend
**Decision:** Use React with TypeScript.
**Reason:** The workflow canvas (React Flow) and page builder (dnd-kit) have the best React integrations. React's ecosystem for complex editor-style UIs is unmatched.

### ADR-006: TanStack Router for SPA Routing
**Decision:** Use TanStack Router instead of React Router.
**Reason:** Provides 100% type-safety for paths and search parameters (query string). Its built-in loader mechanism integrates perfectly with TanStack Query for prefetching data, preventing waterfall rendering issues typical in complex dashboards.

### ADR-007: Native Fetch API over Axios
**Decision:** Use a lightweight wrapper around the native `fetch` API instead of Axios.
**Reason:** Reduces external dependencies since `fetch` is built-in. Modern TanStack Query handles the heavy lifting (caching, deduplication, retry) making a heavy HTTP client like Axios unnecessary. The wrapper ensures proper JSON parsing, cookie inclusion, and error throwing for non-2xx responses.

### ADR-008: Biome over ESLint + Prettier
**Decision:** Use Biome as the single tool for linting, formatting, and import sorting in `frontend/`.
**Reason:** Biome replaces ESLint + Prettier with one Rust-based tool that is significantly faster and has zero configuration conflicts between formatter and linter. The only meaningful trade-off is losing `eslint-plugin-react-refresh` (Vite HMR dev hint, not a correctness check). Tailwind `@tailwind` at-rules are handled via `css.parser.tailwindDirectives: true` and a targeted `overrides` rule suppression — not by disabling CSS checking entirely.

### ADR-009: Wolverine durable inbox/outbox in a dedicated `wolverine` schema

**Decision:** Persist Wolverine inbox/outbox envelopes in PostgreSQL via `WolverineFx.Postgresql` and `WolverineFx.EntityFrameworkCore`, in a **dedicated `wolverine` schema** in the primary application database. Every module DbContext enlists outgoing envelopes in its EF transaction via `.IntegrateWithWolverine()`. Tenant identity rides on `Envelope.TenantId` so handlers running outside an HTTP context can resolve tenant state.

**Reason:**

- **Why a dedicated schema, not `public` and not per-tenant `tenant_{orgId}`:** Wolverine creates ~12 infrastructure tables (`outgoing_envelopes`, `incoming_envelopes`, `dead_letters`, `scheduled`, …). Placing them in `public` mixes messaging infrastructure with Identity tables (per [ADR-002](#adr-002-schema-per-tenant) `public` is reserved for Identity); placing them per-tenant multiplies the table set by every organization and complicates Wolverine's own migrations. A dedicated `wolverine` schema is the only option that keeps migrations centralised and tenant schemas free of cross-cutting infra.

- **Why same database (not a separate one):** Outbox guarantees only hold when the envelope write and the aggregate write share a single transaction. A second database breaks that guarantee. The dedicated schema gives logical separation without sacrificing transactional consistency.

- **Why `IntegrateWithWolverine()` per DbContext, not a global interceptor:** Each module owns its own DbContext (`IdentityDbContext`, `DataModelingDbContext`, `WorkflowBuilderDbContext`, `FormBuilderDbContext`, `WorkflowEngineDbContext`, `PageBuilderDbContext`). The integration extension hooks Wolverine into the EF `SaveChanges` pipeline so domain events queued during the request commit atomically with the aggregate. The current `Axis.Shared.Infrastructure.Persistence.UnitOfWork` pattern — collect events → `SaveChangesAsync` → `bus.PublishAsync` — loses durability if the process crashes between save and publish; outbox closes that window.

- **Why `Envelope.TenantId` for tenant propagation:** Handlers may run on background workers without an `HttpContext`, so `HttpTenantContext` is not available. Wolverine 5 has first-class tenancy support: outbound middleware reads `ITenantContext.OrganizationId` and stamps the envelope; the dispatched handler restores it into a scoped `ITenantContext` before execution. Tenant schema (`tenant_{orgId:N}`) is then resolved through the existing Redis-cached mapping.

- **Multi-tenant Postgres gotcha — `search_path`:** `HttpTenantContext` sets `search_path` per request so module queries resolve to the tenant schema. Wolverine must write to fully-qualified tables (`wolverine.outgoing_envelopes`) regardless of `search_path`. WolverineFx.Postgresql does this by default when the schema is passed to `PersistMessagesWithPostgresql(connStr, "wolverine")` — verify during wiring, do not rely on unqualified names.

- **Inbox falls out for free:** Once `PersistMessagesWithPostgresql` is configured, Wolverine durably tracks incoming envelope IDs in the same schema and rejects duplicates. This becomes necessary the first time we add a non-idempotent message handler with retry/at-least-once delivery (e.g. notification dispatch, tenant provisioning retry).

- **Schema migration strategy:** Development uses `services.AddResourceSetupOnStartup()` so Wolverine creates and updates its own tables on app start. Production scripts the schema as a separate SQL migration in the CI pipeline so Wolverine table changes ship independently of EF migrations and can be rolled back in isolation. The two paths produce identical schemas — `AddResourceSetupOnStartup()` is a developer ergonomics convenience, not a production deployment mechanism.

**Anti-patterns rejected:**

- Wolverine tables in tenant schemas (× tenant count, no centralised migration).
- Wolverine in a separate database (breaks the transactional outbox guarantee).
- Marten as a Wolverine companion (project is EF Core throughout; pivoting modules to Marten would invalidate every aggregate mapping).
- Keeping the in-memory `bus.PublishAsync` after `SaveChangesAsync` (current behaviour) — works in tests but loses every event if the process crashes between commit and publish.
