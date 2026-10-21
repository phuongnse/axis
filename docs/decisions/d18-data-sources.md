# D18. Data sources

Status: **Agreed**

Data sources are read-only queries over entities, added in M2. See
[the reference](../reference/data-sources.md).

- **The resource:** a `dataSource` has one root entity. It projects the
  entity's own fields and fields reached through references. It has a filter
  expression with typed parameters, a default sort and paging, and
  aggregates: group by, with count, sum, min and max.
- **Filters are safe:** a filter becomes parameterized SQL and is never
  concatenated into it. A filter that cannot be translated is a compile
  error.
- **Serving:** a read endpoint under the application serves each data source.
  It follows the record API's paging and sorting rules. The SPA gets each data
  source's schema in page metadata.
- **Read-only:** data sources never write. Policy record filters are added in
  M4.
- **Widget binding:** a widget binds to a `dataSource`, and `entity` stays as
  shorthand for all records of an entity. In M2 only the `table` widget binds.

_Why:_ see [knowledge](../domain/knowledge.md#authoring-and-packaging). Related
data is queried, not copied into each record, so it never goes stale. Filters
use the one expression language (D16), so one check covers them.
