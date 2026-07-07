---
name: axis-module-patterns
description: Apply Axis module-level tactical DDD/CQRS implementation patterns after module architecture readiness. Use when adding or changing aggregate roots, entities, value objects, strongly typed IDs, command/query handlers, repositories, Unit of Work, optimistic concurrency, read models/query DTOs, domain events, integration events, outbox/inbox, or event-sourcing-adjacent code in an Axis module.
---

# Axis Module Patterns

## Goal

Implement only the tactical patterns the approved module design needs, with explicit boundaries, tests, and enforcement status.

## Hard gates

Follow [reference.md](../reference.md).
- Do not use this skill as a substitute for `$axis-module-architecture`; carry a **Ready** module architecture verdict first when adding a module or foundational pattern.
- Do not introduce a pattern "for later." Adopt, reject, or block each pattern based on the current use-case ACs and architecture readiness output.
- Event sourcing, outbox/inbox, and cross-module integration events require explicit architecture decisions before implementation.
- Domain projects keep zero external dependencies; persistence, messaging, clocks, current user, and workspace scope stay outside Domain.
- Do not call a pattern enforced unless [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) names the deterministic check.

## Inputs

- Owning use-case ACs and Acceptance Test Matrix.
- `$axis-module-architecture` readiness output, when the change adds a module or foundational pattern.
- Same-module Domain/Application/Infrastructure code and existing architecture tests.
- Current [docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md) and [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md).

## Workflow

1. Select the pattern set.
   - List each pattern as **Adopt**, **Reject**, or **Blocked**.
   - Adopt only patterns required by ACs, persistence safety, module boundaries, or contract stability.
   - Reject unused enterprise patterns explicitly, especially saga/process manager, inbox/outbox, event sourcing, projection rebuild, and integration events.

2. Model Domain patterns.
   - Use aggregate roots for transactional invariants and lifecycle changes.
   - Use entities only when identity and lifecycle are meaningful inside the aggregate.
   - Use value objects for validated concepts with equality by components.
   - Use strongly typed IDs for aggregate/entity identifiers that cross method, persistence, or module boundaries; convert to primitives only at DTO/API/persistence edges.
   - Keep expected user/business failures observable through Application `Result` / `Result<T>` mapping.

3. Implement Application/CQRS patterns.
   - Commands mutate one transaction boundary and return `Result` / `Result<T>` for expected failures.
   - Queries are side-effect-free and return DTO/read-model shapes, not aggregates.
   - Handlers load the aggregate/read model, call domain behavior, persist through Unit of Work, and map known persistence exceptions to business-safe results.
   - Keep validation split clear: syntactic/request validation at validators or request factories; invariant validation in Domain behavior; persistence conflicts at Unit of Work.

4. Implement persistence patterns.
   - Use one module-owned DbContext and module-owned migrations for module data.
   - Put persistence commit in module `IUnitOfWork`; repositories do not expose commit-style methods or `IQueryable`.
   - Define unique constraints, foreign keys, indexes, and concurrency tokens in Infrastructure configuration, then prove them with integration tests when behavior depends on the database.
   - For optimistic concurrency, require a caller-supplied last-seen token/version, reject stale writes without overwrite, and test stale update plus concurrent publish/save paths.

5. Implement read-model/query patterns.
   - Shape reads inside the owning module.
   - Use deterministic ordering for lists and stable pagination metadata.
   - Keep read DTOs stable for API/front-end contracts; route wire shape changes through `$axis-api-contract`.
   - Do not expose draft/write-only state as a future record contract unless the AC says so.

6. Decide event patterns at implementation time.
   - Domain events may describe domain facts inside an aggregate, but do not imply dispatch, messaging, or outbox.
   - Integration events require an outbox/retry/idempotency/versioning decision before code.
   - Inbox is required before consuming at-least-once external or cross-module messages.
   - Event sourcing remains rejected unless `$axis-module-architecture` records event store, stream identity, replay, versioning/upcasters, snapshots, projection rebuild, privacy/retention, and deterministic replay tests.

7. Prove and record enforcement.
   - Add behavior tests for adopted patterns that affect ACs.
   - Add architecture/policy checks only for reusable rules that can be checked deterministically.
   - Update [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) when the diff creates a recurring enforced, partial, or review-only rule class.
   - Use `$axis-script-scope` to run the narrowest checks after implementation.

## Output

```text
Module Pattern Readiness: Ready / Blocked

Pattern set:
- Adopt:
- Reject:
- Blocked:

Domain:
- Aggregates / entities / value objects / strongly typed IDs / invariants

Application/CQRS:
- Commands / queries / handlers / validation / Result mapping

Persistence:
- DbContext / repositories / Unit of Work / constraints / optimistic concurrency

Reads:
- Read models / query DTOs / ordering / pagination

Events:
- Domain event / integration event / inbox-outbox / event sourcing decision

Enforcement:
- Tests / architecture checks / review-only gaps / docs ledger updates
```
