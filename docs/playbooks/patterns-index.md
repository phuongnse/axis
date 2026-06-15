# Patterns index

> **Navigation**: [← docs/README.md](../README.md) · [← patterns.md](./patterns.md) · [← AGENTS.md](../../AGENTS.md)

One-page map into [`patterns.md`](./patterns.md). Open only the section you need — do not read the full playbook.

---

## By task

| You are… | Open section |
|----------|----------------|
| Adding a NuGet package | [NuGet / packaging rules](./patterns.md#nuget--packaging-rules) |
| Mapping JSONB `List<T>` | [EF Core JSONB collection change tracking](./patterns.md#ef-core-jsonb-collection-change-tracking) |
| Fixing EF tracking / migrations | [EF Core common pitfalls](./patterns.md#ef-core-common-pitfalls) |
| Modeling aggregates / owned entities | [DDD pitfalls](./patterns.md#ddd--aggregate-design-pitfalls) · [OwnsMany](./patterns.md#ef-core-ownsmany-pattern) |
| Wiring DI / scoped services | [Dependency Injection pitfalls](./patterns.md#dependency-injection-pitfalls) |
| Tenant schema / raw SQL | [Multi-tenancy pitfalls](./patterns.md#multi-tenancy-pitfalls) |
| `Task.Run` / fire-and-forget | [Async fire-and-forget pitfalls](./patterns.md#async-fire-and-forget-pitfalls) |
| New `IEntityTypeConfiguration` | [EF aggregate mapping patterns](./patterns.md#ef-core-aggregate-mapping-patterns) |
| Writing unit tests | [Testing rules](./patterns.md#testing-rules) · [testing.md](./testing.md) |
| `CancellationToken` / async handlers | [Async patterns](./patterns.md#async-patterns) |
| List endpoint / N+1 | [Query & N+1](./patterns.md#query--n1-patterns) · [Pagination](./patterns.md#pagination-pattern) |
| API response shape | [Response DTO convention](./patterns.md#response-dto-convention) |
| New Minimal API endpoint | [Minimal API wiring](./patterns.md#minimal-api-endpoint-wiring) · [OpenAPI annotation](./patterns.md#openapi-annotation-reference) |
| `Result` → HTTP status | [Result → HTTP mapping](./patterns.md#result--http-status-code-mapping) |
| Scalar / Swagger setup | [OpenAPI / Scalar setup](./patterns.md#openapi--scalar-setup) |
| Domain events / Wolverine handlers | [Wolverine patterns](./patterns.md#wolverine-patterns) |
| Traces / metrics / structured logs | [OpenTelemetry observability](./patterns.md#opentelemetry-observability) |
| Data owned by another module | [Cross-module data pattern](./patterns.md#cross-module-communication-pattern) · [gRPC dev (grpcurl)](./patterns.md#dev--verify-getuserpermissions-with-grpcurl) |
| Editing `buf.yaml` / removing a proto field | [Buf breaking rules — what's actually configured (and the gotcha)](./patterns.md#buf-breaking-rules--whats-actually-configured-and-the-gotcha) |
| Idempotent command / migration | [Command idempotency](./patterns.md#command-idempotency-pattern) |
| Pre-commit hygiene grep | [Code hygiene checklist](./patterns.md#code-hygiene-checklist) |
| New module / Kafka / proto / domain README index | [Repo layout discovery](./repo-layout-discovery.md) |
| React / TanStack Query screen | [Frontend Patterns](./patterns.md#frontend-patterns) · [frontend.md](./frontend.md) |
| Where logic belongs (handler vs aggregate) | [Axis layering](./patterns.md#axis-layering-srp-at-a-glance) · [Key patterns](./patterns.md#key-patterns) |
| Business failure vs exception | [Result vs exceptions](./patterns.md#result-pattern-vs-exceptions--when-to-use-what) |

---

## All sections (anchor list)

| # | Section |
|---|---------|
| 1 | [Key patterns](./patterns.md#key-patterns) |
| 2 | [Result vs exceptions](./patterns.md#result-pattern-vs-exceptions--when-to-use-what) |
| 3 | [NuGet / packaging](./patterns.md#nuget--packaging-rules) |
| 4 | [EF JSONB collections](./patterns.md#ef-core-jsonb-collection-change-tracking) |
| 5 | [EF common pitfalls](./patterns.md#ef-core-common-pitfalls) |
| 6 | [DDD / aggregate pitfalls](./patterns.md#ddd--aggregate-design-pitfalls) |
| 7 | [EF OwnsMany](./patterns.md#ef-core-ownsmany-pattern) |
| 8 | [DI pitfalls](./patterns.md#dependency-injection-pitfalls) |
| 9 | [Multi-tenancy](./patterns.md#multi-tenancy-pitfalls) |
| 10 | [Async fire-and-forget](./patterns.md#async-fire-and-forget-pitfalls) |
| 11 | [EF aggregate mapping](./patterns.md#ef-core-aggregate-mapping-patterns) |
| 12 | [Testing rules](./patterns.md#testing-rules) |
| 13 | [Async patterns](./patterns.md#async-patterns) |
| 14 | [Query & N+1](./patterns.md#query--n1-patterns) |
| 15 | [Response DTOs](./patterns.md#response-dto-convention) |
| 16 | [Pagination](./patterns.md#pagination-pattern) |
| 17 | [Minimal API](./patterns.md#minimal-api-endpoint-wiring) |
| 18 | [Axis layering](./patterns.md#axis-layering-srp-at-a-glance) |
| 19 | [OpenAPI annotations](./patterns.md#openapi-annotation-reference) |
| 20 | [Result → HTTP](./patterns.md#result--http-status-code-mapping) |
| 21 | [OpenAPI / Scalar](./patterns.md#openapi--scalar-setup) |
| 22 | [Wolverine](./patterns.md#wolverine-patterns) |
| 23 | [OpenTelemetry](./patterns.md#opentelemetry-observability) |
| 24 | [Cross-module data](./patterns.md#cross-module-communication-pattern) |
| 24a | [Buf breaking rules + counterexample lesson](./patterns.md#buf-breaking-rules--whats-actually-configured-and-the-gotcha) |
| 25 | [Command idempotency](./patterns.md#command-idempotency-pattern) |
| 26 | [Code hygiene](./patterns.md#code-hygiene-checklist) |
| 27 | [Frontend patterns](./patterns.md#frontend-patterns) |

---

## Contributing a new pattern

1. Check whether an existing section can absorb the rule.
2. Write the **principle** (WHY), then one **Axis-specific** example.
3. Add a row to the tables above and to the Contents list in `patterns.md`.
