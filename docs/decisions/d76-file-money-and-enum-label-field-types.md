# D76. File, money and enum label field types

Status: **Agreed**

## Context

Attachments are the most common gap in the samples, and amounts and enum labels are what users read
on every page. A file must stay under the same policies as its record, while record writes stay
JSON.

## Decision

- **A `file` field holds one file.** Several files are a child collection whose child entity has a
  `file` field, so there is no list type.
- **Upload first, attach by a record write.** An upload stores a pending file that belongs to the
  user, the entity and the field it was uploaded for, and needs a `create` or `update` grant there. A
  record write attaches it by its id, and only that user's pending file for that field.
- **A download goes through the owner record.** It checks the read grant, the record filter and the
  field as a read of that record would, and the file always comes as an attachment with `nosniff`.
- **Bytes go where a `storage` binding says.** `storage` is a third kind of
  [deployment binding](d25-connectors-deployment-bindings-and-secrets.md), named by the application
  manifest's `fileStorage`. Its value is `database`, the default, which keeps the bytes in the tenant
  database.
- **No malware scanning.** Axis refuses executables and SVG and never serves a file inline. An
  operator that needs scanning puts a scanning proxy in front of the upload route.
- **`money` is a decimal with a currency.** The currency is a fixed ISO 4217 code or the value of a
  field of the same record. Its expression type is `decimal`, and there is no conversion between
  currencies, so a total over amounts whose currency comes from a field must group by that field.
- **Enum values get labels apart from the values.** `valueLabels` maps each value to a text key.
  Screens show the label, while the record API, data sources, exports and expressions keep the value.

The detail is in [file fields](../reference/file-fields.md),
[entity field types](../reference/resource-files.md#entity-field-types-and-constraints),
[deployment bindings](../reference/integration-resources.md#deployment-bindings) and
[data source aggregates](../reference/data-sources.md#aggregates).

## Consequences

- Record writes stay JSON, and nobody can attach another user's file by guessing its id.
- Files in the tenant database share its isolation, transactions and backups, and need nothing more
  under several replicas. Moving bytes elsewhere changes a tenant's binding, not the release, and a
  storage outside the database has to say how its files are backed up.
- An installation that must scan uploads runs and updates the scanner itself.
- A total across currencies is refused rather than silently wrong.
- An enum value's wording changes without a migration.

## Alternatives

- **Files sent inside the record write.** Record writes would stop being JSON.
- **Scanning in Axis.** Signatures need constant updates that an on-premises installation cannot
  promise.
- **A money type in the expression language.** Correct display needs only the field's currency, so a
  new expression type would add work without a gain.
