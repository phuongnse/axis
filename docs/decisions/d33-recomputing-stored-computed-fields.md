# D33. Recomputing stored computed fields

Status: **Agreed**

## Context

[D17](d17-entity-logic-and-child-collections.md) stores computed values with their records, so a
release that changes them must correct the stored values. Doing that inside the activation
transaction does not scale to large tables. Work pinned to an older release by
[D29](d29-release-pinning-and-text-resources.md) can still write rows after activation, and
[execution](../domain/knowledge.md#execution) asks that stored state and the active release never
disagree silently.

## Decision

- **One recompute for three cases:** a changed computed expression, a computed field added to a
  table with rows, and a migration `fill` or `convert` of a field that a computed field reads.
- **Small tables at activation.** When the affected rows are at most the inline limit, a server
  setting, activation recomputes them in its own transaction with the release it activates.
- **Large tables in the worker.** Above the limit, activation marks the field pending and commits,
  and the worker recomputes the rows in batches, each in its own transaction, with the newest
  activated release.
- **Each row is stamped with its release.** A row records the release that computed it, and a row
  written by older-release work, or not known to be up to date, is taken again until no row is
  stale.
- **A batch never overwrites a concurrent write.** It updates a row only if the row's version is
  unchanged since the batch read it.
- **A failing row does not stop the work.** It gets `NULL`, and the error is recorded for operators.
- **Pending is visible, never silent.** While a field is pending, a single read computes it on the
  spot, and a filter or sort that needs it is refused with an error that names the field.

The steps, the setting, the refusals and an example are in
[recomputing computed fields](../reference/migrations.md#recomputing-computed-fields).

## Consequences

- For a short, clearly marked time after activating a large change, lists that filter or sort on
  the changed field are refused.
- Every write stamps its release, and the worker carries a backfill job.

## Alternatives

- **Recompute every row in the activation transaction.** It does not scale to large tables, and
  one row error stops the activation.
- **Answer filters on old values with a flag.** A list that filters on old values would still
  mislead its reader.
