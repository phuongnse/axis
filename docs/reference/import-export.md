# Import and export

Detailed reference for exporting records to CSV and spreadsheet files and
importing records from them. The design follows
[D79](../decisions/d79-import-and-export-under-policies.md). The conventions
of [architecture.md](../architecture.md) apply: sections marked
_(planned for M15)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Everything on this page is _(planned for M15)_, except the grants, the
policy matrix, the export routes, the upload, dry run and commit of a CSV
or `xlsx` import, and the Import button in the SPA, which are built. A commit only creates records. `key`
matching and the file cleanup of an import are not built.

## Grants

- An `entity` grant takes `export` and `import`, and a `dataSource` grant
  takes `export`. See [roles and policies](roles-and-policies.md).
- `export` reaches only what the user's `read` grants reach: the same record
  filters and the same field lists. A grant with `export` and no `read` on the
  same target in the same policy is `AXC0265`. The policy evaluator allows an
  export only when the user also holds `read`.
- `import` needs `create`, and a grant with `import` and no `create` on the
  same entity in the same policy is `AXC0265`. An import with a `key` needs
  `update` as well. Each row is checked against those grants as a record API
  write would be.

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
- **Size.** At typical size, an export of 100,000 records finishes in under 2 s
  as CSV and under 6 s as `xlsx`, and grows the managed heap by less than 64 MB.
  `ExportPerformanceTests` fails when the best of three exports takes 2 s or more
  as CSV or 6 s or more as `xlsx`, or when the heap grows by 64 MB or more.
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

- **Upload.** _(built)_ The file is the raw request body, not a multipart form,
  with at most `Data:ImportMaxBytes` bytes. Its `Content-Type` names its format:
  `text/csv`, UTF-8, or the `xlsx` type
  `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` with no
  parameter. The import stores the format. An optional `?key=` names a unique
  field of the entity, and nothing uses it until key matching is built, so a
  commit creates every row. Only a signed-in user uploads, never
  a service account. The checks come in order: session `401`, the path `404`,
  `import` and `create` `403`, `update` `403` when there is a `key`, a `key`
  that is no unique field `400` at `key`, another content type `415`, and a
  file over the limit `413`. The answer is `202` with the import, `pending`.
  The upload does not look inside the file, so a body that is no `xlsx`
  workbook is `202` and then fails its dry run.
- **CSV format.** UTF-8 with the quoting of RFC 4180: a cell in double quotes
  may hold commas, line breaks and doubled quotes. A leading byte order mark is
  skipped. The first row names the fields of the entity's table, matching
  ignoring letter case. An unknown name, a field named twice, a child
  collection, a `references` field and a file field are errors.
- **Spreadsheet format.** _(built)_ Only the first sheet of an
  `xlsx` workbook is read, and other sheets are ignored. Its first row with a
  value is the header, and a row with no value is skipped. A data row's number
  is its sheet row minus the header's, so it is the number Excel shows. A cell
  to the right of the header that holds a value is an error of its row, keyed
  `/rows/{n}`, as is a cell that holds an error value such as `#DIV/0!` and a
  date that does not exist. Cells become the text a CSV file holds: a string as
  it is, a number as written, a boolean as `true` or `false`, and a formula cell
  as the value saved with it. A date cell is `yyyy-MM-dd`, and one that holds a
  time is `yyyy-MM-ddTHH:mm:ssZ`. A workbook stores no time zone, so the time is
  read as UTC. A date-only cell in a date-time field fails the check, as it has
  no time. A workbook that is not readable, or that unpacks to more than 1,000
  entries or 100 MiB, is a header problem at `/columns`.
- **Cells.** An empty cell leaves the field out, so its default applies or
  `Required.` is reported. A cell of an integer, decimal or money field that
  is written as a JSON number is a number, and `true` or `false` in a boolean
  field is a boolean. Every other cell is text. The record parser then checks
  the row exactly as it checks a JSON body.
- **Dry run.** _(built)_ The worker runs one `records.importCheck` work
  item per import. It parses every row and runs each through the record
  command's checks as the user who uploaded the file, without storing
  anything: parsing, field grants, defaults, computed fields and validations.
  It writes no record, no audit record and no sequence number. It reads
  storage only to look up the display values of reference cells, see
  References. It does not check that a record id exists or that a unique value
  is free, so a `checked` import can still fail at commit. The import becomes `checked`
  with no errors, or `failed` with every error keyed
  `/rows/{n}/values/{field}`, `n` counting data rows from 1. A failed import's
  file is deleted at once. The dry run ends the link between the import and its
  work item.
- **Other error keys.** A problem of the header is keyed `/columns/{n}`, `n`
  counting the columns from 1, and a file with no header is keyed `/columns`.
  A row with the wrong number of cells, or broken quoting, is keyed
  `/rows/{n}`. More than `Data:ImportMaxRows` data rows fails the dry run at
  `/rows`. An import whose entity the active release no longer has fails at
  the empty pointer.
- **The import.** `GET` answers `id`, `state`, `key`, `errors` and
  `createdAt`, the errors in row order. It reads the import of the signed-in
  user only. Any other user, an unknown id and an import of another entity
  get `404`. The import is stored in `axis.record_imports`, see
  [storage](storage.md).
- **Commit.** _(built for creates)_ Only the uploader of a `checked` import
  commits it. The checks come in order: session `401`, the path `404`, the
  user's current `import` and `create` grants `403`, then the import and its
  state. Another user's import and an unknown id are `404`. Any state other
  than `checked` is `409`, and so is a second commit while one is queued, and so
  is a `checked` import whose file was deleted, see Who sees it. The
  answer is `202` with the import, still `checked`, and the route queues one
  `records.importCommit` work item that the import names in its
  `work_item_id`. The worker writes every row in one transaction that holds the
  application's start lock shared, or none, and the import becomes `committed`
  or `failed`. A row that fails at write time, for example on a unique value
  that is taken or a reference to a record that does not exist, writes nothing.
  The import is `failed` with that row's errors, keyed `/rows/{n}/...` as the
  dry run's errors are. The file is deleted when the commit ends. A commit
  queued before the file was deleted ends `failed` with one error at the empty
  pointer and writes nothing. An import
  holds at most `Data:ImportMaxRows` rows, 10,000 by default, checked by the
  dry run.
- **Each row.** _(built for creates)_ It goes through the record command as the
  user who uploaded the file, with the roles stored at upload. Field grants,
  record filters, validations, defaults, sequences, the creator field,
  computed fields and audit records all apply. Until key matching is built,
  every row creates a record, even with a `key`, so a row whose key is taken
  fails the whole import on that unique value. Once it is built, with a `key`,
  a row whose key matches a record updates it, and other rows create records.
- **References.** _(built)_ A reference column takes a record id, or the
  target's display value. A cell that is a record id is used as it is. Any
  other cell is looked up by an exact match on the target's display field, in
  both the dry run and the commit, and each distinct value is looked up once
  per run. The lookup runs only when the uploader may read the target's display
  field, and the target's record filters do not narrow it, as they do not hide
  a label, see [security](security.md). Otherwise the cell is checked as a
  record id. A value that matches no record, or more than one, is an error at
  `/rows/{n}/values/{field}` that does not repeat the value.
- **Triggers.** _(built for creates)_ Each created or updated record fires its
  data change triggers in the commit's transaction, as any record command
  write does.
- **Not imported.** _(built)_ Child rows, `references` fields and files. A
  column that names a child collection, a `references` field or a file field
  is an error at `/columns/{n}`.
- **Audit.** _(built for creates)_ A committed import writes `records.imported`
  with the entity and the counts of created and updated records. The count of
  updated records is 0 until key matching is built.
- **Who sees it.** Only the user who uploaded an import reads it or commits
  it. The uploaded file is deleted when the import is committed, fails or is
  older than 24 hours. The worker clears the file of an import uploaded more
  than 24 hours ago at most every 5 minutes, so a file can live a little
  longer. The import stays, and its uploader still reads its state and errors.
  A `pending` import whose file is cleared becomes `failed` with one error at
  the empty pointer.
- **Speed.** A CSV import of 10,000 rows into an entity with a reference, a sequence and a validation finishes its dry run within 3 s and its commit within 96 s. `ImportPerformanceTests` fails past either limit. It also checks that all created rows share one `xmin`, which shows the single transaction.

## In the SPA

- A table whose user holds `export` shows an Export button with CSV and Excel.
  It exports the table's data source or entity with the table's current
  parameters, sort and search.
- _(built)_ An entity table whose user holds `import` shows an Import button. It
  uploads the file, waits for the dry run, lists its errors, and commits when
  there are none.
- Studio's policy matrix shows the `export` and `import` actions.
