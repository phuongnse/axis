---
name: axis-module-patterns
description: Implement Axis tactical DDD, CQRS, persistence, read-model, concurrency, or event patterns already required by current ACs or a Ready module-architecture decision.
---

# Axis Module Patterns

## Goal

Implement and prove only the tactical pattern set supplied by the caller.

## Hard gates

Follow [reference.md](../reference.md).
- Foundational or new-module work **Requires** a **Ready** `$axis-module-architecture` decision.
- Do not adopt patterns for hypothetical future use.
- Domain projects keep zero external dependencies; infrastructure concerns remain outside Domain.
- Do not call a rule enforced unless [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) names its mechanism and proof.

## Inputs

- Caller-supplied adopted pattern set and owning ACs.
- Architecture decision when required.
- Same-module code, migrations, tests, and current enforcement status.

## Workflow

1. Confirm each requested pattern is required by current ACs or the architecture decision; return unrelated or undecided patterns instead of rejecting an inventory of possibilities.
2. Implement domain invariants, entities/value objects/IDs, and business-safe failures within the aggregate boundary.
3. Implement command/query coordination, validation ownership, Unit of Work, repositories, read models, deterministic ordering/pagination, and concurrency only as selected.
4. Implement event dispatch, integration delivery, inbox/outbox, or event sourcing only when the supplied architecture decision covers their operational semantics.
5. Prove behavior, persistence, stale-write, ordering, delivery, or replay properties at the lowest reliable boundary required by the selected pattern.
6. Add deterministic checks only for reusable invariants, update enforcement status, and return implementation evidence to the caller.

## Output

Report implemented pattern set, domain/application/persistence/read/event evidence, enforcement changes, and blocked decisions.
