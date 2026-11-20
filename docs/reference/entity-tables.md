# Entity tables and schema planning

This page holds the entity tables of each tenant database, with their physical names and column types,
and the schema planning that brings them in line with a release. The rest of storage, such as the system
tables and the database roles, is in [storage](storage.md).

## Entity tables

Each entity has a table in the tenant database, in the `entities` schema, apart from the `axis`
system tables. `Axis.Data` plans the tables, as [schema planning](#schema-planning) says, and applies
the plan.

- **One transaction.** Plan and apply run together in one transaction. One fixed advisory
  transaction lock (`pg_advisory_xact_lock`) serializes entity schema changes, so the catalog and
  records read for the plan are still current when it is applied.
- **Records with the DDL.** The provisioning records are written in the same transaction as the
  DDL. When the plan has any diagnostic, the transaction is rolled back and nothing is applied.
- **Runtime writes go on.** The lock does not block runtime writes to entity tables, so a statement
  can still fail, for example adding a `NOT NULL` column after a row was inserted following the
  catalog read. Any failing statement rolls back the whole transaction, DDL and records, and the
  exception propagates.
- **SQL safety.** The data module builds every SQL statement for entity data from compiled
  metadata. It resolves and quotes the identifiers, and values are always parameters, so
  configuration can never supply raw SQL. Request text, such as the entity segment, `sort` and
  `values` names, is matched against the model's declared names and never used as an identifier.

### Physical names

Table and column names are derived from stable ids and names, never from labels, so renaming a
label, or renaming an entity while keeping its `id`, never touches storage.

| Object                              | Name                                                                                                                                                                  | Length     |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------- |
| Table                               | `e_` + entity `id` as 32 lowercase hex characters                                                                                                                     | 34         |
| Primary key column                  | `id` (`uuid`)                                                                                                                                                         | 2          |
| Version column                      | `version` (`bigint`)                                                                                                                                                  | 7          |
| Computed release column             | `computed_release_id` (`uuid`, null), on tables that are not child tables                                                                                             | 19         |
| Field column                        | `f_` + field name in lowercase                                                                                                                                        | at most 62 |
| Created columns _(planned for M41)_ | `created_at` (`timestamptz`) and `created_by` (`text`), on tables that are not child tables                                                                           | 10         |
| Updated columns _(planned for M41)_ | `updated_at` (`timestamptz`) and `updated_by` (`text`), on tables that are not child tables                                                                           | 10         |
| Deleted columns _(planned for M41)_ | `deleted_at` (`timestamptz`, null) and `deleted_by` (`text`, null), set while the record is in the [recycle bin](recycle-bin.md), on tables that are not child tables | 10         |
| Primary key                         | `pk_` + table                                                                                                                                                         | 37         |
| Unique constraint                   | `uq_` + table + `_` + hash of the column name                                                                                                                         | 54         |
| Foreign key                         | `fk_` + table + `_` + hash of the column name                                                                                                                         | 54         |
| Index                               | `ix_` + table + `_` + hash of the column list                                                                                                                         | 54         |
| Trigram index                       | `ix_` + table + `_` + hash of `trgm:` + column name                                                                                                                   | 54         |
| Junction table                      | `m_` + entity `id` + `_` + hash of the field name                                                                                                                     | 51         |
| Junction foreign key / index        | `fk_` or `ix_` + junction table + `_` + first 8 hex of the hash                                                                                                       | 63         |

- **The hash** is the first 16 lowercase hex characters of SHA-256 over the UTF-8 column name. For an
  index it is over the column names joined by commas, in order. For a trigram index it is over
  `trgm:` and the column name, so its name differs from a plain index on the same column. For a
  junction table it is over the field name in lowercase. A junction table's foreign keys and index
  use only the first 8 hex characters of the hash, because the full hash after the 51-character
  table name would be 71 bytes.
- **No truncation.** Names are ASCII and at most 60 characters, so every identifier fits
  PostgreSQL's 63-byte limit by construction and is never truncated. Identifiers are always
  double-quoted in SQL.
- **Module resource ids.** The id of a module resource inside an application is a UUID v5 (RFC
  9562), whose namespace is the application `id` and whose name is the resource's own declared id,
  as lower-case hyphenated UTF-8 text. The compiler builds the derived id into the model. Activation
  provisions a module entity's table for each application that uses the module: the table is `e_` +
  the derived id, and `axis.provisioned_entities` records the application that uses it. So two
  applications in one tenant that use one module get different `e_` tables, and `AXC0018` never
  fires between them. The derived id stays the same across module versions, so an upgrade maps onto
  the same table. See [modules](modules-and-templates.md#modules).

### Column types

Each field type maps to one PostgreSQL type, spelled as `format_type` renders it, so a catalog column
matches by string equality. A `sensitive` field's column is `bytea`, whatever its type.

| Field type         | Column type                                                                                                                          |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `text`             | `character varying(n)` with `maxLength`, otherwise `text`                                                                            |
| `integer`          | `bigint`                                                                                                                             |
| `decimal`          | `numeric(p,s)` with `precision` (`s` is 0 when `scale` is omitted), otherwise `numeric`                                              |
| `boolean`          | `boolean`                                                                                                                            |
| `date`             | `date`                                                                                                                               |
| `date-time`        | `timestamp with time zone`                                                                                                           |
| `enum`             | `text`. The values are recorded, not enforced by a `CHECK`                                                                           |
| `reference`        | `uuid` with a foreign key to the target table's `id`                                                                                 |
| `child-collection` | No column on the owner table. See [child tables](child-tables-and-computed-columns.md#child-tables-and-computed-columns)             |
| `money`            | `numeric(p,s)`, as `decimal` with its defaults. The currency is the field's, or the `currencyField` column's                         |
| `file`             | `uuid` with a foreign key to `axis.files (id)`. See [file fields](file-fields.md#storage-and-clean-up)                               |
| `references`       | No column on the owner table. A junction table holds `owner_id`, `target_id` and `position`, see [junction tables](#junction-tables) |

## Schema planning

Activation compares what a release declares with what the tenant database holds, and plans the
statements that bring the entity tables in line, or the diagnostics that say why it cannot.
`Axis.Data` compares the compiled application with a snapshot of the tenant catalog and with the
provisioning records. The planner itself has no database access: it returns diagnostics, SQL
statements and the new records to write.

- **The catalog snapshot.** It holds the entity tables, their columns, types, `NOT NULL`,
  single-column unique constraints, foreign key targets, their `ix_` indexes with whether each is
  valid, and whether the table has rows.
- **The provisioning records.** They hold each provisioned entity with its application and table,
  and each recorded enum value. They cover the application's entities and any entity recorded with
  one of its entity `id`s, whatever its application.
- **Errors.** When any diagnostic exists, the plan has no statements and no new records, and nothing
  is applied.

### Migrations first

The planner first applies the pending `renameField`, `dropField`, `dropEntity`, `fill` and `convert`
[migrations](migrations.md#migrations) to its catalog snapshot and its copy of the provisioning
records, in ordinal path order and file order, and then plans the additive changes.

- **What a migration allows.** A `convert` makes the planner accept a type change, or enum values
  the release removes, where it gives `AXC0016` without one. Switching a field between `enum` and
  another type stays `AXC0016`, even with a `convert`. Removals and incompatible changes not covered
  by a migration keep `AXC0015`, `AXC0016` and `AXC0017`.
- **Recompute.** A `fill` or `convert` clears the release stamp of the rows it changes. On a child
  entity it also increments the owner's `version` and clears the owner's stamp, for each owner of a
  changed row. Activation then recomputes those rows, see
  [recomputing computed fields](migrations.md#recomputing-computed-fields).
- **Diagnostics.** A `fill` or `convert` whose value names a field that is not a column of the
  stored table is `AXC0171` at `/changes/{i}/value`. A `fill` whose value leaves a `null` is
  `AXC0176` at `/changes/{i}/value` when activation applies it. A `convert` that fails for a stored
  row, or maps an enum value to one the field does not list, is `AXC0177` at `/changes/{i}/value`.
- **Skipped changes.** A change that does not match the stored schema is skipped: the table does
  not exist, the `from` column is missing or the `to` column already exists. The migration is still
  recorded when activation succeeds, so a fresh tenant activates a release that has migrations. A
  skip cannot lose data, because a leftover column or table still gives `AXC0015` or `AXC0017`.
- **Which entity.** A migration's `entity` name is looked up first in the application's active
  release, read from its stored resources, because that release describes the tables that hold the
  data now and only it has a dropped entity. It is then looked up in the release being activated.
- **Enum values.** A rename also renames the field in the recorded enum values, and a drop deletes
  them.

### Tables and columns

The planner creates what is missing and changes an existing column only where no data can be lost.

- **System columns.** `id`, `version` and `computed_release_id` are created with the table, or
  `id`, `owner_id` and `position` for a child table. `version` is added to an existing table other
  than a child table that lacks it as `bigint NOT NULL DEFAULT 1`, even when the table has rows,
  because the default fills them. `computed_release_id` is added to such a table as `uuid`, nullable
  and with no default, so the rows are kept and stay unstamped. System columns are never compared or
  changed otherwise: only Axis DDL creates them, and an author cannot fix them through
  configuration.
- **Missing table.** It is created with the system columns and every field column, `NOT NULL` for
  required fields and a unique constraint for unique fields. A `child-collection` field has no
  column. The entity and its enum values are recorded.
- **Child table.** The owner of a child entity is the entity whose `child-collection` field names
  it, and the compiler ensures there is exactly one. The columns of a child table are in
  [child collections in storage](child-tables-and-computed-columns.md#child-tables-and-computed-columns).
  - A missing child table is created with `id`, `owner_id`, `position` and its field columns, and no
    `version`. Its owner foreign key is added with the other foreign keys, so the owner table exists
    whatever the entity order. Adding a child collection and its child entity to an entity that
    already has rows only creates the child table, and the owner table and its rows are untouched.
  - An existing child table whose `owner_id` has no foreign key gets one. A foreign key that points
    at another table than the owner's is `AXC0016` at the owner's `/fields/{i}/target`.
  - An existing table without `owner_id` cannot become a child table, because adding a `NOT NULL`
    owner column needs a migration. It is `AXC0016` at the owner's `/fields/{i}/target`.
  - An entity that stops being a child keeps `owner_id` and `position`, which are reported as
    removed fields (`AXC0015`), like any column without a field.
- **Missing column.** It is added. `NOT NULL` is added only when the table has no rows, so a
  required field added to a table with rows is `AXC0016` at `/fields/{i}/required`, unless a pending
  `fill` covers it: the `fill` adds the column as nullable and fills it, and the planner then makes
  it `NOT NULL`. A unique field also gets its unique constraint.
- **Existing column.**
  - The type must equal the expected type. Widening text (`character varying(n)` to a larger `n` or
    to `text`) is applied. Narrowing it, including `text` to `character varying(n)`, is `AXC0016` at
    `/fields/{i}/maxLength`. Any other type difference is `AXC0016` at `/fields/{i}/type`. A field
    that becomes or stops being `sensitive` gets a new column that activation fills, see
    [changing the flag](data-encryption.md#changing-the-flag).
  - Dropping `required` drops `NOT NULL`, and dropping `unique` drops the unique constraint. Adding
    either to an existing column is `AXC0016` at `/fields/{i}/required` or `/fields/{i}/unique`. A
    pending `fill` of the field allows `required`, and the column becomes `NOT NULL` after the
    `fill`.
  - A reference column without a foreign key gets one. A foreign key that points at another table
    than the target's is `AXC0016` at `/fields/{i}/target`.
- **Enum values.** Values are compared ordinally, so letter case matters. Recorded values missing
  from the field's `values` are one `AXC0016` at `/fields/{i}/values` that lists them, and new
  values become new records and need no SQL. A column switching between `enum` and another type
  (recorded values present for a non-enum field, or absent for an enum field) is `AXC0016` at
  `/fields/{i}/type`.
- **Removed field.** A column other than the table's system columns without a matching field is
  `AXC0015` at `/fields`, naming the column.
- **Removed entity.** An entity recorded for the application but missing from it is `AXC0017` at
  `application.json` with an empty path and the entity's `id` as `resourceId`.
- **Entity owned by another application.** Tables are keyed by entity `id`, so an entity whose `id`
  is recorded for another application, for example in an application copied with only its manifest
  `id` changed, would share that application's table. It is `AXC0018` at `/id` of the entity file,
  and the entity is not planned.
- **Labels.** A label change produces no statements.

### Statement order

The planner orders its statements so that references between entities, cycles and self-references
need no further ordering:

1. The pending migration statements, as [order of statements](migrations.md#order-of-statements)
   says.
2. Every `CREATE TABLE`.
3. The `ALTER TABLE` column changes.
4. The `DROP INDEX` statements, then the `CREATE INDEX` statements.
5. Every `ADD CONSTRAINT ... FOREIGN KEY`.
6. The recompute of stored computed fields inline, or the mark that leaves them pending for the
   worker above the inline limit, see
   [recomputing computed fields](migrations.md#recomputing-computed-fields).

### Indexes

Activation builds the indexes that an entity declares and those that Axis adds itself, without
blocking a large table.

- **When an index is built.** A unique index, an index on an empty table and an index on a table with
  at most `Activation:InlineRecomputeLimit` rows are built inside the activation transaction. A new
  non-unique index on a table with more rows is left out: activation commits without it, and
  `EntityIndexBuilder` creates it later with `CREATE INDEX CONCURRENTLY`, so reads and writes on the
  table go on.
- **Invalid indexes.** The catalog snapshot reads whether each index is valid. An `ix_` index that
  PostgreSQL marks `INVALID`, for example after a cancelled concurrent build, counts as missing, so
  activation and the builder drop it and create it again.
- **The builder.** It runs without a transaction, holds the schema lock that activation takes, and
  uses the schema role. The worker cannot build, because its runtime role cannot create indexes, see
  [database roles](storage.md#database-roles). Every host that activates calls the builder after activation
  commits: `Axis.Admin activate`, the publisher's publish and activation of a stored release, and the
  server's startup activation. A publisher validate builds nothing.
- **Automatic indexes.** Every `reference` column and every child table's `owner_id` gets an `ix_`
  index without being declared, and a junction table's `target_id` index is created with the table.
  Every `searchable` field and every entity's display field gets a `pg_trgm` GIN `ix_` index on
  `lower()` of the column, named by the trigram index name, and a field that is both has one. The
  statement names the operator class as `public.gin_trgm_ops`, because `tenant create` and
  `migrate` install `pg_trgm` in `public`. A table that lacks an automatic index gets it on its next
  activation, and removing `searchable` drops the index on the next activation. The catalog reads
  such an index, which is a GIN index built on an expression, and takes its column from
  `pg_depend`.
- **Declared indexes.** An entity's `indexes` add the declared indexes, with the columns in the order
  of `fields`. A declared index over one `reference` field has the same name as the automatic index
  on that field, so they are one index, which is unique when the declared one is.
- **Unique values and the bin** _(planned for M41)_. A `unique` field or index is a partial unique index over the
  records outside the recycle bin, `where deleted_at is null`, so a new record may take the unique value of a
  record in the bin, and a restore finds it held, see [restore](recycle-bin.md#restore).
- **Changes.** The planner adds an index the release declares and the table lacks, and drops an
  `ix_` index the release no longer declares, which needs no migration. Changing the field list of an
  index is a drop and a create under the new name. An index of the same name with another `unique`
  flag is dropped and created again.
- **Unique indexes.** A unique index added to a table with rows is `AXC0016` at
  `/indexes/{i}/unique`, as for `unique`, even when all of its columns are new in the release.
  Removing `unique` from an index is allowed. A second record that repeats the values of a unique
  index is a `409`, as for a `unique` field, see [record API](record-api.md#create-update-and-delete).
- **Migrations.** A `renameField` change renames the `ix_` indexes over the column, trigram ones
  included, so a renamed field keeps its unique index. A `dropField` change drops them with the
  column, and the planner does not drop them again.

### Junction tables

A `references` field stores its ids in a junction table of its own rather than in a column.

- **Columns.** The table has `owner_id`, a foreign key to the owner that is deleted with it, and
  `target_id`, a foreign key to the target that refuses a delete while linked. Its primary key is
  both columns, and `target_id` has an `ix_` index. `position` holds the zero-based index of the id
  in the written array. The planner adds no `position` column to a junction table that lacks one.
- **Writes and reads.** The record API writes the whole set of a field in one write and reads it in
  `position` order.
- **Planning.** A junction table whose `target_id` foreign key is missing gets it, and one that
  points at another table than the field's target is `AXC0016` at `/fields/{i}/target`. Junction
  tables are not recorded in `provisioned_entities`: the planner finds them in the catalog by the
  `m_` + entity `id` prefix. Removing the field drops nothing and is `AXC0015` at `/fields`, as for
  a column.
