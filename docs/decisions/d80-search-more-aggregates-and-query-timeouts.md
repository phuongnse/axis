# D80. Search, more aggregates and query timeouts

Status: **Agreed**

## Context

Search on the display field alone misses what users type, and a scan of the whole table does not
scale to the typical application size. Dashboards first ask for averages, distinct counts, filtered
measures and totals by month. And one slow query can hold a database connection that every other
user needs.

## Decision

- **Search is a substring match over searchable fields.** A `text` field takes `searchable`, and
  `search` keeps the records where the display field or any searchable field the user may read
  contains the text, ignoring letter case, with no wildcards and no stemming. Each searched column
  gets a `pg_trgm` index, and a data source opts in with `"search": true`.
- **Data sources get more aggregates.** Measures add `avg` and `countDistinct`, each measure takes an
  optional `filter`, and a `groupBy` entry on a date or date-time field takes a `day`, `week`,
  `month` or `year` bucket in the [application time zone](d75-the-application-time-zone.md).
  Expressions add `avg(list, item)`.
- **Every statement runs under a timeout.** Interactive reads, exports, writes, worker items and
  activation each run with `SET LOCAL statement_timeout` from a setting of their own. Each default
  is about 30 times the p95 objective of its group in the [SLOs](d82-performance-slos-and-load-tests.md),
  so it stops only a runaway statement.
- **A timeout is a fixed `503`.** Its title tells the client to try again and names no query, and a
  write that timed out wrote nothing. The server logs the source and the duration, never a value,
  and counts timeouts in a metric. When only a list's count times out, the list still answers, with
  no total.

The detail is in [paging and sorting](../reference/record-api.md#paging-and-sorting),
[data source aggregates](../reference/data-sources.md#aggregates),
[data settings](../reference/host-settings.md#data-settings) and the
[performance targets](../reference/performance.md).

## Consequences

- Search works the same in Vietnamese and every other language, and finds no word forms.
- A slow query fails for its own user instead of slowing everyone, and the client learns nothing of
  the query.

## Alternatives

- **Word search with stemming.** It ranks results, but stemming suits Vietnamese worse than a
  substring match does.
- **No timeout.** One runaway query would hold a connection for everyone.
