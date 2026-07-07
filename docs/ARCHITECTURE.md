# Architecture

> **Navigation**: [docs/README.md](./README.md) · [AGENTS.md](../AGENTS.md)

This file owns durable source and runtime boundaries. Current behavior lives in [docs/use-cases/README.md](./use-cases/README.md); stack choices live in [docs/TECH_STACK.md](./TECH_STACK.md).

## Boundary Rules

- `frontend/` calls `Axis.Api` only.
- `Axis.Api` is the REST/OpenAPI gateway and composes module infrastructure at startup.
- Modules expose Application contracts to `Axis.Api`; module internals stay inside the module.
- Module Domain models follow DDD tactical boundaries; aggregate roots own invariants and domain events.
- Module Application exposes CQRS commands and side-effect-free queries through handlers.
- Domain projects have zero external dependencies.
- `Axis.Shared.*` is for shared primitives and cross-cutting helpers only, not product behavior.
- Module-owned data changes use EF Core migrations.
- Event sourcing is opt-in and requires an approved event store, replay, projection, and versioning design before source changes.
- New product behavior starts in an owning use-case spec before source changes.

## Dependency Direction

```text
frontend
  -> Axis.Api
    -> Module.Application
      -> Module.Domain
    -> Module.Infrastructure
      -> Module.Application
      -> Module.Domain

Axis.Shared.* supports layers without owning product behavior.
```

## Ownership

- Use-case docs own behavior, flows, acceptance criteria, and implementation status.
- Module code owns business rules and persistence details.
- [docs/TECH_STACK.md](./TECH_STACK.md) owns approved runtime and library categories.
- [docs/ENFORCEMENT.md](./ENFORCEMENT.md) owns recurring architecture enforcement status.
