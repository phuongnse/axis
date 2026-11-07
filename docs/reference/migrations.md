# Migrations and recomputing

This page says how activation changes stored data that additive provisioning cannot change: the
migrations that rename, drop, fill or convert a field, and the recompute that keeps stored computed
fields true to their release. The schema planning that decides what changes is in
[storage](storage.md#schema-planning).

## Recomputing computed fields

A computed field is stored, so its stored values must be computed again whenever its inputs or its
definition change. One recompute covers three cases: a changed computed expression, a computed field
added to a table that has rows, and a migration `fill` or `convert` of a field that a computed field
reads. A table with few affected rows recomputes inside the activation transaction, and a table with
many recomputes in the worker, in batches, while reads answer as [while pending](#while-pending)
says.

- **Affected rows.** A changed or added computed field affects every owner row of its entity. A
  changed child computed field also affects every owner computed field that reads it through an
  aggregate, because the owner's stored total is as stale as the line amounts it sums. A `fill` or
  `convert` affects the rows it changed, and the owner rows of changed child rows.

### Stamps and fingerprints

Each row records the release that computed it, and each release records a hash of its computed
definitions, so Axis can tell which rows are out of date without computing them.

- **Stamp.** Each table that is not a child table has the system column
  `computed_release_id uuid NULL`, which names the release that last computed the row. Adding it is
  cheap on a large table, because it is nullable and has no default.
  - A create stamps the release it ran with. So does an update that sends, and so recomputes, every
    child collection whose child entity has computed fields. That includes a record API write, an
    inbound endpoint create, a step of a process pinned to an older release, a connector or package
    result write, a task completion and seed data. Work pinned to an older release stamps that
    release, not the active one. A row written before the column existed has no stamp.
  - An update that leaves out such a collection did not recompute its rows, so it keeps the stamp
    only when the stamp already names the update's release, and otherwise sets it to `NULL`. A step,
    a connector or package result write and a task completion never send child rows, so they clear
    the stamp of a row that has child rows with computed fields, unless it already names their
    release. A collection whose child entity has no computed field does not count.
  - Child rows recompute with their owner and carry no stamp.
  - A `fill` or `convert` sets the stamp to `NULL` on every row it changes and on the owner row of
    every changed child row, because their computed inputs changed and the old stamp no longer
    proves the row is up to date.
- **Fingerprint.** Each release stores, for each entity that is not a child entity, one hash of the
  computed definitions of the entity and its child entities, in
  `axis.release_computed_fingerprints`.
  - The hash covers each computed field's name, type, length or precision settings and expression
    text, for the entity and for each child collection, and each rule the expressions call, directly
    or through other rules, with its parameters, result type and expression text. Two releases with
    the same computed definitions have the same fingerprint, even when other parts of the entity
    differ. A change to whitespace only in an expression changes it.
  - A child entity has no fingerprint row, because its rows carry no stamp and recompute with their
    owner. An entity with no computed field still has a row, so adding one later shows as a changed
    fingerprint.
  - A release stored before fingerprints existed has no rows, and nothing fills them in. A missing
    fingerprint counts as different.
- **Up to date.** A row is up to date when its stamp names a release whose fingerprint for the
  entity equals the target release's. A row with a `NULL` stamp is not known to be up to date. Only
  an activation that changes its entity's computed definitions, an activation whose `fill` or
  `convert` touches the entity, or a later full write fixes it. The backfill treats a row with no
  stamp as out of date only while the entity has a pending field, because otherwise every release
  that changes nothing about a computed field would recompute every row of every table. The pending
  row keeps the releases whose stored fingerprint for the entity equals the target's as
  `current_release_ids`, and always the target itself, because a release found again by content
  hash may come back without its fingerprints. After a large `fill` the backfill also recomputes
  rows stamped by an older release that has no stored fingerprints.

### At activation

Activation recomputes what it can inside its own transaction, and leaves the rest pending for the
worker.

- **Recompute.** After the additive DDL, activation compares the fingerprint of each entity that is
  not a child entity with the active release's, read from `axis.release_computed_fingerprints`. It
  recomputes every owner row of an entity whose fingerprint is missing or differs, with the release
  being activated.
  - It recomputes every computed field of the entity and of its child entities, not only the
    changed ones, and the owner's aggregates read the new child values.
  - The release being activated gets its fingerprints from its compiled model, because a release
    found again by content hash comes back without them. Activating the release that is already
    active recomputes nothing, even if it has no stored fingerprints. On the first activation of an
    application every entity counts as changed, which costs nothing because the tables are new.
  - A row is updated only when its `version` still matches the version that was read, so a
    concurrent write is never overwritten.
  - Then, for each entity that a `fill` or `convert` touched, and that the step did not just
    recompute whole, it recomputes the owner rows whose stamp is `NULL`. The entity is the one whose
    rows the migration changed, or the owner of a changed child entity. It recomputes the computed
    fields of the owner and its child entities, deletes the error rows of those records only, and
    stamps the rows with the release being activated. It does this even when no computed field reads
    the changed field, which only stamps the rows again. Rows that already had a `NULL` stamp are
    recomputed too.
- **Inline limit.** Activation counts the affected rows of each entity: every row of the table for a
  changed computed definition, and the rows left with a `NULL` stamp for a `fill` or `convert`. An
  entity with both counts every row once. When the count is at most
  `Activation:InlineRecomputeLimit`, a [server setting](host-settings.md#startup-activation), activation
  recomputes the rows in its own transaction with the release being activated, and stamps each row.
- **Pending.** Above the limit, activation writes a row in `axis.pending_computed_fields` and a
  `computed.backfill` work item, then commits without touching a row. The computed field is then
  _pending_: its stored values may still come from an older expression. The pending row lists every
  computed field of the entity and of its child entities, and the backfill recomputes and stamps the
  rows with the target release.
- **Row errors.** A row whose expression fails at run time, or reads a decimal that cannot be held
  exactly, gets `NULL` and its stamp, and the work goes on. The recompute inserts a row in
  `axis.computed_field_errors` with the entity, the record id, the field, the release and a fixed
  error text, the message the record API gives for that failure. It stores no value, because the
  error can name one. A failing field of a child row is keyed by the child entity id and the child
  row id, because child rows have their own ids. Before it recomputes an entity, activation deletes
  the error rows of that entity and of its child entities, because it computes every row again.
- **Restart.** Activating a release while a field is pending sets the target to the new release,
  recomputes `current_release_ids`, merges the pending fields and applies the inline limit again,
  so the backfill continues with the newest release. This holds for every pending entity of the
  application, also when its fingerprint did not change.
  - Activation counts the rows that are out of date for the new release's current releases. When
    there are at most `Activation:InlineRecomputeLimit`, it recomputes them in its own transaction,
    and if none is left it deletes the pending row and the work item.
  - Above the limit, it updates the pending row and keeps the work item. The merged field list can
    hold a name that only an older release had, such as a renamed computed field, until a later
    restart replaces it.

### Writes of older releases

Work pinned to an older release can write rows after the backfill has ended, so such a write makes
its entity pending again.

- **The check.** A write whose release has a different fingerprint from the active release's
  inserts the pending row and the `computed.backfill` work item in its own transaction, when they
  are missing. It compares two stored fingerprints, so the check is cheap.
- **Who checks.** Four paths can write under a release that is not the active one: a task
  completion, a process step update, and a connector or package result write. Each calls the check
  right after its record write. A write on the active release, such as the record API, an inbound
  endpoint or seed data, adds nothing, and neither does a write whose release stores the same
  fingerprint as the active release. A release with no stored fingerprint counts as different.
- **Locks.** The write takes a `FOR KEY SHARE` lock on the pending row, after it locked the record
  row. That lock conflicts only with the delete of a backfill's last batch, so it blocks neither
  activation nor other writes. When there is no pending row, the write inserts one, whose target is
  the active release and whose fields are the computed fields of the write's own release model.
- **Work item.** The work item stores the trace and span ids of the write, as the work item of an
  activation does. One helper inserts every `computed.backfill` work item, and it inserts none when
  the entity has one.

### The backfill

The worker recomputes a pending entity in batches, without changing what users see as an edit.

- **Batches.** The worker claims a `computed.backfill` work item and runs one batch of 500 rows, in
  `ORDER BY id`, in its own transaction. It selects out-of-date rows with their `version`, recomputes
  them with the application's active release, and runs `UPDATE ... WHERE id = $1 AND version = $2`.
  A row whose version changed is skipped, and a later batch takes it if its stamp is still out of
  date.
- **The active release.** The pending row can name a release older than the active one, when a
  write inserted it while an activation was still committing. The batch then computes with the
  active release, and the up-to-date releases are those whose stored fingerprint for the entity
  equals the active release's, because computing with the old target would roll the table back to
  old values.
- **No lock on the pending row.** A batch holds no lock on the pending row. Pinned writes lock the
  record row before the pending row, so a batch that held the pending row while it updated rows
  could deadlock with them. Only the last check locks it.
- **No edit.** The update does not change `version`, writes no audit record and starts no trigger,
  because a higher `version` would give a user who is editing the record a `409` for a change they
  did not make.
- **Next batch.** The work item is made due again at once, so the next claim runs the next batch,
  until a batch finds no out-of-date row. A batch keeps no cursor: it selects the first 500
  out-of-date rows again, so a skipped row is taken by a later batch. A row that users keep updating
  can be skipped batch after batch, and then the item stays queued until the row is left alone.
- **Row errors.** A row whose expression fails gets `NULL` and an error row, as at activation. The
  batch deletes the error rows of its records first.
- **End.** The batch that finds no out-of-date row locks the pending row `FOR UPDATE`, checks the
  table again, and deletes the row and completes its work item if still no row is out of date. The
  lock waits for a write that holds the row `FOR KEY SHARE`, and the check then sees that write's
  row. If a row is out of date, the batch reschedules the item and the row stays. A write after the
  delete finds no pending row and inserts a new one. When the active release no longer has the
  entity, the batch deletes the pending row and the item without a batch.

### While pending

While a field is pending, its stored values may be stale, so reads that show one record compute it
on the spot, and requests that would sort, search or filter on stale values are refused.

- **One record.** A single read, the response of an update, and the engine's reads of one subject
  record compute a pending field with the active release and do not store the result. Process
  start conditions and the `when` of a data change trigger read the same way. A read must not
  write, so the backfill or the next write stores the value. A run-time error reads as `null`. A row
  is computed only when its stamp is out of date. An instance pinned to an older release takes a
  computed value only for a field with the same name and type in both releases.
- **Stored values.** A list, a data source and a projection show the stored value, and so do the
  inbound endpoint's output and the test scenario runner.
- **Record API and processes.** A record list sort or search that names a pending field is refused
  with `409`. A policy `read` filter that reads one is refused too, for a list, a single read, a
  history, a process start, the process instance list, the instance detail and an operator cancel
  or retry, and so is an `update` or `delete` filter. The check reads the pending state of the
  entity, not the stamp of a row, so it holds until the backfill ends. See the
  [record API](record-api.md#paging-and-sorting).
- **Data sources and schedules.** A data source sort, filter or grouping that reads a pending field
  is refused with `409`, and so is a root read filter that reads one. A schedule fan-out whose data
  source filter or grouping reads one starts nothing and runs again one lease later. See
  [data sources](data-sources.md#errors) and [schedules](process-events.md#schedules).

### Recompute tables

`Axis.Data` owns `axis.pending_computed_fields` and `axis.computed_field_errors`. Activation and the
worker write both, and so does a record write that finds an older release, so the runtime database
role may write both. The work item of a pending entity holds the entity `id` in
`axis.process_work_items.entity_id`.

`axis.pending_computed_fields` has one row per entity with a pending field:

| Column                | Type          | Meaning                                                                              |
| --------------------- | ------------- | ------------------------------------------------------------------------------------ |
| `entity_id`           | `uuid`        | Primary key, the entity `id`                                                         |
| `application_id`      | `uuid`        | The application `id`                                                                 |
| `target_release_id`   | `uuid`        | The release the backfill computes with, the newest activated                         |
| `current_release_ids` | `uuid[]`      | The releases whose fingerprint for the entity equals the target's                    |
| `fields`              | `text[]`      | The pending field names, including owner fields that aggregate a pending child field |
| `created_at`          | `timestamptz` | When the field became pending                                                        |

`axis.computed_field_errors` has one row per record and field whose expression failed:

| Column        | Type          | Meaning                                           |
| ------------- | ------------- | ------------------------------------------------- |
| `entity_id`   | `uuid`        | The entity `id`                                   |
| `record_id`   | `uuid`        | The record id                                     |
| `field`       | `text`        | The computed field name                           |
| `release_id`  | `uuid`        | The release that computed the row                 |
| `error`       | `text`        | A fixed error text. It never holds a stored value |
| `occurred_at` | `timestamptz` | When the recompute recorded it                    |

Its primary key is `(entity_id, record_id, field)`, and a later error for the same key replaces the
row.

`axis.release_computed_fingerprints` belongs to `Axis.Configuration`, next to `axis.releases`. It has
`release_id`, `entity_id` and `fingerprint` (`varchar(64)`, SHA-256 as lower-case hex), with
`(release_id, entity_id)` as its primary key. It is written when the release is stored, and the
runtime database role reads it.

### Example: changing a computed expression on a large table

This example uses the purchase request. R1 and R2 stand for stored release UUIDs of the same
application, as in the [migrations example](#example-renaming-a-field-while-a-task-waits). The table
holds about 50,000 purchase requests. R2 changes the `amount` expression of the line item, so
`amount` and the aggregate `total` of the request become pending.

1. R2 is activated. Activation counts 50,000 affected rows, which is more than 10,000. It inserts
   the pending row for the purchase request, with R2 as the target and `amount` and `total` as the
   fields. It queues a `computed.backfill` work item and commits. No row changes.
2. A list sorted by `-total` answers `409`, "A field is being recomputed.", with `errors.sort`
   naming `total`. A list with no sort answers with the stored values. A data source refuses the
   same way.
3. A single read of one request returns `total` computed on the spot with R2. It stores nothing.
4. A user updates a request. The update sends the line items, so the write recomputes them with R2
   and stamps R2. The backfill skips this row later, because its stamp is up to date.
5. The worker claims the work item and runs batches of 500 rows. Each batch is one transaction, and
   each stores R2 values and stamps R2, with no change to `version`, no audit record and no
   trigger.
6. A task completion pinned to R1 writes a purchase request. The row gets R1 values and an R1 stamp.
   R1's fingerprint differs from R2's, so a later batch takes that row again. The pending row
   already exists, so the write adds nothing.
7. One row has a quantity that makes the R2 expression overflow. Its batch sets `total` to `null`,
   stamps the row and inserts a row in `axis.computed_field_errors` with the field and a fixed error
   text. The backfill goes on.
8. A batch finds no out-of-date row. It deletes the pending row and its work item. Sorting by
   `total` works again.
9. Later, another task completion pinned to R1 writes a request and stamps R1. Its transaction sees
   that R1's fingerprint differs from the active release's, so it inserts the pending row and a new
   work item again. The next backfill takes only that row.

## Migrations

A [migration resource](resource-files.md#resource-file-shape) changes one entity's storage in a way
that additive provisioning cannot: it renames or drops a field, drops an entity, fills a field or
converts its values. It runs once per tenant, before additive provisioning, and migrations run
forward only. A migration may name an entity of a declared module as `<module id>/<entity>`, to drop
or convert a column that a [customization layer](customization-layers.md#module-entities) removed
or retyped in the application's copy, but it cannot drop the module entity.

A migration is pending when `axis.applied_migrations` has no row for its resource `id` in the tenant
database.

### Order of statements

An activation with a pending migration runs these steps in one transaction:

1. Take the schema lock, `pg_advisory_xact_lock`.
2. Take the application's [start lock](#start-lock) exclusively. An activation with no pending
   migration skips this step. The pending migrations are known only after reading
   `axis.applied_migrations`, so the lock comes right after that read and before any changed-history
   diagnostic is acted on. The schema lock already keeps those rows from changing in between.
3. Check [changed history](#changed-history). Any diagnostic stops the activation. Right after it,
   the activation checks the module upgrade: a module copy that drops an entity or a field, or
   changes a field's type, against the active release's copy is `AXC0213`. This check runs whether
   or not a migration is pending, and any diagnostic stops the activation with nothing applied. See
   [modules](modules-and-templates.md#modules).
4. Count [old-release work](#old-release-work) for the destructive changes. Any such work stops the
   activation with `AXC0178`. The count runs after planning and before the first statement, and only
   when the plan holds a destructive change that plans a statement.
5. Run the pending migrations in ordinal path order, and their changes in file order. The entity
   designer names its migrations by time, so the files of one entity run in the order they were
   written, and a hand-written migration of the same entity whose name sorts later runs after them.
   Nothing else orders them.
6. Run the additive DDL, as [schema planning](storage.md#schema-planning) orders it.
7. Recompute inline or mark pending with the release being activated, as
   [recomputing computed fields](#recomputing-computed-fields) says. This includes the rows that a
   `fill` or `convert` changed and the owners of changed child rows. An activation without a pending
   migration runs the same step after its additive DDL.
8. Write `axis.applied_migrations` and the provisioning records.
9. Upsert the active release and its `axis.active_sites` rows.
10. Commit, releasing both locks.

The lock order is always the schema lock, then the start lock. Process starts never take the schema
lock, so this order cannot deadlock with them. A start takes the start lock as the first statement of
its transaction, before it reads or writes any entity table, because otherwise a start that already
holds a table lock could wait for the activation, which then needs an exclusive lock on that table,
and PostgreSQL would abort one of them.

### Changes

Each change of a migration plans the SQL below. A destructive change waits until no work of an older
release can still use the column.

| Change           | SQL                                                                                                                                                                                                                                                            | Waits for old-release work |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| `renameField`    | `ALTER TABLE ... RENAME COLUMN`, plus `RENAME CONSTRAINT` for its `uq_` and `fk_` names, which hash the column name. A sensitive field's values are encrypted again under the new name                                                                         | Yes                        |
| `dropField`      | `ALTER TABLE ... DROP COLUMN`, without `CASCADE`                                                                                                                                                                                                               | Yes                        |
| `dropEntity`     | `DROP TABLE` for child tables, then the entity table, without `CASCADE` so a remaining foreign key fails. `DELETE` its provisioning records.                                                                                                                   | Yes                        |
| `fill`           | `ADD COLUMN` as nullable when missing, then `UPDATE ... SET f = <value> WHERE f IS NULL`                                                                                                                                                                       | No                         |
| `convert`        | For a type change, `ADD COLUMN axis_convert`, `UPDATE ... SET axis_convert = (<value>)::<type>`, `ALTER COLUMN ... TYPE ... USING axis_convert`, then `DROP COLUMN axis_convert`. For removed enum values, `UPDATE ... SET f = <value> WHERE f IN (<removed>)` | Yes                        |
| Additive changes | As in [schema planning](storage.md#schema-planning)                                                                                                                                                                                                            | No                         |

- **Fill.** A `fill` on a missing column adds it as nullable before the `UPDATE`, and the additive
  step afterwards adds `NOT NULL` for a required field. The `UPDATE` counts the rows it left `null`,
  and any such row is `AXC0176` and rolls the activation back.
- **Convert to a new type.** A type-changing `convert` runs four statements, because DDL cannot take
  the parameters that a value's literals need. A temporary nullable column `axis_convert`, which no
  field column can clash with, takes the value from one parameterized `UPDATE`.
  `ALTER COLUMN ... TYPE ... USING axis_convert` then moves it into the field's column, and the
  temporary column is dropped. The `UPDATE` casts the value to the field's new column type, and that
  cast does the parsing, so `"value": "quantity"` turns a text `quantity` into an integer.
  PostgreSQL's cast rules decide what parses: `' 42 '` becomes 42, and `'42.0'` fails as an integer.
  A cast to a decimal with a smaller scale rounds. For a text field with a `maxLength`, the cast is to
  `text`, so the `ALTER` fails on a value that is too long instead of truncating it.
- **Convert removed enum values.** An enum mapping is one `UPDATE` that changes only the rows holding
  a removed value, so other rows keep their value and their `version`. The removed values are then
  deleted from the recorded enum values.
- **What a convert cannot do.** A `convert` between `enum` and another type is refused with
  `AXC0016`. A `convert` of a `reference` field to another type fails on its foreign key, as
  `AXC0177`. The values of a `fill` and a `convert` must translate to the
  [SQL subset](expressions.md#sql-subset).
- **No sensitive fields.** The target of a `fill` or `convert` cannot be a `sensitive` field, and
  neither can a field its value reads (`AXC0308`, at `/changes/{i}/field` or `/changes/{i}/value`),
  because the SQL would write plain text into the encrypted column. See
  [data encryption](data-encryption.md#what-a-sensitive-field-cannot-do).
- **A failed convert.** A `convert` that cannot convert a row rolls the whole activation back. That
  covers unparsable text, a value that is too long, a required field left empty and a duplicate in a
  unique field. It is `AXC0177`, reported with a fixed message and the PostgreSQL error code. The
  message never quotes PostgreSQL's text, because that can hold a stored value. An enum mapping that
  leaves a value the field does not list is `AXC0177` too.
- **Versions.** Each `fill` and `convert` increments `version` on every row it changes. Child rows
  have no version, so changing them increments their owner's `version`, in the same statement. So
  an edit begun before the migration cannot overwrite a migrated value without a version conflict.
  There is no audit record for each changed row: the `axis.applied_migrations` row is the trace.
- **Computed fields.** A `fill` or `convert` clears the stamp of the rows it changes, and of the
  owner of each changed child row. Activation recomputes those rows inline, or leaves them pending
  for the worker above the inline limit, as
  [recomputing computed fields](#recomputing-computed-fields) says.

### Changed history

An applied migration with a changed content hash or a missing file is `AXC0174`, and nothing runs.
A changed file is reported at that file with an empty path, and a missing file at
`application.json` with an empty path. The `resourceId` is the migration `id`. The hash is the
SHA-256 of the file's canonical JSON alone, without its path, so moving a file does not count as
changing it. A release that lacks an applied migration file cannot be activated, so an older release
without that file cannot be activated again. The way back is a new release with a new forward
migration.

### Old-release work

A destructive change waits until no open work of another release can still use the column it
changes. After taking both locks, activation counts the `running` and `waiting` process instances
pinned to a release other than the one being activated whose stored model has the column the
change affects.

- **Changes that wait.** `renameField` (its `from` column), `dropField`, `dropEntity` (the dropped
  entity and the child entities dropped with it) and `convert`. A `fill`, an additive change and a
  change that plans no statement never wait for old-release work.
- **Which releases count.** A release has the column when its stored entity has the same entity id
  and a field of that name, ignoring letter case. A release whose stored content cannot be found
  counts as using every column. Instances of the release being activated and of other applications
  do not count.
- **The refusal.** For each such release and migration, `AXC0178` names the migration, the release
  and the count of open instances. It is reported at the migration file with an empty path, and the
  migration `id` is its `resourceId`. Activation then changes nothing. Once the old instances end,
  activating the same release again applies the migration.

### Start lock

The start lock keeps a process start from slipping between the count of old-release work and the
switch of the active release.

- **Starts share it.** Every transaction that reads the active release and inserts a process
  instance takes `pg_advisory_xact_lock_shared` keyed on the application `id`, before it reads the
  active release. The key is a `bigint`, the XOR of the two 64-bit halves of the id. Another key can
  collide with it, which only makes a start wait longer.
- **Who takes it.** The process start endpoint, the inbound endpoint, record API creates, updates and
  deletes, task completion, event delivery, `sync` event triggers, update triggers from worker steps,
  schedule fan-out, and operator [retry](process-inspection.md#retry) and
  [resolve](process-inspection.md#resolve). A record API create or update always takes it, because
  the new release may add triggers, and a delete takes it because the new release may add delete
  triggers.
- **Activation holds it exclusively.** An activation with a pending migration takes the same lock
  with `pg_advisory_xact_lock` and holds it to commit. It waits for open starts and holds new ones
  until commit, so a start either commits before the activation counts open work, or reads the new
  release after the activation commits. An activation with no pending migration never takes the
  lock.
- **A changed release.** A request that finds a different active release under the lock runs again
  on the new release. See [releases and running work](processes.md#releases-and-running-work).

### Failure

Any failure rolls back the migrations, the additive provisioning, the migration and provisioning
records, the active release and its site paths, including a refused application name or site path
at the upsert. The active release does not change, and a retry runs the pending migrations again
because nothing stayed applied. A `dropEntity` that a remaining foreign key blocks surfaces as a
database exception, not a diagnostic, and everything still rolls back.

The active release is always written through the active release store in the provisioning
transaction, under the schema lock held since the migration history check, so another activation
cannot commit a rename or drop between them. The store and the data context must therefore share one
tenant connection, and a store on another connection throws before anything commits.

### Applied migrations

`Axis.Data` owns `axis.applied_migrations` in each tenant database. It records a migration only when
activation commits, and has an index on `application_id`.

| Column            | Type          | Meaning                                                                                  |
| ----------------- | ------------- | ---------------------------------------------------------------------------------------- |
| `migration_id`    | `uuid`        | Primary key, the migration resource `id`                                                 |
| `application_id`  | `uuid`        | The application `id`                                                                     |
| `release_id`      | `uuid`        | The release that applied the migration                                                   |
| `content_hash`    | `varchar(64)` | SHA-256 of the file's canonical JSON as lower-case hex, without its path                 |
| `applied_at`      | `timestamptz` | When the migration was applied                                                           |
| `changed_columns` | `jsonb`, null | The columns the migration changed, as `[{ entityId, field }]`, or null when not recorded |

### Example: renaming a field while a task waits

This example uses the purchase request migration in
[configuration](resource-files.md#resource-file-shape). R1 and R2 stand for stored release UUIDs of
the same application.

1. R1 is active. An employee submits a purchase request. Its process instance is pinned to R1 and is
   `waiting` on `managerApproval`.
2. R2 adds `RenameDecisionComments`. Its entity file renames `decisionComments` to `reviewComments`
   and adds the required `priority`. The migration renames the column and fills `priority` with
   `'normal'`.
3. Activating R2 is refused with `AXC0178`, naming migration `RenameDecisionComments`, release R1
   and 1 open instance. R1 stays active. No schema, stored value or migration record changes.
4. The manager approves. The instance runs to `completed`.
5. Once no `running` or `waiting` instance is pinned to R1, activating R2 applies the rename and
   fill, provisions the required constraint, recomputes affected computed fields and switches the
   active release and site paths in one transaction.

New submissions while R1 is active also count, so on a busy application the rename may be refused
for a long time. The start lock stops a submission from slipping between the count and the switch.
Shipping the `fill` alone in an earlier release never waits for old-release work.

### Limits

These limits follow from running migrations inside activation.

- **Long fills.** A large `fill` or `convert` holds the schema lock and its row locks until commit.
  The `fill` itself is not batched. The recompute of the computed fields that read it runs inline in
  the same transaction, up to the inline limit, and above the limit the rows go to the worker, which
  recomputes them in batches.
- **Waiting starts.** Process starts for the application wait while activation holds the exclusive
  start lock, so a long fill delays them.
- **Draining old work.** Old work must finish before a destructive migration can run. An operator can
  drain it by cancelling the `running` and `waiting` old-release instances, see
  [cancel](process-inspection.md#cancel). A call that is `sending` or `unknown` must settle or be
  [resolved](process-inspection.md#resolve) first. A `failed` instance does not block the migration,
  and a [retry](process-inspection.md#retry) of one is refused when the migration changed a column it
  uses.
- **Requests across the switch.** A record API create or update takes the start lock and runs again
  on the new release when the active release changed. A list, a read, a history or a delete that
  resolved the previous release just before the commit can still fail once on a renamed or dropped
  column. The same holds for a create or update that reads the stored record before it takes the
  lock.
