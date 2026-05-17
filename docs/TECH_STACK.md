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
| **Wolverine** | 5.x | Background jobs + messaging | Handles background jobs, scheduling, intra- and inter-module domain event dispatch via durable outbox. Not Hangfire. |
| **OpenIddict** | 5.x | Auth (OAuth2/OIDC) | Standards-compliant OAuth2/OIDC server. Authorization Code + PKCE for the SPA; Client Credentials for external system integrations (e.g. triggering workflows via API). |
| **SignalR** | (built-in) | Real-time updates | Workflow execution status pushed to client |
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
| **Zod** | 3.x | Schema validation | Runtime validation; source of truth for form types via `z.infer`. ⏳ Not yet installed — add when forms are first implemented. |
| **react-hook-form** | 7.x | Form state management | Performant form handling; always paired with Zod via `zodResolver`. ⏳ Not yet installed — add alongside Zod. |
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
