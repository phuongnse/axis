# Data sources

Detailed reference for data sources: the resource shape, the compile checks,
the read endpoint, the response and the errors. Parts of this file are built:

- the `dataSource` resource with `entity`, `fields` whose `path` may go
  through `reference` fields, `parameters`, `filter`, `aggregate`, `sort`
  and `pageSize`
- the read endpoint with `page`, `pageSize`, `sort` and the data source
  parameters, for plain and grouped data sources
- `labels` for projected `reference` fields
- the `table` widget binding, with the data source's schema in page metadata,
  its columns, paging and sorting
- the filter inputs of a bound table, with their values in the URL
- the page parameter binding of a table, with the not-found page for a
  required page parameter that is missing or malformed
- the table binding of grouped data sources
- record and field policies: the root entity's read filters in the query,
  the field check on every projected field, and labels limited to readable
  target display fields

Dn refers to
[decisions.md](../decisions.md). The design follows
[D18](../decisions/d18-data-sources.md). The reason to query instead
of denormalize is in
[knowledge](../domain/knowledge.md#authoring-and-packaging).

A data source is a read-only, named query over one root entity. It projects
fields, filters rows with typed parameters, sorts, pages and can group. A
table widget shows its rows. A chart or KPI widget shows its groups.
Filters use the expression language of
[expressions.md](expressions.md) and become parameterized SQL.

## Resource shape

A `dataSource` resource looks like this:

```json
{
  "id": "f8b1a5d4-0e6c-4f20-9a4d-5c6e7f8a9b09",
  "kind": "dataSource",
  "name": "OpenRequests",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "fields": [
    { "name": "title", "path": "title" },
    { "name": "status", "path": "status" },
    { "name": "total", "path": "total" },
    { "name": "submittedAt", "path": "submittedAt" },
    { "name": "departmentName", "path": "department.name" }
  ],
  "parameters": [
    {
      "name": "statusFilter",
      "type": "enum",
      "values": ["draft", "submitted", "approved", "rejected"],
      "required": false,
      "label": { "textKey": "requests.statusFilter" }
    }
  ],
  "filter": "statusFilter is null or status == statusFilter",
  "sort": "-submittedAt",
  "pageSize": 20
}
```

A grouped data source adds `aggregate`. Its rows are groups, not records:

```json
{
  "id": "a9c2b6e5-1f7d-4a31-8b5e-6d7f8a9b0c10",
  "kind": "dataSource",
  "name": "RequestsByDepartment",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "fields": [
    { "name": "departmentName", "path": "department.name" },
    { "name": "total", "path": "total" }
  ],
  "aggregate": {
    "groupBy": ["departmentName"],
    "measures": [
      { "name": "requests", "function": "count" },
      { "name": "totalSum", "function": "sum", "field": "total" }
    ]
  },
  "sort": "-totalSum"
}
```

- **`entity`.** The root entity. In M2 it cannot be a child entity
  (`AXC0064`).
- **`fields`.** The projection, in order. Each entry has a `name`, which is
  the key in a row, and a `path` to a value of the root entity.
- **`path`.** A field name of the root entity, or a dotted path through
  `reference` fields such as `department.name`, with at most 3 hops, as in
  [expressions.md](expressions.md#names-and-references). Each name matches
  ignoring letter case. A path cannot go through or end at a
  `child-collection`. A `null` reference along the path gives `null` and the
  row is kept, because each hop is a left join.
- **`parameters`.** Typed inputs of the filter. A parameter has a `name`, a
  `type` and `required`, which defaults to `false`. The types are the scalar
  field types plus `enum` and `reference`. An `enum` needs `values`, and a
  `reference` needs `target`. An `enum` may also take `valueLabels`. They are
  checked by the same rules as entity fields, see
  [Entity field types and constraints](resource-files.md#entity-field-types-and-constraints).
- **Parameter `label`.** Optional, with the `{ "textKey" }` shape of a field
  label. A table shows it on the parameter's filter input.
- **Missing parameter.** An optional parameter that is not given is `null` in
  the filter. The idiom is `p is null or field == p`, as in the first example.
- **`filter`.** A boolean expression. It sees the root entity's fields and
  the parameters as plain names, ignoring letter case, and paths through
  `reference` fields such as `department.name`, with at most 3 hops. A path
  starts at a field, never at a parameter, because a reference parameter is
  an id and not a row. It can call named
  [rules](resource-files.md#resource-file-shape), and its SQL inlines each
  rule's expression in place of the call. With every rule inlined, the filter
  may have at most 2,000 syntax nodes. It uses the syntax of
  [expressions.md](expressions.md#grammar) and only the
  [SQL subset](expressions.md#sql-subset). Values are always sent as SQL
  parameters and never spliced into the SQL text. A row is kept only when the
  filter is `true`, so a `null` result drops it. Without a `filter`, every
  row passes.
- **`sort`.** The default order, in the syntax of the query `sort`: a name, or
  `-` and a name for descending order. Without it, rows are ordered by the
  tie-break below.
- **`pageSize`.** The default page size, from 1 to 100. It defaults to 20.

### Aggregates

- **`groupBy`.** A list of names from `fields`. An empty list gives one total
  row.
- **`measures`.** Each has a `name` and a `function`: `count`, `sum`, `min` or
  `max`. `avg` and `countDistinct` are _(planned for M15)_, see
  [Added in M15](#added-in-m15).
- **`count`.** It takes no `field` and counts rows.
- **`sum`, `min` and `max`.** They need a `field` that names an entry of
  `fields`. `sum` takes an integer, a decimal or a money field. `min` and
  `max` take an integer, a decimal, a money field, a date or a date-time.
- **Result types.** `count` is an integer. `sum` is written like a decimal,
  except that a `sum` over a money field is money. `min` and `max` keep the
  type of their field.
- **Money.** A `sum`, `min` or `max` over a `money` field whose currency
  comes from a `currencyField` must group by that field, or the data source
  mixes currencies. The group field is a projected field whose path reaches
  the money field's `currencyField` on the same record, through the same
  reference fields. A root `currency` field does not cover `order.amount`,
  whose currency is `order.currency`. Without it the compile fails with
  `AXC0248`. A `count` and a money field with a fixed `currency` need no
  currency group. A grouped row keeps the currency with each amount: the
  currency code is in the group column, and the total is summed in
  PostgreSQL `numeric`, so no digit is lost.
- **`null` values.** `sum`, `min` and `max` skip `null` values. Over only
  `null` values they are `null`, not 0, as in SQL. This tells "no values"
  apart from a real zero. `count` counts every row, whatever its values.
- **`null` groups.** Rows whose group field is `null`, such as items with no
  department, form one group of their own.
- **Row values.** A grouped row holds the group fields in `groupBy` order,
  then the measures. The other `fields` are only inputs of the measures.
- **Names.** Group fields name entries of `fields`, and measure names are
  keys of a grouped row. Both match exactly, so letter case matters, as for
  projected names.
- **Empty `groupBy`.** The data source has one total row, even when no row
  passes the filter. Then `count` is 0 and the other measures are `null`.

### Added in M15

These are _(planned for M15)_. See
[D76](../decisions/d76-file-money-and-enum-label-field-types.md) and
[D80](../decisions/d80-search-more-aggregates-and-query-timeouts.md).

- **`avg`.** Needs a `field` of type integer, decimal or money, and gives a
  decimal rounded to the field's scale, or to 6 digits for an integer. It
  skips `null` values and is `null` over none.
- **`countDistinct`.** Needs a `field` of any type a `groupBy` accepts, and
  counts its distinct values that are not `null`.
- **Measure `filter`.** A measure takes an optional `filter`, a boolean
  expression in the [SQL subset](expressions.md#sql-subset) over the same
  names as the data source filter. The measure counts only the rows where it
  is `true`, as SQL `FILTER (WHERE ...)`. So one data source can count open
  and closed tickets side by side.
- **Date buckets.** A `groupBy` entry may be `{ "field": "<name>", "bucket":
  "day" }`, with `day`, `week`, `month` or `year`, on a date or date-time
  field. The row holds the first date of the bucket. A week starts on
  Monday. Date-time values are bucketed in the application time zone of
  [D75](../decisions/d75-the-application-time-zone.md).
- **Money and `avg`.** `avg` over a `money` field whose currency comes from a
  `currencyField` needs that field among the group fields, as `sum`, `min`
  and `max` do now, see [Aggregates](#aggregates). The compile check keeps
  its list of such functions, and `avg` joins it.
- **Enum labels and files.** Rows hold enum values, and page metadata gives
  their labels. A `file` field cannot be a group field or a measure field.
- **Search.** `"search": true` lets the rows route take `search`, matched
  against the projected fields that are `searchable` or the root's display
  field, as the [record API](record-api.md#paging-and-sorting) matches.
- **Export.** A data source can be exported to CSV or a spreadsheet, see
  [import and export](import-export.md#export).

## Compile checks

A data source is checked when the application is compiled. Any failure is a
compile error and no release is produced. The built checks have their codes,
see the Data sources bullet of the Resolve step in
[configuration.md](configuration.md):

- `entity` names a loaded entity (`AXC0042`).
- `entity` is not a child entity (`AXC0064`). A child entity's rows are read
  only through its owner's child collection.
- Each `path` names a field of the root entity, or goes through `reference`
  fields only, takes at most 3 hops, and does not end at a
  `child-collection`. A path through a field that is not a `reference`, an
  unknown field and more than 3 hops are each `AXC0043` at
  `/fields/{i}/path`, with a message that says which.
- Field names are unique, compared exactly (`AXC0044`).
- `sort` names a projected field whose path does not end at a `reference`
  (`AXC0045`).
- `pageSize` is from 1 to 100. The JSON Schema checks it (`AXC0004`).
- A parameter name differs from every field of the root entity, because the
  filter reads both as plain names. Parameter names are unique, and `page`,
  `pageSize` and `sort` are reserved. All three compare ignoring letter case,
  so `Page` and `SORT` are reserved too. A bad name is `AXC0054` at
  `/parameters/{i}/name`.
- A parameter's type is valid for its properties, as for entity fields, with
  the same codes at `/parameters/{i}/...`: `AXC0013` for a property its type
  does not take, `AXC0014` for an `enum` without `values` or a `reference`
  without `target`, `AXC0012` for an unknown target, `AXC0040` for a child
  entity target and `AXC0030` for a target without a display field.
- A parameter's `valueLabels` follow the entity field rules: a missing or
  unknown value is `AXC0242` and a text key that no locale has is `AXC0028`,
  at `/parameters/{i}/valueLabels/...`.
- A parameter label's text key is in some locale (`AXC0028`), as for field
  labels. See the Labels bullet of the Resolve step in
  [configuration.md](configuration.md).
- The `filter` parses, is a boolean expression over the root entity's
  fields, the parameters, paths through `reference` fields and the named
  rules, and stays inside the SQL subset. Its first problem is reported at `/filter` with its
  [expression diagnostic](expressions.md#diagnostics) code: for example
  `AXC0048` when it is not boolean, `AXC0046` for an unknown name, also after
  a `.`, `AXC0047` for a `.` after a field that is not a `reference` or after
  a parameter, `AXC0058` for a path of more than 3 hops and `AXC0053` for
  anything outside the SQL subset, such as an
  [aggregate](expressions.md#aggregates) over a child collection or a call to
  a rule whose expression uses `lower`.
- An enum parameter compares with an enum field only when every value in the
  parameter's `values` is also in the field's `values`. Otherwise the filter
  is `AXC0047`. See [Types](expressions.md#types).
- The filter is not checked while a parameter of the data source has a
  diagnostic, because that parameter may have no usable type.

- `groupBy` names entries of `fields`, compared exactly. An unknown name is
  `AXC0061` at `/aggregate/groupBy/{i}`.
- Measure names are unique. A measure name also differs from every group
  field, because both are keys of one row. A clash is `AXC0062` at
  `/aggregate/measures/{i}/name`.
- `count` takes no `field`, and `sum`, `min` and `max` need one. Otherwise
  it is `AXC0063` at `/aggregate/measures/{i}`.
- A measure `field` names an entry of `fields`, and its type follows the
  aggregate rules above. Otherwise it is `AXC0063` at
  `/aggregate/measures/{i}/field`. A field whose path is already `AXC0043`
  is not reported again.
- A `sum`, `min` or `max` over a money field with a `currencyField` needs a
  group field that reaches that currency field on the same record. Otherwise
  it is `AXC0248` at `/aggregate/measures/{i}/field`. A `count`, a money field
  with a fixed `currency`, and a money field whose `currencyField` is
  already `AXC0243` are not reported.
- When the data source is grouped, `sort` names a group field whose path
  does not end at a `reference`, or a measure, instead of a projected field
  (`AXC0045`).

## Endpoint

| Method and path | Response |
| --- | --- |
| `GET /api/apps/{app}/data-sources/{dataSource}/rows?page=&pageSize=&sort=` | `200` with one page of rows |

The endpoint is read-only, and only `GET` is routed. `{app}` is the name of an
active release, and `{dataSource}` a data source in that release. Both match
ignoring letter case, as in the record API.

A module data source's `{dataSource}` is written `{module id}/{data source name}`,
for example `data-sources/departments/Departments/rows`. It matches ignoring
letter case, is two path segments, and only that form reaches a module data
source. A data source of the application has one segment. This is how the
[record API routes](record-api.md#routes) name module entities. A module data
source has the same checks as any other.

### Authorization

It follows [Authorization](security.md#authorization). The session, the
grants, the record filters, the fields and the joined targets are built.

- **Session.** A request without a session is a `401`, before the path.
- **Grants.** The user needs a `read` grant on the data source and a `read`
  grant on its root entity, otherwise `403`. Only roles claim values that
  name a declared role count. An entity grant with a `filter` or a `fields`
  list counts, and then limits the rows or the fields as described below.
- **Record filters.** The root entity's read filters are combined with OR and
  added to the data source's own `filter` with AND, in SQL, before any
  grouping. So rows, groups, measures and `totalCount` cover only the
  records the user may read. A root read filter that reads a
  [pending field](migrations.md#recomputing-computed-fields) is a `409` and no
  rows come back. It is checked right after the grants, before the query, so
  a bad `sort` does not hide it. See [Errors](#errors).
- **Fields.** Every projected field must be one the user may read, otherwise
  the whole data source is a `403`. This covers every entry of `fields`,
  also in a grouped data source, and so every field that a measure
  aggregates. For a path through `reference` fields, every field along the
  path must be readable on its own entity, not only the field it ends at. So
  `department.name` needs `department` on the root entity and `name` on the
  target. The path also shows which record is referenced, so the stricter
  rule cannot leak. There are no partial rows, so authors make one data
  source per audience.
- **Joined targets.** The record filters of a target that a path joins do not
  apply. A label in `labels` shows when the user may read the target's display
  field, as for the record API's [labels](record-api.md#record-shape).
- **Limit.** The all-or-nothing rule covers only projected and aggregated
  fields. A data source `filter` or a parameter can still compare a field the
  user may not read. A parameter can then probe that field's value, by
  checking which rows come back. Authors keep such fields out of the filters
  of data sources that users without access to them can read.

## Query parameters

- **`page`, `pageSize` and `sort`.** They follow
  [record-api.md](record-api.md#paging-and-sorting), except that the
  `pageSize` default is the data source's own. `sort` names an entry of
  `fields`, or a group field or measure when the data source is grouped. It
  matches exactly, so letter case matters. A `sort` cannot name a projected
  field whose path ends at a `reference`.
- **Data source parameters.** They are query parameters under their declared
  names. The names match exactly, so a declared name in another letter case
  is an unknown parameter.
- **Empty value.** An empty value means the parameter was not given.
- **Repeated parameter.** A parameter given twice, such as `page=1&page=2`, is
  invalid.
- **Unknown parameters.** They are ignored.
- **Required parameter.** A required parameter that is not given is invalid.
- **Pending fields.** A data source has no single read. A
  projected [pending field](migrations.md#recomputing-computed-fields) shows its
  stored value, which may come from an older expression. A query is refused
  with the `409` in [Errors](#errors) when its `sort`, its default `sort`,
  its `filter` or its `groupBy` field or measure reads a pending field. That
  includes a path through references and a filter that inlines a rule. The
  record API computes the pending field on a single read.
- **Order.** Ties are broken by the root `id` ascending, or by the group
  fields in `groupBy` order, each ascending, when the data source is
  grouped. `NULL` ordering and the count statement work as in the record
  API. Without a `sort`, grouped rows are in group field order.

Query strings have no JSON types, so each parameter type has a plain text
form. A value is only ever sent as a typed SQL parameter, so no value can
change the query:

| Parameter type | Query value |
| --- | --- |
| `text` | The text as it is, with no U+0000 |
| `integer` | An optional `-` and digits, within 64 bits |
| `decimal` | Plain number text with no exponent, such as `1250.50` |
| `boolean` | `true` or `false` |
| `date` | `yyyy-MM-dd` |
| `date-time` | RFC 3339, as in the record API |
| `enum` | One of the parameter's `values` |
| `reference` | A hyphenated UUID |

## Response shape

A response is `{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 42 }`.
`page` and `pageSize` are the values used, and `totalCount` counts every row
that passes the filter. It is `null` when only the count runs past
`Data:QueryTimeout`, and then the items are still there.

Each item is `{ "id", "values", "labels" }`:

```json
{
  "items": [
    {
      "id": "6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7",
      "values": {
        "title": "Standing desks",
        "status": "submitted",
        "total": 1250.50,
        "submittedAt": "2026-10-06T02:00:00.123456Z",
        "departmentName": "Finance"
      },
      "labels": {}
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

- **`id`.** The id of the root record.
- **`values`.** Every projected field under its name, in `fields` order. The
  value forms are those of
  [record-api.md](record-api.md#reading-values). A field that is `NULL`,
  including one behind a `null` reference, is `null`.
- **`labels`.** Each projected field whose path ends at a non-null
  `reference` maps to the display field value of the target. It is `{}`
  otherwise. Like the record API, the labels come from the same SQL statement
  as the rows.
- **Joins.** Each distinct path through `reference` fields is one left join
  on the target's `id`, shared by the projection, the labels, the sort and
  the filter. A page is one SQL statement plus the count statement, whatever
  the page size. The count statement uses the same joins, and each join
  matches at most one row, so it counts root rows.

A grouped row has `id: null`. Its `values` holds the group fields in
`groupBy` order, then the measures. `totalCount` counts groups. A group field
whose path ends at a non-null `reference` has its label in `labels`, as in an
ungrouped row. The count statement wraps the grouped statement, so it counts
groups, and it is 1 for an empty `groupBy`.

```json
{
  "items": [
    {
      "id": null,
      "values": { "departmentName": "Finance", "requests": 4, "totalSum": 5200.00 },
      "labels": {}
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

## Errors

Every error is problem details (`application/problem+json`). The titles are
fixed and never contain text from the request.

A request is checked in this order, and the first failure is the response:
the session, then the path, then the grants, then the query, then the
database. The readable fields are checked with the grants. A root read filter
that reads a pending field is checked right after the grants, before the
query. The pending fields of the `sort`, `filter` and `aggregate` are checked
after the query and before the database.

- **`401`.** A request without a session, or with a session from another
  tenant. It comes before the path, so it is a `401` even for an unknown
  application. The title is fixed.
- **`403`.** The user has no `read` grant on the data source or on its root
  entity, or may not read one of its projected or aggregated fields. The
  title is fixed and names no data source or field. See
  [Authorization](#authorization).
- **`404`.** An unknown application, an application with no active release
  and an unknown data source. The path is resolved before the query is
  checked, so a path that names nothing is a 404 whatever its query.
- **`400` for the query.** A validation problem whose `errors` is keyed
  `page`, `pageSize`, `sort` and the parameter names. It covers a bad value, a
  missing required value and a repeated parameter. Every invalid parameter is
  reported in the same response.
- **`409` for a pending field.** The `sort`, default
  `sort`, `filter`, `groupBy` field or measure reads a field that is being
  recomputed. The title is fixed: "A field is being recomputed." `errors` is
  keyed `sort`, `filter` or `aggregate`, with a message that names the field.
  The request is valid, so this is a state conflict, not a `400`. A sum over
  stale totals would be the same silent failure.
- **`409` for a policy that reads a pending field.** A root
  read filter of the user's grants reads a field that is being recomputed. No
  rows come back. The title is fixed: "Your access to this entity cannot be
  checked while a field is being recomputed." There is no `errors`, and the
  response names no field or policy. The server logs the policy and grant
  index, never a record value. It fails closed, as a failing read filter
  does.
- **`400` for the database.** A database data exception while evaluating the
  filter, such as an integer overflow or a date out of range from `addDays`.
  The title is fixed, and the response holds no SQL. Any other database
  error is a `500`. A run-time failure in a policy read filter is the same
  `400`, and no rows are returned. PostgreSQL cannot fail one branch of an
  `or`, so the whole query fails closed. The server logs the policy and grant
  index of each of the user's read filters on the root entity, never a
  record value.
- **`503` for a query timeout.** The rows query runs past
  `Data:QueryTimeout` and PostgreSQL cancels it. The title is the record API's:
  "The query took too long. Try again or narrow it." The server logs the data
  source name and the duration, never a value, and counts it in
  `axis.queries.timeouts`. A count alone that runs past the limit is no error:
  the answer is `200` with `totalCount: null`. See
  [data settings](host-settings.md#data-settings).
- **`500`.** An unexpected error is caught by the exception handler. The
  response has no exception type, message or stack trace.

**No leaks.** The no-leaks rule of the
[record API](record-api.md#errors) applies. No response body ever contains
SQL, a table, column or constraint name, an exception type or a stack trace.
The `errors` keys repeat the request's parameter names by design.

## Widget binding

The table binding and its filter inputs are built, for plain and grouped
data sources.

- **Widgets.** The `table` widget binds to a data source, and so does the
  `kpi` widget, and so does the `chart` widget. A `form` that
  names a `dataSource` is `AXC0060`. A table names an `entity` or a
  `dataSource`, never both and never neither (`AXC0060`). `entity` stays as
  shorthand for all records of an entity. A `dataSource` that names no
  loaded data source is `AXC0059`.
- **Parameters.** A bound table needs every parameter of its data source to be
  optional, otherwise it is `AXC0060` at `/widgets/{i}/dataSource`, naming the
  parameter, unless the parameter is bound to a required page parameter. It
  shows one filter input per unbound parameter and sends their values from the
  URL. See [Table widget](frontend.md) for the details.
- **Grouped data sources.** A table may bind a data source with an
  `aggregate`. Each row is a group. Its columns are the group fields in
  `groupBy` order, then the measures. A group row has `id: null`, so it has
  no id to open. It has no row actions either: `rowActions` on such a table is
  `AXC0060` at `/widgets/{i}/rowActions`.
- **Opening a row.** Each row carries the id of its root record. A row
  `navigate` action with `record: id` opens a form over the root entity, and the
  `record` rule of the action checks it (`AXC0235`). A table over a data source
  with an `aggregate` has no row actions, because a group is not a record.
- **Page parameters.** A table, a chart or a KPI binds a page parameter to a
  data source parameter with a name map on the widget:
  `"parameters": { "<data source parameter>": "<page parameter>" }`. The two
  types must match. A required data source parameter is allowed only when it
  is bound to a required page parameter. A bound parameter has no filter
  input, and its value comes from the page parameter in the URL. A chart or a
  KPI sends the value with its request for the rows, and reads again when the
  value changes. The data source's read grant and record filters apply as
  they do to a table. See
  [D63](../decisions/d63-navigate-actions-and-page-parameters.md). The compile
  checks:
  - Each entry is checked at `/widgets/{i}/parameters/{key}` and every problem
    is `AXC0060`: the key names no data source parameter, a key repeats
    ignoring letter case, the value names no page parameter, the types differ,
    or the data source parameter is required and the page parameter is not.
    Names match ignoring letter case, and the compiled model keeps the
    declared names.
  - The types match when the type names are equal and a `reference` has the
    same target. For an `enum`, every value of the page parameter must also be
    a value of the data source parameter.
  - A required data source parameter that is not bound is `AXC0060` at
    `/widgets/{i}/dataSource`. A chart or a KPI has no filter input, so every
    parameter that its `parameters` leaves unbound must be optional.
  - A page parameter with the same name as an unbound parameter of its table,
    ignoring letter case, is `AXC0231` at `/parameters/{i}/name`, because both
    live in the same URL query. A chart and a KPI have no filter input, so this
    check applies only to a table.
  - `parameters` on any widget that is not a table, a chart or a KPI over a
    data source is `AXC0060` at `/widgets/{i}/parameters`.
- **Chart binding.** A `chart` names a `dataSource` and
  reads only grouped data. See
  [D64](../decisions/d64-detail-chart-and-kpi-widgets.md). The compile checks:
  - A `dataSource` that names no loaded data source is `AXC0059`.
  - The data source has an `aggregate` with exactly one `groupBy` entry, and
    the chart's `category` equals it, ignoring letter case, otherwise it is
    `AXC0240` at `/widgets/{i}/dataSource` or `/widgets/{i}/category`.
  - Each chart measure names a measure of the data source, ignoring letter
    case, and it is numeric, otherwise it is `AXC0241` at
    `/widgets/{i}/measures/{j}/name`. A `count` and a `sum` are numeric. A
    `min` or `max` is numeric over an integer, a decimal or a money field, not
    over a date or a date-time. A `pie` has exactly one measure, otherwise it is `AXC0241` at
    `/widgets/{i}/measures`.
  - `parameters` binds page parameters with the table's name map, under the
    same type and required rules as above.
  - A data source parameter that `parameters` leaves unbound must be optional
    (`AXC0060` at `/widgets/{i}/dataSource`, naming the parameter), because a
    chart has no filter bar.
  - The `title` and the measure labels join `AXC0028`.
- **KPI binding.** A `kpi` names a `dataSource` and reads
  its one total row. The compile checks:
  - A `dataSource` that names no loaded data source is `AXC0059`.
  - The data source has an `aggregate` with an empty `groupBy`, otherwise it
    is `AXC0237` at `/widgets/{i}/dataSource`.
  - `measure` and `comparison.measure` each name a measure of the data source,
    ignoring letter case, otherwise it is `AXC0238` at that property.
  - `parameters` binds page parameters with the table's name map, under the
    same type and required rules as above.
  - A data source parameter that `parameters` leaves unbound must be optional
    (`AXC0060` at `/widgets/{i}/dataSource`, naming the parameter), because a
    KPI has no filter bar.
  - The `label` and the `comparison` label join `AXC0028`. A `navigate`
    follows the checks of
    [D63](../decisions/d63-navigate-actions-and-page-parameters.md).
- **Schema.** The data source's schema reaches the SPA in page metadata, in
  the widget's `dataSource` object. There is no separate schema endpoint. A
  money column or measure has the type `money`. It also has `currency`, the
  fixed currency code, or `currencyColumn`, the name of the column that holds
  the code. A measure's `currencyColumn` is a group column. A projected or
  group column's is the sibling column that reads the currency field on the
  same record. Both are left out of the JSON when they do not apply.
