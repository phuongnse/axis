# Record API

Detailed reference for the record API: routes, record shape, paging, writes,
concurrency, errors and request bodies. How another system calls an inbound endpoint is in
[inbound-endpoints.md](inbound-endpoints.md). The conventions of
[architecture.md](../architecture.md) apply: sections marked
*(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Records of an entity are read and written through the record API. It serves
the entities of the active release of an application, in the tenant that the
request host resolves to. A record of another tenant does not exist for the
request, so it is a 404 like any unknown record.

Every record endpoint needs a session, or a bearer token of a
[service account](security.md#service-accounts), and a policy grant for the
action on the entity, as [Authorization](security.md#authorization) describes.
A request with an `Authorization: Bearer` header is a service account request.
It is judged by the token alone, never reads or renews the session cookie and
needs no CSRF header. Its caller id is the audit actor and the creator of a new
record. Without a session or with a bad token the answer is a `401`, with
`WWW-Authenticate: Bearer` for a token, and without a grant a `403`. A
grant with a `filter` reaches only the records that match it, so a record
outside every read filter is a `404`. A grant with a `fields` list covers
only those fields: fields the user may not read are left out, and a write of
a field no grant covers is a `403`.

[Inbound endpoints](inbound-endpoints.md) are the one exception to the
session rule. Another system calls them with a bearer token, and they take
no session. The bearer token check, the endpoint call with its `output` and
`wait`, the call's `Idempotency-Key` and the status location are built.

## Routes

| Method and path | Response |
| --- | --- |
| `GET /api/apps/{app}/entities/{entity}/records?page=&pageSize=&sort=&search=` | `200` with one page of records |
| `GET /api/apps/{app}/entities/{entity}/records/{id}` | `200` with one record |
| `POST /api/apps/{app}/entities/{entity}/records` | `201` with the new record and a `Location` header |
| `PATCH /api/apps/{app}/entities/{entity}/records/{id}` | `200` with the updated record |
| `DELETE /api/apps/{app}/entities/{entity}/records/{id}` | `204` with no body |
| `GET /api/apps/{app}/entities/{entity}/records/{id}/history?page=&pageSize=` | `200` with one page of the record's audit records. See [Audit records and history](#audit-records-and-history) |

`{app}` is the name of an active release, and `{entity}` an entity name in that
release. Both match ignoring letter case. `{id}` is a record id in the
hyphenated 8-4-4-4-12 hex form, in either letter case.

A module entity's `{entity}` is written `{module id}/{entity name}`, for example
`entities/departments/Department/records`, and matches ignoring letter case. It
is two path segments, and only that form reaches a module entity. An entity of
the application has one segment. A module entity named `records` cannot be listed
or created, because the route that reads a record by id takes that URL.

## Record shape

```json
{
  "id": "6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7",
  "version": 1,
  "values": {
    "name": "Desk",
    "quantity": 3,
    "price": 1250.50,
    "orderedAt": "2026-10-06T02:00:00.123456Z",
    "parts": [{ "name": "Leg" }, { "name": "Top" }],
    "department": "0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b"
  },
  "labels": {
    "department": "Finance"
  }
}
```

`values` holds every declared field of the entity that the user may read,
under its declared name, in declaration order. A field that is SQL `NULL` is `null`; it is never left out.
A `child-collection` field is an array of its rows, in their stored order. It
is `[]` when the record has no rows. List responses leave it out. See
[Child rows, computed fields and validations](#child-rows-computed-fields-and-validations).

`labels` maps each `reference` field that is not `null` to the display field
value of the referenced record, so a page can show "Finance" without one more
request per row. It is always present, and it is `{}` when the entity has no
reference or every reference is `null`. A `null` reference has no entry.
`values` keeps the record id of each reference, so a record read and sent back
is unchanged.

`values` and `labels` hold only the fields the user may read. They are the
union of the `fields` of the user's `read` grants on the entity, the same for
every record, and a grant without `fields` covers every field. A field the
user may not read is left out of both, so `null` still means an empty field.
That holds for computed, sequence and creator fields too. `id` and `version`
are always there. A user with a `create` grant and no `read` grant gets a
create response with empty `values` and `labels`. A child entity's rows follow their owner's `fields` entry for the
`child-collection` field, as a whole. See
[Authorization](security.md#authorization).

The labels come from the same SQL statement as the records, through one left
join per reference field to the target table. They are never read with one
query per row. Create and update return their labels from the same statement
as the write: the write is a data-modifying `WITH` clause, and the labels are
selected from it with the same joins, because `RETURNING` cannot join.

A reference field of a child row has its label in the row's place:
`"labels": { "lineItems": [{ "product": "Desk" }] }`, one object per row in
row order, read in the same statement as the rows. A collection gets an entry
only when its child entity has a reference whose label the user may see. The
object is `{}` for a row without a label, and the entry is `[]` when there are
no rows. A list response leaves child collections out, so it has no such
entry. A `references` field has an array of labels in the order of its ids, one per
id, in a single read and in the response of a create or update. A target whose
display field is `null` has `null` at its position. A user who may not read the
field, or the target's display field, gets no entry for it. A list response has no
such entry.

A label shows when the user may read the reference field and the target
entity's display field. For a child row's label, the reference field is the
owner's `child-collection` field. Otherwise the field has no entry in `labels`, as for a
`null` reference. The target's record filters do not hide it, so a user who
reads a request sees its department's name even when no read filter on
`Department` matches that department. The joins stay as they are.

A list response is `{ "items": [ ... ], "page": 1, "pageSize": 20,
"totalCount": 42 }`. `items` holds records in the shape above, including
`labels`. `page` and `pageSize` are the values used, and `totalCount` counts
the records of the entity that match the `search`, or every record without
one. A list holds only records that match one of the user's read filters,
and `totalCount` counts only those. A grant without a `filter` matches every
record. Each reference step of a read filter finds the target record through its
primary key. Every reference column and every child table's `owner_id` has an
index, and so does each index an entity declares, see
[schema planning](storage.md#schema-planning). A filter on a field no index
covers reads the whole entity table.

## Reading values

| Field type | JSON value |
| --- | --- |
| `text`, `enum` | A string |
| `integer` | A number |
| `decimal` | A number written as PostgreSQL renders the stored `numeric`. It is read as text, so no digit is lost and stored trailing zeros stay, such as `1250.50` |
| `boolean` | `true` or `false` |
| `date` | A string `yyyy-MM-dd` |
| `date-time` | A string in UTC with exactly six fraction digits and `Z`, such as `2026-10-06T02:00:00.123456Z`. PostgreSQL stores microseconds, so no precision is lost |
| `reference` | A string with the record id in the lowercase hyphenated form. Its label is in `labels` |
| `child-collection` | An array of row objects in stored order, in a single read and in the response of a create or update. List responses leave it out. See [Child rows](#child-rows-computed-fields-and-validations) |
| `money` | A number written as for `decimal`. The currency is the field's, or the value of its `currencyField` |
| `file` | An object `{ "id", "name", "mediaType", "size" }`, or `null`, also in child rows and in list responses. See [file fields](file-fields.md#field-and-value) |
| `references` | An array of record ids, in the order they were written. Its labels are an array in the same order in `labels`. List responses leave it out, as for a child collection |

## Paging and sorting

- **`page`.** An integer of at least 1. It defaults to 1.
- **`pageSize`.** An integer from 1 to 100. It defaults to 20.
- **`sort`.** A declared field name, or `-` and the name for descending
  order. The name matches exactly, so letter case matters. A
  `child-collection` field is not a valid `sort`. A field the user may not
  read is not a valid `sort` either, as for an unknown field.
- **`search`.** Keeps only the records whose display field contains the text,
  ignoring letter case. Both sides go through PostgreSQL `lower()`, so case
  folding follows the tenant database. The text has no wildcards: `%`, `_`,
  quotes and backslashes match as plain characters. The text is a parameter
  and never part of the SQL. It matches exactly as given, spaces included,
  and an empty `search` means no filter. It is invalid on an entity without a
  display field, when repeated, when longer than 200 characters, or when it
  contains U+0000. The filter cannot use an index, so it reads the whole
  table. A `search` is invalid when the user may not read the display field.
  From M15 _(planned for M15)_, `search` matches the display field and every
  `searchable` field the user may read, and a `pg_trgm` index serves it. It
  stays a substring match ignoring letter case, with the same limits. A
  searchable field the user may not read is skipped, and only an unreadable
  display field makes `search` invalid. See
  [D80](../decisions/d80-search-more-aggregates-and-query-timeouts.md).
- **Order.** Records are ordered by the sort column and then by `id`
  ascending, also for descending sorts. Without `sort`, they are ordered by
  `id` alone. `NULL` values follow the PostgreSQL defaults: last when
  ascending and first when descending. Text sorts by the tenant database's
  collation.
- **Past the end.** A page past the last one is `200` with empty `items` and
  the real `totalCount`.
- **Count.** `totalCount` is counted in a separate statement from the page.
  Both run in one read-only `READ COMMITTED` transaction, so under concurrent
  writes it can still differ from the items by a few rows. When only the count
  runs past `Data:QueryTimeout`, `totalCount` is `null`.

The paging parameters are digits only: a sign, a space or a repeated parameter
(`page=1&page=2`) is invalid.

A `sort` or `search` that names a
[pending field](migrations.md#recomputing-computed-fields) is a `409`. For
`search`, that is the display field. The title is fixed: "A field is being
recomputed." `errors` is keyed `sort` or `search`, with a message that names
the field. The request is valid, so this is a state conflict, not a `400`. It
is only reported once the query is valid. A list with no `sort` or `search` on
a pending field answers with the stored values. A single read computes the
pending field on the spot.

## Create, update and delete

Writes go through the record command in `Axis.Processes`, which hosts without
HTTP also call. The record API maps its outcome to the responses below.

- **Create.** `POST` takes `{ "values": { ... } }`. The record gets a new
  version 7 UUID as its id and `version` 1. A field with a `default` that the
  body leaves out gets its default, and a body that sends `null` keeps `null`.
  Any other field the body leaves out is SQL `NULL`. A required field with a
  default may be left out, but sending `null` for it is still a `400`
  "Required.". `now()` in a default is the start time of the write
  transaction, the same time the validations and the `record.created` audit
  record see. A default that fails at run time, such as a division by zero,
  is a `400` at `/values/<field>` with "Could not be computed.", and nothing
  is written. The same holds for a record the record command creates as
  `system`. A field filled from its default is not checked against the
  user's field grants, because the server sets it. Rows of a child collection
  get no defaults yet: a field a row leaves out is SQL `NULL`, and its
  required fields stay required. The response is `201` with the record, and `Location` is
  `/api/apps/{app}/entities/{entity}/records/{id}`. It uses the application
  and entity names from the active model, not the letter case of the request,
  so a record always has one URL. The id is in the lowercase hyphenated form.
- **Update.** `PATCH` takes `{ "version": n, "values": { ... } }`. Only the
  fields in `values` change, and fields left out keep their value. An update
  never applies a `default`. `null` clears a field that is not required. An empty `values` only increments
  `version`. The response is `200` with the record and its new version.
- **Data change triggers.** A create or update that commits also starts the
  processes of the matching [data change triggers](process-events.md#starts-from-triggers)
  in the same transaction, as `system`. The user needs no `start` grant. A
  write that fails starts none, and a start that is skipped never fails the
  write.
- **Delete.** `DELETE` removes the record by id only. It needs no body and no
  version. The response is `204` with no body. A record that another record
  references through a `reference` field is kept, and the response is `409`.
  So is a record that is the subject of a running or waiting process instance.
  A delete that commits also starts the processes of the matching
  [delete triggers](process-events.md#delete-triggers) in the same transaction,
  as `system`. A delete that is refused starts none.
- **Content type.** Both need `Content-Type: application/json`. The media
  type matches ignoring letter case. The only allowed parameter is `charset`
  with the value `utf-8`, in any letter case. A cross-site page can send a
  `text/plain` or form body without a CORS preflight, so accepting it would
  open a CSRF path for the session cookie. SameSite cookies and the CSRF
  header stay the main defence.
- **CSRF header.** Every `POST`, `PUT`, `PATCH` and
  `DELETE` under `/api` must carry the header `X-Axis-Csrf: 1`. Without it,
  the response is a `403` problem details response, and nothing is written.
  Only exactly one header with the value `1` passes: an empty value, any
  other value or a repeated header is a `403` too. `GET` requests and paths
  outside `/api` need no header. A cross-site page cannot
  set a custom header without a CORS preflight, which Axis does not allow.
  This adds to the content type rule and the SameSite session cookie. See
  [D23](../decisions/d23-sign-in-through-oidc-and-the-bff-session.md).
  `POST /api/apps/{app}/endpoints/{name}` is exempt from the header and
  takes no session. See [Inbound endpoints](inbound-endpoints.md). A
  [service account](security.md#service-accounts) request, which carries a
  bearer token, is exempt too, and takes no session.
- **One transaction.** A create or update writes the record and the rows of
  its child collections in one transaction. When any write fails, nothing is
  written, neither the owner nor any row. A delete also runs in a
  transaction. Each write commits its
  [audit record](#audit-records-and-history) in the same transaction. The
  validations run in the write's transaction, so `now()` in a validation is
  that transaction's start time
  ([D22](../decisions/d22-current-time-and-time-zones.md)).
- **References.** Before the write, each non-null `reference` value is looked
  up in the target table by id, in the write's transaction. The check does not
  lock the target. The foreign key is the backstop: a target removed in
  between is a foreign-key violation that maps to the same error.
- **Sequence fields.** A `text` field that names a
  [sequence](resource-files.md#sequences) gets its number on create, in the
  write's transaction, after the reference check. A create that is refused
  uses no number, so the next create gets it. A body that sets it on create
  or update, even to `null`, is a `400` at its pointer with the message
  "Cannot be set.", as for a computed field. See
  [Audit records and sequence counters](storage.md#audit-records-and-sequence-counters).
- **Creator fields.** A create by a signed-in user sets each `text` field that
  declares [`creator`](roles-and-policies.md#record-creator) to the user id from
  the session, in the write's transaction. It is set before the computed
  fields and validations, so they see it. A create without a session is a
  `401`, so the record API never leaves it `null`. A body that sets it on create or update, even to `null`, is a
  `400` at its pointer with the message "Cannot be set.", as for a sequence
  field.

- **Form conditions.** `POST` and `PATCH` take an optional query
  `?form=<name>`. The name must be a `form` resource over the entity. A name
  that is not, and an empty or repeated `form`, is a `400` keyed `form` with
  the message "Must name a form of this entity.". With a form, the server
  enforces its [`requiredWhen`](resource-files.md#form-conditions-and-steps) at
  the validation step, on the record as it will be stored: a visible field
  whose condition is true and whose value is `null` is a `400` at
  `/values/<field>` with "Required.". A hidden field is not required. A child
  collection and a sequence field are never checked. The "Required." errors
  come in the same `400` as the entity's validation messages, and "Required."
  wins when both fail on one key. An update of a form with `requiredWhen`
  reads the stored record first, so an unknown record is a `404` there.
  Without `?form=`, form conditions are not applied, so a rule that must
  always hold belongs in an entity validation. `readOnlyWhen` follows
  `readOnly`: the record API does not enforce it, and the field write grants
  decide. The `form` name is checked after the grant and record checks and
  before the content type and the body, so a user without the grant still
  gets `403` or `404`.

## Concurrency

Every record has a `version` that starts at 1 and grows by one on each update.
An update names the version it read and is applied only when the stored
version is still that one: `WHERE "id" = @id AND "version" = @version`. When
no row matches, a read by id decides between an unknown record (`404`) and a
stale version (`409`). The client then reads the record again and retries.

## Errors

Every error is problem details (`application/problem+json`). The titles are
fixed and never contain text from the request.

A request is checked in this order, and the first failure is the response:
the path (`404`), the content type (`415`), the body (`400`), then storage
(`400`, `404` or `409`). A request with the wrong content type is answered
before its body is read. Validations are a step between the body and
storage: see
[Child rows, computed fields and validations](#child-rows-computed-fields-and-validations). A delete has no body, so it is checked for the path,
then for a running process (`409`), then in storage (`404` or `409`).

A write without the [CSRF header](#create-update-and-delete) is a `403`
before any of these checks. A service account request has no such check. The
session comes next: a request without a session, or with a bad bearer token, is
a `401`, before the path. So a caller without a session cannot
find out which applications or records exist. See
[Sign-in and the session](security.md#sign-in-and-the-session).
After the path, the user needs a grant for the action on the entity, or the
answer is a `403` before the query or body is read.
`POST /api/apps/{app}/endpoints/{name}` is exempt from the CSRF header and
takes no session. Its own order is in [Inbound endpoints](inbound-endpoints.md).

Policies add more checks. The full order is:

1. The CSRF header, `403`.
2. The session or the bearer token, `401`.
3. The path, `404`.
4. A grant for the action on the entity, `403`.
5. The stored record: unknown or outside every read filter, `404`. On
   update and delete, a user with no `read` grant on the entity cannot see
   the record either, so it is a `404`. A read filter that reads a pending field
   is a `409` first, before the `404`. That holds for a list too, and for a
   history.
6. On update and delete, the stored record outside every `update` or
   `delete` filter, `403`. An `update` or `delete` filter that reads a pending
   field of the stored record is a `409` first, before the `403`.
   On delete, a stored record that is the subject of a running or waiting
   process instance is a `409`, after the `403` and before storage.
7. The content type, `415`.
8. The query of a list, or the body, `400`. Then, on a list, a `sort` or
   `search` on a pending field, `409`.
9. Fields the user may not write, `403`.
10. The computed fields and the validations, `400`, then storage.
11. On create and update, the new record outside every `create` or `update`
    filter, `403`.

The `503` below is not a check of its own. It can follow any check that passed
on a create or update, because the request runs those checks again on the new
release.

So a caller learns that a record exists only once they may read it. The field
and value checks need the parsed body, so they come after it. The new record
is checked as it is stored, inside the write transaction and after storage,
so its creator, sequence and computed fields are set. When it is outside the
filters, the write is rolled back and nothing is stored. On update, the
`version` in the body must be the version the checks in steps 5 and 6 saw,
otherwise the answer is the stale-version `409`. See
[Authorization](security.md#authorization).

- **`403` for the CSRF header.** A `POST`, `PUT`, `PATCH` or `DELETE`
  without `X-Axis-Csrf: 1`.
- **`401`.** A request without a session, or with a session from another
  tenant. A request with a bearer token that fails the checks of
  [service accounts](security.md#service-accounts) is a `401` too, with
  `WWW-Authenticate: Bearer`, and so is a token for another tenant's identity
  provider or the wrong audience. It comes before the path, so it is a `401`
  even for an unknown application. The title is fixed.
- **`403` without a grant.** The user has no grant for the action on the
  entity. A list or a single read needs `read`, a create `create`, an update
  `update` and a delete `delete`. History needs `read`. Only roles claim
  values that name a declared role of the application count. A grant with a
  `filter` counts, and reaches only the records that match it. A grant with
  a `fields` list counts too, and covers only those fields. The title is
  fixed and names no entity or action.
- **`404` outside every read filter.** A record that matches no read filter
  of the user is answered like an unknown record. That covers a single read,
  a history, an update and a delete. An update or a delete by a user with no
  `read` grant on the entity is a `404` too.
- **`403` for a record outside its write filters.** The stored record
  matches no `update` or `delete` filter, or the new record matches no
  `create` or `update` filter. Nothing is written. The title is fixed: "The
  record is outside your grants for this action." It names no entity or
  filter.
- **A filter that fails.** A filter that fails at run time for a record,
  such as an integer overflow, adds no records through its grant, and the
  server logs the policy name and the grant's index. Another grant can still
  match the record. A list whose combined read filters fail is a `400` with
  a fixed title, because PostgreSQL cannot fail one branch of an `or`.
- **`409` for a pending field.** A `sort` or `search`
  names a field that is being recomputed. The title is fixed: "A field is being
  recomputed." `errors` is keyed `sort` or `search`, with a message that names
  the field. See [Paging and sorting](#paging-and-sorting).
- **`409` for a policy that reads a pending field.** A
  `read` filter of the user's grants reads a field that is being recomputed.
  It covers a list, a single read and a history. An `update` or `delete`
  filter does the same on the stored record. A filter that reads a field of
  another entity through a reference counts when that entity is pending.
  Nothing is written. Read filters fail closed, as
  [D12](../decisions/d12-authorization-is-server-side-and-default-deny.md) requires, because a
  stale value could allow or deny wrongly. The title is fixed: "Your access to
  this entity cannot be checked while a field is being recomputed." There is no
  `errors`, and the response names no field or policy. The server logs the
  policy name and the grant's index, never a record value. The answer comes
  before the `404`, so it holds until the backfill ends, and it is the same
  whether the record exists or not. It also comes before the query `400`.
- **`403` for a field write.** The body writes a field that no `create` or
  `update` grant of the user covers. It has the `errors` shape of a `400`,
  keyed `/values/<field>`, and lists every such field. A child collection is
  one field, so its rows are not checked one by one. Nothing is written. The
  title is fixed: "You have no grant to write these fields."
- **`404`.** An unknown application, an application with no active release,
  an unknown entity, an unknown record and an `{id}` that is not a UUID in the
  hyphenated form. The path is resolved before the query is checked, so a
  path that names nothing is a 404 whatever its query.
- **`400`.** An invalid `page`, `pageSize`, `sort` or `search` is a
  validation problem whose `errors` is keyed by the parameter's name. Every invalid
  parameter is reported in the same response.
- **`400` for a body.** A body the parser rejects (see "Request bodies and
  values") is a validation problem whose `errors` is keyed by JSON Pointer. A
  `reference` value that names no record of the target entity is an error at
  `/values/<field>`.
- **`400` for a validation.** A record that breaks one of its entity's
  validations is a validation problem keyed at the validation's field. See
  [Child rows, computed fields and validations](#child-rows-computed-fields-and-validations).
- **`415`.** A `POST` or `PATCH` without `Content-Type` or with any type other
  than `application/json` with an optional `utf-8` charset.
- **`409` for a unique value.** A value that repeats the value of a `unique`
  field in another record is a validation problem with status `409`, whose
  `errors` is keyed `/values/<field>`. A unique field of a child row repeats
  a value of any row in the child table, and is keyed
  `/values/<collection>/<index>/<field>`. A record that repeats the values of
  a unique index the entity declares is the same `409`, keyed at the first
  field of the index. The field is found by matching the violated constraint
  or index against the names the model declares.
- **`409` for a stale version.** An update whose `version` is not the stored
  one.
- **`409` for a referenced record.** A delete of a record that another record
  references. Any foreign-key violation on delete maps to it. The title is
  fixed, and the response never names the referencing entity, table or
  constraint.
- **`409` for a record in a running process.** A delete of a record that is
  the subject of a running or waiting process instance. The title is fixed,
  and the response never names the process. The check runs after the record
  filters, so a user who cannot reach the record never learns about its
  processes. It runs under the [start lock](processes.md#releases-and-running-work)
  and the record's row lock, so a start and a delete of the same record
  cannot both succeed.
- **`409` for a schema conflict.** The table has a constraint the active model
  does not declare, such as a `NOT NULL` column left by an activation that
  failed after provisioning committed. The title is fixed, and the response
  never names a table, column or constraint.
- **`503` for a changed release.** Create, update and delete. The application's
  active release changed on each of three attempts, found under the
  [start lock](processes.md#releases-and-running-work). The server already ran
  the request again on the new release each time. Nothing was written, and the
  client may send the request again. The title is fixed: "The application's active release changed while the request ran. Try again."
- **`503` for a query timeout.** A read that runs past
  `Data:QueryTimeout` is cancelled by PostgreSQL. The title is fixed: "The
  query took too long. Try again or narrow it." The server logs the entity
  name and the duration, and adds one to the counter `axis.queries.timeouts`
  (unit `{query}`, attributes `axis.tenant` and `axis.application`). When only
  the count of a list times out, the answer is `200` with `totalCount: null`.
  See [data settings](host-settings.md#data-settings).
- **`500`.** An unexpected error on any path is caught by the exception
  handler. The response has no exception type, message or stack trace.
- **`traceId`**. Every problem details answer has a
  `traceId` member, the request's 32-hex W3C trace id. It names no tenant,
  record or user. An operator uses it to find the request and its run, see
  [Telemetry](../architecture.md#telemetry).

**No leaks.** No response body, including `title`, `detail` and the `errors`
messages, ever contains SQL, the `entities` schema, a table, column or
constraint name, an exception type or a stack trace. The `traceId` member is
allowed. The `errors` keys repeat
the request's property names by design, so they can hold any text the request
sent. A row of a child collection has a key such as
`/values/lineItems/1/quantity`. The integration tests check every error
response of the record API for this.

## Request bodies and values

`Axis.Data` parses a create or update body against the compiled entity
(`RecordInputParser`) without database access. It reports every problem in
one pass, or returns the typed values in the entity's field declaration order,
holding only the fields the body names.

- **Body.** The body is UTF-8 JSON. An empty body, malformed JSON, invalid
  UTF-8, nesting beyond the default depth of 64, a property name with an
  unpaired surrogate escape, or a body that is not an object is one error at
  `""`.
- **Body properties.** The body object has only `values`, plus `version` on
  update. Any other property, including `version` on create, is an error at
  `/<property>`.
- **`values`.** It is required and must be an object; otherwise it is one
  error at `/values`, and required fields are not reported as well. On update
  it may be empty, so the update only increments `version`.
- **Field names.** A `values` property name must equal a declared field name
  exactly, so letter case matters. Any other name is an error at
  `/values/<name>`.
- **Child collections.** A `child-collection` value must be an array of row
  objects. `null` or any other JSON type is an error at
  `/values/<collection>`, and an element that is not an object is an error at
  `/values/<collection>/<index>`. Each row is parsed like a create body
  against the child entity, with its errors keyed
  `/values/<collection>/<index>/<field>`. A parsed row holds every field of
  the child entity, and a field the row leaves out is SQL `NULL`.
- **Sets of records.** A `references` value must be an array of record ids
  in the form of a `reference`. `null` or any other JSON type is an error at
  `/values/<field>`: "Must be an array of record ids.". An empty array `[]`
  clears the set, and it has no other empty form. A malformed id, a repeated
  id and an id that names no record are errors at
  `/values/<field>/<index>`, the index of the element, and a repeat is
  reported at each element after the first. More than 1,000 ids are one error
  at `/values/<field>`, and the ids are not checked one by one. The array
  replaces the stored set and, like a child collection, raises the owner's
  `version` by one. A stale `version` is a `409` and leaves the links as they
  were. A user who holds no write grant on the field is refused with a `403`,
  as for any field.
- **Duplicates.** A property name repeated in the body or in `values` is an
  error at that property's pointer; the repeated value is not parsed.
- **Required fields.** On create, a required field that is missing or `null`
  is an error at `/values/<field>`. On update, missing fields are left
  untouched; `null` clears a field that is not required and is an error for a
  required one. `null` is SQL `NULL`. A required `references` field needs at
  least one id: a create must send it, and an update cannot set it to `[]`.
  Both are an error at `/values/<field>`.
- **`version`.** On update it is required and is an integral number from 1 to
  2^63 − 1, by the same integral rule as `integer`; otherwise an error at
  `/version`.

Values are never coerced between JSON types: `"5"` is not an `integer` and `5`
is not a `text`. A wrong JSON type or a value outside its bounds is an error at
`/values/<field>`.

| Field type | JSON value | Parsed as |
| --- | --- | --- |
| `text` | A string with no U+0000 and no unpaired surrogate escape, at most `maxLength` Unicode code points when set; a character outside the BMP counts as one | `string` |
| `integer` | A number that is integral and within the signed 64-bit range; `5.0` and `5e0` are integral, `5.5` is not | `long` |
| `decimal` | A number. After the exponent is applied, integer digits are counted without leading zeros and fraction digits without trailing zeros. With `precision`, at most `scale` (0 when omitted) fraction digits and `precision - scale` integer digits; without it, at most 131072 integer and 16383 fraction digits. Values are never rounded, and the limits are checked on the number text before any text is built, so a huge exponent is cheap to reject | `string` in plain notation that keeps the written trailing zeros (`1.50` stays `1.50`, `1.5e2` becomes `150`, `-0` becomes `0`), bound as text cast to `numeric` |
| `boolean` | `true` or `false` | `bool` |
| `date` | A string `yyyy-MM-dd` that is a valid date from 0001-01-01 to 9999-12-31 | `DateOnly` |
| `date-time` | An RFC 3339 string `yyyy-MM-ddTHH:mm:ss` with an optional fraction of 1 to 6 digits and `Z` or `±HH:mm`; `T` and `Z` may be lower case. A missing offset, an offset beyond ±14:00, a leap second (`:60`), more than 6 fraction digits (PostgreSQL stores microseconds) or an instant outside 0001..9999 in UTC is an error | `DateTimeOffset` converted to UTC with offset zero, the only offset Npgsql writes to `timestamp with time zone` |
| `enum` | A string equal to one of `values`, compared ordinally | `string` |
| `reference` | A string in the hyphenated 8-4-4-4-12 hex form, in either letter case; whether the record exists is checked when it is written | `Guid` |
| `money` | A number, checked as for `decimal` with the field's `precision` and `scale` | `string`, as for `decimal` |
| `file` | A file id from an upload, in the form of a `reference`. The file must be `pending` and uploaded by the same user for the same entity and field path, or already attached to this record at that path, see [file fields](file-fields.md#upload) | `Guid` |
| `references` | An array of distinct record ids, at most 1,000. The whole set replaces the stored one | `Guid[]` |

A value outside a field's limits is an error at the field's pointer, as for
`maxLength`. A value equal to a limit is accepted, and a missing or `null`
value is never checked against a limit.

- `min` and `max` apply to `integer`, `decimal`, `money`, `date` and
  `date-time` values. The messages are "Must be at least 1." and "Must be at most 10.",
  with the limit as written in the entity file. A `decimal` or `money` is compared
  exactly, with no rounding, so `0.49999999999999999999999999999999` is below
  `0.5`. A `date-time` is compared as an instant in UTC, whatever offset the
  value or the limit is written with.
- `minLength` applies to `text` values and counts code points, as `maxLength`
  does. The message is "Must be at least 3 characters.".
- The check is part of the request parser, so the record API, processes, task
  completion, form state, inbound endpoints and seeds all apply it. The limits
  are not stored as a `CHECK` constraint, so a stored row outside a tightened
  limit keeps its values and is read back unchanged.
- An endpoint `input` field has no limits of its own. An endpoint that writes
  a record is still checked by the entity field's limits.

**Errors** are a dictionary from RFC 6901 JSON Pointer into the body to its
messages, in ordinal key order, usable as is for a validation problem
response. The keys are `""` for the body, `/<property>` for a body property,
`/values`, `/values/<field>`, `/values/<collection>/<index>`,
`/values/<collection>/<index>/<field>`, `/values/<field>/<index>` for the id of a set and `/version`. Pointers escape `~` as `~0` and
`/` as `~1`; declared names never need it, but unknown names can. Each key has
one fixed English message for the first problem found there, such as
"Unknown property." or "Must be at most 200 characters.". The message of a
failed validation is its text key instead. Messages may use
field type names and limits from the model. They never contain a table,
column, constraint or schema name, a PostgreSQL type name, exception text, or
text from the request; the key already says where the problem is.

## Child rows, computed fields and validations

Child rows, computed fields and validations are built (D17): the record shape
and the bodies described above include them. Computed fields and validations
can [aggregate](expressions.md#aggregates) over the child rows.

A record with a child collection holds its rows inside `values`:

```json
{
  "id": "6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7",
  "version": 2,
  "values": {
    "title": "Standing desks",
    "total": 2550.90,
    "lineItems": [
      { "description": "Desk", "quantity": 2, "unitPrice": 1250.45, "amount": 2500.90 },
      { "description": "Cable tray", "quantity": 1, "unitPrice": 50.00, "amount": 50.00 }
    ]
  },
  "labels": {}
}
```

- **Rows.** A row is a flat object of the child entity's fields, in
  declaration order. It has no id. Rows are ordered by `position`, and the
  array index is the position. A row includes its computed values.
- **Reads.** A single read and the response of a create or update include the
  collections. List responses leave them out.
- **Create.** A missing collection means no rows. `null` is an error at
  `/values/<collection>`.
- **Update.** A missing collection keeps its rows. An array replaces all the
  rows, in the same transaction, and increments the owner's `version`. `null`
  is an error at `/values/<collection>`. A stale `version` is a `409`, and no
  row changes.
- **References.** A child entity may have `reference` fields. The labels of a
  row's references come with the rows, as `labels` above describes. A row
  that names a record that does not exist is a `400` at its cell,
  `/values/<collection>/<index>/<field>`, with the message of a root
  reference. A delete of a record that a child row points to is the `409` for
  a referenced record. Once the owner record is deleted, its rows go with it
  and the target can be deleted.
- **No routes of its own.** A child entity has no record routes. Every method
  on `/api/apps/{app}/entities/{child}/records` is a `404` with the same title
  as an unknown entity.
- **Parsing.** A row is parsed like a create body, so a required field of the
  child entity must be present in every row. An array element that is not an
  object is an error at its index. A row error is keyed
  `/values/<collection>/<index>/<field>`, with a zero-based index.
- **Computed fields.** A body that sets a computed field, of the owner or of
  a row, even to `null`, is an error at its pointer with the message "Cannot
  be set.". The server calculates the value on every create and update of the
  record, including an update with no values, and stores it in the same
  statement as the other values. Child rows are calculated before the owner.
  A record stored before its computed field existed reads `null` until its
  next write. A single read, and the response of an update, return a pending
  field computed with the active release when the row's stamp is out of date.
  A create always computes with the active release, so its response needs no
  such step. A run-time error there reads `null`, and the read answers `200`.
  The read stores nothing. A list shows the stored value. See
  [Recomputing computed fields](migrations.md#recomputing-computed-fields).
- **Aggregates.** An aggregate of the owner, such as `sum(lineItems, amount)`,
  reads the rows the write stores. When the body sends the collection, it
  reads the body's rows after their own computed fields. When an update
  leaves the collection out, it reads the stored rows. A create without the
  collection has no rows. A stored row value that cannot be evaluated
  exactly is a `400` at the row's pointer, such as
  `/values/lineItems/0/amount`, with "Cannot be evaluated exactly.".
- **Delete.** Deleting the owner deletes its rows.

**Validations.** The order for a write is:

1. Computed fields and validations run only on a body that parsed cleanly.
   A body with parse errors is answered with those errors alone.
2. On update of an entity with computed fields or validations, the stored
   record is read first, with its rows and sets. An unknown record is still a `404`,
   never a `400`.
   The server then builds the record as it will be stored: the stored values,
   the changes from the body, the rows it will store and the computed fields. A computed field that
   fails is a `400`, and then no validation runs. So a change to one field
   can break a validation reported on another.
3. The validations of the entity and of each row the body sends run, inside
   the write's transaction, so `now()` is that transaction's start time. A
   validation's aggregates read the same rows as the computed fields, so
   `count(lineItems) >= 1` refuses a create without rows and an update that
   sends an empty array. Rows an update leaves out are unchanged and are not
   checked again as rows, but the owner's aggregates still read them. An
   aggregate over a `references` field reads the target records of the ids the
   body sends, or of the stored set when an update leaves the field out, and a
   create without the field has an empty set. It reads them whatever the
   user's read grants and record filters on the target entity. An id that
   names no record is left out of the aggregate, and then storage refuses it.
   Any failure is a `400`.
4. Storage runs. It answers as described above, for example `409` for a stale
   `version`.

A failure is keyed `/values/<field>` or by a row path such as
`/values/lineItems/1/quantity`. Each key holds one message. When several
validations fail on one key, it reports the first in declaration order. Every
other key is reported in the same response. The message is the validation's
text key, and the client shows the text in the user's locale. The client gets
validation as the user types from the [form state endpoint](#form-state).

A run-time error in an expression, or a computed value that does not fit its
field, such as one with more fraction digits than `scale`, rejects the write
with a `400` at that field's pointer. A run-time error in a computed field has
the message "Could not be computed.". A value that does not fit gets the
message a body value would get, such as "Must have at most 2 fraction
digits.". Values are never rounded. So on an entity with computed fields or
validations, a `decimal` value that the interpreter cannot hold exactly
(see [expression types](expressions.md#types)) is a `400` at its own
pointer, with the message "Cannot be evaluated exactly.", and no validation
of that record runs. A stored decimal of a target record that cannot be held
exactly is the same `400`, at the pointer of the `references` field.

For example, a create or update with `quantity` `0` in the second row is
answered with `400`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "/values/lineItems/1/quantity": ["lineItem.quantityPositive"]
  }
}
```

## Form state

The form state endpoint answers what a form shows for a draft record, and which messages the user's input earns so far. It writes
nothing ([D62](../decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md)).
The conditions are in [Form conditions and steps](resource-files.md#form-conditions-and-steps).

`POST /api/apps/{app}/forms/{form}/state` needs `Content-Type: application/json`
and the [CSRF header](#create-update-and-delete). `{form}` is a form name in
the active release, ignoring letter case.

```json
{
  "recordId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
  "values": { "status": "resolved" },
  "touched": ["status", "resolution"]
}
```

- `recordId` is `null` for a new record, or the id of the record being edited.
- `values` has the shape of the `values` of a write, in the
  [value format](#request-bodies-and-values). Child rows and their keys work
  as on writes.
- `touched` lists the fields the user has changed or left. It may be empty.

The draft is the stored record, when there is one, overlaid with `values`. For
a new record, each field that `values` leaves out first takes its `default`, as
a create does. Computed fields are calculated on it.

```json
{
  "sections": [{ "name": "update", "visible": true }],
  "fields": {
    "status": { "visible": true, "required": false, "readOnly": false },
    "resolution": { "visible": true, "required": true, "readOnly": false }
  },
  "errors": { "/values/resolution": ["Required."] },
  "defaults": {}
}
```

- `sections` has one entry for each named section of the form that holds a
  field the user may read. A hidden section hides its fields, and an unnamed
  section has no entry.
- `fields` has one entry for each field of the form that the user may read.
  - `visible` is false in a hidden section and for a field whose `visibleWhen`
    is not true.
  - `required` is true for a visible field that the entity marks `required` or
    whose `requiredWhen` is true.
  - `readOnly` covers only the form's rules: the form's `readOnly`, a computed,
    sequence or creator field and a true `readOnlyWhen`. It does not cover field
    write grants, which are in the page metadata's `createFields` and
    `updateFields`.
- `errors` holds the parse errors, "Required." for an empty required field, the
  computed-field errors and the entity's validation messages, keyed as on a
  write. It holds only keys of fields that the user touched and may read.
- `defaults` holds, for a new record, the `default` of each field of the form
  that the user may read and that `values` leaves out, in form order. The values
  are in the [value format](#request-bodies-and-values), so a date-time is UTC
  text and a decimal shows its scale. The conditions and messages above are
  worked out with them in place, so a form can show what a save would store.
  - A field that `values` sends, even as `null`, is not in `defaults`. Neither
    is a field whose value does not fit its type, because the user's text must
    stay.
  - A default that gives `null` for an optional field is `null` in `defaults`.
  - A default that cannot be computed is left out, and its message is in
    `errors` under the touched-field rule. The other defaults stay in.
  - A field that is not on the form, or that the user may not read, is not in
    `defaults`. A save still gives a field that is not on the form its default.
  - `now()` is the time of this call, not of the save.
  - It is `{}` for a record id, and on the
    [task form state](task-api.md) route.
- A value that does not fit its field, or a computed field that cannot be
  computed, is a message in `errors` of a `200` answer, so that one bad value
  does not stop the other fields from getting their states. A `400` is only for
  a body of the wrong shape: it is not an object, `recordId` is not `null` or a
  UUID, `values` is not an object, `touched` is not an array of text, a property
  is repeated, unknown or missing (`recordId` may be left out).
- At typical size, a form of 25 fields with a condition each and 50 line items,
  it answers within 300 ms. `TypicalFormStatePerformanceTests` fails when a call
  stays over that at best of three attempts.
- It writes nothing, adds no audit record and uses no sequence number. It
  does not run unique or reference-existence checks, because they query
  storage on every keystroke and could reveal which records exist. A save runs
  them.

The checks run in this order, and the first failure is the response:

1. `401` when nobody is signed in.
2. `404` for an unknown application or form.
3. `403` when the user has neither a `create` nor an `update` grant on the
   form's entity.
4. `415` for a wrong content type, then `400` for a malformed body.
5. `403` without a `create` grant when `recordId` is `null`, or without an
   `update` grant when it is set.
6. `404` when the user has no `read` grant, or when the record is not one the
   user's `read` filters reach. `409` when a `read` filter reads a computed
   field that is being recomputed.

The grant check is split because `recordId` is in the body, so the server
cannot tell which grant the call needs before it reads the body. The call does
not check `update` record filters. A save does.

The problem details are as in [Errors](#errors).

## Inbound endpoints

How another system calls an inbound endpoint is in [inbound-endpoints.md](inbound-endpoints.md).

## Audit records and history

Every record write leaves an audit record, and one record's audit records
can be read as its history (D21).

- **Writes.** A create, update or delete writes one audit record in the
  write's transaction. When the write fails, no audit record is written. For
  example, a stale version, a missing reference, a unique violation or a
  delete of a referenced record leaves none. When the audit record cannot be
  written, the write rolls back and the response is a `500`. Seed records
  applied during activation write no audit record.
- **Actions.** The actions are `record.created`, `record.updated` and
  `record.deleted`.
- **Actor.** The actor is the user of the
  [session](security.md#sign-in-and-the-session). Every write needs a
  session, so the record API never writes `anonymous`. The audit record also
  stores the user's display name as it was at that moment. A process
  [operation step](process-execution.md#running-a-step) that updates its subject
  record writes `record.updated` with the actor `system`, no display name and
  its instance id. A [task completion](task-api.md#completing-a-task) that
  writes values writes no `record.updated`. Its `task.completed` record names
  the fields. An [endpoint call](inbound-endpoints.md) writes `record.created`
  with the caller as the actor, and the caller's display name.
- **Details.** `details` is `{ "version": n }` with the record's new version.
  A delete holds the version the record had when it was deleted. An endpoint
  call's `record.created` also has `"endpoint"`, the endpoint's name.
  - On update it also has `"fields"`: the declared names of the fields and
    child collections the body sets, in declaration order. A field sent with
    its stored value is listed too. Computed fields are never listed.
  - Field values are never included, so the audit holds no sensitive data
    before field masking exists.
- **History**. `GET /api/apps/{app}/entities/{entity}/records/{id}/history`
  returns the audit records of one record.
  - It lists every audit record that names the record. That covers the
    record API's writes, and the [process start](processes.md#start-endpoint),
    each step and each [task decision](task-api.md#completing-a-task), claim and release on it,
    including those whose actor is `system`.
  - The path follows the 404 rules of a single read. An unknown record, a
    deleted record, or an id from another tenant is a `404`.
  - `page` and `pageSize` follow the [paging rules](#paging-and-sorting).
    An invalid one is a `400` keyed by its name. There is no `sort` or
    `search`. The session, the path and the grant are checked first, then
    the paging, then whether the record exists.
  - Items are ordered by `occurredAt` descending, then by `id` descending.
    The audit records of one transaction share one time, so their order
    among themselves follows their ids. That order may differ from the order
    they were written in.
  - Each item has its `id`, `occurredAt`, `actor`, `actorName`, `action`,
    `processInstanceId` and `details`, as stored. `actorName` is the display
    name stored with the audit record. It is `null` for `system`,
    `anonymous` and audit records written before names were stored.
  - Each item also has `inspectPath`, the last property of the item. It is the
    [instance detail](process-inspection.md#instance-detail) path of the entry's
    process instance, such as
    `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances/0192f4a9-…`,
    when the user holds an `inspect` grant on the entry's process. It is `null`
    when the user holds none, when the entry has no instance, and when the
    process is no longer in the active release. The server
    decides, so a client never guesses it from roles. The path leads to a
    `404` when the user may not read the instance's subject record.
  - The history needs a session and a `read` grant on the entity. Without a
    session it is a `401`, and without the grant a `403`, before the record
    is read. A record outside every read filter is a `404`. The `fields` of
    `details`, in `record.updated` and `task.completed`, leave out the fields
    the user may not read.

For example:

```json
{
  "items": [
    {
      "id": "01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7d",
      "occurredAt": "2026-10-06T02:05:00.123456Z",
      "actor": "anna",
      "actorName": "Anna Employee",
      "action": "record.updated",
      "processInstanceId": null,
      "details": { "version": 2, "fields": ["title"] },
      "inspectPath": null
    },
    {
      "id": "01926b3a-1c20-7b4d-8e6f-7a8b9c0d1e2f",
      "occurredAt": "2026-10-06T02:00:00.123456Z",
      "actor": "anna",
      "actorName": "Anna Employee",
      "action": "record.created",
      "processInstanceId": null,
      "details": { "version": 1 },
      "inspectPath": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 2
}
```
