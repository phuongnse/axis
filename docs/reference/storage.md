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
    `axis.active_releases` (the active release of each application `id`,
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
  - `Axis.Data` also owns `axis.audit_records`, with history in
    `axis.__data_migrations` *(planned for M3)*. Processes write audit
    records through its contract, inside their own transaction. See
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
  `/fields/{i}/required`. A unique field also gets its unique constraint.
- **Existing column.**
  - The type must equal the expected type. Widening text
    (`character varying(n)` to a larger `n` or to `text`) is applied.
    Narrowing it, including `text` to `character varying(n)`, is `AXC0016` at
    `/fields/{i}/maxLength`. Any other type difference is `AXC0016` at
    `/fields/{i}/type`.
  - Dropping `required` drops `NOT NULL`, and dropping `unique` drops the
    unique constraint. Adding either to an existing column is `AXC0016` at
    `/fields/{i}/required` or `/fields/{i}/unique`.
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
  entities, cycles and self-references need no further ordering.
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
  transaction, and one run-time error would stop the activation.

## Audit records and sequence counters

`Axis.Data` owns both tables (D21). They are system tables in the `axis`
schema of the tenant database.

- **Audit records** *(planned for M3)*. `axis.audit_records` is
  append-only. Each row is written in the same transaction as the action it
  records.

  | Column | Type | Meaning |
  | --- | --- | --- |
  | `id` | `uuid` | A version 7 UUID |
  | `occurred_at` | `timestamptz` | When the action happened |
  | `actor` | `text` | A user id, `system` for the worker, or `anonymous` before M4 when nobody is signed in |
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
    statement trigger raise an exception, so no row can be changed or
    removed.
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
