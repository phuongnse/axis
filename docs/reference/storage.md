# Storage

Detailed reference for the storage layout, physical names, column types and
schema planning. The conventions of [architecture.md](../architecture.md)
apply: sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

- **Platform tables.** These are managed by EF Core migrations shipped with
  Axis.
- **System tables.** These live in the `axis` schema of each tenant database.
  Each module owns its tables and manages them with its own EF Core context
  and migrations. Each module context keeps its migration history in its own
  table, `axis.__<module>_migrations`, so the contexts sharing the schema do
  not collide.
  - `Axis.Configuration` owns `axis.releases`, `axis.release_resources`,
    `axis.release_packages` (the extension package versions each release
    pins, as release id, package id and version, unique per release and
    package), `axis.active_releases` (the active release of each application `id`,
    with its name, unique ignoring letter case) and `axis.active_sites` (the
    site paths of each active release, as application `id` and path, unique
    on path), with history in `axis.__configuration_migrations`. It also owns
    `axis.release_computed_fingerprints`, the hash of the
    computed definitions of each entity in each release. It also owns
    `axis.publish_requests` (the requests that Studio stores and the publisher
    runs). It will also own `axis.release_activations` (the activation history
    with release numbers) _(planned for M10)_, see
    [Publish requests and release history](#publish-requests-and-release-history). The caller
    supplies the tenant database connection. Other modules read and set the
    active release and its site paths only through `IActiveReleaseStore`;
    they never use `axis.active_releases`, `axis.active_sites` or the
    configuration context directly.
    Files of a module are stored in
    `axis.release_resources` with their `modules/<id>/` path, see
    [Modules](configuration.md#modules).
  - `Axis.Data` owns `axis.provisioned_entities` (each provisioned entity
    with its application and table), `axis.provisioned_enum_values` (each
    recorded enum value) and `axis.sequence_counters` (the last number of
    each sequence and period), with history in `axis.__data_migrations`.
    It also owns `axis.applied_migrations`, the migrations applied once per
    tenant. See [Migrations](#migrations). It also owns
    `axis.computed_field_errors`, for the
    [recompute of computed fields](#recomputing-computed-fields). It also owns
    `axis.pending_computed_fields`, the entities whose recompute the worker
    finishes in batches.
  - `Axis.Processes` owns `axis.process_instances` (one row per process
    instance), `axis.process_step_history` (one row per step occurrence),
    `axis.process_tasks` (one row per human task),
    `axis.connector_calls` (one row per staged connector call),
    `axis.package_calls` (one row per staged package call),
    `axis.process_call_attempts` (one row per attempt and per lookup of a
    connector or package call),
    `axis.events` (one row per event occurrence that a step raised),
    `axis.process_start_receipts` (the stored response of each
    `Idempotency-Key` of a start), `axis.endpoint_receipts` (the record and
    instance of each endpoint call's `Idempotency-Key`, unique per
    application, endpoint, caller and key, with a hash of the input) and
    `axis.process_work_items` (the work that
    workers claim and run), with history in `axis.__processes_migrations`.
    See [processes](processes.md#tables).
  - `Axis.Data` also owns `axis.audit_records`, with history in
    `axis.__data_migrations`. Other modules append and read audit records
    only through its contract. `AuditRecords.AppendAsync` takes the
    caller's open transaction, and `AuditRecords.ListForRecordAsync` reads
    one record's history. A process start and each process step write
    their audit records this way, inside their own transaction. See
    [Audit records and sequence counters](#audit-records-and-sequence-counters).
  - Releases are immutable, enforced through the context's change tracking:
    a release and its resources are only inserted together. Saving fails when
    a stored release or resource is modified or deleted, or when a resource is
    added to a stored release. Bulk operations (`ExecuteUpdate`,
    `ExecuteDelete`) and raw SQL bypass this guard, so a database trigger backs
    it: it refuses `UPDATE`, `DELETE` and `TRUNCATE` on the release tables with
    SQLSTATE `23001`. The guard still fails first, before any SQL runs. See
    [Database roles](#database-roles).
- **Entity tables.** These are generated per entity in the tenant database and
  live in the `entities` schema, separate from the `axis` system tables.
  `Axis.Data` plans them (see [Schema planning](#schema-planning)) and
  applies the plan.
  - In M1, plan and apply run together in one transaction. One fixed
    advisory transaction lock (`pg_advisory_xact_lock`) serializes entity
    schema changes, so the catalog and records read for the plan are still
    current when it is applied.
  - The provisioning records are written in the same transaction as the DDL.
    When the plan has any diagnostic, the transaction is rolled back and
    nothing is applied.
  - The lock does not block runtime writes to entity tables, so a statement
    can still fail, for example adding a `NOT NULL` column after a row was
    inserted following the catalog read. Any failing statement rolls back the
    whole transaction, DDL and records, and the exception propagates.
- **Physical names.** Table and column names are derived from stable IDs and
  names, never from labels. Renaming a label, or renaming an entity while
  keeping its `id`, never touches storage.

  | Object                  | Name                                                                      | Length     |
  | ----------------------- | ------------------------------------------------------------------------- | ---------- |
  | Table                   | `e_` + entity `id` as 32 lowercase hex characters                         | 34         |
  | Primary key column      | `id` (`uuid`)                                                             | 2          |
  | Version column          | `version` (`bigint`)                                                      | 7          |
  | Computed release column | `computed_release_id` (`uuid`, null), on tables that are not child tables | 19         |
  | Field column            | `f_` + field name in lowercase                                            | at most 62 |
  | Primary key             | `pk_` + table                                                             | 37         |
  | Unique constraint       | `uq_` + table + `_` + hash of the column name                             | 54         |
  | Foreign key             | `fk_` + table + `_` + hash of the column name                             | 54         |

  The hash is the first 16 lowercase hex characters of SHA-256 over the UTF-8
  column name. Because names are ASCII and at most 60 characters, every
  identifier fits PostgreSQL's 63-byte limit by construction and is never
  truncated. Identifiers are always double-quoted in SQL.

- **Module resource ids.** The compiler builds the derived id into the model.
  Activation provisions a module entity's table for each application that
  uses the module. The id of a module resource
  inside an application is a UUID v5 (RFC 9562). The namespace is the
  application `id`. The name is the resource's own declared id, as lower-case
  hyphenated UTF-8 text. A module entity's table is `e_` + the derived id, and
  `axis.provisioned_entities` records the application that uses it. So two
  applications in one tenant that use one module get different `e_` tables,
  and `AXC0018` never fires between them. The derived id stays the same across
  module versions, so an upgrade maps onto the same table. See
  [Modules](configuration.md#modules).
- **Column types.** Each field type maps to one PostgreSQL type, spelled as
  `format_type` renders it, so a catalog column matches by string equality.

  | Field type         | Column type                                                                                               |
  | ------------------ | --------------------------------------------------------------------------------------------------------- |
  | `text`             | `character varying(n)` with `maxLength`, otherwise `text`                                                 |
  | `integer`          | `bigint`                                                                                                  |
  | `decimal`          | `numeric(p,s)` with `precision` (`s` is 0 when `scale` is omitted), otherwise `numeric`                   |
  | `boolean`          | `boolean`                                                                                                 |
  | `date`             | `date`                                                                                                    |
  | `date-time`        | `timestamp with time zone`                                                                                |
  | `enum`             | `text`; the values are recorded, not enforced by a `CHECK`                                                |
  | `reference`        | `uuid` with a foreign key to the target table's `id`                                                      |
  | `child-collection` | No column on the owner table. See [Child tables and computed columns](#child-tables-and-computed-columns) |

- **SQL safety.** Every SQL statement for entity data is built by the data
  module from compiled metadata. Identifiers are resolved and quoted by the
  module; values are always parameters. Configuration can never supply raw SQL.
  Request text, such as the entity segment, `sort` and `values` names, is
  matched against the model's declared names and never used as an identifier.
- **Database credentials.** Each tenant has two connection strings.
  `SchemaConnectionString` serves startup activation only, and
  `ConnectionString` serves everything else. The integration tests and the
  development environments run activation as the schema role and everything
  else as the runtime role. See [Database roles](#database-roles).

## Platform database

See
[D42](../decisions.md#d42-tenant-directory-in-the-platform-database--agreed).
The `platform` schema, its migrations, the read-only grant, the secret cipher
and the directory source of the server and the worker are built. `Axis.Admin
tenant create` writes the tables and encrypts the stored passwords and the OIDC
client secret. `tenant suspend` and `tenant resume` change a tenant's status,
and `tenant restore` adds a suspended tenant. Today, under `Tenancy:Source=configuration`
the platform database holds no tables, and its connection string only serves the
readiness check. _(planned for M10)_ That changes: the platform tables exist
under both tenant sources, because the server also keeps its data protection
keys there, see [D49](../decisions.md#d49-several-server-replicas-behind-a-tls-proxy--agreed).
Under `Tenancy:Source=directory` the server creates the tables at
start, then reads the `active` tenants, their hosts and their bindings over
`ConnectionStrings:Platform`. Both hosts read them again every
`Tenancy:RefreshInterval`. The server and the worker keep each password, the client
secret and each `secret` binding encrypted in memory, and decrypt one only when a
connection opens, the sign-in client is set up or a binding is resolved. Neither
reads any `schema_password`. The worker reads the same rows over the same role,
and creates no tables. See
[tenant directory](../architecture.md#tenant-directory).

- **The `platform` schema.** Its EF Core migrations ship with Axis, with their
  history in `platform.__platform_migrations`. The server applies them at
  start over `ConnectionStrings:PlatformSchema`, and uses that setting for
  nothing else.
- **Read-only hosts.** The server reads the directory over
  `ConnectionStrings:Platform`. Its role has `USAGE` on the `platform` schema
  and `SELECT` on its tables, and nothing else. The server sets this in one
  transaction after the migrations, and revokes any wider right the role had
  there. It refuses a role that is the role of `ConnectionStrings:PlatformSchema`.
  It does not yet check whether the role is a superuser, owns the schema or
  inherits rights through membership.
- **One write grant.** _(planned for M10)_ The same role also gets `INSERT` on
  `platform.data_protection_keys`, and on no other table. Key rotation happens
  while the server runs, so the server must add keys. It never updates or
  deletes one. The Studio write lock is a PostgreSQL advisory lock. It needs
  no grant, see [authoring API](authoring.md#versions).
- **The writer.** `Axis.Admin` writes the directory over
  `ConnectionStrings:PlatformSchema`, see
  [administration](administration.md) and
  [D43](../decisions.md#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).

The tables:

| Table                                               | Columns                                                                                                                                                                                                                                                                                                                     |
| --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `platform.tenants`                                  | The tenant `id`, `status` (`provisioning`, `active` or `suspended`), `version`, the database host, port and name, the runtime and schema user names, the encrypted runtime and schema passwords, the OIDC authority, client id and encrypted client secret, the claim names, the endpoint audience, the `schema_version` and `preview_of`, the id of the main tenant of a preview tenant, which is unique and refers to `platform.tenants` |
| `platform.tenant_hosts`                             | The tenant id and the host. The host is unique on `lower(host)` across all tenants                                                                                                                                                                                                                                          |
| `platform.tenant_bindings`                          | The tenant id, the binding name, the `kind` and the value: a URL for a `url` binding, an encrypted value for a `secret` binding. The name is unique per tenant ignoring letter case                                                                                                                                         |
| `platform.data_protection_keys` _(planned for M10)_ | The key's `id`, its `friendly_name`, unique, and the encrypted key XML. Rows are only inserted, never updated or deleted. Every server replica reads them                                                                                                                                                                   |

The tenant `version` grows on every change to the tenant, its hosts or its
bindings.

The `schema_version` is text. It is the id of the newest EF Core migration
across the configuration, data and processes contexts that `Axis.Admin` applied
to the tenant database, and it is empty until the first migration. A host serves
a tenant only when this value is the one the host was built for, see
[administration](administration.md#migrate).

- **Where secrets are stored.** Each of these is stored encrypted, and nowhere
  else:
  - the runtime and schema database passwords, in `platform.tenants`;
  - the OIDC client secret, in `platform.tenants`;
  - the value of a `secret` binding, in `platform.tenant_bindings`.
- **Encrypted format.** An encrypted value is a `bytea` that holds a 12-byte
  nonce, the ciphertext and a 16-byte tag. The cipher is AES-256-GCM with the
  installation key `Platform:SecretKey`, see
  [tenancy settings](configuration.md#tenancy-settings). The associated data
  is the tenant id and the field name, so a value copied to another tenant or
  field does not decrypt. The associated data is the UTF-8 bytes of the tenant
  id, one zero byte, and the UTF-8 bytes of the field name. The field names are
  `runtime_password`, `schema_password`, `oidc_client_secret`, and `binding:`
  followed by the binding name in lower case. Every writer must use these
  exact bytes and names. _(planned for M10)_ A data protection key uses an
  empty tenant id, so the associated data is one zero byte and the field name
  `data_protection_key:` followed by the key's `id`. A tenant id is never
  empty, so a key and a tenant field cannot be swapped.
- **Decryption.** A value is decrypted only at the moment of use, as
  `SecretValue` does today. No API, log, problem details or backup manifest
  holds a decrypted value.
- **No key rotation.** Axis has none. After a leaked key, every stored secret
  must be set again by hand.

## Database roles

See [D31](../decisions.md#d31-separate-database-roles--agreed). The two
settings, the connection each step uses, the grants that limit the runtime
role and the release trigger are built. The integration tests create the two
database roles, `axis_schema` and `axis_runtime`, own each test database with
`axis_schema`, run the server and worker as `axis_runtime`, and run activation
as `axis_schema`. Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the
same: `samples/postgres/init-roles.sql` creates the roles and grants them
nothing. The server and the worker run the start checks for a superuser, a
schema owner or a role that may create objects, also when the right comes
through role membership.

- **Two database roles per tenant.** The schema database role owns the `axis`
  and `entities` schemas. The runtime database role reads and writes data, and
  can change nothing about the schema. "Database role" always means this
  PostgreSQL role. It is not the application `role` resource of
  [D24](../decisions.md#d24-roles-and-policies--agreed).
- **Two settings.** `ConnectionString` is for the runtime database role, and
  `SchemaConnectionString` is for the schema database role. See
  [tenant configuration](../architecture.md#tenant-configuration). There is no
  fallback to one connection string.

What each database role may do:

| Objects                                                                                                                                                                                                                                     | Schema database role                                                        | Runtime database role                                             |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `entities` tables                                                                                                                                                                                                                           | Owns them                                                                   | Read and write rows                                               |
| `axis` tables that runtime writes: `process_*`, `connector_calls`, `package_calls`, `events`, the receipts, `trigger_schedules`, `trigger_starts`, `audit_records`, `sequence_counters`, `computed_field_errors`, `pending_computed_fields` | Owns them                                                                   | Read and write rows                                               |
| Tables that activation writes: `active_releases`, `active_sites`, `provisioned_entities`, `provisioned_enum_values`, `applied_migrations`, `release_computed_fingerprints`, the `__*_migrations` tables                                     | Owns them                                                                   | Read only                                                         |
| Release tables: `releases`, `release_resources`, `release_packages`                                                                                                                                                                         | Insert and read. The trigger refuses update, delete and truncate            | Read only                                                         |
| `publish_requests`                                                                                                                                                                                                                          | Owns it. The publisher changes the status and the lease _(planned for M10)_ | `SELECT` and `INSERT` only. It never updates or deletes a request |
| `release_activations` _(planned for M10)_                                                                                                                                                                                                   | Insert and read. The trigger refuses update, delete and truncate            | Read only                                                         |
| Sequences in `axis` and `entities`                                                                                                                                                                                                          | Owns them                                                                   | `USAGE`, `SELECT`                                                 |
| Create, alter, drop                                                                                                                                                                                                                         | Yes                                                                         | No                                                                |

- **Default privileges.** The schema database role sets default privileges. A
  new table in `entities` gives the runtime database role `SELECT`, `INSERT`,
  `UPDATE` and `DELETE`. A new table in `axis` gives it `SELECT` only. The
  grant step also clears the database-wide default privileges that older
  setups set, because a per-schema default cannot narrow them.
- **Explicit grants.** The grant step holds the list of `axis` tables that
  runtime writes. It grants row access on those and revokes `INSERT`, `UPDATE`,
  `DELETE` and `TRUNCATE` on every other `axis` table, so a database with
  older, wider grants is repaired. A new system table is read-only until
  someone adds it to the list. The list has a second
  category, tables where runtime may only insert. `publish_requests` is the
  only one. It grants `SELECT` and `INSERT` and revokes `UPDATE`, `DELETE` and
  `TRUNCATE`, so the server cannot change the status of a request.
- **Role name.** The runtime database role is the user of `ConnectionString`.
  The name comes from configuration, so a migration cannot hold it. A grant
  step runs over the schema connection at every server start, both before and
  after the EF Core migrations. The first run sets the default privileges for
  the tables the migrations create. The second run grants the tables that now
  exist. The step refuses a runtime database role that is the schema database
  role.
- **Release trigger.** A trigger refuses every `UPDATE`, `DELETE` and
  `TRUNCATE` on the three release tables, for every database role, with
  SQLSTATE `23001`. An `Axis.Configuration` migration creates it. It backs the
  change-tracking guard described above, which stays and fails first. A stored
  release can no longer be deleted at all.
  Cleaning up old releases needs a later decision. A superuser or the table
  owner can still disable the trigger. It guards against mistakes by Axis, not
  against a database administrator.
- **Start checks.** A missing or blank setting stops the start, naming the
  tenant and the setting. The server does this today. So does a
  `ConnectionString` that names no user, and two settings that name the same
  user. So does a runtime database role that is a superuser, owns the `axis` or
  `entities` schema, or may create objects in either. A role that is a member
  of the role that owns a schema counts as its owner, because it can `SET ROLE`
  to the owner. The same goes for a member of a superuser role and for a
  member of any role with `CREATE` on either schema, whatever the `INHERIT`
  option of the membership. The message names the tenant, the role and the
  first failed check, in the order superuser, owner, `CREATE`, with `axis`
  before `entities`. When the right comes through another role, it also names
  that role. It never holds the connection string or the password. The
  server runs the role check after its grant step, so a fresh database with the
  right roles starts, and again in its start-time release check, which runs for
  every tenant even when no folder is activated. The worker runs it once for
  each tenant, when the tenant's database counts as migrated and before it
  claims any work of that tenant. The server skips the check for a tenant
  whose database it cannot reach, as the release check does. The checks do not
  cover `CREATE` on the database itself, ownership of single tables or
  `CREATEROLE`. The server runs all the other checks. The worker validates
  `ConnectionString` and the runtime database role. The server also validates
  `SchemaConnectionString` and that the two settings name different users,
  because the worker holds no `SchemaConnectionString`.

Which connection string each step uses. The server does this today:

| Step                                                                                                                               | Connection string        |
| ---------------------------------------------------------------------------------------------------------------------------------- | ------------------------ |
| EF Core migrations of configuration, data and processes                                                                            | `SchemaConnectionString` |
| Compiling a folder and storing its release                                                                                         | `SchemaConnectionString` |
| Activation: migrations, provisioning, active release, site paths                                                                   | `SchemaConnectionString` |
| Grants for the runtime database role, before and after the migrations                                                              | `SchemaConnectionString` |
| Schedule trigger state                                                                                                             | `ConnectionString`       |
| Seed records                                                                                                                       | `ConnectionString`       |
| Publish routes of the server _(planned for M10)_: insert a request, read requests and the release history                          | `ConnectionString`       |
| Publisher _(planned for M10)_: claim, renew and finish a request, compile, store, activate, write the history and the audit record | `SchemaConnectionString` |
| Publisher _(planned for M10)_: seed records                                                                                        | `ConnectionString`       |
| Start release check (`AXC0146`) and the runtime database role check, in the server and the worker                                  | `ConnectionString`       |
| Server requests: record API, data sources, process starts, endpoints, tasks, sites                                                 | `ConnectionString`       |
| Worker                                                                                                                             | `ConnectionString` only  |

The server holds both connection strings for its whole life. It opens the
schema connection only during startup activation, and closes it when each
tenant is done. Under `Tenancy:Source=directory`, the
server holds no tenant schema connection, see
[under the directory source](#under-the-directory-source).

_(Planned for M10:)_ the publisher, `axis-admin publisher`, runs every
publish request. Under `Tenancy:Source=configuration` it uses each tenant's
`SchemaConnectionString`, so it holds the schema role. The server still opens
that connection only during startup activation, and runs no publish request
itself.

### Under the directory source

See
[D43](../decisions.md#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).
Under `Tenancy:Source=directory`, `tenant create` creates
the two database roles of a tenant, its database and the ownership, with no
superuser. See [administration](administration.md) for the admin commands. Each step below runs on one
host with one database role:

| Step                                                                                                                    | Host                            | Database role                      |
| ----------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ---------------------------------- |
| Create the database and the two roles                                                                                   | `Axis.Admin`                    | `Admin:ConnectionString`           |
| Directory writes                                                                                                        | `Axis.Admin`                    | `ConnectionStrings:PlatformSchema` |
| Platform migrations, at start                                                                                           | Server                          | `ConnectionStrings:PlatformSchema` |
| Grants, EF Core migrations, compile, store and activate                                                                 | `Axis.Admin`                    | The tenant's schema role           |
| Seeding and schedule trigger state                                                                                      | `Axis.Admin`                    | The tenant's runtime role          |
| Claim, run and finish a publish request: compile, store, activate, release history and audit record _(planned for M10)_ | `axis-admin publisher`          | The tenant's schema role           |
| Seeding of a publish request _(planned for M10)_                                                                        | `axis-admin publisher`          | The tenant's runtime role          |
| Insert and read publish requests, read the release history _(planned for M10)_                                          | Server                          | The tenant's runtime role          |
| Runtime role check                                                                                                      | `Axis.Admin`, server and worker | The tenant's runtime role          |
| Requests and work items                                                                                                 | Server and worker               | The tenant's runtime role          |

- **No tenant schema connection.** The server holds no tenant schema
  connection. It runs no tenant migration and no grant step. The worker never
  held one. This stays true when Studio publishes _(planned for M10)_: the
  server only inserts a request over the runtime role, and `axis-admin
publisher` runs it with the tenant's schema role.
- **The publisher.** _(Planned for M10.)_ It reads the tenants from the
  directory as the server does and holds the schema role of every active
  tenant. It becomes the most valuable host to attack, so run it apart from the
  server and give it no inbound network. It has no HTTP endpoint, see
  [publisher](administration.md#publisher).
- **Platform schema.** The server still holds `ConnectionStrings:PlatformSchema`
  at start, for the platform migrations. So "no schema connection" means no
  tenant schema connection.
- **Tenant schema version.** Each tenant database migrates on its own, with
  `Axis.Admin migrate`, which records the version in the directory. A host
  serves a tenant only when the tenant's schema version is the host's own, see
  [tenant directory](../architecture.md#tenant-directory).

## Schema planning

`Axis.Data` compares a compiled application with a snapshot of the tenant
catalog (entity tables, their columns, types, `NOT NULL`, single-column
unique constraints, foreign key targets and whether the table has rows) and
with the provisioning records (each provisioned entity with its application
and table, and each recorded enum value). The records cover the application's
entities and any entity recorded with one of its entity `id`s, whatever its
application. The planner itself has no database
access. It returns diagnostics, SQL statements and the new records to write.

- **Migrations.** The planner first applies the pending `renameField`,
  `dropField`, `dropEntity`, `fill` and `convert` [migrations](#migrations) to
  its catalog snapshot and its copy of the provisioning records, in ordinal path
  order and file order. It then plans as today. A `convert` makes the planner
  accept a type change, or enum values the release removes, where it gives
  `AXC0016` without one. A `fill` or `convert` clears the release stamp of the
  rows it changes. On a child entity it also increments the owner's `version`
  and clears the owner's stamp, for each owner of a changed row. Activation then
  recomputes those rows, see
  [Recomputing computed fields](#recomputing-computed-fields). A `fill` or
  `convert` that names, in its value, a field that is not a column of the stored table is
  `AXC0171` at `/changes/{i}/value`. A `fill` whose value leaves a `null` is
  `AXC0176` at `/changes/{i}/value` when activation applies it. A `convert`
  that fails for a stored row, or maps an enum value to one the field does not
  list, is `AXC0177` at `/changes/{i}/value`. Switching a field between `enum`
  and another type stays `AXC0016`, even with a `convert`. Removals and incompatible
  changes not covered by a migration keep `AXC0015`, `AXC0016` and
  `AXC0017`. A change that does not match the stored schema is skipped: the
  table does not exist, the `from` column is missing or the `to` column
  already exists. The migration is still recorded when activation succeeds,
  so a fresh tenant activates a release that has migrations. A skip cannot
  lose data, because a leftover column or table still gives `AXC0015` or
  `AXC0017`. A migration's `entity` name is looked up first in the
  application's active release, read from its stored resources, because that
  release describes the tables that hold the data now and only it has a
  dropped entity. It is then looked up in the release being activated. A
  rename also renames the field in the recorded enum values, and a drop
  deletes them.
- **System columns.** `id`, `version` and `computed_release_id` are created
  with the table, or `id`, `owner_id` and `position` for a child table (see
  **Child table** below). `version` is added to an existing table other than a
  child table that lacks it as
  `bigint NOT NULL DEFAULT 1`, even when the table has rows, because the
  default fills them. `computed_release_id` is added to such a table as
  `uuid`, nullable and with no default, so the rows are kept and stay
  unstamped. See [Recomputing computed fields](#recomputing-computed-fields).
  System columns are never compared or changed otherwise:
  only Axis DDL creates them, and an author cannot fix them through
  configuration.
- **Missing table.** It is created with the system columns and every field
  column, `NOT NULL` for required fields and a unique constraint for unique
  fields. A `child-collection` field has no column. The entity and its enum
  values are recorded.
- **Child table.** The owner of a child entity is the entity whose
  `child-collection` field names it; the compiler ensures there is exactly
  one.
  - A missing child table is created with `id`, `owner_id`, `position` and
    its field columns, and no `version`. Its owner foreign key is added with
    the other foreign keys, so the owner table exists whatever the entity
    order. Adding a child collection and its child entity to an entity that
    already has rows only creates the child table: the owner table and its
    rows are untouched.
  - An existing child table whose `owner_id` has no foreign key gets one. A
    foreign key that points at another table than the owner's is `AXC0016`
    at the owner's `/fields/{i}/target`.
  - An existing table without `owner_id` cannot become a child table, because
    adding a `NOT NULL` owner column needs a migration. It is `AXC0016` at the
    owner's `/fields/{i}/target`.
  - An entity that stops being a child keeps `owner_id` and `position`. They
    are reported as removed fields (`AXC0015`), like any column without a
    field.
- **Missing column.** It is added. `NOT NULL` is added only when the table
  has no rows; a required field added to a table with rows is `AXC0016` at
  `/fields/{i}/required`, unless a pending `fill` covers it. The `fill` adds
  the column as nullable and fills it, and the planner then makes it
  `NOT NULL`. A unique field also gets its unique constraint.
- **Existing column.**
  - The type must equal the expected type. Widening text
    (`character varying(n)` to a larger `n` or to `text`) is applied.
    Narrowing it, including `text` to `character varying(n)`, is `AXC0016` at
    `/fields/{i}/maxLength`. Any other type difference is `AXC0016` at
    `/fields/{i}/type`.
  - Dropping `required` drops `NOT NULL`, and dropping `unique` drops the
    unique constraint. Adding either to an existing column is `AXC0016` at
    `/fields/{i}/required` or `/fields/{i}/unique`. A pending `fill` of the
    field allows `required`: the column becomes `NOT NULL` after the `fill`.
  - A reference column without a foreign key gets one. A foreign key that
    points at another table than the target's is `AXC0016` at
    `/fields/{i}/target`.
- **Enum values.** Values are compared ordinally, so letter case matters.
  Recorded values missing from the field's `values` are one `AXC0016` at
  `/fields/{i}/values` listing them; new values become new records and need
  no SQL. A column switching between `enum` and another type (recorded values
  present for a non-enum field, or absent for an enum field) is `AXC0016` at
  `/fields/{i}/type`.
- **Removed field.** A column other than the table's system columns without
  a matching field is `AXC0015` at `/fields`, naming the column.
- **Removed entity.** An entity recorded for the application but missing from
  it is `AXC0017` at `application.json` with an empty path and the entity's
  `id` as `resourceId`.
- **Entity owned by another application.** Tables are keyed by entity `id`,
  so an entity whose `id` is recorded for another application, for example in
  an application copied with only its manifest `id` changed, would share that
  application's table. It is `AXC0018` at `/id` of the entity file, and the
  entity is not planned.
- **Labels.** A label change produces no statements.
- **Order.** Every `CREATE TABLE` comes first, then the `ALTER TABLE` column
  changes, then every `ADD CONSTRAINT ... FOREIGN KEY`, so references between
  entities, cycles and self-references need no further ordering. Pending
  migration statements run before these additive statements. Then stored
  computed fields are recomputed inline, or marked pending above the inline
  limit for the worker to recompute.
  See [Order of statements](#migrations) and
  [Recomputing computed fields](#recomputing-computed-fields).
- **Errors.** When any diagnostic exists, the plan has no statements and no
  new records; nothing is applied.

## Child tables and computed columns

Child tables and computed columns are built (D17). The planning rules of
child tables are in [Schema planning](#schema-planning).

- **Child table.** The entity that a `child-collection` field names has a
  table with the usual names. Besides `id` (`uuid`) and its field columns, it
  has two system columns:

  | Column     | Type               | Meaning                                                                                                                              |
  | ---------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
  | `owner_id` | `uuid NOT NULL`    | The owner record's `id`. A foreign key to the owner table's `id` with `ON DELETE CASCADE`, named `fk_` + table + `_` + hash as usual |
  | `position` | `integer NOT NULL` | The row's order in the collection, the zero-based index in the array                                                                 |

  Rows are deleted with their owner.

- **No version.** A child table has no `version` column. Its rows live under
  the owner's version, and a write to the rows increments the owner's
  `version`.
- **Computed column.** A computed field is an ordinary
  column of its type. It is never `NOT NULL`, because a computed field cannot
  be `required`. It is written in the same transaction as the record, and for
  a child row before the owner's computed fields.
- **Existing rows.** A computed field added to a table that has rows is
  filled at activation through
  [Recomputing computed fields](#recomputing-computed-fields). A row whose
  expression fails gets `NULL` and an error row, and activation goes on.

## Recomputing computed fields

This section records
[D33](../decisions.md#d33-recomputing-stored-computed-fields--agreed). The
stamp, the fingerprint, the inline recompute at activation, the stamp clearing
of a `fill` or `convert`, the inline limit, the pending state and the worker
backfill are built, and so are the restart by a newer activation and the writes
of older releases. Activation recomputes every row of an entity whose computed
definitions changed, and the rows a `fill` or `convert` changed, in its own
transaction, up to the inline limit. Above the limit it leaves the entity
pending and the worker recomputes it. A single read, the response of an update,
the engine's reads of a subject record and process start conditions answer
while a field is pending as the "While pending" list below says. So do the
refusals of the record API and the process endpoints, for a sort or search on a
pending field and for a policy filter that reads one. Data sources and schedule
fan-outs refuse the same way.

One recompute covers three cases: a changed computed expression, a computed
field added to a table that has rows, and a migration `fill` or `convert` of a
field that a computed field reads. A table with few affected rows recomputes
inside the activation transaction. A table with many does it in the worker,
in batches.

- **Affected rows.**
  - A changed or added computed field affects every owner row of its entity.
  - A changed child computed field also makes pending every owner computed
    field that reads it through an aggregate. The owner's stored total is as
    stale as the line amounts it sums.
  - A `fill` or `convert` affects the rows it changed, and the owner rows of
    changed child rows.
- **Recompute at activation** _(built)_. After the additive DDL, activation
  compares the fingerprint of each entity that is not a child entity with the
  active release's, read from `axis.release_computed_fingerprints`. It
  recomputes every owner row of an entity whose fingerprint is missing or
  differs, with the release being activated. It recomputes every computed field
  of the entity and of its child entities, not only the changed ones, and the
  owner's aggregates read the new child values. The release being activated gets
  its fingerprints from its compiled model, because a release found again by
  content hash comes back without them. Activating the release that is already
  active recomputes nothing, even if it has no stored fingerprints. On the first
  activation of an application every entity counts as changed, which costs
  nothing because the tables are new. A row is updated only when its `version`
  still matches the version that was read, so a concurrent write is never
  overwritten.
  - Then, for each entity that a `fill` or `convert` touched, and that the
    step did not just recompute whole, it recomputes the owner rows whose
    stamp is `NULL`. The entity is the one whose rows the migration changed,
    or the owner of a changed child entity. It recomputes the computed fields
    of the owner and its child entities, deletes the error rows of those
    records only, and stamps the rows. It does this even when no computed field
    reads the changed field, which only stamps the rows again. Rows that
    already had a `NULL` stamp are recomputed too. Above the inline limit
    the rows go to the backfill instead, see _Inline_ and _Pending_.
- **Inline** _(built)_. Activation counts the affected rows of each entity.
  For a changed computed definition that is every row of the table. For a
  `fill` or `convert` it is the rows of the entity left with a `NULL` stamp.
  An entity with both counts every row once. When the count is at most
  `Activation:InlineRecomputeLimit`, activation recomputes the rows in its own
  transaction, with the release being activated. It stamps each row. The limit
  is a [server setting](configuration.md#startup-activation) and defaults to
  10,000.
- **Pending** _(built)_. Above the limit, activation writes a row in
  `axis.pending_computed_fields` and a `computed.backfill` work item, then
  commits without touching a row. The computed field is now _pending_: its
  stored values may still come from an older expression. The pending row lists
  every computed field of the entity and of its child entities. Activating a
  newer release while the entity is pending restarts it, see _Restart_.
- **Stamp.** Each table that is not a child table has the system column
  `computed_release_id uuid NULL`. It names the release that last computed the
  row. Adding it is cheap on a large table, because it is nullable and has no
  default.
  - A create stamps the release it ran with. So does an update that sends, and
    so recomputes, every child collection whose child entity has computed
    fields. That includes a record API write, an inbound endpoint create, a step
    of a process pinned to an older release, a connector or package result
    write, a task completion and seed data. Work pinned to an older release
    stamps that release, not the active one. A row written before the column
    existed has no stamp.
  - An update that leaves out such a collection did not recompute its rows. It
    keeps the stamp only when the stamp already names the update's release.
    Otherwise it sets the stamp to `NULL`. A step, a connector or package result
    write and a task completion never send child rows, so they clear the stamp
    of a row that has child rows with computed fields, unless it already names
    their release. A collection whose child entity has no computed field does
    not count. A `NULL` stamp counts as not up to date.
  - Child rows recompute with their owner and carry no stamp.
  - A `fill` or `convert` sets the stamp to `NULL` on every row it changes and
    on the owner row of every changed child row. Their computed inputs changed,
    so the old stamp no longer proves the row is up to date. Activation then
    recomputes the rows of each touched entity that have a `NULL` stamp, and
    stamps them with the release being activated. Above the inline limit it
    leaves them to the backfill, which recomputes and stamps them with the
    same release.
- **Fingerprint.** Each release stores, for each entity that is not a child
  entity, one hash of the computed definitions of the entity and its child
  entities, and of every rule their expressions call. It is written with the
  release in `axis.release_computed_fingerprints`.
  - The hash covers each computed field's name, type, length or precision
    settings and expression text, for the entity and for each child
    collection. It also covers each rule the expressions call, directly or
    through other rules, with its parameters, result type and expression text.
    Two releases with the same computed definitions have the same fingerprint,
    even when other parts of the entity differ. A change to whitespace only in
    an expression changes it.
  - A child entity has no row, because its rows carry no stamp and recompute
    with their owner. An entity with no computed field still has a row, so
    adding one later shows as a changed fingerprint.
  - A release stored before fingerprints existed has no rows, and nothing fills
    them in. A missing fingerprint counts as different.
- **Up to date.** A row is up to date when its stamp names a release whose
  fingerprint for the entity equals the target release's. The pending row
  keeps the ids of those releases as `current_release_ids`. A row with a `NULL`
  stamp is not known to be up to date. Today only an activation that changes
  its entity's computed definitions, an activation whose `fill` or `convert`
  touches the entity, or a later full write, fixes it. The backfill treats a
  row with no stamp as out of date, but only while the entity has a pending
  field. Otherwise every release that changes nothing about a computed field
  would recompute every row of every table. The pending row keeps the releases
  whose stored fingerprint for the entity equals the target's, and always the
  target itself, because a release found again by content hash may come back
  without its fingerprints. After a large `fill` the backfill also recomputes
  rows stamped by an older release that has no stored fingerprints.
- **Older-release writes** _(built)_. A write whose release has a different
  fingerprint from the active release's inserts the pending row and the
  `computed.backfill` work item in its own transaction, when they are missing.
  It compares two stored fingerprints, so the check is cheap. Without this, a
  row that pinned work writes after the backfill ends would stay stale with no
  pending flag.
  - Four paths can write under a release that is not the active one: a task
    completion, a process step update, and a connector or package result
    write. Each calls the check right after its record write. A write on the
    active release, such as the record API, an inbound endpoint or seed data,
    adds nothing. Neither does a write whose release stores the same
    fingerprint as the active release. A release with no stored fingerprint
    counts as different.
  - The write takes a `FOR KEY SHARE` lock on the pending row, after it locked
    the record row. That lock conflicts only with the delete of a backfill's
    last batch, so it blocks neither activation nor other writes. When there is
    no pending row, the write inserts one. Its target is the active release, and
    its fields are the computed fields of the write's own release model.
  - The work item stores the trace and span ids of the write, as the work item
    of an activation does. One helper inserts every `computed.backfill` work
    item, and it inserts none when the entity has one.
  - The final delete of a backfill locks the same pending row, so a write
    cannot slip between the last check and the delete. See _End_.
- **Batches** _(built)_. The worker claims a `computed.backfill` work item and runs one
  batch of 500 rows, in `ORDER BY id`, in its own transaction.
  - It selects out-of-date rows with their `version`, recomputes them with the
    application's active release, and runs
    `UPDATE ... WHERE id = $1 AND version = $2`. A row whose version changed is
    skipped. A later batch takes it if its stamp is still out of date.
  - The pending row can name a release older than the active one, when a write
    inserted it while an activation was still committing. The batch then
    computes with the active release, and the up-to-date releases are those
    whose stored fingerprint for the entity equals the active release's.
    Computing with the old target would roll the table back to old values.
  - A batch holds no lock on the pending row. Pinned writes lock the record row
    before the pending row, so a batch that held the pending row while it
    updated rows could deadlock with them. Only the last check locks it, see
    _End_.
  - The update does not change `version`. It writes no audit record and starts
    no trigger. A higher `version` would give a user who is editing the record
    a `409` for a change they did not make.
  - The work item is made due again at once, so the next claim runs the next
    batch, until a batch finds no out-of-date row. A batch keeps no cursor. It
    selects the first 500 out-of-date rows again, so a skipped row is taken
    by a later batch. A row that users keep updating can be skipped batch
    after batch, and then the item stays queued until the row is left alone.
  - A row whose expression fails gets `NULL` and an error row, as at
    activation. The batch deletes the error rows of its records first.
- **Row errors** _(built for the recompute at activation)_. A row whose
  expression fails at run time, or reads a decimal that cannot be held exactly,
  gets `NULL` and its stamp. The recompute inserts a row in
  `axis.computed_field_errors` with the entity, the record id, the field, the
  release and a fixed error text, the message the record API gives for that
  failure. It stores no value, because the error can name one. The row no longer
  stops the work. This replaces the rule of D30 that one run-time error rolls
  activation back. A failing field of a child row is keyed by the child entity
  id and the child row id, because child rows have their own ids. Before it
  recomputes an entity, activation deletes the error rows of that entity and of
  its child entities, because it computes every row again.
- **Restart** _(built)_. Activating a release while a field is pending sets the
  target to the new release. It recomputes `current_release_ids`, merges the
  pending fields and applies the inline limit again. The backfill continues
  with the newest release. This holds for every pending entity of the
  application, also when its fingerprint did not change.
  - Inline: activation counts the rows that are out of date for the new
    release's current releases. When there are at most
    `Activation:InlineRecomputeLimit`, it recomputes them in its own
    transaction. If none is left, it deletes the pending row and the work item.
  - Above the limit, it updates the pending row and keeps the work item. The
    merged field list can hold a name that only an older release had, such as
    a renamed computed field, until a later restart replaces it.
- **End** _(built)_. The batch that finds no out-of-date row locks the pending row
  `FOR UPDATE`, checks the table again, and deletes the row and completes its work
  item if still no row is out of date. The lock waits for a write that holds the
  row `FOR KEY SHARE`, and the check then sees that write's row. If a row is out
  of date, the batch reschedules the item and the row stays. A write after the
  delete finds no pending row and inserts a new one. The field is no longer
  pending. When the active release no longer has the entity, the batch deletes
  the pending row and the item without a batch.
- **While pending.**
  - _(Built.)_ A single read, the response of an update, and the engine's
    reads of one subject record compute a pending field with the active
    release and do not store the result. Process start conditions and the
    `when` of a data change trigger read the same way. A read must not write.
    The backfill or the next write stores the value. A run-time error reads as
    `null`. A row is computed only when its stamp is out of date. An instance
    pinned to an older release takes a computed value only for a field with
    the same name and type in both releases. The inbound endpoint's output and
    the test scenario runner show the stored values.
  - A list, a data source and a projection show the stored value.
  - _(Built for the record API and the process endpoints.)_ A record list
    sort or search that names a pending field is refused with `409`. A policy
    `read` filter that reads one is refused too, for a list, a single read, a
    history, a process start, the process instance list, the instance detail
    and an operator cancel or retry, and so is an `update` or `delete` filter.
    The check reads the pending state of the entity, not the stamp of a row, so
    it holds until the backfill ends. See the
    [record API](record-api.md#paging-and-sorting).
  - A data source sort, filter or grouping that reads a pending field is
    refused with `409`, and so is a root read filter that reads one. A schedule
    fan-out whose data source filter or grouping reads one starts nothing and
    runs again one lease later. See [data sources](data-sources.md#errors) and
    [schedules](processes.md#schedules).

Tables, both owned by `Axis.Data`. The runtime database role may write both.
Activation writes both, and the worker writes them too. So does a record write
that finds an older release. The work item
of a pending entity holds the entity `id` in `axis.process_work_items.entity_id`.

`axis.pending_computed_fields`, one row per entity with a pending field:

| Column                | Type          | Meaning                                                                              |
| --------------------- | ------------- | ------------------------------------------------------------------------------------ |
| `entity_id`           | `uuid`        | Primary key, the entity `id`                                                         |
| `application_id`      | `uuid`        | The application `id`                                                                 |
| `target_release_id`   | `uuid`        | The release the backfill computes with, the newest activated                         |
| `current_release_ids` | `uuid[]`      | The releases whose fingerprint for the entity equals the target's                    |
| `fields`              | `text[]`      | The pending field names, including owner fields that aggregate a pending child field |
| `created_at`          | `timestamptz` | When the field became pending                                                        |

`axis.computed_field_errors`, one row per record and field whose expression failed:

| Column        | Type          | Meaning                                           |
| ------------- | ------------- | ------------------------------------------------- |
| `entity_id`   | `uuid`        | The entity `id`                                   |
| `record_id`   | `uuid`        | The record id                                     |
| `field`       | `text`        | The computed field name                           |
| `release_id`  | `uuid`        | The release that computed the row                 |
| `error`       | `text`        | A fixed error text. It never holds a stored value |
| `occurred_at` | `timestamptz` | When the recompute recorded it                    |

The primary key is `(entity_id, record_id, field)`, and a later error for the
same key replaces the row. `axis.release_computed_fingerprints` belongs to
`Axis.Configuration`, next to `axis.releases`. It has `release_id`, `entity_id`
and `fingerprint` (`varchar(64)`, SHA-256 as lower-case hex), with
`(release_id, entity_id)` as its primary key. It is written when the release is
stored. The runtime database role reads it.

### Example: changing a computed expression on a large table

This example uses the purchase request. R1 and R2 stand for stored release
UUIDs of the same application, as in the
[migrations example](#example-renaming-a-field-while-a-task-waits). The table
holds about 50,000 purchase requests. R2 changes the `amount` expression of the
line item, so `amount` and the aggregate `total` of the request become
pending. Every step is built, and the record API answers step 2. A data
source refuses the same way.

1. R2 is activated. Activation counts 50,000 affected rows, which is more than
   10,000. It inserts the pending row for the purchase request, with R2 as the
   target and `amount` and `total` as the fields. It queues a
   `computed.backfill` work item and commits. No row changes.
2. A list sorted by `-total` answers `409`, "A field is being recomputed.",
   with `errors.sort` naming `total`. A list with no sort answers with the
   stored values.
3. A single read of one request returns `total` computed on the spot with R2.
   It stores nothing.
4. A user updates a request. The update sends the line items, so the write
   recomputes them with R2 and stamps R2. The backfill skips this row later,
   because its stamp is up to date.
5. The worker claims the work item and runs batches of 500 rows. Each batch is
   one transaction. Each stores R2 values and stamps R2, with no change to
   `version`, no audit record and no trigger.
6. A task completion pinned to R1 writes a purchase request. The row gets R1
   values and an R1 stamp. R1's fingerprint differs from R2's, so a later batch
   takes that row again. The pending row already exists, so the write adds
   nothing.
7. One row has a quantity that makes the R2 expression overflow. Its batch sets
   `total` to `null`, stamps the row and inserts a row in
   `axis.computed_field_errors` with the field and a fixed error text. The
   backfill goes on.
8. A batch finds no out-of-date row. It deletes the pending row and its work
   item. Sorting by `total` works again.
9. Later, another task completion pinned to R1 writes a request and stamps R1.
   Its transaction sees that R1's fingerprint differs from the active
   release's, so it inserts the pending row and a new work item again. The next
   backfill takes only that row.

## Migrations

The migration resource, its compile checks and the once-per-tenant apply of
`renameField`, `dropField`, `dropEntity`, `fill` and `convert` at activation are
built, and so are the start lock and the old-release work check. The recompute
of computed fields at activation is built too, and so is its use by a `fill` or
`convert`, on a child entity or a field that a computed field reads. Rule calls
in computed fields remain unbuilt (`AXC0050`). This section records
[D30](../decisions.md#d30-schema-migrations--agreed).
A [migration resource](configuration.md#resource-file-shape) changes one
entity's storage and runs once per tenant, before additive provisioning.
Migrations run forward only.

- **Pending.** A migration is pending when `axis.applied_migrations` has
  no matching row for its resource `id` in the tenant database.
- **Order of statements.** An activation with a pending migration runs
  these steps in one transaction:

  1. Take the existing schema lock, `pg_advisory_xact_lock`. _(Built.)_
  2. Take the application's start lock exclusively. _(Built.)_ An activation
     with no pending migration skips this step. The pending migrations are
     known only after reading `axis.applied_migrations`, so the lock comes
     right after that read and before any changed-history diagnostic is acted on.
     The schema lock already keeps those rows from changing in between.
  3. Check changed history. Any diagnostic stops the activation. _(Built.)_
     Right after it, the activation also checks the module upgrade: a module
     copy that drops an entity or a field, or changes a field's type, against
     the active release's copy is `AXC0213`. This check runs whether or not a
     migration is pending, and any diagnostic stops the activation with
     nothing applied. _(Built.)_ See [Modules](configuration.md#modules).
  4. Count old-release work for the destructive changes below. Any such
     work stops the activation with `AXC0178`. _(Built.)_ The count runs
     after planning and before the first statement, and only when the plan
     holds a destructive change that plans a statement.
  5. Run pending migrations in ordinal path order, and changes in file order.
     _(Built for `renameField`, `dropField`, `dropEntity`, `fill` and `convert`.
     A `fill` runs one `UPDATE` that counts the rows it left `null`. Any such row
     is `AXC0176` and rolls the activation back. A `convert` that fails for a
     row, or leaves an enum value the field does not list, is `AXC0177` and
     rolls the activation back.)_
  6. Run additive DDL in the order used today. _(Built.)_
  7. Recompute inline or mark pending, see
     [Recomputing computed fields](#recomputing-computed-fields), using the
     release being activated. _(Built, including the inline limit and marking
     pending. It also recomputes the rows that a `fill` or `convert` changed,
     and the owners of changed child rows, or leaves them pending above the
     limit.)_ An activation without a pending migration runs the same step after
     its additive DDL.
  8. Write `axis.applied_migrations` and the provisioning records. _(Built.)_
  9. Upsert the active release and its `axis.active_sites` rows. _(Built.)_
  10. Commit, releasing both locks. _(Built.)_

  The fixed lock order is schema lock, then start lock. Process starts
  never take the schema lock, so this order cannot deadlock with them.
  A start takes the start lock as the first statement of its transaction,
  before it reads or writes any entity table. Otherwise a start that already
  holds a table lock could wait for the activation, which then needs an
  exclusive lock on that table, and PostgreSQL would abort one of them.

- **Changed history.** An applied migration with a changed content hash or
  a missing file is `AXC0174`. Nothing runs. A changed file is reported at
  that file with an empty path, and a missing file at `application.json`
  with an empty path. The `resourceId` is the migration `id`. The hash is
  the SHA-256 of the file's canonical JSON alone, without its path, so
  moving a file does not count as changing it. Activating a release
  that lacks an applied migration file is refused, so an older release
  without that file cannot be activated again. The way back is a new
  release with a new forward migration.
- **Old-release work** _(built)_. After taking both locks, count `running` and
  `waiting` process instances pinned to a release other than the one being
  activated whose stored model has the column the change affects. This applies to
  `renameField` (its `from` column), `dropField`, `dropEntity` (the dropped
  entity and the child entities dropped with it) and `convert`. A release
  has the column when its stored entity has the same entity id and a field of
  that name, ignoring letter case. For each such release and migration,
  `AXC0178` names the migration, the release and the count of open instances.
  It is reported at the migration file with an empty path, and the migration
  `id` is its `resourceId`. Activation then changes nothing. A release whose
  stored content cannot be found counts as using every column. A `fill`, an
  additive change and a change that plans no statement never wait for
  old-release work. Instances of the release being activated and of other
  applications do not count. Once the old instances end, activating the same
  release again applies the migration.
- **Start lock** _(built)_. Every
  transaction that reads the active release and inserts a process instance
  takes `pg_advisory_xact_lock_shared` keyed on the application `id`, before
  it reads the active release. The key is a `bigint`: the XOR of the two
  64-bit halves of the id. Another key can collide with it, which only makes
  a start wait longer. This covers the process start endpoint, the inbound
  endpoint, record API creates and updates, task completion, event delivery,
  `sync` event triggers, update triggers from worker steps and schedule
  fan-out. Operator
  [retry](processes.md#retry) and [resolve](processes.md#resolve) take it
  too. A record API create or update always takes it, because the new
  release may add triggers. A delete never starts a process and does not. An
  activation with a pending migration takes the same lock exclusively, using
  `pg_advisory_xact_lock`, and holds it to commit. It waits for open starts
  and holds new ones until commit. A start either commits before the
  activation counts open work, or reads the new release after the activation
  commits. An activation with no pending migration never takes the lock.
  A request that finds a different active release under the lock runs
  again on the new release. See
  [Releases and running work](processes.md#releases-and-running-work).
- **Failure.** Any failure rolls back migrations, additive provisioning,
  migration and provisioning records, the active release and its site paths.
  This includes a refused application name or site path at the upsert.
  The active release does not change. A retry runs the pending migrations
  again because nothing stayed applied. A `dropEntity` that a remaining
  foreign key blocks surfaces as a database exception, not a diagnostic, and
  everything still rolls back. The active release is always written through
  the active release store in the provisioning transaction, under the schema
  lock held since the migration history check, so another activation cannot
  commit a rename or drop between them. The store and the data context
  must therefore share one tenant connection. A store on another connection
  throws before anything commits.
- **Computed fields.** A `fill` or `convert` of a field that a computed field
  reads recomputes through
  [Recomputing computed fields](#recomputing-computed-fields). `fill` and
  `convert` clear the stamp of the rows they change, and of the owner of each
  changed child row. Activation recomputes those rows inline, or leaves them
  pending for the worker above the inline limit. Rule calls in
  computed fields remain unbuilt (`AXC0050`), so the case through a rule
  describes future behaviour.

| Change           | SQL                                                                                                                                                                                                                                                            | Waits for old-release work |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| `renameField`    | `ALTER TABLE ... RENAME COLUMN`, plus `RENAME CONSTRAINT` for its `uq_` and `fk_` names, which hash the column name                                                                                                                                            | Yes                        |
| `dropField`      | `ALTER TABLE ... DROP COLUMN`, without `CASCADE`                                                                                                                                                                                                               | Yes                        |
| `dropEntity`     | `DROP TABLE` for child tables, then the entity table, without `CASCADE` so a remaining foreign key fails. `DELETE` its provisioning records.                                                                                                                   | Yes                        |
| `fill`           | `ADD COLUMN` as nullable when missing, then `UPDATE ... SET f = <value> WHERE f IS NULL`                                                                                                                                                                       | No                         |
| `convert`        | For a type change, `ADD COLUMN axis_convert`, `UPDATE ... SET axis_convert = (<value>)::<type>`, `ALTER COLUMN ... TYPE ... USING axis_convert`, then `DROP COLUMN axis_convert`. For removed enum values, `UPDATE ... SET f = <value> WHERE f IN (<removed>)` | Yes                        |
| Additive changes | As in [Schema planning](#schema-planning) today                                                                                                                                                                                                                | No                         |

A `fill` on a missing column adds it as nullable before the `UPDATE`.
The additive step afterwards adds `NOT NULL` for a required field. A
type-changing `convert` runs four statements, because DDL cannot take the
parameters that a value's literals need. A temporary nullable column
`axis_convert`, which no field column can clash with, takes the value from one
parameterized `UPDATE`. `ALTER COLUMN ... TYPE ... USING axis_convert` then
moves it into the field's column, and the temporary column is dropped. The
`UPDATE` casts the value to the field's new column type, and that cast does
the parsing, so `"value": "quantity"` turns a text `quantity` into an integer.
PostgreSQL's cast rules decide what parses: `' 42 '` becomes 42, and `'42.0'`
fails as an integer. A cast to a decimal with a smaller scale rounds. For a
text field with a `maxLength`, the cast is to `text`, so the `ALTER` fails on
a value that is too long instead of truncating it. An enum mapping is one
`UPDATE` that changes only the rows holding a removed value, so other rows
keep their value and their `version`. The removed values are then deleted from
the recorded enum values. A `convert` between `enum` and another type is not
built: it is `AXC0016`. A `convert` of a `reference` field to another type
fails on its foreign key, as `AXC0177`. Both values must translate to the
[SQL subset](expressions.md#sql-subset).

A `convert` that cannot convert a row rolls the whole activation back. That
covers unparsable text, a value that is too long, a required field left empty
and a duplicate in a unique field. It is `AXC0177`, reported with a fixed
message and the PostgreSQL error code. The message never quotes PostgreSQL's
text, because that can hold a stored value. An enum mapping that leaves a
value the field does not list is `AXC0177` too.

Each `fill` and `convert` increments `version` on every row it changes.
Child rows have no version, so changing them increments their owner's
`version`, in the same statement. This prevents an edit begun before the migration from overwriting
a migrated value without a version conflict. There is no audit record for
each changed row. The `axis.applied_migrations` row is the trace.

### Applied migrations

`Axis.Data` owns `axis.applied_migrations` in each tenant database. It
records a migration only when activation commits. The table has an index on
`application_id`.

| Column            | Type          | Meaning                                                                                                     |
| ----------------- | ------------- | ----------------------------------------------------------------------------------------------------------- |
| `migration_id`    | `uuid`        | Primary key, the migration resource `id`                                                                    |
| `application_id`  | `uuid`        | The application `id`                                                                                        |
| `release_id`      | `uuid`        | The release that applied the migration                                                                      |
| `content_hash`    | `varchar(64)` | SHA-256 of the file's canonical JSON as lower-case hex, without its path                                    |
| `applied_at`      | `timestamptz` | When the migration was applied                                                                              |
| `changed_columns` | `jsonb`, null | The columns the migration changed, as `[{ entityId, field }]`; null for rows applied before it was recorded |

### Example: renaming a field while a task waits

This example uses the purchase request migration in
[configuration](configuration.md#resource-file-shape).
R1 and R2 stand for stored release UUIDs of the same application.

1. R1 is active. An employee submits a purchase request. Its process
   instance is pinned to R1 and is `waiting` on `managerApproval`.
2. R2 adds `RenameDecisionComments`. Its entity file renames
   `decisionComments` to `reviewComments` and adds the required `priority`.
   The migration renames the column and fills `priority` with `'normal'`.
3. Activating R2 is refused with `AXC0178`, naming migration
   `RenameDecisionComments`, release R1 and 1 open instance. R1 stays active.
   No schema, stored value or migration record changes.
4. The manager approves. The instance runs to `completed`.
5. Once no `running` or `waiting` instance is pinned to R1, activating R2
   applies the rename and fill, provisions the required constraint,
   recomputes affected computed fields and switches the active release and
   site paths in one transaction.

New submissions while R1 is active also count. On a busy application, the
rename may be refused for a long time. The start lock stops a submission
from slipping between the count and the switch. Shipping the `fill` alone
in an earlier release never waits for old-release work.

### Limits

These limits apply to migrations.

- A large `fill` or `convert` holds the schema lock and its row locks until
  commit. The `fill` itself is not batched. The recompute of the computed
  fields that read it runs inline in the same transaction, up to the inline
  limit. Above the limit the rows go to the worker, which recomputes them in
  batches.
- Process starts for the application wait while activation holds the
  exclusive start lock. A long fill delays them.
- Old work must finish before a destructive migration can run. An operator
  can drain it by cancelling the `running` and `waiting` old-release
  instances, see [Cancel](processes.md#cancel). A call that is
  `sending` or `unknown` must settle or be
  [resolved](processes.md#resolve) first. A `failed`
  instance does not block the migration, and a [retry](processes.md#retry)
  of one is refused when the migration changed a column it
  uses. There is no way to pause starts yet.
- A record API create or update takes the start lock and runs again on the new
  release when the active release changed. A list, a read, a history or a
  delete that resolved the previous release just before the commit can still
  fail once on a renamed or dropped column. The same holds for a create or
  update that reads the stored record before it takes the lock.

## Audit records and sequence counters

`Axis.Data` owns both tables (D21). They are system tables in the `axis`
schema of the tenant database.

- **Audit records.** `axis.audit_records` is append-only. Each row is
  written in the same transaction as the action it records. The record API
  and the process [start endpoint](processes.md#start-endpoint) write them,
  and so do [process steps](processes.md#running-a-step) and
  [task completions](processes.md#completing-a-task). So
  do the [operator commands](processes.md#operator-commands), with the user as
  actor. The BFF session writes them too for sign-in, sign-out and a refused
  sign-in, see the session actions below.

  | Column                | Type                             | Meaning                                                                                                                                                                                                                                          |
  | --------------------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
  | `id`                  | `uuid`                           | A version 7 UUID                                                                                                                                                                                                                                 |
  | `occurred_at`         | `timestamptz`                    | When the action happened: the transaction time, `now()`, so the audit records of one transaction share it                                                                                                                                        |
  | `actor`               | `text`                           | A user id, or `system` for the worker. The record API and the process start endpoint need a session, so they never write `anonymous`. Only process starts from before M4, with nobody signed in, have it. `session.signInRefused` also writes it |
  | `actor_name`          | `text`, null                     | The actor's display name at the time of the action. It is null for `system` and `anonymous`, and for audit records written before it was added                                                                                                   |
  | `action`              | `text`                           | The action, such as `record.created`                                                                                                                                                                                                             |
  | `application_id`      | `uuid`, null for session actions | The application `id`. A session action belongs to no application                                                                                                                                                                                 |
  | `entity_id`           | `uuid`, null                     | The entity `id`, when the action concerns a record                                                                                                                                                                                               |
  | `record_id`           | `uuid`, null                     | The record id                                                                                                                                                                                                                                    |
  | `process_instance_id` | `uuid`, null                     | The process instance id                                                                                                                                                                                                                          |
  | `details`             | `jsonb`                          | An object that depends on the action                                                                                                                                                                                                             |
  - Application and entity are stored by their stable resource ids, never by
    name, so a rename keeps the history.
  - _(Planned for M10.)_ A `publish` and a `rollback` write an audit record
    when they succeed and when they fail, see
    [publish requests and release history](#publish-requests-and-release-history).
  - **Session actions.** The
    BFF session writes three actions, see
    [D50](../decisions.md#d50-sign-in-audit-records-and-security-scanning-in-ci--agreed)
    and [sign-in and the session](../architecture.md#sign-in-and-the-session).
    Each is written in the tenant database of the resolved host.

    | Action                  | When                                     | `actor` / `actor_name`           | `details`                       |
    | ----------------------- | ---------------------------------------- | -------------------------------- | ------------------------------- |
    | `session.signedIn`      | The callback creates a session           | The user id and the display name | `{ "clientAddress" }`           |
    | `session.signedOut`     | `POST /api/auth/sign-out` with a session | The user id and the display name | `{ "clientAddress" }`           |
    | `session.signInRefused` | The callback is refused                  | `anonymous`, null                | `{ "reason", "clientAddress" }` |
    - `reason` is one of `otherTenant`, `invalidToken`, `invalidState` or
      `providerError`. The last covers a person who cancels at the identity
      provider, and a provider error.
    - `application_id`, `entity_id`, `record_id` and `process_instance_id` are
      null.
    - `clientAddress` is always present. It is the address as text, with an
      IPv4-mapped IPv6 address written as IPv4, or `null` when the server sees
      no address.
    - A `session.signedIn` or `session.signedOut` record commits before its
      action takes effect. If it cannot be written, the callback fails and
      creates no session, or sign-out fails with a `500` and the session stays.
      If a `session.signInRefused` record cannot be written, the server logs a
      warning and the callback still answers its `500`.
    - `otherTenant` means the callback's `state` came from a sign-in that
      another tenant's client started.
    - They never hold a token, a cookie or the user agent.
    - A session that expires writes nothing. A sign-out with no session writes
      nothing.

  - An index on `(entity_id, record_id, occurred_at desc, id desc)` serves
    the [record history](record-api.md#audit-records-and-history).
  - A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE`
    statement trigger raise an exception with SQLSTATE `23001`
    (`restrict_violation`), so no row can be changed or removed, even by
    raw SQL.

- **Sequence counters.** `axis.sequence_counters` holds one row per
  [sequence](configuration.md#sequences) and period. The row is created by
  the first number of the period.

  | Column           | Type      | Meaning                                            |
  | ---------------- | --------- | -------------------------------------------------- |
  | `sequence_id`    | `uuid`    | The `sequence` resource `id`                       |
  | `application_id` | `uuid`    | The application `id`                               |
  | `period`         | `integer` | The UTC year, or 0 when the format has no `{yyyy}` |
  | `last_value`     | `bigint`  | The last number handed out                         |
  - The primary key is `(sequence_id, period)`.
  - For a module sequence, `sequence_id` is the id derived
    for the application, see the module resource ids under physical names. The
    compiler builds that id into the model. So two
    applications that use one module get separate `axis.sequence_counters`
    rows and count on their own.
  - Taking a number is one
    `INSERT ... ON CONFLICT DO UPDATE SET last_value = last_value + 1 RETURNING last_value`
    in the caller's transaction.
  - Its row lock makes concurrent creates of the same sequence wait for each
    other. A create that rolls back uses no number, so there are no gaps.

## Publish requests and release history

`Axis.Configuration` owns both tables, in the `axis`
schema of each tenant database. `axis.publish_requests` is built. The release
history, the routes and the publisher are planned for M10. See
[D47](../decisions.md#d47-publishing-from-studio--agreed) and the
[authoring API](authoring.md#publishing).

### Publish requests

`axis.publish_requests` holds one row per request. The server inserts a row
over the runtime role _(planned for M10)_. Only the publisher changes a row,
over the schema role _(planned for M10)_.
An index on `(application_id, status, requested_at, id)` serves the claim.

| Column              | Type                | Meaning                                                                                                                                |
| ------------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                | `uuid`              | Primary key, a version 7 UUID                                                                                                          |
| `application_id`    | `uuid`              | The application `id`                                                                                                                   |
| `kind`              | `text`              | `validate`, `publish` or `rollback`                                                                                                    |
| `target`            | `text`              | *(Planned for M10.)* `tenant` or `preview`. It defaults to `tenant`. See the preview note below |
| `status`            | `text`              | `queued`, `running`, `succeeded` or `failed`                                                                                           |
| `snapshot`          | `jsonb`, null       | For `validate` and `publish`, an array of `{ file, version, content }`, one for every resource file of the folder. Null for `rollback` |
| `release_id`        | `uuid`, null        | For `rollback`, the release to activate. For `publish`, set when the release is stored                                                 |
| `label`             | `text`, null        | The optional label, up to 100 characters                                                                                               |
| `note`              | `text`, null        | The optional note, up to 2000 characters                                                                                               |
| `requested_by`      | `text`              | The requesting user's id                                                                                                               |
| `requested_by_name` | `text`, null        | The user's display name at the time                                                                                                    |
| `requested_at`      | `timestamptz`       | When the request was stored                                                                                                            |
| `claimed_by`        | `text`, null        | The publisher that holds the claim                                                                                                     |
| `lease_until`       | `timestamptz`, null | When the claim runs out unless the publisher renews it                                                                                 |
| `claims`            | `integer`           | How many times a publisher claimed the request, from 0                                                                                 |
| `started_at`        | `timestamptz`, null | When it was first claimed                                                                                                              |
| `finished_at`       | `timestamptz`, null | When it reached `succeeded` or `failed`                                                                                                |
| `diagnostics`       | `jsonb`, null       | The diagnostics, as an array                                                                                                           |

- **A snapshot.** `content` is the file's JSON as the author saw it, and
  `version` is its SHA-256, as in [Versions](authoring.md#versions). A later
  edit never changes a stored snapshot.
- **Claim.** The publisher takes the oldest `queued` row of an application with
  `FOR UPDATE SKIP LOCKED`, sets `running`, `claimed_by`, `lease_until` and
  adds 1 to `claims`. A `running` row whose `lease_until` has passed is claimed
  again the same way. A row with `claims` of 3 and an expired lease becomes
  `failed` with `AXC0215`.
- **Preview target.** *(Planned for M10.)* A row with `target` `preview` is
  stored in the session tenant's database and run against that tenant's preview
  tenant, see [preview](authoring.md#preview). Its history row lives in the
  preview tenant's `release_activations` and keeps this row's `id` as
  `request_id`. That `request_id` points at a request in another database, so no
  foreign key can hold it. The publisher sets the row `failed` with `AXC0216`
  when the main tenant no longer has a preview tenant.
- **Final.** A `succeeded` or `failed` row is never changed again.
- **Size and age.** No limit on the size of a snapshot is set, and nothing
  deletes old requests. Large folders or many requests grow the table. A later
  decision sets both.

### Release history

`axis.release_activations` holds one row per activation, and is append-only.

| Column           | Type          | Meaning                                                              |
| ---------------- | ------------- | -------------------------------------------------------------------- |
| `id`             | `uuid`        | Primary key, a version 7 UUID                                        |
| `release_id`     | `uuid`        | The activated release                                                |
| `application_id` | `uuid`        | The application `id`                                                 |
| `number`         | `integer`     | The release number: 1, 2, 3… per application                         |
| `content_hash`   | `varchar(64)` | The release's content hash                                           |
| `activated_at`   | `timestamptz` | When the activation committed                                        |
| `actor`          | `text`        | A user id, `startup`, `operator`, or `unknown` for a backfilled row  |
| `actor_name`     | `text`, null  | The user's display name, or null                                     |
| `label`          | `text`, null  | The label of the publish, or null                                    |
| `note`           | `text`, null  | The note of the publish, or null                                     |
| `request_id`     | `uuid`, null  | The publish request, or null for a startup and `axis-admin activate` |

- **Append only.** A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE
TRUNCATE` statement trigger raise SQLSTATE `23001`, as for `audit_records`.
  The runtime role has `SELECT` only.
- **Numbers.** A release gets its number the first time it is activated in the
  tenant. Every later row of that release holds the same number, and no two
  releases of one application share one. The activation takes the number in
  the transaction that holds the schema lock, so two activations cannot give
  one number to two releases.
- **No copies.** Activating the release that is already active adds no row and
  keeps the number. A restart under `scripts/dev.sh` and a request that runs
  again therefore add nothing.
- **Every path.** Startup activation, `axis-admin activate`, a publish and a
  rollback each add a row. The actor is the user for a publish and a rollback,
  `startup` for a startup and `operator` for `axis-admin activate`.
- **Backfill.** The migration that creates the table adds one row for each
  application's current active release, with number 1, the actor `unknown`, the
  activation time of `axis.active_releases` and no request. Without it the
  release that is live today would have no number and no row to roll back to.
  Older releases that are not active are not in the history, so no rollback can
  name them.
- **Per tenant.** Numbers belong to one tenant. The same content can have
  different numbers in two tenants.

### Rollback refusals

A `rollback` activates the stored release as it is and never compiles it
again. The publisher refuses it, and the request ends `failed` with one of these
diagnostics. Nothing is changed.

| Case                                                                                                                  | Code      |
| --------------------------------------------------------------------------------------------------------------------- | --------- |
| A migration in [`axis.applied_migrations`](#applied-migrations) was applied after that release and is missing from it | `AXC0174` |
| An extension package version that the release pins is not installed under `Extensions:Path`                           | `AXC0146` |

The second check is the start check of
[extension packages](extension-packages.md). Both codes have the meaning they
have at activation. See also [rollback](authoring.md#rollback).

### Audit records of publishing

Each `publish` and `rollback` writes one record in `axis.audit_records`, in the
transaction that sets the request's final status. The actor is the requesting
user, and `application_id` is the application `id`. *(Planned for M10.)* A
preview request writes no audit record. The preview tenant's release history
records who published it.

| Action                   | When                   |
| ------------------------ | ---------------------- |
| `release.published`      | A `publish` succeeded  |
| `release.publishFailed`  | A `publish` failed     |
| `release.rolledBack`     | A `rollback` succeeded |
| `release.rollbackFailed` | A `rollback` failed    |

`details` is `{ requestId, releaseId, number }`. `releaseId` and `number` are
null when the request failed before a release was known. A `validate` writes
no audit record.
