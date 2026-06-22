# Technical Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

`patterns.md` is a compatibility router. The owner docs below hold the rules and examples; start at [patterns-index.md](./patterns-index.md) and open only the surface you need.

## Owner Docs

| Surface | Owner |
|---|---|
| Domain/Application | [domain-application-patterns.md](./domain-application-patterns.md) |
| Dependencies/DI | [dependency-patterns.md](./dependency-patterns.md) |
| Persistence/EF/workspace schema | [persistence-patterns.md](./persistence-patterns.md) |
| API/OpenAPI/HTTP mapping | [api-patterns.md](./api-patterns.md) |
| Runtime/async/observability | [runtime-patterns.md](./runtime-patterns.md) |
| Wolverine handlers/jobs | [wolverine-patterns.md](./wolverine-patterns.md) |
| Cross-module events/read models | [cross-module-patterns.md](./cross-module-patterns.md) |
| gRPC/proto/Buf/JWKS | [grpc-patterns.md](./grpc-patterns.md) |
| Testing | [testing.md](./testing.md) |
| Code hygiene/policy regexes | [code-hygiene-patterns.md](./code-hygiene-patterns.md) |
| Frontend | [frontend.md](./frontend.md) |
| Design source | [design-source.md](./design-source.md) |
| Wireframes/visual docs | [wireframes.md](./wireframes.md) |

## Compatibility Anchors

These headings keep older `patterns.md#...` links resolvable. Add or edit detailed guidance in the owner doc, not here.

## Key patterns

Owner: [./domain-application-patterns.md#key-patterns](./domain-application-patterns.md#key-patterns).

## Result Pattern vs. exceptions — when to use what

Owner: [./domain-application-patterns.md#result-pattern-vs-exceptions--when-to-use-what](./domain-application-patterns.md#result-pattern-vs-exceptions--when-to-use-what).

## NuGet / packaging rules

Owner: [./dependency-patterns.md#nuget--packaging-rules](./dependency-patterns.md#nuget--packaging-rules).

## EF Core JSONB collection change tracking

Owner: [./persistence-patterns.md#ef-core-jsonb-collection-change-tracking](./persistence-patterns.md#ef-core-jsonb-collection-change-tracking).

## EF Core common pitfalls

Owner: [./persistence-patterns.md#ef-core-common-pitfalls](./persistence-patterns.md#ef-core-common-pitfalls).

## DDD / Aggregate design pitfalls

Owner: [./domain-application-patterns.md#ddd--aggregate-design-pitfalls](./domain-application-patterns.md#ddd--aggregate-design-pitfalls).

## EF Core OwnsMany pattern

Owner: [./persistence-patterns.md#ef-core-ownsmany-pattern](./persistence-patterns.md#ef-core-ownsmany-pattern).

## Dependency Injection pitfalls

Owner: [./dependency-patterns.md#dependency-injection-pitfalls](./dependency-patterns.md#dependency-injection-pitfalls).

## Multi-workspace isolation pitfalls

Owner: [./persistence-patterns.md#multi-workspace-isolation-pitfalls](./persistence-patterns.md#multi-workspace-isolation-pitfalls).

## Async fire-and-forget pitfalls

Owner: [./runtime-patterns.md#async-fire-and-forget-pitfalls](./runtime-patterns.md#async-fire-and-forget-pitfalls).

## EF Core aggregate mapping patterns

Owner: [./persistence-patterns.md#ef-core-aggregate-mapping-patterns](./persistence-patterns.md#ef-core-aggregate-mapping-patterns).

## Testing rules

Owner: [./testing.md#additional-net-test-patterns](./testing.md#additional-net-test-patterns).

## Async patterns

Owner: [./runtime-patterns.md#async-patterns](./runtime-patterns.md#async-patterns).

## Query & N+1 patterns

Owner: [./api-patterns.md#query--n1-patterns](./api-patterns.md#query--n1-patterns).

## Response DTO convention

Owner: [./api-patterns.md#response-dto-convention](./api-patterns.md#response-dto-convention).

## Pagination pattern

Owner: [./api-patterns.md#pagination-pattern](./api-patterns.md#pagination-pattern).

## Minimal API endpoint wiring

Owner: [./api-patterns.md#minimal-api-endpoint-wiring](./api-patterns.md#minimal-api-endpoint-wiring).

## Axis layering (SRP at a glance)

Owner: [./domain-application-patterns.md#axis-layering-srp-at-a-glance](./domain-application-patterns.md#axis-layering-srp-at-a-glance).

## OpenAPI annotation reference

Owner: [./api-patterns.md#openapi-annotation-reference](./api-patterns.md#openapi-annotation-reference).

## Result → HTTP status code mapping

Owner: [./api-patterns.md#result--http-status-code-mapping](./api-patterns.md#result--http-status-code-mapping).

## OpenAPI / Scalar setup

Owner: [./api-patterns.md#openapi--scalar-setup](./api-patterns.md#openapi--scalar-setup).

## Wolverine patterns

Owner: [./wolverine-patterns.md#wolverine-patterns](./wolverine-patterns.md#wolverine-patterns).

## OpenTelemetry observability

Owner: [./runtime-patterns.md#opentelemetry-observability](./runtime-patterns.md#opentelemetry-observability).

## Cross-module communication pattern

Owner: [./cross-module-patterns.md#cross-module-communication-pattern](./cross-module-patterns.md#cross-module-communication-pattern).

## Buf breaking rules

Owner: [./grpc-patterns.md#buf-breaking-rules](./grpc-patterns.md#buf-breaking-rules).

## Command idempotency pattern

Owner: [./domain-application-patterns.md#command-idempotency-pattern](./domain-application-patterns.md#command-idempotency-pattern).

## Code hygiene checklist

Owner: [./code-hygiene-patterns.md#code-hygiene-checklist](./code-hygiene-patterns.md#code-hygiene-checklist).

## Drift script regex constraints

Owner: [./code-hygiene-patterns.md#drift-script-regex-constraints](./code-hygiene-patterns.md#drift-script-regex-constraints).

## Frontend Patterns

Owner: [./frontend.md](./frontend.md).

## Wireframe convention

Owner: [./wireframes.md](./wireframes.md).
