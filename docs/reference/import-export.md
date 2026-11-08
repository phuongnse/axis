# Import and export

A user exports records to a CSV or spreadsheet file, and imports records from one, under the same
policies as the record API. This page covers the grants, the export routes, the import's upload,
dry run and commit, and the SPA's buttons. Spreadsheets are written and read with
DocumentFormat.OpenXml, which is MIT licensed. The reasons for the design are in
[D79](../decisions/d79-import-and-export-under-policies.md).

## Grants

Export and import are actions of their own in a policy, and each stays within the user's read and
write grants.

- An `entity` grant takes `export` and `import`, and a `dataSource` grant takes `export`. See
  [roles and policies](roles-and-policies.md).
- `export` reaches only what the user's `read` grants reach: the same record filters and the same
  field lists. A grant with `export` and no `read` on the same target in the same policy is
  `AXC0265`. The policy evaluator allows an export only when the user also holds `read`.
- `import` needs `create`, and a grant with `import` and no `create` on the same entity in the same
  policy is `AXC0265`. An import with a `key` needs `update` as well. Each row is checked against
  those grants as a record API write would be.

## Export

An export writes the rows of a data source or an entity list to a file, with the same checks, order
and hidden fields as the route it mirrors.

| Route                                                                   | Response                                                                              |
| ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `GET /api/apps/{app}/data-sources/{dataSource}/export?format=`          | `200` with the file                                                                   |
| `GET /api/apps/{app}/entities/{entity}/records/export?format=`          | `200` with the file                                                                   |
| `GET /api/apps/{app}/data-sources/{module}/{dataSource}/export?format=` | `200` with the file of a module data source, see [endpoint](data-sources.md#endpoint) |
| `GET /api/apps/{app}/entities/{module}/{entity}/records/export?format=` | `200` with the file of a module entity, see [routes](record-api.md#routes)            |

- **Query.** Each route takes the query of its rows or list route, such as parameters, `sort`,
  `filter` and `search`, without `page` and `pageSize`. `format` is `csv` or `xlsx`. A missing or unknown
  `format` is a `400` at `format`, found after the grants.
- **Same checks.** The session, the grant, the root read filters and the field check run as for the
  rows or list route. A missing `export` grant is a `403`. Read filters hide rows and field lists
  hide whole columns, exactly as on that route.
- **Session only.** The routes are for signed-in users, so a service account's bearer token is a
  `401` on them.
- **Rows.** Every matching row is written in the route's order, up to `Data:ExportMaxRows`. The
  rows are read from the database one at a time and written to a temporary file, so the server
  never holds them all in memory. The file is complete before it is sent, so no byte leaves before
  the row count and the audit record are known. Past `Data:ExportMaxRows` the file stops there with
  no partial row, and the response carries `Axis-Export-Truncated: true`. A file is sent with
  `Cache-Control: no-store` and no record id column.
- **Size.** At typical size, an export of 100,000 records finishes in under 2 s as CSV and under
  6 s as `xlsx`, and grows the live managed heap, the memory still in use during the export, by less
  than 64 MB. `ExportPerformanceTests` fails when the best of three exports takes 2 s or more as CSV
  or 6 s or more as `xlsx`, or when the live heap grows by 64 MB or more. It measures the heap on
  exports that are not timed.
- **Columns.** The first row holds the field names. An entity export writes the fields the user may
  read, in declaration order, and leaves out child collections and `references` fields, which have
  no column. A grouped data source writes its group fields, then its measures.
- **Values.** Dates and instants are ISO text. Integers, decimals and money are text with a dot and
  every stored digit. Enums are their values rather than their labels, and booleans are `true` and
  `false`. A reference is its target's display value, or the target's record id when the user may
  not see the label or the target has no display value, which import accepts too. A file is its
  name.
- **CSV.** UTF-8 with a byte order mark, so Excel reads non-ASCII text, CRLF line endings and
  RFC 4180 quoting.
- **Spreadsheet.** One sheet named `Export`. Numbers are number cells that hold the exact digits,
  though Excel shows integers above 2^53 rounded. Booleans are boolean cells. Dates, instants and
  all text are text cells in their ISO form, so a time zone is never lost. No cell is a formula.
- **Formula cells.** A text-like CSV cell, which is text, an enum value, a reference label or a
  file name, that starts with `=`, `+`, `-`, `@`, a tab or a carriage return gets a leading `'`. A
  number cell never does, because a number cannot be a formula, and a prefix on `-5` would break its
  machine form and its re-import.
- **Timeout.** The query runs under `Data:ExportQueryTimeout`, see
  [data settings](host-settings.md#data-settings). A query past it is a `503` and sends no file.
- **Concurrency.** In a cell an export takes a `concurrentImports` lease of the tenant. When none is
  free after 5 seconds it answers `503`, see [work at once](tenant-quotas.md#work-at-once).
- **Audit.** Each export writes `records.exported` after the file is complete and before it is
  sent, in a transaction of its own, and the audit record holds no value. An export that fails
  writes no audit record and sends no file. See
  [audit records](storage.md#audit-records-and-sequence-counters).

## Import

An import creates or updates the records of one entity from a file in three steps: the user uploads
the file, the worker checks every row in a dry run, and the user commits the checked import, which
the worker writes in one transaction.

| Route                                                                 | Response                                                                                          |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `POST /api/apps/{app}/entities/{entity}/imports`                      | `202` with the import and a `Location` header                                                     |
| `GET /api/apps/{app}/entities/{entity}/imports/{id}`                  | `200` with the import's state and errors                                                          |
| `POST /api/apps/{app}/entities/{entity}/imports/{id}/commit`          | `202`                                                                                             |
| `POST /api/apps/{app}/entities/{module}/{entity}/imports`             | `202` with the import of a module entity, see [routes](record-api.md#routes)                      |
| `GET /api/apps/{app}/entities/{module}/{entity}/imports/{id}`         | `200` with the import's state and errors, for a module entity, see [routes](record-api.md#routes) |
| `POST /api/apps/{app}/entities/{module}/{entity}/imports/{id}/commit` | `202` for a module entity, see [routes](record-api.md#routes)                                     |

### Upload

The upload stores the file and its format, and answers before anything inside the file is read.

- **Body.** The file is the raw request body, not a multipart form, with at most
  `Data:ImportMaxBytes` bytes. Its `Content-Type` names its format: `text/csv`, UTF-8, or the `xlsx`
  type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` with no parameter. The
  import stores the format.
- **Key.** An optional `?key=` names a unique field of the entity, and the dry run and the commit
  match each row's key cell with a record, as [rows and references](#rows-and-references) says.
- **Who.** Only a signed-in user uploads, never a service account.
- **Checks.** The checks come in order: session `401`, in a cell the tenant's
  [storage quota](tenant-quotas.md#refusals) `403`, the path `404`, `import` and `create` `403`,
  `update` `403` when there is a `key`, a `key` that is no unique field `400` at `key`, another
  content type `415`, and a file over the limit `413`.
- **Answer.** The answer is `202` with the import, `pending`. The upload does not look inside the
  file, so a body that is no `xlsx` workbook is `202` and then fails its dry run.

### File formats

A CSV file and the first sheet of a workbook are read into the same cells, which the record parser
then checks as it checks a JSON body.

- **CSV.** UTF-8 with the quoting of RFC 4180: a cell in double quotes may hold commas, line breaks
  and doubled quotes. A leading byte order mark is skipped.
- **Header.** The first row names the fields of the entity's table, matching ignoring letter case.
  An unknown name, a field named twice, a child collection, a `references` field and a file field
  are errors.
- **Spreadsheet.** Only the first sheet of an `xlsx` workbook is read, and other sheets are
  ignored. Its first row with a value is the header, and a row with no value is skipped. A data
  row's number is its sheet row minus the header's, so it is the number Excel shows. A cell to the
  right of the header that holds a value is an error of its row, keyed `/rows/{n}`, as is a cell
  that holds an error value such as `#DIV/0!` and a date that does not exist.
- **Spreadsheet cells.** Cells become the text a CSV file holds: a string as it is, a number as
  written, a boolean as `true` or `false`, and a formula cell as the value saved with it. A date
  cell is `yyyy-MM-dd`, and one that holds a time is `yyyy-MM-ddTHH:mm:ssZ`. A workbook stores no
  time zone, so the time is read as UTC. A date-only cell in a date-time field fails the check, as it
  has no time.
- **Unreadable workbook.** A workbook that is not readable, which includes rows that are not in
  ascending order, or that unpacks to more than 1,000 entries or 100 MiB, is a header problem at
  `/columns`.
- **Cells.** An empty cell leaves the field out, so its default applies or `Required.` is
  reported. A cell of an integer, decimal or money field that is written as a JSON number is a
  number, and `true` or `false` in a boolean field is a boolean. Every other cell is text. The
  record parser then checks the row exactly as it checks a JSON body.
- **Not imported.** Child rows, `references` fields and files. A column that names a child
  collection, a `references` field or a file field is an error at `/columns/{n}`.

### Dry run

The dry run checks every row as the record command would, stores nothing, and leaves the import
`checked` or `failed` with every error.

- **Work item.** The worker runs one `records.importCheck` work item per import. It parses every
  row and runs each through the record command's checks as the user who uploaded the file, without
  storing anything: parsing, field grants, defaults, matched references, computed fields and
  validations.
- **Key.** With a `key`, it also reads the record that each row's key cell matches, and checks a
  matching row as a record API update: the `update` grant, its record filters and its field lists,
  then parsing, computed fields and validations. The body of an update leaves out the key column and
  the empty cells. A refused update is keyed `/rows/{n}`, or `/rows/{n}/values/{field}` for a field
  outside the grant. A file with a `key` and no column for it fails at `/columns`.
- **Nothing written.** It writes no record, no audit record and no sequence number. It reads storage
  only to look up the display values of reference cells, as
  [rows and references](#rows-and-references) says, and the records that keys match. It does not
  check that a record id exists or that a unique value is free, so a `checked` import can still fail
  at commit. It does not check a module contract's lifecycle either.
- **Result.** The import becomes `checked` with no errors, or `failed` with every error keyed
  `/rows/{n}/values/{field}`, `n` counting data rows from 1. A failed import's file is deleted at
  once. The dry run ends the link between the import and its work item.
- **Other error keys.** A problem of the header is keyed `/columns/{n}`, `n` counting the columns
  from 1, and a file with no header is keyed `/columns`. A row with the wrong number of cells, or
  broken quoting, is keyed `/rows/{n}`. More than `Data:ImportMaxRows` data rows, 10,000 by
  default, fails the dry run at `/rows`. An import whose entity is not in the active release fails
  at the empty pointer.

### Commit

The commit writes every row of a checked import in one transaction, so an import is written whole or
not at all.

- **Who.** Only the uploader of a `checked` import commits it.
- **Checks.** The checks come in order: session `401`, in a cell the tenant's
  [storage quota](tenant-quotas.md#refusals) `403`, the path `404`, the user's current `import` and
  `create` grants `403`, then the import and its state. Another user's import and an unknown id are
  `404`. Any state other than `checked` is `409`, and so is a second commit while one is queued, and
  so is a `checked` import whose file was deleted, as
  [the import and its file](#the-import-and-its-file) says.
- **Answer.** The answer is `202` with the import, still `checked`, and the route queues one
  `records.importCommit` work item that the import names in its `work_item_id`.
- **Write.** The worker writes every row, or none, in one transaction that holds the application's
  start lock shared, and the import becomes `committed` or `failed`. A row that fails at write time,
  for example on a unique value that is taken or a reference to a record that does not exist, makes
  the whole commit write nothing. A row that the
  [module contract](module-contracts.md#when-a-write-is-refused) refuses fails the same way, at
  `/rows/{n}/values/<state field>` with the contract's message. The import is then `failed` with
  that row's errors, keyed `/rows/{n}/...` as the dry run's errors are.
- **File.** The file is deleted when the commit ends. A commit queued before the file was deleted
  ends `failed` with one error at the empty pointer and writes nothing.
- **Triggers.** Each created or updated record fires its data change triggers in the commit's
  transaction, as any record command write does.
- **Audit.** A committed import writes `records.imported` with the entity and the counts of created
  and updated records. Each updated record also has its own `record.updated` audit record by the
  uploader.
- **Concurrency.** In a cell the dry run and the commit each take a `concurrentImports` lease of the
  tenant. With none free the work item is due again in 1 second and the import keeps its state, see
  [work at once](tenant-quotas.md#work-at-once).
- **Speed.** A CSV import of 10,000 rows into an entity with a reference, a sequence and a
  validation finishes its dry run within 3 s and its commit within 96 s. `ImportPerformanceTests`
  fails past either limit. It also checks that all created rows share one `xmin`, which shows the
  single transaction.

### Rows and references

Each row goes through the record command as its uploader, and a reference cell may name its target
by id or by display value.

- **Each row.** It goes through the record command as the user who uploaded the file, with the
  roles stored at upload. Field grants, record filters, validations, defaults, sequences, the
  creator field, matched references, computed fields and audit records all apply. A grant removed
  after the upload fails the row as refused, and the import `failed`.
- **Key matches.** With a `key`, a row whose key cell matches a record the uploader may read updates
  it, and other rows create records. A row with an empty key cell, with no match, or whose match the
  uploader cannot read, creates a record, so a hidden record is never revealed. A row whose key then
  matches a hidden record fails the commit on the taken unique value.
- **Updates.** The commit updates a matched record at the version it reads in its own transaction.
  The body of the update leaves out the key column and the empty cells, so they never change the
  record.
- **Repeated keys.** Two rows with the same key value and no stored match both pass the dry run as
  creates. In the commit, the first creates the record and the second updates it.
- **References.** A reference column takes a record id, or the target's display value. A cell that
  is a record id is used as it is. Any other cell is looked up by an exact match on the target's
  display field, in both the dry run and the commit, and each distinct value is looked up once per
  run. The lookup runs only when the uploader may read the target's display field, and the target's
  record filters do not narrow it, as they do not hide a label, see [security](security.md).
  Otherwise the cell is checked as a record id. A value that matches no record, or more than one, is
  an error at `/rows/{n}/values/{field}` that does not repeat the value.

### The import and its file

An import belongs to the user who uploaded it, and its file lives at most a little over a day, while
the import's state and errors stay.

- **Reading it.** `GET` answers `id`, `state`, `key`, `errors` and `createdAt`, the errors in row
  order. It reads the import of the signed-in user only, so any other user, an unknown id and an
  import of another entity get `404`. The import is stored in `axis.record_imports`, see
  [storage](storage.md).
- **Who sees it.** Only the user who uploaded an import reads it or commits it.
- **The file.** The uploaded file is deleted when the import is committed, fails or is older than
  24 hours. The worker clears the file of an import uploaded more than 24 hours ago at most every 5
  minutes, so a file can live a little longer. The import stays, and its uploader still reads its
  state and errors. A `pending` import whose file is cleared becomes `failed` with one error at the
  empty pointer.

## In the SPA

A table offers Export and Import buttons to a user who holds those actions.

- A table whose user holds `export` shows an Export button with CSV and Excel. It exports the
  table's data source or entity with the table's current parameters, filter, sort and search. A data source table
  has no filter.
- An entity table whose user holds `import` shows an Import button. It uploads the file, waits for
  the dry run, lists its errors, and commits when there are none.
- Studio's [policy matrix](studio.md#policy-matrix) shows the `export` and `import` actions.
