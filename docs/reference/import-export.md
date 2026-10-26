# Import and export

Detailed reference for exporting records to CSV and spreadsheet files and
importing records from them. The design follows
[D79](../decisions/d79-import-and-export-under-policies.md). The conventions
of [architecture.md](../architecture.md) apply: sections marked
_(planned for M15)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Everything on this page is _(planned for M15)_, except the grants, the
policy matrix and the export routes, which are built.

## Grants

- An `entity` grant takes `export` and `import`, and a `dataSource` grant
  takes `export`. See [roles and policies](roles-and-policies.md).
- `export` reaches only what the user's `read` grants reach: the same record
  filters and the same field lists. A grant with `export` and no `read` on the
  same target in the same policy is `AXC0265`. The policy evaluator allows an
  export only when the user also holds `read`.
- `import` needs `create`, and a grant with `import` and no `create` on the
  same entity in the same policy is `AXC0265`. An import with a `key` needs
  `update` as well, and each row is checked against those grants as a record
  API write would be _(planned for M15)_.

## Export

| Method and path | Response |
| --- | --- |
| `GET /api/apps/{app}/data-sources/{dataSource}/export?format=` | `200` with the file |
| `GET /api/apps/{app}/entities/{entity}/records/export?format=` | `200` with the file |

- **Query.** Each route takes the query of its rows or list route, such as
  parameters, `sort` and `search`, without `page` and `pageSize`. `format` is
  `csv` or `xlsx`. A missing or unknown `format` is a `400` at `format`, found
  after the grants.
- **Same checks.** The session, the grant, the root read filters and the field
  check run as for the rows or list route. A missing `export` grant is a
  `403`. Read filters hide rows and field lists hide whole columns, exactly as
  on that route.
- **Session only.** The routes are for signed-in users. A service account's
  bearer token is a `401` on them.
- **Rows.** Every matching row is written in the route's order, up to
  `Data:ExportMaxRows`. The rows are read from the database one at a time and
  written to a temporary file, so the server never holds them all in memory.
  The file is complete before it is sent, so no byte leaves before the row
  count and the audit record are known. Past `Data:ExportMaxRows` the file
  stops there with no partial row, and the response carries
  `Axis-Export-Truncated: true`. A file is sent with `Cache-Control: no-store`
  and no record id column.
- **Columns.** The first row holds the field names. An entity export writes
  the fields the user may read, in declaration order, and leaves out child
  collections and `references` fields. A `references` field has no storage
  yet, so it is left out until it has, and then the export adds it. Grouped
  data sources write their group fields, then their measures.
- **Values.** ISO dates and instants, integers, decimals and money as text
  with a dot and every stored digit, enum values rather than labels, booleans
  as `true` and `false`, and a reference as its target's display value. A
  reference whose label the user may not see, or whose target has no display
  value, is the target's record id, which import accepts too. A file is its
  name. A `references` field's value is its display values joined by `; `.
- **CSV.** UTF-8 with a byte order mark, so Excel reads non-ASCII text, CRLF
  line endings and RFC 4180 quoting.
- **Spreadsheet.** One sheet named `Export`. Numbers are number cells that hold
  the exact digits, though Excel shows integers above 2^53 rounded. Booleans
  are boolean cells. Dates, instants and all text are text cells in their ISO
  form, so a time zone is never lost. No cell is a formula.
- **Formula cells.** A text-like CSV cell, which is text, an enum value, a
  reference label or a file name, that starts with `=`, `+`, `-`, `@`, a tab
  or a carriage return gets a leading `'`. A number cell never does: a number
  cannot be a formula, and a prefix on `-5` would break its machine form and
  its re-import.
- **Timeout.** The query runs under `Data:ExportQueryTimeout`, see
  [data settings](host-settings.md#data-settings). A query past it is a `503`
  and sends no file.
- **Audit.** Each export writes `records.exported` after the file is complete
  and before it is sent, in a transaction of its own. An export that fails
  writes no audit record and sends no file. It holds no value. See
  [audit records](storage.md#audit-records-and-sequence-counters).

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
