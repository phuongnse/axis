# D33. Recomputing stored computed fields

Status: **Agreed**

A stored computed field stays correct when its expression or its inputs
change. One recompute covers a changed expression, a computed field added to a
table with rows and a migration `fill` or `convert`. Small tables recompute at
activation. Large tables recompute in the worker, in batches. The recompute at
activation of a changed or added computed field, and of the rows a migration
`fill` or `convert` changed, is built, and so are the inline limit, the
pending state, the worker backfill, a newer activation while a field is
pending, and the writes of older releases. A single read, the response of an
update, the engine's reads of a subject record and process start conditions
compute a pending field on the spot and store nothing. The record API and the
process endpoints refuse a sort, a search or a policy filter that reads a
pending field with `409`. Data sources refuse the same way, and a schedule
fan-out over a pending field waits for the backfill. See
[storage](../reference/migrations.md#recomputing-computed-fields) for the steps and
a walkthrough.
The owner confirmed these points on issue #473 on 2026-10-17.

- **One mechanism for three cases:**
  - A changed computed expression.
  - A computed field added to a table with rows.
  - A migration `fill` or `convert` of a field that a computed field reads.
- **Small tables at activation:**
  - When the affected rows are at most the inline limit, activation
    recomputes them in its own transaction, using the release being
    activated, as D30 says.
  - The limit is the server setting `Activation:InlineRecomputeLimit`. It
    defaults to 10,000 affected rows.
- **Large tables in the worker:**
  - Above the limit, activation marks the computed field **pending** and
    commits.
  - The worker then recomputes the affected rows in batches. Each batch runs in
    its own transaction, with the release being activated.
- **Release stamp:**
  - Each row records the release that computed it. A create, and an update
    that sends every child collection whose child entity has computed fields,
    recompute with their release, so they stamp that release.
  - An update that leaves out such a collection did not recompute its rows. It
    keeps the stamp only when it already names the update's release, and
    otherwise sets it to `NULL`, which means the row is not known to be up to
    date.
  - The backfill works until no row of the table carries an older stamp.
  - A write made under an older release, by work pinned to it, stamps the older
    release. The backfill then takes that row again.
- **Concurrent writes:** a batch updates a row only if its version is
  unchanged since the batch read it. A batch never overwrites a concurrent
  write.
- **Reading one record:** while a field is pending, the record API computes it
  on read for a row with an older stamp.
- **Row errors:** a row whose expression fails at run time gets `NULL`. The
  field and the error are recorded, for operators to see and fix. The work
  goes on.
- **Newer release:** activating another release while a field is pending makes
  the backfill continue with the newest release.
- **Filters and sorts:** a filter or sort on a pending field is refused with
  an error that names the field and says it is being recomputed. It is not
  answered with a flag.
- **Pending is accepted:** for a large table, a short, clearly marked period
  in which lists may not reflect the new expression is accepted.

_Why:_ [D17](d17-entity-logic-and-child-collections.md) stores computed
values with their records, so a release that changes them must correct the
stored values. [D29](d29-release-pinning-and-text-resources.md) keeps
running work on its release, so a row can be written under an older release
after activation. The stamp finds those rows.
[D30](d30-schema-migrations.md) recomputed in the activation
transaction, which does not scale to large tables and stopped activation on one
row error. [Execution](../domain/knowledge.md#execution) asks that stored state
and the active release not disagree silently. A list that filters on old values
without warning is that failure, so Axis refuses it.
