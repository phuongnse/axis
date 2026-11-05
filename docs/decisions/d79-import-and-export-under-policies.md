# D79. Import and export under policies

Status: **Agreed**

## Context

Every business application imports a first data set and exports data for reports. An export is a
read that leaves the platform, and an import is a burst of writes, so both must stay under the same
policies, validations and triggers as the record API of [D24](d24-roles-and-policies.md), with no
second path to secure.

## Decision

- **Their own grant actions.** An `entity` grant takes `export` and `import`, and a `dataSource`
  grant takes `export`. `export` needs `read` and reaches only what `read` reaches. `import` needs
  `create`, and `update` too for an import that updates records by a key.
- **An export runs the rows query.** A data source or an entity exports to CSV or `xlsx` with the
  same query, grants, read filters and field checks as its rows route, up to a row limit. Values are
  written in machine form, and a text cell that a spreadsheet could run as a formula is neutralised.
  Spreadsheets are written and read with DocumentFormat.OpenXml, which is MIT licensed.
- **An import goes through the record command.** The worker runs it, never the request thread. Each
  row is written as the user who uploaded the file, so field grants, filters, validations, defaults,
  sequences and data change triggers all apply.
- **A dry run first, then all or nothing.** The dry run reports every error by row and column and
  writes nothing. The commit writes every row in one transaction, or none, up to a row limit.
- **Both are audited,** with counts and never values.

The detail is in [import and export](../reference/import-export.md) and
[roles and policies](../reference/roles-and-policies.md).

## Consequences

- An auditor can read records without being able to download them all.
- Policies, validations and triggers have one home, whichever way the data comes in or goes out.
- A failed import leaves nothing half done to clean up.
- An import is bounded by what one transaction can hold, so a larger data set is imported in parts.
- The usual CSV injection is closed, at the cost of a leading `'` on text that starts like a
  formula.

## Alternatives

- **Export as part of `read`.** Anyone who may read a record could then take every record out of the
  platform.
- **Write rows one by one and keep the good ones.** The user would be left to find and repair a half
  import.
