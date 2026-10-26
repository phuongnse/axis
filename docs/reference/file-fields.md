# File fields

Detailed reference for the `file` field type: its values, the upload and
download routes, where the bytes are stored and how they are cleaned up. The
design follows [D76](../decisions/d76-file-money-and-enum-label-field-types.md).
The conventions of [architecture.md](../architecture.md) apply: sections
marked _(planned for M15)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Upload up to the `pending` state, the storage of a file's details and bytes,
attach by a record write, the file value in a read, replace and remove, and
audit are _(built)_. Download, the sweep and the SPA are _(planned for M15)_. Inbound
endpoints, task completion and process steps attach nothing for now, so a file
id they send that the record does not already hold fails.

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

This is _(built)_.

`POST /api/apps/{app}/entities/{entity}/fields/{field}/files` takes the bytes
as `multipart/form-data` with one part, `file`, and answers `201` with
`{ "id", "name", "mediaType", "size" }`. A field of a child entity is named by
its collection and field, such as
`/api/apps/{app}/entities/{entity}/fields/attachments/file/files`. An entity of
a module has no upload route yet. A service account may upload, as it may
write records.

- **Order of checks.** The route checks in the order of the record routes:
  sign-in (`401`), then the path (`404`), then the grant (`403`), then the
  request content type (`415`), then the body. A user without a grant learns
  nothing from the body checks. An unknown entity or field, and a field that is
  not a `file` field, are `404`.
- **Grant.** The user needs a `create` or `update` grant on the entity whose
  field list covers the field, or a `403`. For a child field the list must
  cover the owner's child collection field. Record filters are not read,
  because an upload has no record yet. The attach step runs in a record write,
  and that write applies the filters.
- **Body.** The request must be `multipart/form-data` with a boundary, or it
  is a `415`. A missing `file` part, an extra part, a part without a file name
  and an empty file are `400` at `file`.
- **Name.** The stored name is the last path segment of the part's file name,
  without control characters, at most 255 characters.
- **Media type.** The media type comes from the part's `Content-Type`, without
  its parameters, such as `charset`. A part without one is stored as
  `application/octet-stream`.
- **Checks.** A file larger than the field's `maxSize`, or than
  `Files:MaxSize`, is a `413` as soon as the count passes the limit. A media
  type that `accept` does not allow is a `415`. A file that starts like a
  Windows, Linux or macOS executable is a `415` whatever type it declares. A
  file declared as PDF, PNG, JPEG, GIF, WebP or ZIP must start as that type
  does, or it is a `415`. Other types are checked only for an executable start.
- **Pending.** The file is stored as `pending`, owned by the user, the entity
  and the field path (`receipt`, or `attachments/file` for a child field).
  Nothing points to it yet. The response has no `Location` header, because the
  download route does not exist yet.
- **Memory.** The server holds one upload in memory, up to the limit, before
  it writes the file to the database.
- **Attach** _(built)_. A create or update that sends the id attaches the
  file in its transaction: the state becomes `attached` and the owner record
  becomes the root record, also for a field of a child row. The file must be
  `pending`, uploaded by the same user, for the same entity and field path.
  - **Kept ids.** An id the record already holds at the same path stays as it
    is. This covers a resent unchanged field and a kept child row.
  - **Refusal.** Any other id is a `400` at the field's pointer, such as
    `/values/receipt` or `/values/attachments/0/file`, with one message, "No
    uploaded file for this field has this id." The message is the same whether
    the id is unknown, belongs to another user, is attached elsewhere or was
    uploaded for another field. A malformed id is "Must be a UUID."
  - **Repeated id.** An id sent twice in one body is refused at every pointer
    after the first.
  - **Other writers.** Inbound endpoints, task completion and process steps
    pass no uploader, so they attach nothing and refuse any id the record does
    not already hold.
  - **Replaced and removed files.** A file that a write replaced or cleared
    is `deleted`, see [storage and clean-up](#storage-and-clean-up). Its id is
    refused like any other unknown id, so it cannot be attached again.

## Download

This is _(planned for M15)_.

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

- **Metadata** _(built)_. The platform table `axis.files` in the tenant
  database holds the id, application, entity, field path, owner record, name, media type,
  size, SHA-256, storage kind, state (`pending`, `attached` or `deleted`), and
  who uploaded it and when. The field's column is a `uuid` that points to it.
- **Bytes** _(built)_. The bytes live in `axis.file_contents` of the tenant
  database, under the id of the file's row. A `storage` binding that names
  where the bytes go is _(planned for M15)_, see
  [deployment bindings](integration-resources.md#deployment-bindings). In M15
  its only value is `database`. An application without `fileStorage` in its
  manifest uses the tenant database too.
- **Replace and remove** _(built)_. A write that replaces or removes a file,
  an update that removes a child row and a record delete mark each old file
  `deleted` in the write's transaction. An update marks the files that the
  record holds at a written file field, or at the file field of a written
  collection, and that the write did not send. A record delete marks every
  file of the record, also a file of a child row, in the same statement as the
  delete. A refused write leaves the files as they were.
- **Sweep** _(planned for M15)_. A worker sweep purges `deleted` files and
  `pending` files older than 24 hours. Until it is built, failed and abandoned
  uploads stay in the tenant database. It is internal, so it needs no scheduled job.
- **Backup.** Files in the tenant database are in the tenant's `pg_dump`, so
  [tenant backup](administration.md) archives hold them.
- **Audit** _(built)_. An audit record names the file field that changed,
  never the file's name or bytes. A `sensitive` file field also hides its name
  and id in step history and inspection. Step history stores no field values,
  and a call input can never map a file, because only `is null` and
  `is not null` accept one, so a file's name and id never reach either.

## In the SPA

This is _(planned for M15)_.

- A form shows a file input: the current file's name as a download link, an
  upload button and, when the field is not required, a remove button. The
  upload starts when the user picks a file, and the save sends the id.
- A table cell and a detail show the file name as a download link.
- Studio's entity designer edits `maxSize` and `accept`.
