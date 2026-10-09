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

The rest is marked *(planned for M2)*: a table bound to a grouped data
source. Dn refers to
[decisions.md](../decisions.md). The design follows
[D18](../decisions.md#d18-data-sources--agreed). The reason to query instead
of denormalize is in
[knowledge](../domain/knowledge.md#authoring-and-packaging).

A data source is a read-only, named query over one root entity. It projects
fields, filters rows with typed parameters, sorts, pages and can group. A
table widget shows its rows. Filters use the expression language of
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
  `reference` needs `target`. They are checked by the same rules as entity
  fields, see
  [Entity field types and constraints](configuration.md#entity-field-types-and-constraints).
- **Parameter `label`.** Optional, with the `{ "textKey" }` shape of a field
  label. A table shows it on the parameter's filter input.
- **Missing parameter.** An optional parameter that is not given is `null` in
  the filter. The idiom is `p is null or field == p`, as in the first example.
- **`filter`.** A boolean expression. It sees the root entity's fields and
  the parameters as plain names, ignoring letter case, and paths through
  `reference` fields such as `department.name`, with at most 3 hops. A path
  starts at a field, never at a parameter, because a reference parameter is
  an id and not a row. It cannot call rules.
  It uses the syntax of
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
  `max`.
- **`count`.** It takes no `field` and counts rows.
- **`sum`, `min` and `max`.** They need a `field` that names an entry of
  `fields`. `sum` takes an integer or a decimal. `min` and `max` take an
  integer, a decimal, a date or a date-time.
- **Result types.** `count` is an integer. `sum` is written like a decimal.
  `min` and `max` keep the type of their field.
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
- A parameter label's text key is in some locale (`AXC0028`), as for field
  labels. See the Labels bullet of the Resolve step in
  [configuration.md](configuration.md).
- The `filter` parses, is a boolean expression over the root entity's
  fields, the parameters and paths through `reference` fields, and stays
  inside the SQL subset. Its first problem is reported at `/filter` with its
  [expression diagnostic](expressions.md#diagnostics) code: for example
  `AXC0048` when it is not boolean, `AXC0046` for an unknown name, also after
  a `.`, `AXC0047` for a `.` after a field that is not a `reference` or after
  a parameter, `AXC0058` for a path of more than 3 hops and `AXC0053` for
  anything outside the SQL subset, such as an
  [aggregate](expressions.md#aggregates) over a child collection.
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

The endpoint has no authorization yet. Policy record filters are added to
every data source query from M4 (see
[Authentication and authorization](../architecture.md#authentication-and-authorization)).

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
that passes the filter.

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
the path, then the query, then the database.

- **`404`.** An unknown application, an application with no active release
  and an unknown data source. The path is resolved before the query is
  checked, so a path that names nothing is a 404 whatever its query.
- **`400` for the query.** A validation problem whose `errors` is keyed
  `page`, `pageSize`, `sort` and the parameter names. It covers a bad value, a
  missing required value and a repeated parameter. Every invalid parameter is
  reported in the same response.
- **`400` for the database.** A database data exception while evaluating the
  filter, such as an integer overflow or a date out of range from `addDays`.
  The title is fixed, and the response holds no SQL. Any other database
  error is a `500`.
- **`500`.** An unexpected error is caught by the exception handler. The
  response has no exception type, message or stack trace.

**No leaks.** The no-leaks rule of the
[record API](record-api.md#errors) applies. No response body ever contains
SQL, a table, column or constraint name, an exception type or a stack trace.
The `errors` keys repeat the request's parameter names by design.

## Widget binding

The table binding and its filter inputs are built. Grouped data sources are
*(planned for M2)*.

- **Widgets.** Only the `table` widget binds to a data source. A `form` that
  names a `dataSource` is `AXC0060`. A widget names an `entity` or a
  `dataSource`, never both and never neither (`AXC0060`). `entity` stays as
  shorthand for all records of an entity. A `dataSource` that names no
  loaded data source is `AXC0059`.
- **Parameters.** A bound table needs every parameter of its data source to be
  optional, otherwise it is `AXC0060`. It shows one filter input per
  parameter and sends their values from the URL. See
  [Table widget](frontend.md) for the details.
- **Grouped data sources.** A table cannot bind a data source with an
  `aggregate` yet (`AXC0060`). The table builds its columns from `fields`
  and opens records by `id`, and a group row has neither *(planned for
  M2)*.
- **Form page.** A `formPage` must be a form over the root entity
  (`AXC0022`), because each row carries the id of its root record. It is not
  allowed with an `aggregate`, because a group is not a record *(planned for
  M2)*.
- **Page parameters.** Values that come from navigation wait for the
  navigation bullet of
  [D15](../decisions.md#d15-presentation-model--agreed), which is still
  **Proposed**.
- **Schema.** The data source's schema reaches the SPA in page metadata, in
  the widget's `dataSource` object. There is no separate schema endpoint.
