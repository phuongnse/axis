# Domain and Application Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- patterns index](./patterns-index.md) . [<- AGENTS.md](../../AGENTS.md)

Domain is pure behavior. Application coordinates use cases, validation, repositories, and transactions inside one module.

## Key patterns

- Put business invariants in aggregates/value objects.
- Use `Result` / `Result<T>` for business failures.
- Use exceptions for infrastructure failures.
- Keep MediatR intra-module.

## Result Pattern vs. exceptions — when to use what

Expected business outcomes return `Result`: validation, conflict, not found, permission denied by business rule. Unexpected infrastructure failures throw and are handled at the boundary.

## DDD / Aggregate design pitfalls

### 1. Anemic domain model — logic in handler instead of aggregate

If a rule protects an invariant, the aggregate owns it.

### 2. Public collection mutation — bypasses domain invariants

Expose read-only collections and intention-revealing methods.

### 3. Raising domain events before the transaction commits

Record events with state changes and publish through the module's reliable outbox path.

### 4. Mismodeled aggregate boundary — separate AggregateRoot for a dependent entity

Make a dependent entity an aggregate only when it has independent lifecycle, identity, and consistency boundary.

### 4a. When to split one concept into two aggregates

Split when transactional consistency is not required and independent workflows benefit from separate repositories/events.

## Axis layering (SRP at a glance)

Domain: rules. Application: orchestration. Infrastructure: persistence/integration. API: transport mapping. Frontend: user workflow.

## Command idempotency pattern

Commands that can retry need idempotency keys, state-transition guards, or natural uniqueness constraints.
