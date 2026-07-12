---
name: axis-module-architecture
description: Decide Axis modular-monolith boundaries and foundational DDD, CQRS, persistence, event, or cross-module patterns before implementation. Use for new modules or changes to durable module architecture.
---

# Axis Module Architecture

## Goal

Emit a durable architecture decision that downstream pattern and use-case work can implement without reopening foundational choices.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence.
- **Blocked** readiness forbids module implementation.
- Event sourcing requires explicit user sign-off and complete operational/replay decisions.
- Product behavior stays out of `Axis.Shared.*`; modules do not depend on another module's internals.

## Inputs

- Owning product/foundation contract or explicit architecture question.
- [docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md), [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md), same-module code, and Design Gate evidence.

## Workflow

1. Classify existing slice, new bounded context, foundational pattern, or event-sourcing decision.
2. Define ubiquitous language, owned lifecycle/data, mutation authority, upstream inputs, downstream contracts/events, and composition-root boundary.
3. Decide aggregates/invariants, CQRS requests/handlers, validation, idempotency, authorization/workspace scope, and business-failure mapping.
4. Decide module-owned persistence, migrations, transaction/repository boundary, read models, concurrency, and rollback needs.
5. Decide domain/integration/event-sourcing semantics, including delivery, idempotency, versioning, replay, retention, and rebuild concerns only when those patterns are in scope.
6. Name deterministic enforcement and review-only gaps; update [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) only for recurring rule classes.
7. Return a **Ready** or **Blocked** decision record to the caller. A **Ready** workflow **Delegates** only its adopted tactical items to `$axis-module-patterns`.

## Output

Report readiness, boundary, DDD/CQRS, persistence, events, enforcement, and adopted downstream pattern set.
