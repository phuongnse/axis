# Architecture

[← Back to Docs Home](./README.md)

---

## System Context

![System Context](./diagrams/system-context.svg)

The Axis platform serves four actor types: **Platform Admins** (Axis team), **Organization Admins**, **Organization Members**, and **End Users**. External systems include an email service for notifications, external APIs called by workflow HTTP steps, and webhook targets that receive workflow events.

---

## Containers

![Container Diagram](./diagrams/container.svg)

| Container | Technology | Responsibility |
|---|---|---|
| **Web Application** | React + TypeScript (Vite) | SPA for all user interactions: workflow builder, form builder, page builder, data management |
| **API Server** | ASP.NET Core 8 | Modular monolith exposing REST API; SignalR hub ⏳ planned (see Real-Time Updates section) |
| **Background Job Runner** | Wolverine (in-process) | Executes scheduled workflows, processes async steps, dispatches domain events |
| **PostgreSQL** | PostgreSQL 16 | Primary data store — schema-per-tenant |
| **Redis** | Redis 7 | Session cache, distributed locks, pub/sub for real-time events |

---

## Modular Monolith Structure

![Module Overview](./diagrams/module-overview.svg)

### Source Tree

```
src/
├── Axis.Api/                    # ASP.NET Core host — all Minimal API endpoints, middleware, DI wiring
│   └── Endpoints/               # One IEndpointRouteBuilder extension per module (e.g. ModelEndpoints.cs)
├── Modules/
│   ├── Identity/
│   │   ├── Axis.Identity.Domain/
│   │   ├── Axis.Identity.Application/
│   │   └── Axis.Identity.Infrastructure/
│   ├── DataModeling/
│   │   ├── Axis.DataModeling.Domain/
│   │   ├── Axis.DataModeling.Application/
│   │   └── Axis.DataModeling.Infrastructure/
│   ├── WorkflowBuilder/
│   │   ├── Axis.WorkflowBuilder.Domain/
│   │   ├── Axis.WorkflowBuilder.Application/
│   │   └── Axis.WorkflowBuilder.Infrastructure/
│   ├── FormBuilder/
│   │   ├── Axis.FormBuilder.Domain/
│   │   ├── Axis.FormBuilder.Application/
│   │   └── Axis.FormBuilder.Infrastructure/
│   ├── WorkflowEngine/
│   │   ├── Axis.WorkflowEngine.Domain/
│   │   ├── Axis.WorkflowEngine.Application/
│   │   └── Axis.WorkflowEngine.Infrastructure/
│   └── PageBuilder/
│       ├── Axis.PageBuilder.Domain/
│       ├── Axis.PageBuilder.Application/
│       └── Axis.PageBuilder.Infrastructure/
└── Shared/
    ├── Axis.Shared.Domain/      # Base entities, value objects, domain events
    ├── Axis.Shared.Application/ # Base handlers, pagination, CQRS abstractions
    └── Axis.Shared.Infrastructure/ # Multi-tenancy, EF Core base, Redis, email
```

> **Note:** There are no per-module `.Api` projects. All HTTP endpoints are Minimal API methods defined in `src/Axis.Api/Endpoints/` and registered in `Program.cs`. Each module contributes one `IEndpointRouteBuilder` extension class.

### Module Layer Convention (per module)

| Layer | Responsibility | Allowed Dependencies |
|---|---|---|
| **Domain** | Entities, value objects, domain events, repository interfaces | Shared.Domain only |
| **Application** | Commands, queries, handlers, DTOs, service interfaces | Domain, Shared.Application |
| **Infrastructure** | EF Core DbContext, repository implementations, external clients | Application, Shared.Infrastructure |
| **Api** | Minimal API endpoint methods (in `Axis.Api/Endpoints/`), OpenAPI annotations | Application |

---

## Multi-Tenancy Strategy

Each organization (tenant) is provisioned with its own **PostgreSQL schema** at sign-up. The `public` schema is reserved for platform-level data (organizations, subscriptions).

```
PostgreSQL
├── public schema
│   ├── organizations
│   ├── subscription_plans
│   └── platform_users
├── tenant_abc schema
│   ├── users
│   ├── roles
│   ├── models
│   ├── workflows
│   ├── executions
│   └── ...
└── tenant_xyz schema
    └── ...
```

**Tenant resolution:** Every API request carries a JWT with an `org_id` claim. `HttpTenantContext` (an `ITenantContext` implementation) resolves the tenant lazily from the claim on first access. `TenantSchemaInterceptor` (an EF Core `DbConnectionInterceptor`) sets `search_path` to the tenant schema when a DB connection opens — no middleware switches the schema context in the pipeline.

---

## Authentication Flow

> Auth is handled by **OpenIddict 5.x** — an in-process OAuth2/OIDC server. See ADR-004 in `docs/TECH_STACK.md`.

### SPA flow (Authorization Code + PKCE)
1. React SPA redirects user to `GET /connect/authorize` with PKCE challenge
2. OpenIddict presents login, user authenticates with email + password
3. OpenIddict issues **Authorization Code** → SPA exchanges it at `POST /connect/token` for **Access Token** (JWT, 15 min) + **Refresh Token**
4. SPA sends `Authorization: Bearer <access_token>` on every API request
5. JWT middleware validates token, extracts `org_id` + `user_id` + `permissions`, injects into `ITenantContext` / `ICurrentUser`
6. SPA silently refreshes via `POST /connect/token` (refresh_token grant) before expiry

### External integration flow (Client Credentials)
1. External system (e.g. third-party tool triggering a workflow) authenticates with its own `client_id` + `client_secret` at `POST /connect/token`
2. OpenIddict issues a scoped **Access Token** — no user context, only granted scopes
3. Token is validated the same way; handler checks for the required scope instead of user permissions

---

## Real-Time Updates (SignalR)

> ⏳ **Not yet implemented.** SignalR is listed in the approved tech stack but no `*Hub.cs` exists in `src/` yet. The architecture below describes the planned design for when this is built.

When a workflow execution changes state (started, step completed, failed, finished), the **WorkflowEngine** will publish a domain event. Wolverine will dispatch it to the SignalR hub, which will push the update to the connected client.

```
WorkflowEngine → domain event → Wolverine → SignalR Hub → Browser
```

---

## Workflow Execution Architecture

![Execution Flow](./epics/E06-workflow-engine/diagrams/execution-flow.svg)

Workflow execution is orchestrated by the **WorkflowEngine** module:

1. A trigger fires (manual API call, cron tick, incoming webhook, or internal event).
2. The engine loads the workflow definition and creates an **Execution** record.
3. Steps are executed in order (or in parallel where configured).
4. Each step type has a dedicated **Step Handler** (Form, HTTP, Condition, Script, Notification).
5. On failure, the engine marks the step as `Failed`, notifies configured channels, and halts (user retries manually).
6. On completion, the execution record is marked `Completed` and a domain event is published.
