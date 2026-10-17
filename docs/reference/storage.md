# Storage

Detailed reference for the storage layout, physical names, column types and
schema planning. The conventions of [architecture.md](../architecture.md)
apply: sections marked *(planned for Mx)* are not built yet, and Dn refers to
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
    on path), with history in `axis.__configuration_migrations`. *(planned for
    M7)* It also owns `axis.release_computed_fingerprints`, the hash of the
    computed definitions of each entity in each release. The caller
    supplies the tenant database connection. Other modules read and set the
    active release and its site paths only through `IActiveReleaseStore`;
    they never use `axis.active_releases`, `axis.active_sites` or the
    configuration context directly.
  - `Axis.Data` owns `axis.provisioned_entities` (each provisioned entity
    with its application and table), `axis.provisioned_enum_values` (each
    recorded enum value) and `axis.sequence_counters` (the last number of
    each sequence and period), with history in `axis.__data_migrations`.
    It also owns `axis.applied_migrations`, the migrations applied once per
    tenant. See [Migrations](#migrations). *(planned for M7)* It also owns
    `axis.pending_computed_fields` and `axis.computed_field_errors`, for the
    [recompute of computed fields](#recomputing-computed-fields).
  - `Axis.Processes` owns `axis.process_instances` (one row per process
    instance), `axis.process_step_history` (one row per step occurrence),
    `axis.process_tasks` (one row per human task),
    `axis.connector_calls` (one row per staged connector call),
    `axis.package_calls` (one row per staged package call),
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

  | Object | Name | Length |
  | --- | --- | --- |
  | Table | `e_` + entity `id` as 32 lowercase hex characters | 34 |
  | Primary key column | `id` (`uuid`) | 2 |
  | Version column | `version` (`bigint`) | 7 |
  | Computed release column *(planned for M7)* | `computed_release_id` (`uuid`, null), on tables that are not child tables | 19 |
  | Field column | `f_` + field name in lowercase | at most 62 |
  | Primary key | `pk_` + table | 37 |
  | Unique constraint | `uq_` + table + `_` + hash of the column name | 54 |
  | Foreign key | `fk_` + table + `_` + hash of the column name | 54 |

  The hash is the first 16 lowercase hex characters of SHA-256 over the UTF-8
  column name. Because names are ASCII and at most 60 characters, every
  identifier fits PostgreSQL's 63-byte limit by construction and is never
  truncated. Identifiers are always double-quoted in SQL.
- **Column types.** Each field type maps to one PostgreSQL type, spelled as
  `format_type` renders it, so a catalog column matches by string equality.

  | Field type | Column type |
  | --- | --- |
  | `text` | `character varying(n)` with `maxLength`, otherwise `text` |
  | `integer` | `bigint` |
  | `decimal` | `numeric(p,s)` with `precision` (`s` is 0 when `scale` is omitted), otherwise `numeric` |
  | `boolean` | `boolean` |
  | `date` | `date` |
  | `date-time` | `timestamp with time zone` |
  | `enum` | `text`; the values are recorded, not enforced by a `CHECK` |
  | `reference` | `uuid` with a foreign key to the target table's `id` |
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

## Database roles

See [D31](../decisions.md#d31-separate-database-roles--agreed). The two
settings, the connection each step uses, the grants that limit the runtime
role and the release trigger are built. The integration tests create the two
database roles, `axis_schema` and `axis_runtime`, own each test database with
`axis_schema`, run the server and worker as `axis_runtime`, and run activation
as `axis_schema`. Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the
same: `samples/postgres/init-roles.sql` creates the roles and grants them
nothing. *(planned for M7)* The start checks for a superuser, a schema owner or
a role that may create objects are still to come.

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

| Objects | Schema database role | Runtime database role |
| --- | --- | --- |
| `entities` tables | Owns them | Read and write rows |
| `axis` tables that runtime writes: `process_*`, `connector_calls`, `package_calls`, `events`, the receipts, `trigger_schedules`, `trigger_starts`, `audit_records`, `sequence_counters`, and *(planned for M7)* `pending_computed_fields`, `computed_field_errors` | Owns them | Read and write rows |
| Tables that activation writes: `active_releases`, `active_sites`, `provisioned_entities`, `provisioned_enum_values`, `applied_migrations`, *(planned for M7)* `release_computed_fingerprints`, the `__*_migrations` tables | Owns them | Read only |
| Release tables: `releases`, `release_resources`, `release_packages` | Insert and read. The trigger refuses update, delete and truncate | Read only |
| Sequences in `axis` and `entities` | Owns them | `USAGE`, `SELECT` |
| Create, alter, drop | Yes | No |

- **Default privileges.** The schema database role sets default privileges. A
  new table in `entities` gives the runtime database role `SELECT`, `INSERT`,
  `UPDATE` and `DELETE`. A new table in `axis` gives it `SELECT` only. The
  grant step also clears the database-wide default privileges that older
  setups set, because a per-schema default cannot narrow them.
- **Explicit grants.** The grant step holds the list of `axis` tables that
  runtime writes. It grants row access on those and revokes `INSERT`, `UPDATE`,
  `DELETE` and `TRUNCATE` on every other `axis` table, so a database with
  older, wider grants is repaired. A new system table is read-only until
  someone adds it to the list.
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
  user. *(planned for M7)* So does a
  runtime database role that is a superuser, owns the `axis` or `entities`
  schema, or may create objects in either. The server runs all of these
  checks. The worker validates `ConnectionString` and the runtime database
  role. The server also validates `SchemaConnectionString` and that the two
  settings name different users, because the worker holds no
  `SchemaConnectionString`.

Which connection string each step uses. The server does this today:

| Step | Connection string |
| --- | --- |
| EF Core migrations of configuration, data and processes | `SchemaConnectionString` |
| Compiling a folder and storing its release | `SchemaConnectionString` |
| Activation: migrations, provisioning, active release, site paths | `SchemaConnectionString` |
| Grants for the runtime database role, before and after the migrations | `SchemaConnectionString` |
| Schedule trigger state | `ConnectionString` |
| Seed records | `ConnectionString` |
| Start release check (`AXC0146`) and the runtime database role check, in the server and the worker | `ConnectionString` |
| Server requests: record API, data sources, process starts, endpoints, tasks, sites | `ConnectionString` |
| Worker | `ConnectionString` only |

The server holds both connection strings for its whole life. It opens the
schema connection only during startup activation, and closes it when each
tenant is done. Moving activation out of the server comes with tenant
provisioning in M9.

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
  `AXC0016` without one. A `fill` or `convert` on a child entity, and a `fill`
  or `convert` of a field that a computed field reads, are `AXC0175`, because
  activation cannot recompute computed fields yet. A `fill` or `convert` that
  names, in its value, a field that is not a column of the stored table is
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
- **System columns.** `id` and `version` are created with the table, or
  `id`, `owner_id` and `position` for a child table (see **Child table**
  below). `version` is added to an existing table other than a child table
  that lacks it as
  `bigint NOT NULL DEFAULT 1`, even when the table has rows, because the
  default fills them. System columns are never compared or changed otherwise:
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
  computed fields are recomputed inline, or marked pending, *(planned for M7)*.
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

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `owner_id` | `uuid NOT NULL` | The owner record's `id`. A foreign key to the owner table's `id` with `ON DELETE CASCADE`, named `fk_` + table + `_` + hash as usual |
  | `position` | `integer NOT NULL` | The row's order in the collection, the zero-based index in the array |

  Rows are deleted with their owner.
- **No version.** A child table has no `version` column. Its rows live under
  the owner's version, and a write to the rows increments the owner's
  `version`.
- **Computed column.** A computed field is an ordinary
  column of its type. It is never `NOT NULL`, because a computed field cannot
  be `required`. It is written in the same transaction as the record, and for
  a child row before the owner's computed fields.
- **Existing rows.** A computed field added to a table that has rows is
  filled through [Recomputing computed fields](#recomputing-computed-fields)
  *(planned for M7)*. Until then, the rows stay `NULL` until their next
  write, and an update with no values is enough. Activation does not
  backfill them, because one run-time error would stop it.

## Recomputing computed fields

*(planned for M7)* This section records
[D33](../decisions.md#d33-recomputing-stored-computed-fields--agreed). Nothing
in it is built. Today a changed computed expression leaves every row on its
old value until its next write.

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
- **Inline.** When the count of affected rows is at most
  `Activation:InlineRecomputeLimit`, activation recomputes them in its own
  transaction, with the release being activated. It stamps each row. The limit
  is a [server setting](configuration.md#startup-activation) and defaults to
  10,000.
- **Pending.** Above the limit, activation writes a row in
  `axis.pending_computed_fields` and a `computed.backfill` work item, then
  commits. The computed field is now *pending*: its stored values may still
  come from an older expression.
- **Stamp.** Each table that is not a child table has the system column
  `computed_release_id uuid NULL`. It names the release that last computed the
  row. Adding it is cheap on a large table, because it is nullable and has no
  default.
  - Every write stamps the release it ran with. That includes a record API
    write, a step of a process pinned to an older release, and a task
    completion.
  - Child rows recompute with their owner and carry no stamp.
  - A `fill` or `convert` sets the stamp to `NULL` on every row it changes and
    on the owner row of every changed child row. Their computed inputs changed,
    so the old stamp no longer proves the row is up to date.
- **Fingerprint.** Each release stores, for each entity, one hash of the
  computed definitions of the entity and its child entities, and of every rule
  their expressions call. It is written with the release in
  `axis.release_computed_fingerprints`.
- **Up to date.** A row is up to date when its stamp names a release whose
  fingerprint for the entity equals the target release's. The pending row
  keeps the ids of those releases as `current_release_ids`. A row with no stamp
  is out of date only while the entity has a pending field. Otherwise every
  release that changes nothing about a computed field would recompute every
  row of every table.
- **Older-release writes.** A write whose release has a different fingerprint
  from the active release's inserts the pending row and the `computed.backfill`
  work item in its own transaction, when they are missing. It compares two
  stored fingerprints, so the check is cheap. Without this, a row that pinned
  work writes after the backfill ends would stay stale with no pending flag.
  The insert and the final delete of a backfill lock the same pending row, so
  a write cannot slip between the last check and the delete.
- **Batches.** The worker claims a `computed.backfill` work item and runs one
  batch of 500 rows, in `ORDER BY id`, in its own transaction.
  - It selects out-of-date rows with their `version`, recomputes them with the
    target release, and runs
    `UPDATE ... WHERE id = $1 AND version = $2`. A row whose version changed is
    skipped. A later batch takes it if its stamp is still out of date.
  - The update does not change `version`. It writes no audit record and starts
    no trigger. A higher `version` would give a user who is editing the record
    a `409` for a change they did not make.
  - The work item stays queued, so the next claim runs the next batch, until
    a batch finds no out-of-date row.
- **Row errors.** A row whose expression fails at run time gets `NULL` and
  its stamp. The batch inserts a row in `axis.computed_field_errors` with the
  entity, the record id, the field, the release and a fixed error text. It
  stores no value, because the error can name one. The row no longer stops
  the work. This replaces the rule of D30 that one run-time error rolls
  activation back.
- **Restart.** Activating a release while a field is pending sets the target
  to the new release. It recomputes `current_release_ids`, merges the pending
  fields and applies the inline limit again. The backfill continues with the
  newest release.
- **End.** The batch that finds no out-of-date row deletes the pending row and
  its work item. The field is no longer pending.
- **While pending.**
  - A single read, and the engine's reads of one subject record, compute a
    pending field with the active release and do not store the result. A
    read must not write. The backfill or the next write stores the value. A
    run-time error reads as `null`.
  - A list, a data source and a projection show the stored value.
  - A sort, search, filter or grouping that reads a pending field is refused
    with `409`. A policy read filter that reads one is refused too.
    See the [record API](record-api.md#paging-and-sorting) and
    [data sources](data-sources.md#errors).

Tables, both owned by `Axis.Data`. The runtime database role may write both.
The worker writes them and so does a record write that finds an older
release. The names below are the planned names.

`axis.pending_computed_fields`, one row per entity with a pending field:

| Column | Type | Meaning |
| --- | --- | --- |
| `entity_id` | `uuid` | Primary key, the entity `id` |
| `application_id` | `uuid` | The application `id` |
| `target_release_id` | `uuid` | The release the backfill computes with, the newest activated |
| `current_release_ids` | `uuid[]` | The releases whose fingerprint for the entity equals the target's |
| `fields` | `text[]` | The pending field names, including owner fields that aggregate a pending child field |
| `created_at` | `timestamptz` | When the field became pending |

`axis.computed_field_errors`, one row per record and field whose expression failed:

| Column | Type | Meaning |
| --- | --- | --- |
| `entity_id` | `uuid` | The entity `id` |
| `record_id` | `uuid` | The record id |
| `field` | `text` | The computed field name |
| `release_id` | `uuid` | The release that computed the row |
| `error` | `text` | A fixed error text. It never holds a stored value |
| `occurred_at` | `timestamptz` | When the batch recorded it |

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
pending.

1. R2 is activated. Activation counts 50,000 affected rows, which is more than
   10,000. It inserts the pending row for the purchase request, with R2 as the
   target and `amount` and `total` as the fields. It queues a
   `computed.backfill` work item and commits. No row changes.
2. A list sorted by `-total` answers `409`, "A field is being recomputed.",
   with `errors.sort` naming `total`. A list with no sort answers with the
   stored values.
3. A single read of one request returns `total` computed on the spot with R2.
   It stores nothing.
4. A user updates a request. The write recomputes with R2 and stamps R2. The
   backfill skips this row later, because its stamp is up to date.
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
of computed fields is *(planned for M7)*, and so are the rules, table rows
and example steps below that use it. Until the recompute is built, a `fill` or
`convert` on a child entity and a `fill` or `convert` of a field that a computed
field reads are `AXC0175`. This section records
[D30](../decisions.md#d30-schema-migrations--agreed).
A [migration resource](configuration.md#resource-file-shape) changes one
entity's storage and runs once per tenant, before additive provisioning.
Migrations run forward only.

- **Pending.** A migration is pending when `axis.applied_migrations` has
  no matching row for its resource `id` in the tenant database.
- **Order of statements.** An activation with a pending migration runs
  these steps in one transaction:

  1. Take the existing schema lock, `pg_advisory_xact_lock`. *(Built.)*
  2. Take the application's start lock exclusively. *(Built.)* An activation
     with no pending migration skips this step. The pending migrations are
     known only after reading `axis.applied_migrations`, so the lock comes
     right after that read and before any changed-history diagnostic is acted on.
     The schema lock already keeps those rows from changing in between.
  3. Check changed history. Any diagnostic stops the activation. *(Built.)*
  4. Count old-release work for the destructive changes below. Any such
     work stops the activation with `AXC0178`. *(Built.)* The count runs
     after planning and before the first statement, and only when the plan
     holds a destructive change that plans a statement.
  5. Run pending migrations in ordinal path order, and changes in file order.
     *(Built for `renameField`, `dropField`, `dropEntity`, `fill` and `convert`.
     A `fill` runs one `UPDATE` that counts the rows it left `null`. Any such row
     is `AXC0176` and rolls the activation back. A `convert` that fails for a
     row, or leaves an enum value the field does not list, is `AXC0177` and
     rolls the activation back.)*
  6. Run additive DDL in the order used today. *(Built.)*
  7. Recompute inline or mark pending, see
     [Recomputing computed fields](#recomputing-computed-fields), using the
     release being activated. *(Planned for M7.)* An activation without a
     pending migration runs the same step after its additive DDL.
  8. Write `axis.applied_migrations` and the provisioning records. *(Built.)*
  9. Upsert the active release and its `axis.active_sites` rows. *(Built.)*
  10. Commit, releasing both locks. *(Built.)*

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
- **Old-release work** *(built)*. After taking both locks, count `running` and
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
- **Start lock** *(built)*. Every
  transaction that reads the active release and inserts a process instance
  takes `pg_advisory_xact_lock_shared` keyed on the application `id`, before
  it reads the active release. The key is a `bigint`: the XOR of the two
  64-bit halves of the id. Another key can collide with it, which only makes
  a start wait longer. This covers the process start endpoint, the inbound
  endpoint, record API creates and updates, task completion, event delivery,
  `sync` event triggers, update triggers from worker steps and schedule
  fan-out. A record API create or update always takes it, because the new
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
- **Computed fields** *(planned for M7)*. A `fill` or `convert` of a field
  that a computed field reads recomputes through
  [Recomputing computed fields](#recomputing-computed-fields). `fill` and
  `convert` clear the stamp of the rows they change. Rule calls in computed
  fields remain unbuilt (`AXC0050`), so the case through a rule describes
  future behaviour.

| Change | SQL | Waits for old-release work |
| --- | --- | --- |
| `renameField` | `ALTER TABLE ... RENAME COLUMN`, plus `RENAME CONSTRAINT` for its `uq_` and `fk_` names, which hash the column name | Yes |
| `dropField` | `ALTER TABLE ... DROP COLUMN`, without `CASCADE` | Yes |
| `dropEntity` | `DROP TABLE` for child tables, then the entity table, without `CASCADE` so a remaining foreign key fails. `DELETE` its provisioning records. | Yes |
| `fill` | `ADD COLUMN` as nullable when missing, then `UPDATE ... SET f = <value> WHERE f IS NULL` *(built, except for a child entity and a field a computed field reads)* | No |
| `convert` | For a type change, `ADD COLUMN axis_convert`, `UPDATE ... SET axis_convert = (<value>)::<type>`, `ALTER COLUMN ... TYPE ... USING axis_convert`, then `DROP COLUMN axis_convert`. For removed enum values, `UPDATE ... SET f = <value> WHERE f IN (<removed>)` *(built, except for a child entity and a field a computed field reads)* | Yes |
| Additive changes | As in [Schema planning](#schema-planning) today | No |

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
`version` *(planned for M7; a `fill` or `convert` on a child entity is not applied yet)*. This prevents an edit begun before the migration from overwriting
a migrated value without a version conflict. There is no audit record for
each changed row. The `axis.applied_migrations` row is the trace.

### Applied migrations

`Axis.Data` owns `axis.applied_migrations` in each tenant database. It
records a migration only when activation commits. The table has an index on
`application_id`.

| Column | Type | Meaning |
| --- | --- | --- |
| `migration_id` | `uuid` | Primary key, the migration resource `id` |
| `application_id` | `uuid` | The application `id` |
| `release_id` | `uuid` | The release that applied the migration |
| `content_hash` | `varchar(64)` | SHA-256 of the file's canonical JSON as lower-case hex, without its path |
| `applied_at` | `timestamptz` | When the migration was applied |

### Example: renaming a field while a task waits

This example uses the purchase request migration in
[configuration](configuration.md#resource-file-shape). Its recompute of
computed fields is *(planned for M7)*.
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
   recomputes affected computed fields *(planned for M7)* and switches the active release and
   site paths in one transaction.

New submissions while R1 is active also count. On a busy application, the
rename may be refused for a long time. The start lock stops a submission
from slipping between the count and the switch. Shipping the `fill` alone
in an earlier release never waits for old-release work.

### Limits

These limits apply to migrations. Those about the recompute of computed
fields by a `fill` or `convert` are *(planned for M7)*.

- A large `fill` or `convert` holds the schema lock and its row locks until
  commit. The `fill` itself is not batched. The recompute of the computed
  fields that read it is batched above the inline limit
  *(planned for M7)*.
- Process starts for the application wait while activation holds the
  exclusive start lock. A long fill delays them.
- Operator cancel comes in M7. Until then, old work must finish before a
  destructive migration can run. There is no way to drain starts yet.
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
  [task completions](processes.md#completing-a-task).

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID |
  | `occurred_at` | `timestamptz` | When the action happened: the transaction time, `now()`, so the audit records of one transaction share it |
  | `actor` | `text` | A user id, or `system` for the worker. The record API and the process start endpoint need a session, so they never write `anonymous`. Only process starts from before M4, with nobody signed in, have it |
  | `actor_name` | `text`, null | The actor's display name at the time of the action. It is null for `system` and `anonymous`, and for audit records written before it was added |
  | `action` | `text` | The action, such as `record.created` |
  | `application_id` | `uuid` | The application `id` |
  | `entity_id` | `uuid`, null | The entity `id`, when the action concerns a record |
  | `record_id` | `uuid`, null | The record id |
  | `process_instance_id` | `uuid`, null | The process instance id |
  | `details` | `jsonb` | An object that depends on the action |

  - Application and entity are stored by their stable resource ids, never by
    name, so a rename keeps the history.
  - An index on `(entity_id, record_id, occurred_at desc, id desc)` serves
    the [record history](record-api.md#audit-records-and-history).
  - A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE`
    statement trigger raise an exception with SQLSTATE `23001`
    (`restrict_violation`), so no row can be changed or removed, even by
    raw SQL.
- **Sequence counters.** `axis.sequence_counters` holds one row per
  [sequence](configuration.md#sequences) and period. The row is created by
  the first number of the period.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `sequence_id` | `uuid` | The `sequence` resource `id` |
  | `application_id` | `uuid` | The application `id` |
  | `period` | `integer` | The UTC year, or 0 when the format has no `{yyyy}` |
  | `last_value` | `bigint` | The last number handed out |

  - The primary key is `(sequence_id, period)`.
  - Taking a number is one
    `INSERT ... ON CONFLICT DO UPDATE SET last_value = last_value + 1 RETURNING last_value`
    in the caller's transaction.
  - Its row lock makes concurrent creates of the same sequence wait for each
    other. A create that rolls back uses no number, so there are no gaps.
