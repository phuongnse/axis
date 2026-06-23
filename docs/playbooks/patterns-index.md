# Patterns index

> **Navigation**: [← docs/README.md](../README.md) · [← technical patterns](./patterns.md) · [← AGENTS.md](../../AGENTS.md)

Open the owner doc for the surface you are changing. `patterns.md` is a compatibility router, not the implementation-pattern owner.

---

## By task

| You are… | Open |
|----------|------|
| Adding a NuGet package | [Dependency patterns](./dependency-patterns.md#nuget--packaging-rules) |
| Wiring DI / scoped services | [Dependency patterns](./dependency-patterns.md#dependency-injection-pitfalls) |
| Modeling aggregates / business failures | [Domain/Application patterns](./domain-application-patterns.md) |
| Making a command idempotent | [Domain/Application patterns](./domain-application-patterns.md#command-idempotency-pattern) |
| Mapping EF JSONB / owned entities / repositories | [Persistence patterns](./persistence-patterns.md) |
| Working with workspace schema or raw SQL | [Persistence patterns](./persistence-patterns.md#multi-workspace-isolation-pitfalls) |
| Writing or maintaining tests | [Testing playbook](./testing.md) |
| Handling `Task.Run`, cancellation, or async analyzers | [Runtime patterns](./runtime-patterns.md) |
| Adding list/query endpoints or pagination | [API patterns](./api-patterns.md#query--n1-patterns) |
| Defining API DTOs, OpenAPI metadata, or status mapping | [API patterns](./api-patterns.md) |
| Wiring Wolverine handlers/jobs or recurring work | [Wolverine patterns](./wolverine-patterns.md) |
| Syncing data from another module by event | [Cross-module patterns](./cross-module-patterns.md) |
| Adding gRPC/proto/Buf/JWKS behavior | [gRPC patterns](./grpc-patterns.md) |
| Adding traces, metrics, or structured runtime behavior | [Runtime patterns](./runtime-patterns.md#opentelemetry-observability) |
| Running pre-commit hygiene checks | [Code hygiene patterns](./code-hygiene-patterns.md) |
| React / TanStack Query screen | [Frontend playbook](./frontend.md) |
| Design-system tokens, components, or pixel-perfect workflow | [Design system](./design-system.md) |
| Penpot design source or AI design agent | [Design source](./design-source.md) |
| Design-source or visual docs | [Design source](./design-source.md) · [Wireframe kit](./wireframes.md) · [Visual artifact checklist](./visual-artifact-checklist.md) |
| New module / Kafka / proto / domain README index | [Repo layout discovery](./repo-layout-discovery.md) |

---

## Owner docs

| Owner | Scope |
|---|---|
| [domain-application-patterns.md](./domain-application-patterns.md) | Domain behavior, `Result`, aggregate boundaries, application idempotency |
| [dependency-patterns.md](./dependency-patterns.md) | NuGet/CPM and DI pitfalls |
| [persistence-patterns.md](./persistence-patterns.md) | EF Core, repositories, workspace schema, migrations |
| [api-patterns.md](./api-patterns.md) | Minimal API, DTOs, OpenAPI, pagination, HTTP mapping |
| [runtime-patterns.md](./runtime-patterns.md) | Async, cancellation, background work, OpenTelemetry |
| [wolverine-patterns.md](./wolverine-patterns.md) | Wolverine host setup, handlers, jobs, idempotency, logging |
| [cross-module-patterns.md](./cross-module-patterns.md) | Cross-module data sovereignty, Kafka/local read models, violation sweep |
| [grpc-patterns.md](./grpc-patterns.md) | gRPC escape hatch, proto/Buf, grpcurl, JWKS validation |
| [testing.md](./testing.md) | Test naming, isolation, Testcontainers, deterministic API tests |
| [code-hygiene-patterns.md](./code-hygiene-patterns.md) | Pre-commit code hygiene and policy regex constraints |
| [frontend.md](./frontend.md) | SPA architecture, state, forms, components, accessibility |
| [design-system.md](./design-system.md) | Tokens, reusable components, pixel-perfect workflow, visual QA |
| [design-source.md](./design-source.md) | Penpot source-of-truth workflow, MCP usage, source/preview link rules |
| [wireframes.md](./wireframes.md) | Low-fidelity screen intent and legacy wireframe workflow |

---

## Contributing a new pattern

1. Pick the owner doc above; do not add broad guidance to `patterns.md`.
2. Write the principle first, then one Axis-specific example.
3. Add or update a row in this index when the task routing changes.
