# D79. Import and export under policies

Status: **Agreed**

M15 lets users export records to CSV and spreadsheet files, and import records
from them, under the same policies as the record API. The owner agreed these
points on 2026-10-25. All of it is _(planned for M15)_. See
[import and export](../reference/import-export.md). It adds two grant actions
to the entity grants of [D24](d24-roles-and-policies.md), and one to data
source grants.

- **Grant actions:**
  - An `entity` grant takes `export` and `import`. A `dataSource` grant takes
    `export`.
  - `export` needs `read` as well, and exports only what `read` reaches.
  - `import` needs `create` as well, and `update` too for an import that
    updates existing records.
- **Export:**
  - A data source and an entity each have an export route that takes the
    query of their rows route and `format=csv` or `format=xlsx`.
  - It runs the same query, grants, read filters and field checks as the rows
    route, and writes every matching row, up to `Data:ExportMaxRows`.
  - Values are written in machine form: ISO dates, decimals as text, enum
    values rather than labels, and the display value of a reference.
  - A text-like CSV cell that starts with `=`, `+`, `-`, `@`, a tab or a
    carriage return gets a leading `'`, so a spreadsheet never runs it as a
    formula. A number cell never does, because a number cannot be a formula
    and a prefix would break its machine form.
  - Spreadsheets are written with DocumentFormat.OpenXml, which is MIT
    licensed.
  - Each export writes an audit record `records.exported` with the source, the
    row count and the user, and no values.
- **Import:**
  - A user uploads a CSV or spreadsheet file for one entity. The first row
    names the fields. The worker runs the import, never the request thread.
  - A dry run comes first. It reports every error by row and column, keyed
    `/rows/{n}/values/{field}`, and writes nothing.
  - A commit writes every row in one transaction, or none. An import holds at
    most 10,000 rows, `Data:ImportMaxRows`.
  - Each row goes through the record command as the user who uploaded it. So
    field grants, filters, validations, defaults, sequences, the creator
    field and audit records all apply.
  - An import that names a unique field as its key updates the record with
    that key and creates the others.
  - A reference column takes a record id or the target's unique display value.
  - Imported records fire data change triggers, as any write through the
    record command does. Async triggers go through event delivery and its
    retry limit.
  - The import writes an audit record `records.imported` with the counts.
  - M15 imports no child rows, no `references` and no files.
- **SPA:** a table whose user holds `export` shows an Export button with the
  two formats. An entity table whose user holds `import` shows an Import
  button that runs the dry run, shows its errors and then commits.
- **Studio:** the policy matrix shows the new actions.

_Why:_ every business application imports a first data set and exports for
reports, and an export is a read that leaves the platform, so it gets its own
grant and an audit record. Separate actions let an auditor read without
downloading everything. Going through the data source query and the record
command keeps one place for policies, validations and triggers, with no
second path to secure. A dry run and an all-or-nothing commit leave no half
import to clean up. Neutralising formula cells closes the usual CSV injection.
