---
name: axis-module-architecture
description: Standardize Axis modular monolith architecture before adding or changing module boundaries, bounded contexts, DDD tactical models, CQRS requests and handlers, module persistence, domain/integration events, event sourcing, or cross-module dependencies.
---

# Axis Module Architecture

## Goal

Decide the durable module shape before implementation so new business capability fits the modular monolith, DDD, CQRS, persistence, and event strategy consistently.

## Hard gates

Follow [reference.md](../reference.md).
- Run `$axis-design-gate` before this skill when source, tests, tooling, contracts, schema, or workflow behavior will change.
- Do not implement module source while `Module Architecture Readiness` is **Blocked**.
- New modules and foundational DDD/CQRS/event changes require a recorded bounded context, aggregate model, CQRS surface, persistence strategy, event decision, and enforcement plan before code.
- Event sourcing is not the default event pattern. Stop for explicit user sign-off before implementation when introducing event store, stream replay, projection rebuild, event versioning, snapshots, or operational replay semantics.
- Do not place product behavior in `Axis.Shared.*` or depend on another module's internals to avoid modeling the boundary.

## Inputs

- Owning use-case or foundation spec, or the explicit architecture question being resolved.
- Current [docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md) boundary rules and [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) enforcement status.
- Same-module code when changing an existing module, or the nearest existing module when creating a new one.
- Design Gate dossier and sign-off when required by `$axis-design-gate`.

## Workflow

1. Classify the architecture surface.
   - Existing module slice: keep the current module boundary unless the spec proves it wrong.
   - New module: name the bounded context, module owner, data ownership, and out-of-scope responsibilities.
   - Foundational pattern: name the recurring rule class and the deterministic enforcement it should gain.
   - Event sourcing: treat it as an architecture decision, not a logging, audit, outbox, or domain-event synonym.

2. Establish the modular-monolith boundary.
   - Define the module's ubiquitous language, owned lifecycle, and data it alone can mutate.
   - List upstream inputs and downstream outputs as contracts or events, not direct module-internal calls.
   - Keep `Axis.Api` as gateway/composition root; module internals stay inside the module.
   - Use `Axis.Shared.*` only for primitives and cross-cutting helpers that carry no product behavior.

3. Model DDD tactical design.
   - Identify aggregate roots, entities, value objects, domain services, domain events, and invariants.
   - State each aggregate's transactional boundary and the commands allowed to mutate it.
   - Put expected business failures in `Result` / `Result<T>` flows; reserve exceptions for unexpected faults.
   - Keep Domain free of EF, MediatR, ASP.NET, infrastructure packages, and application orchestration.

4. Map the CQRS surface.
   - Split writes into commands and reads into side-effect-free queries.
   - Name command/query requests, handlers, return shapes, validation boundary, idempotency need, authorization/workspace-scope boundary, and cancellation path.
   - Keep handlers as use-case coordinators: load aggregate/read model, call behavior, persist atomically, publish or record events through the chosen pattern.
   - Keep API endpoints thin and route contract work through `$axis-api-contract` when wire shape changes.

5. Choose persistence and transaction strategy.
   - Name module-owned DbContext/schema/table ownership, repository boundaries, optimistic concurrency, migration ownership, and rollback/atomicity needs.
   - Use migration-backed schema changes; do not bypass migrations in tests or runtime setup.
   - Keep read models/query shaping inside the owning module; do not leak `IQueryable` across Application boundaries.

6. Decide the event pattern.
   - Domain events: pure domain facts raised by aggregates to express in-transaction business changes.
   - Integration events: cross-module/process contracts with idempotency, outbox/retry, versioning, and consumer ownership.
   - Event sourcing: aggregate state is rebuilt from an append-only event stream; require stream identity, event versioning/upcasters, snapshots policy, replay safety, projection rebuild, privacy/retention plan, concurrency model, and deterministic replay tests before code.
   - If those event-sourcing decisions are not explicit, record `Event sourcing: Rejected for this slice` or `Blocked`, not `Partial`.

7. Plan enforcement.
   - Add project references so architecture tests can load every module layer assembly.
   - Add or update deterministic checks for reusable rules; record review-only gaps in [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md).
   - Map proving tests to behavior, boundaries, persistence, events, and contract surfaces without weakening existing checks.
   - Route adopted tactical patterns to `$axis-module-patterns`; do not rely on this architecture readiness output as the implementation workflow for strongly typed IDs, Unit of Work, optimistic concurrency, read models, or events.
   - Use `$axis-script-scope` to select the narrowest commands after edits.

8. Route implementation.
   - Use `$axis-use-case-spec` when behavior or acceptance criteria are missing.
   - Use `$axis-module-patterns` when implementing the adopted DDD/CQRS/persistence/event pattern set.
   - Use `$axis-use-case-implementation` for product behavior after readiness is **Ready**.
   - Use `$axis-api-contract`, `$axis-frontend-feature`, `$axis-frontend-foundation`, or `$axis-visual-artifact` only when their surfaces are touched.

## Output

```text
Module Architecture Readiness: Ready / Blocked

Boundary:
- ...

DDD model:
- Aggregate roots / entities / value objects / invariants / domain events

CQRS:
- Commands / queries / handlers / validation / idempotency / auth or workspace scope

Persistence:
- DbContext / schema / migrations / repository / concurrency / transaction boundary

Events:
- Domain events / integration events / event sourcing decision

Enforcement:
- Deterministic checks / review-only gaps / docs ledger updates

Next skill:
- $axis-module-patterns / $axis-use-case-spec / $axis-use-case-implementation / $axis-api-contract / none yet
```
