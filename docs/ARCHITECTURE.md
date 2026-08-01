# Architecture

> **Navigation**: [docs/README.md](./README.md) · [AGENTS.md](../AGENTS.md)

This file owns durable source and runtime boundaries. Current behavior lives in [docs/use-cases/README.md](./use-cases/README.md); stack choices live in [docs/TECH_STACK.md](./TECH_STACK.md).

## Boundary Rules

- `frontend/` calls `Axis.Api` only.
- `Axis.Api` is the REST/OpenAPI gateway and composes module infrastructure at startup.
- Modules expose Application contracts to `Axis.Api`; module internals stay inside the module.
- Modules may expose optional `Axis.{Module}.Contracts` projects for stable cross-module contracts; consumers may reference another module's Contracts project only, not its Domain, Application, or Infrastructure projects.
- Module Domain models follow DDD tactical boundaries; aggregate roots own invariants and domain events.
- Module Application exposes CQRS commands and side-effect-free queries through handlers.
- Server-owned search is a CQRS read-side pattern: module Application owns searchable intent and provider ports, Infrastructure owns read-store translation and indexes, and shared code may contain storage-neutral primitives only.
- Cross-module global search, when required by an owning contract, uses one materialized read model built from published module contracts rather than querying module internals or merging provider-native scores.
- Domain projects have zero external dependencies.
- `Axis.Shared.*` is for shared primitives and cross-cutting helpers only, not product behavior.
- Module-owned data changes use EF Core migrations.
- Event sourcing is opt-in and requires an approved event store, replay, projection, and versioning design before source changes.
- New product behavior starts in an owning use-case spec before source changes.

## Dependency Direction

```text
frontend
  -> Axis.Api
    -> Module.Contracts
    -> Module.Application
      -> Module.Domain
    -> Module.Infrastructure
      -> Module.Application
      -> Module.Domain

Module.Application
  -> Same Module.Domain
  -> Other Module.Contracts only when a use case needs a public cross-module contract

Axis.Shared.* supports layers without owning product behavior.
```

## Rules boundary

Rules owns reusable definitions, bindings, and pure evaluation:

- A Rule is a versioned, deterministic, side-effect-free unit of logic that transforms declared typed inputs into declared typed outputs. `Inputs -> Logic -> Outputs` is the stable platform contract; validation, calculation, classification, eligibility, routing, and transformation are consumer meanings rather than separate Rule models.
- `RuleDefinition` owns metadata, stable typed input and output contracts, canonical bounded logic, validation, immutable versions, and activation. Built-in and workspace definitions use one semantic model; source and language capabilities are metadata.
- `RuleBinding` connects one exact rule version to a generic target type/id and use case/trigger. It owns typed input mappings, priority, enabled state, failure behavior, concurrency, audit data, and usage discovery.
- `RuleEvaluationResult` is deterministic, typed, explainable, bounded, and side-effect-free. Consumers interpret outputs and own every business mutation.

Rules persists definitions, immutable definition versions, and bindings in the Rules store. A binding keeps opaque consumer target identifiers rather than foreign keys or consumer entities; deleting or changing a binding never changes or deletes its definition. Canonical logic is the only persisted rule behavior. Syntax, visual composition, decision tables, and localized explanations are projections over that contract and never separate stored truths.

`Axis.Rules.Contracts` exposes only consumer-neutral definition, binding, typed context-provider, and evaluation contracts. A consumer implements its adapter and explicitly maps runtime context, target data, or fixed values into declared rule inputs. Execution-envelope data used only for isolation, authorization, audit, or tracing is not an ambient logic input. Rules projects may not reference Object, Workflow, another consumer module, or consumer domain types; consumers may reference Rules Contracts only.

`Axis.Api` composes consumer adapters and exposes REST/OpenAPI surfaces. Cross-database Object and Rules mutations remain explicit operations; no hidden dual write or distributed transaction is introduced.

## Ownership

- Use-case docs own behavior, flows, acceptance criteria, and implementation status.
- Module code owns business rules and persistence details.
- [docs/TECH_STACK.md](./TECH_STACK.md) owns approved runtime and library categories.
- [docs/ENFORCEMENT.md](./ENFORCEMENT.md) owns recurring architecture enforcement status.
