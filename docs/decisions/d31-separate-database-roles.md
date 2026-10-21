# D31. Separate database roles

Status: **Agreed**

Each tenant gets two database roles, one for runtime access and one for
schema changes. Runtime code can then read and write data, but it cannot
change the schema, a stored release, the active release or the provisioning
records. The server reads both connection strings, startup activation uses
`SchemaConnectionString`, and the server grants the runtime database role its
rights at every start. The server and the worker refuse to start when the
runtime database role is, or is a member of a role that is, a superuser, owns
a schema or may create objects in one.
See [storage](../reference/storage.md#database-roles) for the grants and the
connection string of each step.
The owner confirmed these points for issue #414 on 2026-10-16.

- **Two connection strings:**
  - Each tenant has `ConnectionString` for the runtime database role and
    `SchemaConnectionString` for the schema database role.
  - The server needs both. The worker needs only `ConnectionString` and never
    holds the schema database role.
  - A missing or empty setting stops the start, naming the tenant and the
    setting.
  - The start also stops when both connection strings name the same user.
    There is no fallback to one connection string.
- **Schema database role:**
  - It owns the `axis` and `entities` schemas.
  - It runs the EF Core migrations, entity provisioning, migrations, release
    storage and activation.
  - Only startup activation in the server uses it. The server keeps it for its
    whole life while tenants come from the `Tenants` section.
  - Under `Tenancy:Source=directory` only the admin host
    uses it, see [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md).
- **Runtime database role:**
  - It reads and writes rows in entity tables and system tables, and uses their
    sequences. It may not create, alter or drop anything.
  - It may only read the tables that activation writes: `axis.releases`,
    `axis.release_resources`, `axis.release_packages`, `axis.active_releases`,
    `axis.active_sites`, `axis.provisioned_entities`,
    `axis.provisioned_enum_values`, `axis.applied_migrations` and each EF Core
    migrations history table.
  - Default privileges grant `SELECT`, `INSERT`, `UPDATE` and `DELETE` on new
    tables in `entities`, and `SELECT` only on new tables in `axis`. A
    migration that adds an `axis` table that runtime writes grants those rights
    explicitly. Sequences in both schemas get `USAGE` and `SELECT`.
  - The schema database role grants these rights in a migration, or in a grant
    step that runs with the migrations. The runtime database role name comes
    from the user of `ConnectionString`.
  - The start stops when the runtime database role is a superuser, owns the
    `axis` or `entities` schema, or may create objects in either, or is a member
    of a role that is or may. Membership counts whatever its `INHERIT` option.
- **Stored releases:** a trigger refuses every `UPDATE`, `DELETE` and
  `TRUNCATE` on the three release tables, for every database role. It replaces
  the guard that only EF Core change tracking gives today. A stored release can
  no longer be deleted at all, and cleaning up old releases needs a later
  decision.
- **Creating the database roles:**
  - Axis creates no database roles or databases. Whoever installs Axis does,
    while tenants come from the `Tenants` section.
  - Under `Tenancy:Source=directory`, `tenant create`
    creates them, see
    [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md).
  - Docker Compose, `scripts/dev.sh`, `scripts/e2e.sh` and the test fixtures
    create two database roles, `axis_schema` and `axis_runtime`, with
    development passwords.

_Why:_ [D10](d10-tenant-aware-from-day-one-full-tenancy-later.md) puts
each tenant in its own database, so a tenant's database role is the last
barrier between its data and its schema.
[Execution](../domain/knowledge.md#execution) and security rules need runtime code
to be unable to change the schema, a stored release, the active release or the
provisioning records that the next activation plans from. A separate schema
database role makes that a property of the database, not of the code.
