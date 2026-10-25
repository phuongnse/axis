# File fields

Detailed reference for the `file` field type: its values, the upload and
download routes, where the bytes are stored and how they are cleaned up. The
design follows [D76](../decisions/d76-file-money-and-enum-label-field-types.md).
The conventions of [architecture.md](../architecture.md) apply: sections
marked _(planned for M15)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Everything on this page is _(planned for M15)_.

## Field and value

- **Field.** `{ "name": "receipt", "type": "file", "maxSize": 10485760,
  "accept": ["application/pdf", "image/*"] }`. `maxSize` is in bytes and is
  at most `Files:MaxSize`. `accept` lists media types, and `type/*` matches
  any subtype. Both are optional. See
  [entity field types](resource-files.md#entity-field-types-and-constraints).
- **Several files.** A child collection whose child entity has a `file` field,
  such as `attachments` with rows of `{ file, note }`.
- **Read.** A record holds `{ "id", "name", "mediaType", "size" }` for a file
  field, or `null`.
- **Write.** A create or update body sends the file id as a string, or `null`
  to remove the file.
- **Expressions.** A file allows only `is null` and `is not null`. It cannot be
  a `sort`, a `groupBy` entry or a `search` field, a seed cannot set it, and
  it cannot have `default`, `unique` or `expression`.

## Upload

`POST /api/apps/{app}/entities/{entity}/fields/{field}/files` takes the bytes
as `multipart/form-data` with one part, `file`, and answers `201` with
`{ "id", "name", "mediaType", "size" }`. A field of a child entity is named by
its path, such as `fields/attachments/file`.

- **Grant.** The user needs a `create` or `update` grant on the entity whose
  field list covers the field, or a `403`.
- **Checks.** A file larger than the field's `maxSize`, or than
  `Files:MaxSize`, is a `413`. A media type that `accept` does not allow is a
  `415`. The media type comes from the part's header and is checked against
  the file's first bytes for the common types, so a renamed executable is
  refused.
- **Pending.** The file is stored as `pending`, owned by the user, the entity
  and the field. Nothing points to it yet.
- **Attach.** A create or update that sends the id attaches the file in its
  transaction. The file must be `pending`, uploaded by the same user, for the
  same entity and field. Any other id is a `400` at the field's pointer, with
  the same message whether the id is unknown or belongs to someone else.

## Download

`GET /api/apps/{app}/files/{id}` answers the bytes.

- **Checks.** The server finds the file's owner record and checks the user's
  `read` grant, the record filters and that the user may read the field, as a
  read of the record would. Any failure, an unknown id and a file that is not
  `attached` are all `404`.
- **Headers.** `Content-Type` is the stored media type,
  `Content-Disposition: attachment` with the file name, and
  `X-Content-Type-Options: nosniff`. A browser never shows a file inline, so
  an uploaded HTML or SVG file cannot run in the application's origin.
- **Child rows.** A child row has no id of its own, so a download always goes
  by file id.

## Storage and clean-up

- **Metadata.** The platform table `axis.files` in the tenant database holds
  the id, application, entity, field path, owner record, name, media type,
  size, SHA-256, storage kind, state (`pending`, `attached` or `deleted`), and
  who uploaded it and when. The field's column is a `uuid` that points to it.
- **Bytes.** A `storage` binding names where the bytes go, see
  [deployment bindings](integration-resources.md#deployment-bindings). In
  M15 its only value is `database`: the bytes live in `axis.file_contents` of
  the tenant database. An application without `fileStorage` in its manifest
  uses the tenant database too.
- **Replace and remove.** A write that replaces or removes a file, a removed
  child row and a deleted record mark the old file `deleted` in the write's
  transaction.
- **Sweep.** A worker sweep purges `deleted` files and `pending` files older
  than 24 hours. It is internal, so it needs no scheduled job.
- **Backup.** Files in the tenant database are in the tenant's `pg_dump`, so
  [tenant backup](administration.md) archives hold them.
- **Audit.** An audit record names the file field that changed, never the
  file's name or bytes. A `sensitive` file field also hides its name and id in
  step history and inspection.

## In the SPA

- A form shows a file input: the current file's name as a download link, an
  upload button and, when the field is not required, a remove button. The
  upload starts when the user picks a file, and the save sends the id.
- A table cell and a detail show the file name as a download link.
- Studio's entity designer edits `maxSize` and `accept`.
