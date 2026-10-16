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
    on path), with history in `axis.__configuration_migrations`. The caller
    supplies the tenant database connection. Other modules read and set the
    active release and its site paths only through `IActiveReleaseStore`;
    they never use `axis.active_releases`, `axis.active_sites` or the
    configuration context directly.
  - `Axis.Data` owns `axis.provisioned_entities` (each provisioned entity
    with its application and table), `axis.provisioned_enum_values` (each
    recorded enum value) and `axis.sequence_counters` (the last number of
    each sequence and period), with history in `axis.__data_migrations`.
    It also owns `axis.applied_migrations`, the migrations applied once per
    tenant. See [Migrations](#migrations).
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
    `ExecuteDelete`) and raw SQL bypass this guard; a database-level guard
    is a later change.
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
- **Database credentials** *(planned for M6)*. Runtime access and schema
  changes will use different database roles. M1 uses one connection string per
  tenant.

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
  `dropField`, `dropEntity` and `fill` [migrations](#migrations) to its
  catalog snapshot and its copy of the provisioning records, in ordinal path
  order and file order. It then plans as today. A pending `convert` is
  `AXC0175` until it is built *(planned for M6)*. So is a `fill` on a child
  entity and a `fill` of a field that a computed field reads, because
  activation cannot recompute computed fields yet. A `fill` that names, in its
  value, a field that is not a column of the stored table is `AXC0171` at
  `/changes/{i}/value`. A `fill` whose value leaves a `null` is `AXC0176` at
  `/changes/{i}/value` when activation applies it. Removals and incompatible
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
  migration statements run before these additive statements, then stored
  computed fields affected by a `fill` or `convert` are recomputed
  *(planned for M6)*. See [Order of statements](#migrations).
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
- **Existing rows.** A computed field added to a table that
  has rows leaves them `NULL`. Each record gets its value the next time it is
  written, and an update with no values is enough. Activation does not
  backfill the rows: it would evaluate every row inside the provisioning
  transaction, and one run-time error would stop the activation. Additive
  computed fields are still not backfilled, but a migration's `fill` or
  `convert` recomputes the computed fields that read the changed field
  *(planned for M6)*. See [Migrations](#migrations).

## Migrations

The migration resource, its compile checks and the once-per-tenant apply of
`renameField`, `dropField`, `dropEntity` and `fill` at activation are built.
The start lock, the old-release work check, `convert` and the recompute
of computed fields are *(planned for M6)*, and so are the rules, table rows
and example below that use them. Until the recompute is built, a `fill` on a
child entity and a `fill` of a field that a computed field reads are
`AXC0175`. This section records
[D30](../decisions.md#d30-schema-migrations--agreed).
A [migration resource](configuration.md#resource-file-shape) changes one
entity's storage and runs once per tenant, before additive provisioning.
Migrations run forward only.

- **Pending.** A migration is pending when `axis.applied_migrations` has
  no matching row for its resource `id` in the tenant database.
- **Order of statements.** An activation with a pending migration runs
  these steps in one transaction:

  1. Take the existing schema lock, `pg_advisory_xact_lock`. *(Built.)*
  2. Take the application's start lock exclusively. *(Planned for M6.)*
  3. Check changed history. Any diagnostic stops the activation. *(Built.)*
  4. Count old-release work for the destructive changes below. Any such
     work stops the activation with a diagnostic. *(Planned for M6.)*
  5. Run pending migrations in ordinal path order, and changes in file order.
     *(Built for `renameField`, `dropField`, `dropEntity` and `fill`. A
     `fill` runs one `UPDATE` that counts the rows it left `null`. Any such row
     is `AXC0176` and rolls the activation back.)*
  6. Run additive DDL in the order used today. *(Built.)*
  7. Recompute affected stored computed fields for the changed rows,
     using the release being activated. *(Planned for M6.)*
  8. Write `axis.applied_migrations` and the provisioning records. *(Built.)*
  9. Upsert the active release and its `axis.active_sites` rows. *(Built.)*
  10. Commit, releasing both locks. *(Built.)*

  The fixed lock order is schema lock, then start lock. Process starts
  never take the schema lock, so this order cannot deadlock with them.
- **Changed history.** An applied migration with a changed content hash or
  a missing file is `AXC0174`. Nothing runs. A changed file is reported at
  that file with an empty path, and a missing file at `application.json`
  with an empty path. The `resourceId` is the migration `id`. The hash is
  the SHA-256 of the file's canonical JSON alone, without its path, so
  moving a file does not count as changing it. Activating a release
  that lacks an applied migration file is refused, so an older release
  without that file cannot be activated again. The way back is a new
  release with a new forward migration.
- **Old-release work** *(planned for M6)*. After taking both locks, count `running` and
  `waiting` process instances pinned to a release other than the one being
  activated whose model has the column the change affects. This applies to
  `renameField` (its `from` column), `dropField`, `dropEntity` (its tables'
  columns) and `convert`. For each such release, a new diagnostic names the
  migration, the release and the count of open instances. Activation then
  changes nothing. A `fill` or an additive change never waits for old-release
  work.
- **Start lock** *(planned for M6)*. Every transaction that reads the active release and
  inserts a process instance takes `pg_advisory_xact_lock_shared` keyed on
  the application `id`, before reading the active release. This covers the
  process start endpoint, inbound endpoint, trigger starts and schedule
  fan-out. An activation with a pending migration takes the same lock
  exclusively, using `pg_advisory_xact_lock`, before counting old-release
  work. It waits for open starts and holds new ones until commit. A start
  either commits before the count and is counted, or reads the new release
  after activation commits. See [Releases and running work](processes.md#releases-and-running-work).
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
- **Computed fields** *(planned for M6)*. A `fill` or `convert` recomputes, in the same
  transaction, every stored computed field that reads the changed field.
  It may read it directly, through a rule or through a child aggregate.
  Only the changed rows are recomputed, and for changed child rows, their
  owners. Recompute last, after additive statements, using the release
  being activated, so every column its model reads exists. A run-time error
  rolls back activation. A migration never leaves a stored computed value
  stale. Rule calls in computed fields remain unbuilt (`AXC0050`), so the
  case through a rule describes future behaviour.

| Change | SQL | Waits for old-release work |
| --- | --- | --- |
| `renameField` | `ALTER TABLE ... RENAME COLUMN`, plus `RENAME CONSTRAINT` for its `uq_` and `fk_` names, which hash the column name | Yes *(planned for M6)* |
| `dropField` | `ALTER TABLE ... DROP COLUMN`, without `CASCADE` | Yes *(planned for M6)* |
| `dropEntity` | `DROP TABLE` for child tables, then the entity table, without `CASCADE` so a remaining foreign key fails. `DELETE` its provisioning records. | Yes *(planned for M6)* |
| `fill` | `ADD COLUMN` as nullable when missing, then `UPDATE ... SET f = <value> WHERE f IS NULL` *(built, except for a child entity and a field a computed field reads)* | No |
| `convert` | `ALTER COLUMN ... TYPE ... USING <value>` for a type change, or `UPDATE ... SET f = <value>` for an enum mapping | Yes |
| Additive changes | As in [Schema planning](#schema-planning) today | No |

A `fill` on a missing column adds it as nullable before the `UPDATE`.
The additive step afterwards adds `NOT NULL` for a required field. A
type-changing `convert` is one `ALTER TABLE ... ALTER COLUMN ... TYPE ... USING`
statement. An enum mapping is one `UPDATE`. Both values must translate to
the [SQL subset](expressions.md#sql-subset).

Each `fill` and `convert` increments `version` on every row it changes.
Child rows have no version, so changing them increments their owner's
`version` *(planned for M6; a `fill` on a child entity is not applied yet)*. This prevents an edit begun before the migration from overwriting
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
[configuration](configuration.md#resource-file-shape) *(planned for M6)*.
R1 and R2 stand for stored release UUIDs of the same application.

1. R1 is active. An employee submits a purchase request. Its process
   instance is pinned to R1 and is `waiting` on `managerApproval`.
2. R2 adds `RenameDecisionComments`. Its entity file renames
   `decisionComments` to `reviewComments` and adds the required `priority`.
   The migration renames the column and fills `priority` with `'normal'`.
3. Activating R2 is refused with a new diagnostic naming migration
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

These limits apply to migrations. Those about `fill`, `convert`, the start lock
and old-release work are *(planned for M6)*. Until the old-release check is
built, a running or waiting process instance of an older release can fail at
its next step after a rename or a drop.

- A large `fill` or `convert` holds the schema lock and its row locks until
  commit. There are no batched backfills.
- Process starts for the application wait while activation holds the
  exclusive start lock. A long fill delays them.
- Operator cancel comes in M7. Until then, old work must finish before a
  destructive migration can run. There is no way to drain starts yet.
- A record API request that resolved the previous release just before the
  commit can fail once on a renamed or dropped column. The start lock covers
  process starts only.

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
