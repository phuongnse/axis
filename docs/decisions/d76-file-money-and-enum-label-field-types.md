# D76. File, money and enum label field types

Status: **Agreed**

M15 adds two field types, `file` and `money`, and labels for enum values. The
owner agreed these points on 2026-10-25. It is built in M15. See
[file fields](../reference/file-fields.md) and
[entity field types](../reference/resource-files.md#entity-field-types-and-constraints).
This decision adds a third binding kind to
[D25](d25-connectors-deployment-bindings-and-secrets.md), and says what the
archives of [D45](d45-tenant-backup-and-restore.md) hold.

- **File field:**
  - `"type": "file"` holds one file. It takes `maxSize` in bytes and
    `accept`, a list of media types such as `application/pdf` or `image/*`.
    `maxSize` is at most `Files:MaxSize`, a host setting that defaults to
    25 MB.
  - Several files are a child collection whose child entity has a `file`
    field, as [D17](d17-entity-logic-and-child-collections.md) allows. There
    is no list type.
  - The column is a `uuid` that points to the platform table `axis.files`. It
    holds the file's name, media type, size, SHA-256, owner record, field path
    and state: `pending`, `attached` or `deleted`.
  - A file is uploaded first and attached by a record write. The upload needs
    a `create` or `update` grant on the entity that covers the field. A write
    attaches only a `pending` file that the same user uploaded for the same
    entity and field. So nobody can attach another user's file by its id.
  - A download is by file id. It resolves the owner record and checks the
    read grant, the record filter and the field's readability, as a read of
    that record would. The answer always carries
    `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`.
  - A read returns `{ id, name, mediaType, size }`. A write sends the id.
  - In expressions a file allows only `is null` and `is not null`. It cannot
    be sorted, grouped, searched or set by a seed.
  - A replaced or removed file becomes `deleted` in the write's transaction.
    A worker sweep purges deleted files, and pending files that nobody
    attached within 24 hours. The sweep is internal and needs no scheduled
    job.
  - `sensitive` on a file field hides its name and id in step history and
    inspection, as for other sensitive values.
- **Storage binding:**
  - A third binding kind, `storage`, joins `url` and `secret`. The
    application manifest names it as `"fileStorage": { "binding": "<name>" }`.
  - In M15 its one value is `database`: bytes are stored in a table of the
    tenant database. An application without `fileStorage` stores files there
    too. They share the tenant's isolation, transactions and backups, and need
    nothing more under several replicas.
  - S3-compatible storage and malware scanning come later. The binding exists
    now so that moving to them changes a tenant's binding, not the release.
- **Backups:** files stored in the tenant database are part of its `pg_dump`,
  so a tenant archive holds them and a restore brings them back. A later
  storage outside the database must say how its files are backed up.
- **Money:**
  - `"type": "money"` is stored as `numeric(p,s)`. Its expression type is
    `decimal`, so the expression language gets no new type.
  - It names its currency either as `currency`, a fixed ISO 4217 code, or as
    `currencyField`, an `enum` or `text` field of the same entity that holds
    the code. Exactly one of the two is needed.
  - `precision` defaults to 18. `scale` defaults to the minor units of a fixed
    currency, and to 2 with `currencyField`.
  - There is no conversion between currencies. A data source `sum` over a
    money field with `currencyField` must group by that field, or it would add
    amounts in different currencies. A new diagnostic says so.
  - The SPA shows the amount with its currency in the UI locale and sends back
    the plain number.
- **Enum labels:**
  - An enum field takes `valueLabels`, a map from each value to a
    `{ "textKey" }` label. `values` stays a list of strings, so stored values,
    schema planning and the recorded enum values do not change.
  - When `valueLabels` is present it covers every value, and its keys join the
    text key check (`AXC0028`).
  - Page metadata carries the resolved labels. Tables, details, form choices,
    filter inputs, chart categories and KPIs show the label. The record API,
    data sources, exports and expressions keep the raw value.
- **Studio:** the entity designer's settings column gains `maxSize`, `accept`,
  `currency`, `currencyField` and a label picker per enum value.

_Why:_ attachments are the most common gap in the samples, and money and enum
labels are what users read on every page. Uploading first and attaching by id
keeps record writes JSON, and binding the upload to its user, entity and field
stops one user from attaching another's file. Downloads that re-check the
owner record keep files under the same policies as the record. The tenant
database is the only storage that keeps one transaction, one isolation and
one backup for records and their files, so it is the M15 default. The
`storage` binding keeps a later move to object storage out of the release.
Money as a typed decimal with a currency gives correct display without a new
expression type, and refusing a sum across currencies avoids a silently wrong
total. Labels kept apart from values let a value's wording change without a
migration.
