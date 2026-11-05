# D77. Indexes, defaults and ranges

Status: **Agreed**

## Context

A typical business application has hundreds of indexes, and without any a filtered list reads the
whole table. Authors also need a field to start with a value, and to stay within limits, however the
record is created.

## Decision

- **The platform indexes every reference and every child table's owner,** with no declaration,
  because those are the joins every read makes.
- **An entity declares further indexes** in `indexes`, unique or not, over one or more fields in
  column order. The schema planner adds the ones a release declares and drops the ones it no longer
  declares, which needs no migration because an index holds no data.
- **A large index is built concurrently.** On a table above the inline limit, activation commits
  without a new non-unique index, and the index is then built with `CREATE INDEX CONCURRENTLY`, so
  writes go on. A unique index is always built inside activation, because the release relies on it.
- **A `default` is an expression the record command applies.** It applies on create when the body
  leaves the field out, so every way of creating a record gets it. It is not a database `DEFAULT` and
  never changes stored rows, and the form state shows it on a new form.
- **Ranges are checked by the record command.** `min`, `max` and `minLength` refuse a value outside
  them with a `400`, as `maxLength` does, and no `CHECK` constraint holds them.

The detail is in [indexes](../reference/resource-files.md#indexes),
[entity field types](../reference/resource-files.md#entity-field-types-and-constraints),
[schema planning](../reference/storage.md#schema-planning) and
[create, update and delete](../reference/record-api.md#create-update-and-delete).

## Consequences

- The common joins are fast with no work from the author.
- A publish never blocks writes on a busy table, but reads on it are slower until a concurrent build
  ends.
- Tightening a range needs no migration, so stored rows may keep values outside the new range.
- Filling existing rows with a value needs a `fill` migration, because a default never changes them.

## Alternatives

- **A default in the form only.** Records created by the API, an endpoint, an import or a process
  would not get it.
- **A `CHECK` constraint for a range.** Tightening a range would then need a migration. Enum values
  are checked by the record command for the same reason.
