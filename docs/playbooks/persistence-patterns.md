# Persistence Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- patterns index](./patterns-index.md) . [<- AGENTS.md](../../AGENTS.md)

Persistence belongs to the owning module. No shared `DbContext`, cross-module SQL, or repository base implementation.

## EF Core JSONB collection change tracking

Mutable JSONB collections need explicit conversion/comparer or immutable replacement semantics. Add tests that prove updates persist.

## EF Core common pitfalls

### 1. AsNoTracking on a write path — silent no-save

Tracked queries are required before mutations. Use `AsNoTracking()` only for reads.

### 2. SaveChanges inside a repository/store — breaks Unit of Work

Repositories stage changes; UnitOfWork commits.

### 3. Returning IQueryable from a repository — leaks infrastructure

Return DTOs, entities, or paged results. Do not expose provider-specific query composition.

### 4. Loop + SaveChanges for bulk operations — O(n) round trips

Batch changes and commit once unless each item intentionally owns an independent transaction.

## EF Core OwnsMany pattern

Owned collections need stable keys, explicit table/JSON mapping, and tests for add/update/remove.

## Multi-workspace isolation pitfalls

Workspace data lives under `workspace_{workspaceId:N}` inside the module DB. Set schema/search path through the module infrastructure path every time a connection opens.

### Raw SQL bypassing global query filters — workspace data leakage

Raw SQL must include the owning module schema/workspace scope. Never query another module's tables.

### Workspace schema provisioning

Provision schemas from the owning module's event/command path. Do not centralize per-module schema creation in shared infrastructure.

## EF Core aggregate mapping patterns

Map aggregate internals explicitly with Fluent API. Keep persistence details out of Domain. Prefer value-object conversions and private backing fields over public setters.
