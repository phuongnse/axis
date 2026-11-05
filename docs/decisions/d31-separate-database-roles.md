# D31. Separate database roles

Status: **Agreed**

## Context

[D10](d10-tenant-aware-from-day-one-full-tenancy-later.md) puts each tenant in its own database, so
a tenant's database role is the last barrier between its data and its schema.
[Execution](../domain/knowledge.md#execution) and the security rules need runtime code to be unable
to change the schema, a stored release, the active release or the provisioning records that the next
activation plans from.

## Decision

- **Two database roles per tenant.** The schema role owns the `axis` and `entities` schemas and runs
  migrations, provisioning, release storage and activation. The runtime role reads and writes rows,
  only reads what activation writes, and may create, alter or drop nothing.
- **Two connection strings, no fallback.** Hosts that serve requests or run work hold the runtime
  role. Only activation holds the schema role: the server's startup activation under the
  `configuration` source, and otherwise `Axis.Admin` and its publisher, as
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md) and
  [D47](d47-publishing-from-studio.md) say.
- **The hosts refuse a runtime role with too many rights,** such as a superuser, a schema owner or a
  member of either.
- **Stored releases cannot change.** A trigger refuses every update, delete and truncate on the
  release tables.

The grants, the connection of each step and the start checks are in
[database roles](../reference/storage.md#database-roles).

## Consequences

- Runtime code cannot change the schema or a release, because the database refuses it.
- Every tenant has two roles to create, and old releases can never be deleted.
- The trigger guards against mistakes by Axis, not against a database administrator.

## Alternatives

- **One role, with the limits in code.** A bug in that code would reach the schema and the releases.
