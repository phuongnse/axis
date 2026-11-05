# D18. Data sources

Status: **Agreed**

## Context

Pages and processes need lists, lookups and totals over related records. Copying related data into
each record makes it go stale, as
[authoring and packaging](../domain/knowledge.md#authoring-and-packaging) warns, so Axis queries it
instead.

## Decision

- **A read-only query.** A `dataSource` resource has one root entity. It projects the entity's own
  fields and fields reached through references, filters with an expression over typed parameters,
  has a default sort and paging, and may group and aggregate. A data source never writes.
- **Safe filters.** A filter is written in the expression language of
  [D16](d16-expression-language.md) and becomes parameterized SQL, never concatenated text. A filter
  that cannot be translated is a compile error.
- **Served under the application.** A read endpoint serves each data source, with the paging and
  sorting rules of the record API and under the user's policies.
- **Widgets bind to it.** A widget names a `dataSource`, and a table may name an `entity` as
  shorthand for all records of that entity. The SPA gets each data source's schema in page
  metadata.

The resource, its checks, the endpoint and the widget binding are in
[data sources](../reference/data-sources.md).

## Consequences

- Related data is always read as it is now, with nothing to keep in step.
- One expression check covers filters too, and a filter cannot inject SQL.
