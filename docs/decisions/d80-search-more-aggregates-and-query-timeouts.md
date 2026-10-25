# D80. Search, more aggregates and query timeouts

Status: **Agreed**

M15 makes search use an index and reach more fields, adds aggregates to data
sources, and bounds every interactive query by a timeout. The owner agreed
these points on 2026-10-25. All of it is _(planned for M15)_. See
[paging and sorting](../reference/record-api.md#paging-and-sorting),
[data source aggregates](../reference/data-sources.md#aggregates) and
[data settings](../reference/host-settings.md#data-settings).

- **Search:**
  - A `text` field takes `searchable: true`. Each such field gets a `pg_trgm`
    GIN index on `lower()` of its column. The display field is searchable
    whether or not it says so.
  - `search` keeps the records where any searchable field that the user may
    read contains the text, ignoring letter case. It stays a substring match
    with no wildcards, so it works the same in Vietnamese and every other
    language, with no stemming.
  - The query and its rules do not change: one `search` parameter, at most
    200 characters, and a `409` when a searchable field is pending.
  - A data source takes `"search": true` to accept `search` over its
    projected searchable fields.
  - `Axis.Admin` creates the `pg_trgm` extension at `tenant create`, and
    `migrate` creates it in existing tenant databases.
  - Ranked word search waits.
- **More aggregates:**
  - Data source measures add `avg` and `countDistinct`, and each measure takes
    an optional `filter` that keeps only the rows it counts.
  - A `groupBy` entry on a date or date-time field takes a bucket: `day`,
    `week`, `month` or `year`. Date-time buckets use the application time zone
    of [D75](d75-the-application-time-zone.md).
  - Expressions add `avg(list, item)`.
  - A data source over a child entity stays refused (`AXC0064`).
- **Query timeouts:**
  - Every interactive read runs with `SET LOCAL statement_timeout` from
    `Data:QueryTimeout`, 10 seconds by default. Exports use
    `Data:ExportQueryTimeout`, and imports run on the worker under its own
    limits.
  - A statement that runs past it, SQLSTATE `57014`, is a `503` with a fixed
    title. The server logs the data source or entity name and the duration,
    never a value, and counts timeouts in a metric.
  - When only the count statement of a list times out, the answer is `200`
    with `totalCount: null`.
  - M16 sets the limits against its SLOs. M15 ships the mechanism and the
    defaults.

_Why:_ search on the display field alone misses what users type, and a scan
of the whole table does not scale to the typical application size. Trigram
indexes serve substring search in any language, which suits Vietnamese
better than word stemming. Averages, distinct counts, filtered measures and
date buckets are what dashboards ask for first. A timeout keeps one slow
query from holding a connection for everyone, and a fixed `503` tells the
client to try again without leaking the query.
