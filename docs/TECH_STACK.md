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
| **Entity Framework Core** | 8.x | ORM | Best-in-class ORM for .NET, great migration support |
| **Npgsql** | 8.x | PostgreSQL driver | Official EF Core provider for PostgreSQL |
| **Wolverine** | 2.x | Background jobs + messaging | Modern alternative to Hangfire; handles jobs, scheduling, and module-to-module messaging in one library |
| **OpenIddict** | 5.x | Auth (JWT + OIDC) | Native .NET auth server, no external service dependency |
| **SignalR** | (built-in) | Real-time updates | Workflow execution status pushed to client |
| **FluentValidation** | 11.x | Input validation | Declarative, testable validation |
| **Serilog** | 3.x | Structured logging | JSON logs, easy to ship to any log aggregator |

---

## Frontend

| Technology | Version | Role | Rationale |
|---|---|---|---|
| **React** | 18.x | UI framework | Richest ecosystem for complex builder UIs |
| **TypeScript** | 5.x | Type safety | Catches errors early, essential for a large codebase |
| **Vite** | 5.x | Build tool | Fast dev server and build |
| **TanStack Query** | 5.x | Server state / data fetching | Caching, background refetch, request deduplication |
| **Zustand** | 4.x | Client state management | Lightweight, minimal boilerplate |
| **React Router** | 6.x | Routing | Standard SPA routing |
| **shadcn/ui** | latest | UI components | Unstyled base components, full control over design |
| **Tailwind CSS** | 3.x | Styling | Utility-first, fast iteration |
| **@xyflow/react** (React Flow) | 12.x | Workflow canvas | Best-in-class drag & drop node-based diagram editor |
| **dnd-kit** | 6.x | Page builder drag & drop | Flexible, accessible DnD for UI builder |
| **Zod** | 3.x | Schema validation | Runtime validation for API responses and forms |

---

## Infrastructure & Data

| Technology | Role | Rationale |
|---|---|---|
| **PostgreSQL 16** | Primary database | Schema-per-tenant isolation, JSONB for dynamic fields, strong .NET support |
| **Redis 7** | Cache + distributed lock | Session cache, prevent duplicate job execution |
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

### ADR-004: OpenIddict over External Auth Service
**Decision:** Implement auth server in-process using OpenIddict.
**Reason:** Keeps the stack self-contained, reduces external dependencies and cost. OpenIddict is a well-maintained, standards-compliant OIDC server for .NET.

### ADR-005: React over Vue/Svelte for Frontend
**Decision:** Use React with TypeScript.
**Reason:** The workflow canvas (React Flow) and page builder (dnd-kit) have the best React integrations. React's ecosystem for complex editor-style UIs is unmatched.
