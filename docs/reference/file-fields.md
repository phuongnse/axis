# File fields

Detailed reference for the `file` field type: its values, the upload and
download routes, where the bytes are stored and how they are cleaned up. The
design follows [D76](../decisions/d76-file-money-and-enum-label-field-types.md).
The conventions of [architecture.md](../architecture.md) apply, and Dn refers to
[decisions.md](../decisions.md).

Upload up to the `pending` state, the storage of a file's details and bytes,
attach by a record write, the file value in a read, replace and remove, and
audit, download, the sweep, backup and the SPA are _(built)_. Inbound
endpoints, task completion and process steps attach nothing for now, so a file
id they send that the record does not already hold fails.

## Field and value

- **Field.** `{ "name": "receipt", "type": "file", "maxSize": 10485760,
  "accept": ["application/pdf", "image/*"] }`. `maxSize` is in bytes and is
  at most `Files:MaxSize`. `accept` lists media types, and `type/*` matches
  any subtype. Both are optional. See
  [entity field types](resource-files.md#entity-field-types-and-constraints).
- **Extensions.** The extension of the file name is never checked. The stored
  name keeps whatever extension it was sent with. The declared media type and
  the file's first bytes decide what is accepted, see
  [checks](#upload).
- **Unpacking.** An upload is stored as sent and never unpacked, so `maxSize`
  and `Files:MaxSize` also limit its unpacked size.
- **Malicious files.** An upload is refused with a `415`, and nothing is
  stored, when it starts like an executable, when it declares `image/svg+xml`,
  or when its content does not start as its declared PDF, PNG, JPEG, GIF, WebP
  or ZIP type. Every other file is only ever downloaded as an attachment with
  `nosniff`, see [headers](#download-built). Axis does not scan uploads for
  malware. An operator that needs scanning puts a scanning proxy in front of
  the upload route, see
  [D76](../decisions/d76-file-money-and-enum-label-field-types.md).
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
  type that `accept` does not allow is a `415`. A declared `image/svg+xml` is a
  `415` whatever `accept` allows, because an SVG can carry scripts. A file that starts like a
  Windows, Linux or macOS executable is a `415` whatever type it declares. A
  file declared as PDF, PNG, JPEG, GIF, WebP or ZIP must start as that type
  does, or it is a `415`. Other types are checked only for an executable start.
- **Pending.** The file is stored as `pending`, owned by the user, the entity
  and the field path (`receipt`, or `attachments/file` for a child field).
  Nothing points to it yet. The response has no `Location` header, because a
  pending file cannot be downloaded.
- **Memory.** The server writes the upload to a temporary file in the system
  temp folder as it arrives, deletes it when the request ends, and streams it
  into the database. Memory does not grow with the size of the file.
  `FilePerformanceTests` fails when the best of three 25 MB uploads takes 5 s
  or more, or allocates as many bytes as the file.
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

## Download _(built)_

`GET /api/apps/{app}/files/{id}` answers the bytes. A session user and a
service account may download, as they may read records.

- **Checks.** A request without a session or token is a `401`, as on every
  record route. After that the server finds the file's owner record and checks
  the user's `read` grant, the record filters and that the user may read the
  field, as a read of the record would. Any failure, an unknown or malformed
  id, a file of another application and a file that is not `attached` are all
  the same `404`, so the answer never tells whether a file exists.
- **Pending filter.** A read filter that uses a computed field that is being
  recomputed is a `404` too, not the `409` that a record read gives, because a
  `409` would show that the file exists.
- **Headers.** `Content-Type` is the stored media type,
  `Content-Disposition: attachment` with the file name, and
  `X-Content-Type-Options: nosniff`. A browser never shows a file inline, so
  an uploaded HTML file, or an SVG sent under another type, cannot run in the application's origin.
- **Child rows.** A child row has no id of its own, so a download always goes
  by file id. It goes through the owner record, and the user's `read` grant
  must cover the owner's child collection field, such as `attachments`.
- **Memory.** The checks run under the interactive query timeout. Then the
  bytes stream from the database to the client with no statement limit, and
  the answer carries a `Content-Length`. A download holds one database
  connection until the client has received every byte. `FilePerformanceTests`
  fails when the best of three 25 MB downloads takes 5 s or more, or allocates
  as many bytes as the file.

## Storage and clean-up

- **Metadata** _(built)_. The platform table `axis.files` in the tenant
  database holds the id, application, entity, field path, owner record, name, media type,
  size, SHA-256, storage kind, state (`pending`, `attached` or `deleted`), and
  who uploaded it and when. The field's column is a `uuid` that points to it.
- **Bytes** _(built)_. The bytes live in `axis.file_contents` of the tenant
  database, under the id of the file's row. A `storage` binding that names
  where the bytes go is _(built)_, see
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
- **Sweep** _(built)_. A worker sweep purges `deleted` files and `pending`
  files uploaded more than 24 hours ago, with their bytes. The worker's clock
  sets the 24 hours. The sweep runs when a tenant becomes ready and then at most
  every 5 minutes for each tenant, see
  [process execution](process-execution.md#worker-settings). An `attached` file is never
  purged. It is internal, so it needs no scheduled job.
- **Backup** _(built)_. Files in the tenant database are in the tenant's `pg_dump`, so
  [tenant backup](tenant-backup-and-restore.md) archives hold them.
- **Audit** _(built)_. An audit record names the file field that changed,
  never the file's name or bytes. A `sensitive` file field also hides its name
  and id in step history and inspection. Step history stores no field values,
  and a call input can never map a file, because only `is null` and
  `is not null` accept one, so a file's name and id never reach either.

## In the SPA

This is _(built)_.

- **Metadata.** A file field in the page metadata carries `maxSize`, `accept`,
  `uploadPath` and `downloadPath`, see the
  [frontend reference](frontend.md). The SPA never
  builds a file URL itself. `uploadPath` is `null` for a field of a module's
  entity, and the input then shows no Upload button.
- **Form.** A form shows a file input: the current file's name as a download
  link, an Upload button and, when the field is not required, a Remove button.
  The upload starts when the user picks a file. The save sends the file's id, or
  `null` after a remove. A file that was not changed is not sent. A file field
  of a child row works the same and uploads to its own route.
- **Refused upload.** A refused upload shows its message under the input. A
  `413` shows that the file is too large, a `415` that its type is not
  accepted, and any other failure that the upload failed. The texts are
  platform texts chosen by the status code, not the server's problem title. A
  file larger than `maxSize` is refused in the browser with the message of a
  `413`, and is not uploaded. `accept` only filters the file picker, and the
  server decides the type.
- **Table, detail and read-only child rows.** They show the file's name as a
  download link. A file column cannot be sorted.
- **Keyboard and screen readers.** The Upload and Remove buttons are reached
  and used with the keyboard. The Upload button is named like `Upload Receipt`
  and the Remove button like `Remove Receipt`. A polite live region announces
  each uploaded or removed file, and an upload error has `role="alert"`. After
  a remove, focus moves to the Upload button.
- **Limits.**
  - A data source row holds a file as its bare id, so a data source column
    shows the id and no link.
  - A task page uses the same form, so it shows the Upload button too. Task
    completion attaches no file for now, so saving a newly uploaded file there
    is refused with the server's message on the field.
  - A save while an upload runs sends the value from before the upload.
- Studio's entity designer edits `maxSize` and `accept` _(built)_.
