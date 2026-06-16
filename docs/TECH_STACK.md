# Tech Stack

> **Navigation**: [← docs/README.md](./README.md) · [← AGENTS.md](../AGENTS.md)

---

## Backend

| Technology | Version | Role | Rationale |
|---|---|---|---|
| **C# / .NET 8** | 8.x LTS | Language & runtime | Mature, high-performance, strong ecosystem |
| **ASP.NET Core** | 8.x | Web API & SignalR | Industry standard for .NET APIs |
| **Modulith with strict service boundaries** | — | Architecture pattern | Per-module DB + broker + gRPC + JWT from day 1; extraction = redeploy. See [ADR-010](#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy). |
| **CQRS via MediatR** | 12.x | Intra-module command/query separation | Within a module only; cross-module communication uses Kafka + gRPC, never MediatR. |
| **Entity Framework Core** | 9.x | ORM (per module) | Each module owns a DbContext + database. Migrations mandatory ([ADR-023](#adr-023-per-module-ef-core-migrations-only)). |
| **Npgsql** | 9.x | PostgreSQL driver | Per-module database; per-module Wolverine schema ([ADR-011](#adr-011-per-module-database-with-schema-per-tenant-inside), [ADR-012](#adr-012-per-module-wolverine-schema-in-the-modules-own-database)). |
| **Wolverine** | 5.x | In-module orchestration + outbox + saga runtime | Handlers, scheduled jobs, durable outbox (per-module schema), saga state. Cross-module transports are Kafka + RabbitMQ ([ADR-013](#adr-013-apache-kafka-for-cross-module-domain-events-and-event-sourced-aggregates), [ADR-024](#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration)). |
| **Apache Kafka** | 3.x | Cross-module event transport + event-sourced aggregate log | Durable log + replay for events; Confluent Schema Registry for Avro payloads ([ADR-013](#adr-013-apache-kafka-for-cross-module-domain-events-and-event-sourced-aggregates), [ADR-019](#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). Routing rule per [ADR-025](#adr-025-transport-selection-rule-by-message-name-suffix). |
| **WolverineFx.Kafka** | 5.x | Kafka transport for Wolverine | Bridges Wolverine envelopes to/from Kafka topics. |
| **RabbitMQ** | 3.x | Cross-module command + job + saga transport | Work-queue semantics (ACK, requeue, DLX, prefetch); Wolverine saga orchestration ([ADR-024](#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration)). Routing rule per [ADR-025](#adr-025-transport-selection-rule-by-message-name-suffix). |
| **WolverineFx.RabbitMq** | 5.x | RabbitMQ transport for Wolverine | Bridges Wolverine envelopes to/from RabbitMQ exchanges/queues. |
| **gRPC + Protobuf** | — | Cross-module sync RPC | Internal-only; external API stays REST. Contracts in `Axis.{Module}.Contracts/*.proto` ([ADR-014](#adr-014-grpc-for-internal-sync-rpc-and-rest-openapi-for-external-api)). |
| **CloudEvents 1.0 + Avro** | — | Event envelope + payload format | Routing/correlation metadata in CloudEvents envelope; payload in Avro with Confluent Schema Registry ([ADR-019](#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). |
| **Confluent Schema Registry** | — | Event-schema evolution | Enforces backward-compatible event changes; rejects breaking changes on publish. |
| **OpenIddict** | 5.x | Identity-module OAuth2/OIDC server | Identity issues JWTs; other modules validate via JWKS public-key endpoint ([ADR-015](#adr-015-identity-is-a-remote-dependency-from-day-1)). No cross-module DB lookup of Identity tables. |
| **OpenTelemetry SDK** | 1.x | Tracing + metrics + structured logs | Vendor-neutral; trace IDs propagated through Wolverine + gRPC interceptors ([ADR-018](#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability)). |
| **Grafana Tempo / Loki / Mimir** | latest | Observability backend | Tempo for traces, Loki for logs, Mimir for metrics; Grafana UI on top. |
| **HashiCorp Vault** | 1.x | Secrets management (production) | Per-module policies; Vault Agent sidecar for fetch + rotate ([ADR-022](#adr-022-secrets-management-via-hashicorp-vault-in-production)). |
| **SignalR** | (built-in) | Real-time updates | Capability available in ASP.NET Core; no `*Hub.cs` currently registered in `src/`. Status: [PROGRESS.md](./PROGRESS.md), domain acceptance: [workflow-engine](./use-cases/workflow-engine/README.md). |
| **FluentValidation** | 11.x | Input validation | Declarative, testable validation |
| **Serilog** | 3.x | Structured logging | JSON logs, easy to ship to any log aggregator |
| **Swashbuckle.AspNetCore** | 6.9.0 | OpenAPI metadata | `AddSwaggerGen` + `UseSwagger` generates the Swagger JSON document; wired with Bearer auth definition |
| **Scalar.AspNetCore** | 2.x | API reference UI | Modern OpenAPI UI; reads `/swagger/v1/swagger.json`. Enabled in Development and Staging only. |

---

## Frontend

| Technology | Version | Role | Rationale |
|---|---|---|---|
| **React** | 19.x | UI framework | Richest ecosystem for complex builder UIs. Bumped from 18 → 19; `@vitejs/plugin-react@5` supports React 19 + Vite 6, so the rest of the toolchain stays put. |
| **TypeScript** | 6.x | Type safety | Catches errors early, essential for a large codebase. `strict: true` + `noUnusedLocals/Parameters` enforced. |
| **Vite** | 6.x | Build tool | Fast dev server and build |
| **@vitejs/plugin-react** | 5.x | React Vite plugin | Babel-based React transform; v5 supports vite 4–7 |
| **TanStack Query** | 5.x | Server state / data fetching | Caching, background refetch, request deduplication |
| **TanStack Router** | 1.x | Routing | Type-safe routing with search param management and built-in prefetching |
| **Zustand** | 5.x | Client state management | Lightweight, minimal boilerplate |
| **i18next + react-i18next** | i18next 26.x / react-i18next 17.x | Localization | Key-based EN/VI resources with English fallback; integrated with React hooks for immediate language switching |
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
| **PostgreSQL 16** | Per-module databases (`axis_identity`, `axis_datamodeling`, …) | One database per module; schema-per-tenant inside each ([ADR-011](#adr-011-per-module-database-with-schema-per-tenant-inside)). JSONB for dynamic fields. |
| **Apache Kafka 3.x** | Cross-module event broker + event-sourced aggregate log | KRaft mode (no ZooKeeper). Topics partitioned by `teamAccountId`. ([ADR-013](#adr-013-apache-kafka-for-cross-module-domain-events-and-event-sourced-aggregates)) |
| **RabbitMQ 3.x** | Cross-module command + job + saga broker | Single-node in dev; classic queues with DLX. Management UI on `:15672`. ([ADR-024](#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration)) |
| **Confluent Schema Registry** | Event-schema evolution | Avro schemas with backward-compatibility rules. |
| **Redis 7** | Cache + distributed lock | Session cache, prevent duplicate job execution. Per-module key prefixes. |
| **AWS S3** | File storage | Stores uploaded files (attachments, exports). Accessed via AWSSDK.S3 + AWSSDK.Extensions.NETCore.Setup. |
| **HashiCorp Vault 1.x** | Secrets management (production) | Per-module policies + Vault Agent sidecar ([ADR-022](#adr-022-secrets-management-via-hashicorp-vault-in-production)). |
| **Grafana Tempo / Loki / Mimir** | Observability backend (production) | Traces / logs / long-term metrics. Prometheus scrapes module `/metrics`. |
| **Docker / Docker Compose** | Local development | All modules + Kafka + Schema Registry + Postgres + Redis + Grafana stack as containers. |
| **Kubernetes** | Production deployment target | Per-module Deployment + Service; service DNS for in-cluster discovery ([ADR-016](#adr-016-service-discovery-via-config-in-modulith-mode-and-k8s-dns-in-production)). |

---

## Architecture Decisions

### ADR-001: Modular Monolith over Microservices

**Status:** Superseded by [ADR-010](#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy).

**Original decision:** Start as a modular monolith.
**Original reason:** Microservices add operational overhead that is not justified at the start. The modular structure allows splitting into services later without rewriting domain logic.

**Why superseded:** "Extract later" turned out to mean "refactor later" — single shared DB, in-process Wolverine, shared kernel implementation, in-process auth lookups all create extraction debt. ADR-010 commits to a stronger contract: each module is a service from day 1; deployment is the only thing that changes when extracting.

### ADR-002: Schema-per-Tenant

**Status:** Superseded by [ADR-011](#adr-011-per-module-database-with-schema-per-tenant-inside).

**Original decision:** Each team account gets its own PostgreSQL schema.
**Original reason:** Strong data isolation, no risk of data leakage between tenants. Simplifies backups and restores per tenant. Performance is acceptable at target scale.

**Why superseded:** The original ADR placed all tenant schemas in a single shared database, which couples module data lifecycles together and prevents clean extraction. ADR-011 keeps schema-per-tenant but moves each module to its own database.

### ADR-003: Wolverine over Hangfire
**Decision:** Use Wolverine for background jobs and intra-module messaging.
**Reason:** Wolverine unifies job processing and domain event dispatching. It integrates naturally with DDD patterns and eliminates the need for a separate message bus for internal communication.

### ADR-004: OpenIddict as OAuth2/OIDC Server
**Decision:** Use OpenIddict 5.x as the in-process OAuth2/OIDC authorization server.
**Reason:** Axis will support external systems triggering workflows via API — this requires a standards-compliant OAuth2 Client Credentials flow so third-party tools can authenticate without user interaction. OpenIddict also enables Authorization Code + PKCE for the SPA (more secure than custom JWT for browser clients) and hosts external identity providers for interactive user sign-in (see [ADR-027](#adr-027-external-identity-providers-for-user-sign-in-and-registration)). Keeping the auth server in-process avoids external service dependencies.

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

**Status:** Superseded by [ADR-012](#adr-012-per-module-wolverine-schema-in-the-modules-own-database).

**Original decision:** Persist Wolverine inbox/outbox envelopes in a single dedicated `wolverine` schema in the primary application database; each module DbContext enlists outgoing envelopes via `.IntegrateWithWolverine()`.

**Why superseded:** A single shared `wolverine` schema in a shared database makes it impossible to extract a single module without dragging its envelope tables (and their cross-module envelopes) along. ADR-012 moves to per-module Wolverine schemas, each living in the same database as the module it serves, so a module's outbox extracts cleanly with the module.

---

### ADR-010: Modulith with strict service boundaries so extraction is a redeploy

**Decision:** Treat every module as a future service from day 1. The application boots and ships as a single deployable today (the "modulith" packaging), but every cross-module interaction must use the same contract that would be used between independently-deployed services — broker-mediated events, gRPC for sync RPC, JWT for auth, config-driven service URLs. When a module is extracted, only the deployment topology changes; no domain or application code is rewritten.

**Reason:**

- **Optionality is only real if it survives discipline drift.** The original [ADR-001](#adr-001-modular-monolith-over-microservices) said "extract later without rewriting domain logic." In practice, four kinds of debt accumulate silently and make extraction non-trivial: (a) shared DB transactions across modules, (b) in-process method calls dressed as `IServiceXyz` interfaces, (c) shared kernel that grows implementation (not just abstractions), (d) auth lookups via direct DbContext access. ADR-010 closes all four by making the contract identical in both deployment modes.

- **The honest extraction cost is paid up front.** Every "we'll fix this when we extract" decision is a debt with compound interest. Paying up front means: heavier infrastructure during development (broker, OpenTelemetry, gRPC stubs) but flat cost when extraction actually happens.

- **Loss of intra-process transactional consistency is accepted as a feature.** Cross-module workflows are eventually consistent and use Saga orchestration ([ADR-020](#adr-020-saga-orchestration-for-cross-module-workflows)). This forces the same failure modes during dev as in production, surfacing race conditions and partial-failure paths early instead of at extraction time.

**What this commits us to:**

| Concern | Modulith mode | Extracted mode | Same code? |
|---|---|---|---|
| Cross-module events | Kafka topic | Kafka topic | Yes |
| Cross-module sync queries | gRPC over loopback | gRPC over network | Yes |
| Auth | JWT (Identity issues; modules validate via public key) | JWT | Yes |
| Service location | Config-driven URL (`http://localhost:5001`) | K8s service DNS | Yes (config differs) |
| Domain handler code | unchanged | unchanged | Yes |

**Anti-patterns rejected:**

- Direct C# method calls or `services.GetRequiredService<IFooFromAnotherModule>()` for cross-module interaction.
- Shared `DbContext`, shared transactions, shared aggregate roots across modules.
- "We'll add the broker later" — the broker is part of the Phase 1 foundation.
- "Tests can skip the network hops" — tests run with the same broker (Testcontainers) so failure modes match production.

### ADR-011: Per-module database with schema-per-tenant inside

**Decision:** Each module owns its own PostgreSQL database (e.g. `axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, …). Tenant isolation within a module is **schema-per-tenant** (`tenant_{teamAccountId:N}`) inside that module's database. Identity remains the only module that operates entirely in `public` (it has no tenant data of its own — only tenant *metadata*).

**Reason:**

- **Per-module DB is the prerequisite for [ADR-010](#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy).** A module cannot be extracted without its data; if data is shared, extraction means manual table-by-table copy + dual-write transition periods. Separate databases make extraction a connection-string change.
- **Schema-per-tenant kept inside each module.** Three multitenancy models were evaluated:
  - **Database-per-tenant:** strongest isolation but explodes operations (`N modules × M tenants` databases). Rejected for cost at production scale, can be revisited per-module if a large tenant needs it.
  - **Row-level with Postgres RLS:** scales best with many small tenants but requires RLS discipline on every query and complicates per-tenant backups. Rejected because Axis prefers strong-isolation defaults.
  - **Schema-per-tenant inside the module DB:** preserves existing `tenant_{teamAccountId:N}` shape, isolates tenants strongly, and extracts cleanly. **Chosen.**
- **`public` schema in each module DB** holds module-level metadata (e.g. cross-tenant indexes, configuration). Identity's `public` is special: it owns team accounts, users, and roles — the registry that all other modules reference via JWT claims, never via SQL.
- **Connection-string convention.** `appsettings.json` carries a connection string per module (`ConnectionStrings:Identity`, `ConnectionStrings:DataModeling`, …) pointing to its database. In the modulith mode all these strings may point to the same Postgres host; in extracted mode they point to per-module hosts.

**Anti-patterns rejected:**

- Sharing a single `axis` database across modules (the original [ADR-002](#adr-002-schema-per-tenant) layout).
- Cross-module `DbContext` queries via a shared connection.
- Cross-tenant queries inside a module via `search_path` tricks — these always go through events instead.

### ADR-012: Per-module Wolverine schema in the module's own database

**Decision:** Each module has its own Wolverine envelope schema (`wolverine`) inside the module's database (per [ADR-011](#adr-011-per-module-database-with-schema-per-tenant-inside)). Outbox writes commit in the same transaction as the module's aggregate save, exactly as the original [ADR-009](#adr-009-wolverine-durable-inboxoutbox-in-a-dedicated-wolverine-schema) intended, but localised so each module owns its envelope history.

**Reason:**

- **Outbox locality matches data locality.** When a module is extracted, its outbox history extracts with it. Cross-module envelopes (events the module has published or received) live in the same DB as the aggregates that produced or consumed them, so replay/reconciliation works without cross-service coordination.
- **Transactional guarantee preserved per module.** The atomic "aggregate save + envelope write" still holds because both still happen in the same DB transaction.
- **Cross-module delivery uses the broker, not Postgres.** Envelopes destined for other modules are picked up by Wolverine and forwarded to Kafka ([ADR-013](#adr-013-apache-kafka-for-cross-module-domain-events-and-event-sourced-aggregates)). The receiving module's Wolverine treats the Kafka message as an inbox envelope and durably tracks it in its own `wolverine.incoming_envelopes`.
- **Schema migration per module.** Each module's Wolverine schema is created and migrated independently — production uses scripted SQL migrations in the module's CI pipeline; tests use EF Core migrations through the module's test fixture (see [ADR-023](#adr-023-per-module-ef-core-migrations-only)).

### ADR-013: Apache Kafka for cross-module domain events and event-sourced aggregates

**Decision:** Use Apache Kafka (via WolverineFx.Kafka) as the transport for **cross-module domain events** (facts that other modules need to react to) and as the **event store** for any aggregate the team chooses to event-source per the selective-event-sourcing convention (see ADR-026 when written). Commands, background jobs, sagas, and other work-queue traffic go through RabbitMQ instead — see [ADR-024](#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration) and [ADR-025](#adr-025-transport-selection-rule-by-message-name-suffix).

**Reason:**

- **Log-based replay is mandatory for event sourcing.** Event-sourced aggregates rebuild state by replaying their event log; Kafka's durable, ordered, partitioned log is the canonical store. RabbitMQ classic queues delete messages on consumption; RabbitMQ streams can replay but its ecosystem around event-sourcing is weaker.
- **Cross-module integration events are facts.** A `WorkflowExecutionCompleted` event is published once, consumed by many — possibly years later by a new analytics consumer that didn't exist when the event was emitted. Kafka's retention + replay model fits; RabbitMQ's "consume and forget" doesn't.
- **Partitioning matches the tenancy model.** Topics partitioned by `teamAccountId` preserve per-tenant ordering, allow per-tenant scaling, and keep tenants isolated within the message bus.
- **Industry standard for event-driven systems.** Kafka pairs naturally with schema registries ([ADR-019](#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)), event-lake / analytics tooling, and standard observability stacks.
- **Operational cost accepted for events only.** Kafka has heavier ops than RabbitMQ (broker tuning, partition planning, KRaft config). Scoping Kafka to events + event sourcing rather than "everything" keeps the surface bounded — work-queue traffic goes through RabbitMQ which is operationally lighter.

**What is NOT on Kafka:**

- Commands (`*Command` — explicit intent to perform an action) → RabbitMQ.
- Background jobs (`*Job` — fire-and-forget work) → RabbitMQ.
- Saga step messages (orchestrator coordination) → RabbitMQ.
- See [ADR-025](#adr-025-transport-selection-rule-by-message-name-suffix) for the per-message routing convention.

**Anti-patterns rejected:**

- Kafka as universal message bus including commands/jobs (over-engineering — work-queue semantics belong on RabbitMQ; see [ADR-024](#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration)).
- NATS JetStream (great tech, smaller .NET community + less Kafka-ecosystem parity).
- Postgres-as-queue (works at small scale but does not survive extraction).
- Wolverine in-memory bus for cross-module dispatch (violates ADR-010's "same contract in both modes").

### ADR-014: gRPC for internal sync RPC and REST OpenAPI for external API

**Decision:** Cross-module synchronous calls — used only when a local read model is insufficient — go over **gRPC** with proto-defined contracts. The external HTTP API exposed to the SPA stays **REST + OpenAPI** (JSON). gRPC is internal; REST is external.

**Reason:**

- **Most cross-module data flow is async via Kafka events.** Read models are maintained locally per [ADR-010](#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)'s pattern. gRPC is the escape hatch for cases where eventual consistency is unacceptable (e.g. fresh-permission check at request time).
- **Proto contracts force versioned interfaces.** When a module changes its API shape, the proto file is the breaking-change boundary — code generation fails on incompatible consumers, surfacing the issue at compile time.
- **HTTP/2 binary transport for internal hops.** Lower latency and bandwidth than JSON over HTTP/1.1, important when modules talk to each other on every request.
- **REST/JSON externally because browsers prefer it.** Frontend → API is still standard REST with `application/json`; OpenAPI generates the SPA's TypeScript client.

**What this means in practice:**

- Each module defines its public gRPC surface in `Axis.{Module}.Contracts/*.proto`.
- A module that calls another module gets a generated gRPC client referenced via project reference (modulith) or NuGet package (extracted).
- External REST endpoints live in `src/Axis.Api/Endpoints/{Module}*` and call into the module's Application layer directly (modulith mode) or via the module's gRPC server (extracted mode — `Axis.Api` becomes a thin gateway).

### ADR-015: Identity is a remote dependency from day 1

**Decision:** All other modules treat Identity as a remote service. JWT access tokens are validated via Identity's published JWKS endpoint (asymmetric signing with rotating keys), never by querying Identity's database. User and role lookups go through a gRPC `IdentityService` defined in `Axis.Identity.Contracts`. No module references `Axis.Identity.Infrastructure` or queries Identity's `public` schema.

**Reason:**

- **Auth is the highest-fanout dependency.** Every API request touches it. If Identity stays a hidden in-process call, extraction means rewriting every endpoint. Treating Identity as remote from day 1 means extraction is a connection-string change.
- **JWT public-key validation is the standard.** Identity signs tokens with a private key; other modules fetch the public key from `/.well-known/jwks.json` and validate locally. No round-trip to Identity for every request. OpenIddict already supports this — current setup just happens to short-circuit it because everything is in the same process.
- **User/role lookup is rare.** Most requests carry the user identity in the JWT claims. Only flows like "fetch full user profile for display" or "fetch up-to-date permission list after a change" need a sync lookup — gRPC for these, sparingly.
- **`Axis.Shared` shrinks accordingly.** Anything that touched Identity (e.g. `ICurrentUser`'s implementation) moves: the interface stays in `Axis.Shared.Application.Identity`, the implementation moves into each module's Infrastructure as a JWT-claim reader.

### ADR-016: Service discovery via config in modulith mode and K8s DNS in production

**Decision:** Service URLs are declared in `appsettings.json` under `Modules:{ModuleName}:Url`. In modulith mode all URLs resolve to `http://localhost:{port}` (each module binds a distinct port). In production each URL resolves to a Kubernetes service DNS name (`http://axis-identity.axis.svc.cluster.local`). Consul, Eureka, and full service-mesh discovery are explicitly rejected for this stage.

**Reason:**

- **Configuration is the simplest discovery mechanism that survives extraction.** No new infrastructure component to operate.
- **Kubernetes DNS is free when running on K8s.** Built-in, no extra setup.
- **Health checks per module** (`/health`, `/health/ready`) are exposed via standard ASP.NET Core health-check endpoints and consumed by K8s probes. No external health registry needed.
- **Service mesh (Istio/Linkerd) is overkill** until we have many services, complex traffic-shaping needs, or mutual-TLS requirements beyond what JWT + standard TLS provides. Adding a mesh is a future ADR if the need arises.

### ADR-017: Axis.Shared is abstractions only, no shared implementation

**Decision:** The shared kernel projects (`Axis.Shared.Domain`, `Axis.Shared.Application`, `Axis.Shared.Infrastructure`) contain **only**: domain primitives (Result, Error, value-object base types), application interfaces (`IUnitOfWork`, `ICurrentUser`, `ITenantContext`), and infrastructure abstractions that *every* module needs identically (e.g. envelope serialisers). Concrete implementations live inside the module that uses them.

**Reason:**

- **Shared implementation is hidden coupling.** When `Axis.Shared.Infrastructure.Persistence.UnitOfWork` defines how SaveChanges interacts with Wolverine, every module is locked to that pattern. Extracting one module means either dragging Shared.Infrastructure (with all its dependencies) or rewriting that module's UoW from scratch. Either is friction.
- **Abstractions are not coupling.** An interface in `Axis.Shared` is a contract — modules implement it however they like. Multiple implementations can coexist (one per module) without conflict.
- **`Axis.Shared.Infrastructure` either disappears or shrinks dramatically.** Most of its current content (UnitOfWork base class, Wolverine middleware, tenant-schema interceptor) becomes per-module. What remains is genuinely cross-cutting: e.g. a common JSON-serialisation policy.

**Anti-patterns rejected:**

- A "BaseRepository" in shared infrastructure — repositories belong in modules.
- Shared EF Core configuration helpers that secretly require a specific DbContext shape — these belong in module infrastructure.

### ADR-018: OpenTelemetry SDK with Grafana stack for observability

**Decision:** Every module uses the OpenTelemetry SDK to emit traces, metrics, and structured logs. In production, traces ship to **Grafana Tempo**, logs to **Grafana Loki**, metrics to **Prometheus** (scraped, with **Grafana Mimir** for long-term storage), all visualised in **Grafana**.

**Reason:**

- **OpenTelemetry is vendor-neutral.** Switching backends (e.g. to Honeycomb, Datadog, Lightstep) requires only the exporter change, not application code changes.
- **Grafana stack is self-hostable and free.** Suits a solo or small team building everything themselves. Commercial vendors (Datadog, Honeycomb) are excellent but lock in cost trajectory; deferring that choice keeps options open.
- **Cross-service tracing is non-negotiable for distributed-ready.** Without OTEL, debugging a slow workflow across Identity → Workflow → Form modules requires correlating timestamps by hand. Trace IDs propagated by Wolverine + gRPC interceptors give a single timeline per request.
- **Logs as a query target, not a tail.** Loki + LogQL gives structured-log search by trace ID, tenant ID, etc. Serilog continues to emit JSON; OTEL log exporter ships them to Loki.

### ADR-019: Avro and Schema Registry for event payloads with CloudEvents envelope

**Decision:** Cross-module event payloads are serialised as **Apache Avro** with schemas registered in **Confluent Schema Registry**. Each event is wrapped in a **CloudEvents** envelope (CE spec 1.0) so envelope-level metadata (event ID, source, time, type, correlation ID, tenant ID) is uniformly accessible regardless of payload format.

**Reason:**

- **Schema evolution is enforced.** Confluent Schema Registry rejects breaking schema changes by default; producers cannot ship an incompatible event without explicit allowlist. Consumers degrade gracefully (forward compatibility) when new optional fields appear.
- **Smaller payloads than JSON.** Avro's binary format pays off at Kafka's volumes; over the lifetime of a high-traffic topic this matters for storage and network costs.
- **CloudEvents envelope decouples routing from payload.** Wolverine middleware reads the envelope to dispatch handlers without parsing the Avro body; cross-cutting concerns (tracing, idempotency, tenant context) live in well-known envelope fields.
- **Interop with future polyglot services.** If a future module is built in Go, Rust, or Python, Avro+Schema-Registry+CloudEvents are first-class everywhere; JSON Schema is similar but lacks Schema Registry's compatibility-rule enforcement.

**Anti-patterns rejected:**

- Plain JSON events with no registry (no enforcement against breaking change).
- Protobuf for events (we use Protobuf for gRPC sync RPC — see [ADR-014](#adr-014-grpc-for-internal-sync-rpc-and-rest-openapi-for-external-api) — but Avro is more idiomatic with Kafka and supports schema evolution better than Protobuf when used as event-store payload).

### ADR-020: Saga orchestration for cross-module workflows

**Decision:** Cross-module workflows that need transactional-looking semantics are implemented as **orchestration-based sagas** in the originating module. The orchestrator is a Wolverine handler (or a series of handlers) that listens to events and emits compensating commands when steps fail. Choreography (each module reacting to each other's events without a central script) is explicitly rejected for non-trivial flows.

**Reason:**

- **Loss of cross-module transactions is given.** Per [ADR-010](#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy), no shared DB transaction crosses a module boundary. Sagas are the standard pattern for replacing that consistency.
- **Orchestration over choreography for debuggability.** Choreographed sagas (Module A publishes event → Module B reacts → publishes event → Module C reacts → …) are hard to reason about: there's no single place to see the flow. Orchestration centralises the script in one module, even if the steps fan out across modules.
- **Wolverine is the orchestrator runtime.** A saga is a long-running Wolverine "handler" that holds state in the originating module's database (`saga_state` table per module). Wolverine 5 supports saga state natively.
- **Compensating commands, not 2PC.** When a step fails, the orchestrator emits explicit compensating commands (e.g. "cancel reservation") rather than relying on distributed transaction coordinators.

### ADR-021: API versioning via path with N-2 support window

**Decision:** All API surfaces (external REST, internal gRPC) version paths with `/v1/`, `/v2/`, etc. Two major versions are supported in parallel for a minimum of one quarter after a new version ships; older versions are end-of-lifed on a published schedule. Breaking changes always bump the major version. Backward-compatible additions ship under the same version.

**Reason:**

- **Independent deployment requires independent evolution.** Once modules can be deployed separately, callers cannot assume the callee is on the latest contract.
- **Path-based is the most explicit form.** Header-based versioning (`Accept: application/vnd.axis.v2+json`) is more REST-pure but harder to debug, route, and cache.
- **N-2 keeps the support burden bounded.** Three concurrent versions in production is the practical maximum before code bifurcates uncontrollably.

### ADR-022: Secrets management via HashiCorp Vault in production

**Decision:** Production secrets (DB credentials, signing keys, broker credentials, API tokens) live in **HashiCorp Vault** with per-module policies. Applications fetch secrets at startup (and refresh on TTL) via the Vault Agent sidecar pattern. Local development uses `.env` files (gitignored) loaded via `dotnet user-secrets` or `DotNetEnv`. Plain `appsettings.Production.json` and naked Kubernetes Secrets are not used for sensitive values.

**Reason:**

- **Secrets in `appsettings.Production.json` are checked-in by accident sooner or later.**
- **Naked K8s Secrets are base64, not encrypted.** They appear in plain text to anyone with `kubectl` access to the namespace.
- **Vault gives audit, rotation, and per-module scoping.** Each module's deployment gets a Vault role that only reads its own secrets. Compromise of one module does not leak others' credentials.
- **Per-module rotation.** When a DB password rotates, only that module's pods restart — no global secret-bag to coordinate.

### ADR-023: Per-module EF Core migrations only

**Decision:** Every module manages its database schema (both `public` and tenant schemas) via **EF Core migrations**. `Database.EnsureCreated()` is not used anywhere — not in production, not in development, not in tests. The integration-test fixture applies the same migrations production uses.

**Reason:**

- **`EnsureCreated` is unsafe in tests as soon as more than one schema-owner targets the same database.** EF Core's "if any user table exists, skip" heuristic causes cross-module skips. Migrations have no such heuristic — they always apply each `Up` step deterministically.
- **Tests should mirror production.** If production deploys via migrations, tests must use migrations too. Otherwise tests pass for reasons that won't hold in production.
- **Migration history is the audit trail.** `__EFMigrationsHistory` per module DB documents every schema change. `EnsureCreated` is opaque.
- **Wolverine schema is migrated separately.** Per [ADR-012](#adr-012-per-module-wolverine-schema-in-the-modules-own-database) each module's Wolverine schema is migrated via scripted SQL alongside the EF migrations.

**Anti-patterns rejected:**

- `EnsureCreated` for "speed" in tests — speed difference is negligible vs the silent skip risk.
- Auto-apply migrations on app startup in production — migrations run as a separate step in the CI/CD pipeline so failed migrations don't leave the app in a half-migrated state.

### ADR-024: RabbitMQ for commands, background jobs, and saga orchestration

**Decision:** Use **RabbitMQ** (via WolverineFx.RabbitMq) as the transport for cross-module **commands** (intent to perform an action), **background jobs** (fire-and-forget work), and **saga step messages** (cross-module orchestrator coordination). This sits alongside Kafka per [ADR-013](#adr-013-apache-kafka-for-cross-module-domain-events-and-event-sourced-aggregates), which keeps cross-module domain events and event-sourced aggregate logs. Per-message routing is defined in [ADR-025](#adr-025-transport-selection-rule-by-message-name-suffix).

**Reason:**

- **Work-queue semantics are RabbitMQ's sweet spot.** Per-message ACK, requeue, dead-letter exchange, prefetch count, retry-with-backoff via TTL — these are first-class in RabbitMQ and require non-trivial workarounds in Kafka (retry topics, DLQ topics, manual offset commits, …).
- **Latency is lower for synchronous-ish flows.** A `ProvisionTenantCommand` round-trip through RabbitMQ is milliseconds; the equivalent through Kafka with consumer poll cycles is meaningfully slower.
- **Wolverine + RabbitMQ pairing is mature.** Saga state in Postgres + RabbitMQ for messages is the canonical Wolverine pattern; long-running orchestrators (e.g. tenant provisioning with retry/backoff/alert per [register-team-account § tenant-provisioning](use-cases/platform-foundation/register-team-account/README.md#tenant-provisioning)) fit naturally.
- **Ops cost is low.** Single-node RabbitMQ runs in a few hundred MB, clusters are well-understood, the management UI is excellent for debugging stuck queues.
- **Replay semantics are absent — that's a feature.** A command consumed should NOT be replayable; the action's already been taken. RabbitMQ's "consume and forget" matches command semantics. Use Kafka when you want to replay.

**What is NOT on RabbitMQ:**

- Cross-module domain events (`*Event`) → Kafka.
- Event-sourced aggregate logs → Kafka.
- Intra-module commands/queries → MediatR (in-process; never crosses a module boundary).

**Anti-patterns rejected:**

- RabbitMQ as event store (no replay, no retention model).
- Kafka for commands/jobs (over-engineered; see [ADR-013](#adr-013-apache-kafka-for-cross-module-domain-events-and-event-sourced-aggregates) anti-pattern list).
- Multiple brokers per ecosystem (one Kafka + one RabbitMQ is enough — adding NATS or SQS as a third would double the ops surface without commensurate gain).

### ADR-025: Transport selection rule by message-name suffix

**Decision:** Code-level convention that determines which transport carries which message. The rule keys off **message-name suffix** so it is grep-able, lint-able, and unambiguous at PR-review time:

| Suffix | Transport | Contract |
|---|---|---|
| `*Command` | **RabbitMQ** | Intent to perform an action; consumer is expected to execute exactly once (Wolverine's idempotency middleware handles at-least-once delivery). |
| `*Job` | **RabbitMQ** | Fire-and-forget background work (`SendEmailJob`, `ExpireFormSubmissionJob`). Typically scheduled via Wolverine's durable scheduler. |
| `*SagaStep` / `*OrchestratorMessage` | **RabbitMQ** | Saga state-machine transitions. State lives in Postgres `saga_state` table per module. |
| `*Event` | **Kafka** | Past-tense fact emitted by an aggregate after a state change. May have zero, one, or many consumers — including future consumers that don't exist today. |
| `*Snapshot` (event-sourced aggregates) | **Kafka** (separate compacted topic) | Periodic state snapshot to bound replay cost; consumers can start from the snapshot instead of replaying all history. |

**Reason:**

- **Suffix convention is self-enforcing.** A new contributor naming a class `ProvisionTenantEvent` will route it to Kafka — even if their intent was actually a command, the code review catches "this isn't a past-tense fact, rename to `ProvisionTenantCommand`."
- **Wolverine config stays declarative.** `opts.PublishMessage<T>().ToRabbitExchange(…)` for each `*Command`/`*Job`; `opts.PublishMessage<T>().ToKafkaTopic(…)` for each `*Event`. The Program.cs section reads top-to-bottom as "this kind of message goes here."
- **Grep-able in CI.** A lint step can fail the PR if a `*Event` is routed to RabbitMQ or a `*Command` is routed to Kafka. Mismatch = explicit override required in code with a comment explaining why.

**Edge cases:**

- **Integration commands from external systems** (e.g. external webhook triggers a workflow): wrap as an internal `*Command` on RabbitMQ — don't expose Kafka topics to outside consumers without a deliberate ADR.
- **Read-model rebuild signals** (e.g. "reindex all FormSubmissions"): treat as `*Job` on RabbitMQ even though the implementation involves replaying Kafka events — the signal to start is fire-and-forget work, the data source is the event log.

**Anti-patterns rejected:**

- "Same message goes to both transports" — pick one based on semantics. Duplicating writes doubles failure modes.
- Suffix-less message names (just `ProvisionTenant`) — ambiguous routing; pre-commit hook should reject.

### ADR-027: External identity providers for user sign-in and registration

**Decision:** Support **Microsoft** (Entra ID / Microsoft account), **Google**, and **GitHub** as external providers for user sign-in and provider registration/linking alongside email/password. They are wired into the OpenIddict server (ADR-004) via the ASP.NET Core external-authentication handlers; OpenIddict remains the only token issuer — external providers authenticate the user, Axis still mints its own access/refresh tokens. External providers do **not** create team accounts; team account onboarding is owned by [register-team-account](./use-cases/platform-foundation/register-team-account/README.md) and uses an official team account contact email. Standalone email/password registration is tracked in [register-user](./use-cases/identity-access/register-user/README.md).

**Reason:** Low-friction user sign-up and sign-in are required for onboarding. Users expect to use the corporate or developer identity they already have, and password-only auth raises abandonment at account setup. However, a generic Microsoft / Google / GitHub user identity does not prove authority over a team account, so team account registration remains a separate flow based on team account contact verification. Routing all user providers through OpenIddict keeps a single token format, a single JWKS, and one RBAC mapping regardless of how the user authenticated.

**Configuration:** per-provider `client_id` / `client_secret` stored in HashiCorp Vault in production ([ADR-022](#adr-022-secrets-management-via-hashicorp-vault-in-production)) and `.env` in development. Redirect URIs are registered per environment. A provider can be disabled per deployment without code changes.

**Registration:** an external sign-in either creates a new team account (the register-team-account flow) or signs into an existing one. Accounts are linked to an existing Axis user by **verified email** — a provider login whose email matches a verified local account attaches to it rather than creating a duplicate.

**Rejected:** full enterprise SSO federation (SAML, SCIM provisioning, per-tenant IdP) is deferred — it is a separate initiative driven by enterprise-tenant demand, not part of the baseline product scope in use-case specs.
