# D77. Indexes, defaults and ranges

Status: **Agreed**

M15 adds indexes, default values and value ranges to entity fields. The owner
agreed these points on 2026-10-25. All of it is _(planned for M15)_, except that
the ranges of `integer`, `decimal`, `date`, `date-time` and `text` fields are
built, without the Studio designer. See
[entity field types](../reference/resource-files.md#entity-field-types-and-constraints)
and [schema planning](../reference/storage.md#schema-planning).

- **Indexes the platform adds:**
  - Every `reference` column and every child table's `owner_id` gets an
    index. No author declares these, because a reference without one makes
    every join and every read filter step read the whole table.
- **Declared indexes:**
  - An entity takes `indexes`, a list of `{ "fields": [ ... ] }` in column
    order, with `"unique": true` for a unique index over several fields.
  - An index is named `ix_` + table + `_` + a hash of its column list, like
    the `uq_` and `fk_` names, so it fits the 63-byte limit.
  - The catalog snapshot reads the `ix_` indexes. The planner adds the missing
    ones and drops the ones the release no longer declares. Dropping an index
    loses no data, so it needs no migration.
  - Adding a unique index to a table that has rows is `AXC0016`, as a
    single-field `unique` is today.
- **Building an index:**
  - On a table with at most `Activation:InlineRecomputeLimit` rows, the index
    is built inside the activation transaction.
  - On a larger table, activation commits without it, and the index is then
    built with `CREATE INDEX CONCURRENTLY`, so writes go on. An index left
    `INVALID` by a failed build is dropped and built again. Reads and writes
    work meanwhile, only slower.
  - A unique index is always built inside the activation transaction, because
    the release relies on it.
- **Defaults:**
  - A field takes `default`, an expression with no record in scope. It may use
    literals, rule calls and `now()`.
  - It cannot be combined with `expression`, `sequence` or `creator`, and is
    not allowed on `reference`, `file`, `references` or `child-collection`
    fields.
  - The record command applies it on create when the body leaves the field
    out. A body that sends `null` keeps `null`. So the record API, inbound
    endpoints, imports and processes that create records all get defaults.
  - It is not a database `DEFAULT`, and it never changes stored rows. A `fill`
    migration fills existing rows.
  - The form state endpoint returns the defaults for a new record, so the form
    shows them before the first save.
- **Ranges:**
  - `min` and `max` apply to `integer`, `decimal`, `money`, `date` and
    `date-time` fields, and `minLength` to `text` fields.
  - The record command checks them like `maxLength`: a value outside is a
    `400` at the field's pointer. There is no `CHECK` constraint, as for enum
    values, so tightening a range needs no migration and stored rows keep
    their values.
  - Form inputs show the limits.
- **Studio:** the entity designer edits `indexes`, `default`, `min`, `max`
  and `minLength`.

_Why:_ the typical application has hundreds of indexes, and without any a
filtered list reads the whole table. Indexing references and owners by
default fixes the common joins with no author work. Building a large index
concurrently keeps a publish from blocking writes on a busy table. Defaults
in the record command reach every way a record is created, which a form-only
default would not. Ranges checked by the server, without a `CHECK`, match how
enum values work and keep a range change from needing a migration.
