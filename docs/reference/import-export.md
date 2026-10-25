# Import and export

Detailed reference for exporting records to CSV and spreadsheet files and
importing records from them. The design follows
[D79](../decisions/d79-import-and-export-under-policies.md). The conventions
of [architecture.md](../architecture.md) apply: sections marked
_(planned for M15)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Everything on this page is _(planned for M15)_.

## Grants

- An `entity` grant takes `export` and `import`, and a `dataSource` grant
  takes `export`. See [roles and policies](roles-and-policies.md).
- `export` reaches only what the user's `read` grants reach: the same record
  filters and the same field lists. A grant with `export` and no `read` is a
  compile error.
- `import` needs `create`, and `update` as well for an import with a `key`.
  Each row is checked against those grants as a record API write would be.

## Export

| Method and path | Response |
| --- | --- |
| `GET /api/apps/{app}/data-sources/{dataSource}/export?format=` | `200` with the file |
| `GET /api/apps/{app}/entities/{entity}/records/export?format=` | `200` with the file |

- **Query.** Each route takes the query of its rows or list route, such as
  parameters, `sort` and `search`, without `page` and `pageSize`. `format` is
  `csv` or `xlsx`.
- **Same checks.** The session, the grant, the root read filters and the field
  check run as for the rows or list route. A missing `export` grant is a
  `403`.
- **Rows.** Every matching row is written, streamed, in the route's order. Past
  `Data:ExportMaxRows` the export stops and the file ends with no partial row.
  The response then carries `Axis-Export-Truncated: true`.
- **Columns.** The first row holds the field names. Grouped data sources write
  their group fields and measures.
- **Values.** ISO dates and instants, decimals and money as text with a dot,
  enum values rather than labels, booleans as `true` and `false`, and a
  reference as its target's display value. A file is its name. A `references`
  field is its display values joined by `; `.
- **Formula cells.** A CSV cell that starts with `=`, `+`, `-`, `@`, a tab or a
  carriage return gets a leading `'`. Spreadsheet cells are written as typed
  values, never as formulas.
- **Timeout.** The query runs under `Data:ExportQueryTimeout`, see
  [data settings](host-settings.md#data-settings).
- **Audit.** Each export writes `records.exported` with the data source or
  entity, the row count and the user. It holds no value.

## Import

| Method and path | Response |
| --- | --- |
| `POST /api/apps/{app}/entities/{entity}/imports` | `202` with the import and a `Location` header |
| `GET /api/apps/{app}/entities/{entity}/imports/{id}` | `200` with the import's state and errors |
| `POST /api/apps/{app}/entities/{entity}/imports/{id}/commit` | `202` |

- **Upload.** A CSV (UTF-8) or `xlsx` file, at most `Data:ImportMaxBytes`. A
  spreadsheet is read from its first sheet. The first row names the fields,
  matching ignoring letter case. An optional `key` names a unique field.
- **Dry run.** The worker parses every row and runs each through the record
  command's checks without storing anything. The import becomes `checked`
  with no errors, or `failed` with every error keyed
  `/rows/{n}/values/{field}`, `n` counting data rows from 1.
- **Commit.** Only a `checked` import commits. The worker writes every row in
  one transaction, or none, and the import becomes `committed` or `failed`.
  An import holds at most `Data:ImportMaxRows` rows, 10,000 by default.
- **Each row.** It goes through the record command as the user who uploaded the
  file. Field grants, record filters, validations, defaults, sequences, the
  creator field, computed fields and audit records all apply. With a `key`,
  a row whose key matches a record updates it, and other rows create records.
- **References.** A reference column takes a record id, or the target's
  display value when that value is unique.
- **Triggers.** Each created or updated record fires its data change
  triggers, as any record command write does. Async triggers go through
  [event delivery](process-events.md#event-delivery) and its retry limit.
- **Not imported.** Child rows, `references` fields and files.
- **Audit.** A committed import writes `records.imported` with the entity and
  the counts of created and updated records.
- **Who sees it.** Only the user who uploaded an import reads it or commits
  it. The uploaded file is deleted when the import is committed, fails or is
  older than 24 hours.

## In the SPA

- A table whose user holds `export` shows an Export button with CSV and Excel.
  It exports the table's data source or entity with the table's current
  parameters, sort and search.
- An entity table whose user holds `import` shows an Import button. It
  uploads the file, waits for the dry run, lists its errors, and commits when
  there are none.
- Studio's policy matrix shows the `export` and `import` actions.
