# File fields

A `file` field holds one uploaded file on a record. This page covers its value, the upload and
download routes, where the bytes are stored and how they are cleaned up, the `fileStorage` manifest
property and how the SPA shows a file. The reasons for the design are in
[D76](../decisions/d76-file-money-and-enum-label-field-types.md).

## Field and value

This section says how a file field is declared, what a record holds for it and what an upload may
contain.

- **Field.** `{ "name": "receipt", "type": "file", "maxSize": 10485760, "accept": ["application/pdf", "image/*"] }`.
  `maxSize` is in bytes and is at most `Files:MaxSize`. `accept` lists media types, and `type/*`
  matches any subtype. Both are optional. See
  [entity field types](resource-files.md#entity-field-types-and-constraints).
- **Extensions.** The extension of the file name is never checked, and the stored name keeps
  whatever extension it was sent with. The declared media type and the file's first bytes decide
  what is accepted, as the [upload checks](#upload) say.
- **Unpacking.** An upload is stored as sent and never unpacked, so `maxSize` and `Files:MaxSize`
  also limit its unpacked size.
- **Malicious files.** An upload that starts like an executable, declares `image/svg+xml`, or does
  not start as its declared PDF, PNG, JPEG, GIF, WebP or ZIP type is refused with a `415`, and
  nothing is stored. Every other file is only ever downloaded as an attachment with `nosniff`, as
  the [download headers](#download) say. Axis does not scan uploads for malware, so an
  operator that needs scanning puts a scanning proxy in front of the upload route.
- **Several files.** A record holds several files through a child collection whose child entity has
  a `file` field, such as `attachments` with rows of `{ file, note }`.
- **Read.** A record holds `{ "id", "name", "mediaType", "size" }` for a file field, or `null`.
- **Write.** A create or update body sends the file id as a string, or `null` to remove the file.
- **Expressions.** A file allows only `is null` and `is not null`. It cannot be a `sort`, a
  `groupBy` entry or a `search` field, a seed cannot set it, and it cannot have `default`, `unique`
  or `expression`.

## Upload

An upload stores a file as `pending`, and a later record write attaches it. This section covers the
upload route, its checks and the attach step.

`POST /api/apps/{app}/entities/{entity}/fields/{field}/files` takes the bytes as
`multipart/form-data` with one part, `file`, and answers `201` with
`{ "id", "name", "mediaType", "size" }`. A field of a child entity is named by its collection and
field, such as `/api/apps/{app}/entities/{entity}/fields/attachments/file/files`. An entity of a
module has no upload route. A service account may upload, as it may write records.

- **Order of checks.** The route checks in the order of the record routes: sign-in (`401`), then in
  a cell the tenant's [storage quota](tenant-quotas.md#refusals) (`403`, `quota-exceeded`), then the
  path (`404`), then the grant (`403`), then the request content type (`415`), then the body. A user
  without a grant learns nothing from the body checks. An unknown entity or field, and a field that
  is not a `file` field, are `404`.
- **Grant.** The user needs a `create` or `update` grant on the entity whose field list covers the
  field, or the answer is a `403`. For a child field the list must cover the owner's child
  collection field. Record filters are not read, because an upload has no record yet, and the
  record write that attaches the file applies them.
- **Body.** The request must be `multipart/form-data` with a boundary, or it is a `415`. A missing
  `file` part, an extra part, a part without a file name and an empty file are `400` at `file`.
- **Name.** The stored name is the last path segment of the part's file name, without control
  characters, at most 255 characters.
- **Media type.** The media type comes from the part's `Content-Type`, without its parameters, such
  as `charset`. A part without one is stored as `application/octet-stream`.
- **Checks.** A file larger than the field's `maxSize`, or than `Files:MaxSize`, is a `413` as soon
  as the count passes the limit. A media type that `accept` does not allow is a `415`. A declared
  `image/svg+xml` is a `415` whatever `accept` allows, because an SVG can carry scripts. A file that
  starts like a Windows, Linux or macOS executable is a `415` whatever type it declares. A file
  declared as PDF, PNG, JPEG, GIF, WebP or ZIP must start as that type does, or it is a `415`. Other
  types are checked only for an executable start.
- **Pending.** The file is stored as `pending`, owned by the user, the entity and the field path
  (`receipt`, or `attachments/file` for a child field). Nothing points to it yet, so the response
  has no `Location` header: a pending file cannot be downloaded.
- **Memory.** The server writes the upload to a temporary file in the system temp folder as it
  arrives, deletes it when the request ends, and streams it into the database, so memory does not
  grow with the size of the file. `FilePerformanceTests` fails when the best of three 25 MB uploads
  takes 5 s or more, or allocates as many bytes as the file.
- **Attach.** A create or update that sends the id attaches the file in its transaction: the state
  becomes `attached` and the owner record becomes the root record, also for a field of a child row.
  The file must be `pending`, uploaded by the same user, for the same entity and field path.
  - **Kept ids.** An id the record already holds at the same path stays as it is. This covers a
    resent unchanged field and a kept child row.
  - **Refusal.** Any other id is a `400` at the field's pointer, such as `/values/receipt` or
    `/values/attachments/0/file`, with one message, "No uploaded file for this field has this id."
    The message is the same whether the id is unknown, belongs to another user, is attached
    elsewhere or was uploaded for another field. A malformed id is "Must be a UUID."
  - **Repeated id.** An id sent twice in one body is refused at every pointer after the first.
  - **Other writers.** Inbound endpoints, task completion and process steps pass no uploader, so
    they attach nothing and refuse any id the record does not already hold.
  - **Replaced and removed files.** A file that a write replaced or cleared is `deleted`, as
    [storage and clean-up](#storage-and-clean-up) says. Its id is refused like any other unknown id,
    so it cannot be attached again.

## Download

A download answers a file's bytes to a user who may read the field on its owner record, and answers
every other request with the same `404`.

`GET /api/apps/{app}/files/{id}` answers the bytes. A session user and a service account may
download, as they may read records.

- **Checks.** A request without a session or token is a `401`, as on every record route. After that
  the server finds the file's owner record and checks the user's `read` grant, the record filters
  and that the user may read the field, as a read of the record would. Any failure, an unknown or
  malformed id, a file of another application and a file that is not `attached` are all the same
  `404`, so the answer never tells whether a file exists.
- **Pending filter.** A read filter that uses a computed field that is being recomputed is a `404`
  too, not the `409` that a record read gives, because a `409` would show that the file exists.
- **Headers.** `Content-Type` is the stored media type, `Content-Disposition: attachment` with the
  file name, and `X-Content-Type-Options: nosniff`. A browser never shows a file inline, so an
  uploaded HTML file, or an SVG sent under another type, cannot run in the application's origin.
- **Child rows.** A child row has no id of its own, so a download always goes by file id. It goes
  through the owner record, and the user's `read` grant must cover the owner's child collection
  field, such as `attachments`.
- **Memory.** The checks run under the interactive query timeout. Then the bytes stream from the
  database to the client with no statement limit, and the answer carries a `Content-Length`. A
  download holds one database connection until the client has received every byte.
  `FilePerformanceTests` fails when the best of three 25 MB downloads takes 5 s or more, or
  allocates as many bytes as the file.

## Storage and clean-up

A file's details and bytes live in the tenant database, and a file that no record holds any more is
purged by the worker.

- **Metadata.** The platform table `axis.files` in the tenant database holds the id, application,
  entity, field path, owner record, name, media type, size, SHA-256, storage kind, state (`pending`,
  `attached` or `deleted`), and who uploaded it and when. The field's column is a `uuid` that points
  to it.
- **Bytes.** The bytes live in `axis.file_contents` of the tenant database, under the id of the
  file's row. An application may name a `storage` binding for them through
  [`fileStorage`](#the-filestorage-manifest-property), and one without it uses the tenant database.
- **Replace and remove.** A write that replaces or removes a file, an update that removes a child
  row and a record delete mark each old file `deleted` in the write's transaction. An update marks
  the files that the record holds at a written file field, or at the file field of a written
  collection, and that the write did not send. A record delete marks every file of the record, also
  a file of a child row, in the same statement as the delete. A refused write leaves the files as
  they were.
- **Sweep.** A worker sweep purges `deleted` files and `pending` files uploaded more than 24 hours
  ago, with their bytes. The worker's clock sets the 24 hours. The sweep runs when a tenant becomes
  ready and then at most every 5 minutes for each tenant, see
  [process execution](process-execution.md#worker-settings). An `attached` file is never purged.
  The sweep is internal, so it needs no scheduled job.
- **Backup.** Files in the tenant database are in the tenant's `pg_dump`, so
  [tenant backup](tenant-backup-and-restore.md) archives hold them.
- **Audit.** An audit record names the file field that changed, never the file's name or bytes.
  Step history stores no field values, and a call input can never map a file, because only
  `is null` and `is not null` accept one, so a file's name and id never reach either.

## The `fileStorage` manifest property

The `application` manifest may name the deployment binding that says where the bytes of its files
go:

```json
{
  "id": "6ec71db1-5b21-43d9-95d2-dcc45055eb0e",
  "kind": "application",
  "name": "ServiceDesk",
  "formatVersion": 1,
  "fileStorage": { "binding": "Files" }
}
```

- `fileStorage` is optional. It is an object with one `binding`, a binding name, and any other shape
  is a schema error. Without it, the bytes go to the tenant database and activation needs no
  binding.
- The binding must be a `storage` binding of the tenant. Activation reports a missing binding, or
  one of another kind, as `AXC0100` at `/fileStorage/binding` of `application.json`. On a preview
  tenant a missing binding is a warning.
- The binding's only value is `database`, so the bytes stay in `axis.file_contents`. See
  [deployment bindings](integration-resources.md#deployment-bindings).

## In the SPA

The SPA shows a file field as a file input on forms and as a download link elsewhere, and builds no
file URL of its own.

- **Metadata.** A file field in the page metadata carries `maxSize`, `accept`, `uploadPath` and
  `downloadPath`, see the [frontend reference](frontend.md). `uploadPath` is `null` for a field of a
  module's entity, and the input then shows no Upload button.
- **Form.** A form shows a file input: the current file's name as a download link, an Upload button
  and, when the field is not required, a Remove button. The upload starts when the user picks a
  file. The save sends the file's id, or `null` after a remove, and does not send a file that was
  not changed. A file field of a child row works the same and uploads to its own route.
- **Refused upload.** A refused upload shows its message under the input. A `413` shows that the
  file is too large, a `415` that its type is not accepted, and any other failure that the upload
  failed. These are platform texts chosen by the status code, not the server's problem title. A
  `403` for a quota refusal shows the server's title, as [refusals](tenant-quotas.md#refusals) says.
  A file larger than `maxSize` is refused in the browser with the message of a `413`, and is not
  uploaded. `accept` only filters the file picker, and the server decides the type.
- **Table, detail and read-only child rows.** They show the file's name as a download link. A file
  column cannot be sorted.
- **Keyboard and screen readers.** The Upload and Remove buttons are reached and used with the
  keyboard. The Upload button is named like `Upload Receipt` and the Remove button like
  `Remove Receipt`. A polite live region announces each uploaded or removed file, and an upload
  error has `role="alert"`. After a remove, focus moves to the Upload button.
- **Limits.**
  - A data source row holds a file as its bare id, so a data source column shows the id and no link.
  - A task page uses the same form, so it shows the Upload button too. Task completion attaches no
    file, so saving a newly uploaded file there is refused with the server's message on the field.
  - A save while an upload runs sends the value from before the upload.
- **Studio.** The [entity designer](entity-designer.md#field-drawer) edits `maxSize` and `accept`.
