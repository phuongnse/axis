# D30. Schema migrations

Status: **Agreed**

Activation applies the `renameField`, `dropField`, `dropEntity`, `fill` and
`convert` migrations once per tenant. The start lock and the old-release work
check are built too. The recompute of computed fields at activation is built,
and so is its use by a `fill` or `convert`, including one on a child entity.
Migrations
change entity storage in ways that are not additive, before provisioning.
See [configuration](../reference/resource-files.md#resource-file-shape) for the
resource shape and [storage](../reference/migrations.md#migrations) for planning,
statement order, locks and a waiting purchase request example.
The owner confirmed these points on issue #413 on 2026-10-15.

- **Migration resource:**
  - A new `migration` resource kind has one file per migration, with an
    `id`, a `name`, `formatVersion: 1`, the `entity` it changes and an
    ordered list of `changes`.
  - Files run in ordinal order of their relative path, so authors name
    them `migrations/0001-….json`. Changes run in file order.
  - Migration files are part of the release and its content hash.
- **Changes:**
  - `renameField`, with `from` and `to`, renames the column and keeps its
    values. Its `uq_` and `fk_` constraints are renamed too, because their
    names hash the column name.
  - `dropField`, with `field`, drops the column and its values.
  - `dropEntity` drops the child tables, then the entity table and its
    provisioning records. It never uses `CASCADE`, so a remaining foreign
    key fails rather than disappearing.
  - `fill`, with `field` and `value`, fills every null with an expression
    over the record. This lets a field become required on a table with
    rows, or adds a new required field to one.
  - A `fill` on a missing column first adds it as nullable, then runs
    `UPDATE ... SET f = <value> WHERE f IS NULL`. Additive provisioning
    afterwards adds `NOT NULL` for a required field.
  - `convert`, with `field` and `value`, uses an expression over stored
    values to change the column to its new type or map removed enum values
    to kept ones.
  - A type-changing `convert` runs four statements in the activation
    transaction. It adds a temporary nullable column `axis_convert`, fills it
    with one parameterized `UPDATE ... SET axis_convert = (<value>)::<type>`,
    runs `ALTER TABLE ... ALTER COLUMN ... TYPE ... USING axis_convert`, then
    drops the temporary column. DDL cannot take parameters, and values are
    always parameters. An enum mapping is one
    `UPDATE ... SET f = <value> WHERE f IN (<removed values>)`.
  - Both expressions must translate to the
    [SQL subset](../reference/expressions.md#sql-subset). An expression outside
    it is a compile diagnostic. Each fill or enum mapping uses one `UPDATE`.
    A type conversion uses one `UPDATE` and one `ALTER COLUMN` statement.
  - `fill` and `convert` increment `version` on every row they change.
    For child rows, they increment the owner's `version` instead. They
    write no per-row audit records. The `axis.applied_migrations` row is
    the trace, and the version bump protects concurrent edits.
- **At activation:**
  - Pending migrations run before additive provisioning, in the same
    transaction and under the same schema lock.
  - With a pending migration, the active release and its `axis.active_sites`
    rows are also written in that transaction. Any failure, including a
    refused name or site path, rolls back everything. The active release
    does not change, and a retry runs the migrations again.
  - The lock order is schema lock, then the application's start lock
    exclusively, then the old-release count. Both locks stay held to commit.
    Process starts never take the schema lock.
- **Once per tenant:**
  - `Axis.Data` owns `axis.applied_migrations` in each tenant database.
    It records the migration `id` as primary key, the application, the
    release that applied it, its content hash and the time.
  - The hash is SHA-256 of the file's canonical JSON, hashed as for release
    content. No matching row means the migration is pending.
- **Planning:**
  - The planner applies pending migrations to its catalog snapshot first:
    rename or drop columns or tables, convert types and mark filled columns
    as having no nulls. It then plans as today.
  - A removal or incompatible change without a migration keeps its current
    diagnostic: `AXC0015`, `AXC0016` or `AXC0017`.
- **Changed history:**
  - An applied migration with a changed hash or a missing file is a new
    diagnostic at activation. Nothing runs.
  - **Forward only:** migrations run forward only. Activating a release
    that lacks an applied migration file is refused. An older release
    without that file cannot be activated again. The way back is a new
    release with a new forward migration.
- **Old-release work:**
  - `renameField`, `dropField`, `dropEntity` and `convert` change columns
    that older releases read.
  - After taking both locks, activation counts `running` and `waiting`
    instances pinned to another release whose model has the affected
    column. For a rename, that is the `from` column.
  - For each such release, `AXC0178` names the migration, the
    release and the number of open instances. Activation changes nothing.
    `fill` and additive changes never wait for old-release work.
  - **Start lock:** every transaction that reads the active release and
    inserts a process instance takes `pg_advisory_xact_lock_shared` keyed
    on the application `id`, before reading the active release. This covers
    process start endpoints, inbound endpoints, trigger starts and schedule
    fan-out.
  - An activation with a pending migration takes the same lock exclusively
    before the count and holds it to commit. A start either commits before
    the count and is counted, or reads the new release after activation
    commits. No start can slip between the count and the switch.
- **Computed fields:** a `fill` or `convert` of a field that a computed field
  reads recomputes the changed rows, and their owners for changed child rows,
  as [D33](d33-recomputing-stored-computed-fields.md) describes. Rule
  calls in computed fields remain unbuilt (`AXC0050`), so that case describes
  future behaviour.

_Why:_ [D11](d11-durable-process-engine.md) and
[D29](d29-release-pinning-and-text-resources.md) keep running work on
its release. Destructive changes must wait until that work stops reading
the old columns. The start lock keeps that check true until activation
commits. [D17](d17-entity-logic-and-child-collections.md) stores computed
values with their records, so migrations must recompute affected values, as
[D33](d33-recomputing-stored-computed-fields.md) describes.
[Execution](../domain/knowledge.md#execution) rules out
partial commits that leave storage and the active release disagreeing.
