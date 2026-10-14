# Record API

Detailed reference for the record API: routes, record shape, paging, writes,
concurrency, errors, request bodies and, from M5, inbound endpoints. The conventions of
[architecture.md](../architecture.md) apply: sections marked
*(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Records of an entity are read and written through the record API. It serves
the entities of the active release of an application, in the tenant that the
request host resolves to. A record of another tenant does not exist for the
request, so it is a 404 like any unknown record.

Every record endpoint needs a session and a policy grant for the action on
the entity, as [Authorization](../architecture.md#authorization) describes.
Without a session the answer is a `401`, and without a grant a `403`. A
grant with a `filter` reaches only the records that match it, so a record
outside every read filter is a `404`. A grant with a `fields` list covers
only those fields: fields the user may not read are left out, and a write of
a field no grant covers is a `403`.

[Inbound endpoints](#inbound-endpoints) are the one exception to the
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
[Authorization](../architecture.md#authorization).

The labels come from the same SQL statement as the records, through one left
join per reference field to the target table. They are never read with one
query per row. Create and update return their labels from the same statement
as the write: the write is a data-modifying `WITH` clause, and the labels are
selected from it with the same joins, because `RETURNING` cannot join.

A label shows when the user may read the reference field and the target
entity's display field. Otherwise the field has no entry in `labels`, as for a
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
primary key. No field has an index yet, so a filtered list reads the whole entity
table.

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
- **Order.** Records are ordered by the sort column and then by `id`
  ascending, also for descending sorts. Without `sort`, they are ordered by
  `id` alone. `NULL` values follow the PostgreSQL defaults: last when
  ascending and first when descending. Text sorts by the tenant database's
  collation.
- **Past the end.** A page past the last one is `200` with empty `items` and
  the real `totalCount`.
- **Count.** `totalCount` is counted in a separate statement from the page,
  without a transaction. Under concurrent writes it can differ from the items
  by a few rows.

The paging parameters are digits only: a sign, a space or a repeated parameter
(`page=1&page=2`) is invalid.

## Create, update and delete

- **Create.** `POST` takes `{ "values": { ... } }`. The record gets a new
  version 7 UUID as its id and `version` 1. Fields the body leaves out are
  SQL `NULL`. The response is `201` with the record, and `Location` is
  `/api/apps/{app}/entities/{entity}/records/{id}`. It uses the application
  and entity names from the active model, not the letter case of the request,
  so a record always has one URL. The id is in the lowercase hyphenated form.
- **Update.** `PATCH` takes `{ "version": n, "values": { ... } }`. Only the
  fields in `values` change, and fields left out keep their value. `null`
  clears a field that is not required. An empty `values` only increments
  `version`. The response is `200` with the record and its new version.
- **Data change triggers.** A create or update that commits also starts the
  processes of the matching [data change triggers](processes.md#starts-from-triggers)
  in the same transaction, as `system`. The user needs no `start` grant. A
  write that fails starts none, and a start that is skipped never fails the
  write.
- **Delete.** `DELETE` removes the record by id only. It needs no body and no
  version. The response is `204` with no body. A record that another record
  references through a `reference` field is kept, and the response is `409`.
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
  [D23](../decisions.md#d23-sign-in-through-oidc-and-the-bff-session--agreed).
  `POST /api/apps/{app}/endpoints/{name}` is exempt from the header and
  takes no session. See [Inbound endpoints](#inbound-endpoints).
- **One transaction.** A create or update writes the record and the rows of
  its child collections in one transaction. When any write fails, nothing is
  written, neither the owner nor any row. A delete also runs in a
  transaction. Each write commits its
  [audit record](#audit-records-and-history) in the same transaction. The
  validations run in the write's transaction, so `now()` in a validation is
  that transaction's start time
  ([D22](../decisions.md#d22-current-time-and-time-zones--agreed)).
- **References.** Before the write, each non-null `reference` value is looked
  up in the target table by id, in the write's transaction. The check does not
  lock the target. The foreign key is the backstop: a target removed in
  between is a foreign-key violation that maps to the same error.
- **Sequence fields.** A `text` field that names a
  [sequence](configuration.md#sequences) gets its number on create, in the
  write's transaction, after the reference check. A create that is refused
  uses no number, so the next create gets it. A body that sets it on create
  or update, even to `null`, is a `400` at its pointer with the message
  "Cannot be set.", as for a computed field. See
  [Audit records and sequence counters](storage.md#audit-records-and-sequence-counters).
- **Creator fields.** A create by a signed-in user sets each `text` field that
  declares [`creator`](configuration.md#record-creator) to the user id from
  the session, in the write's transaction. It is set before the computed
  fields and validations, so they see it. A create without a session is a
  `401`, so the record API never leaves it `null`. A body that sets it on create or update, even to `null`, is a
  `400` at its pointer with the message "Cannot be set.", as for a sequence
  field.

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
[Child rows, computed fields and validations](#child-rows-computed-fields-and-validations). A delete has no body, so it is checked for the path
and then in storage (`404` or `409`).

A write without the [CSRF header](#create-update-and-delete) is a `403`
before any of these checks. The session comes next: a request without a
session is a `401`, before the path. So a caller without a session cannot
find out which applications or records exist. See
[Sign-in and the session](../architecture.md#sign-in-and-the-session).
After the path, the user needs a grant for the action on the entity, or the
answer is a `403` before the query or body is read.
`POST /api/apps/{app}/endpoints/{name}` is exempt from the CSRF header and
takes no session. Its own order is in [Inbound endpoints](#inbound-endpoints).

Policies add more checks. The full order is:

1. The CSRF header, `403`.
2. The session, `401`.
3. The path, `404`.
4. A grant for the action on the entity, `403`.
5. The stored record: unknown or outside every read filter, `404`. On
   update and delete, a user with no `read` grant on the entity cannot see
   the record either, so it is a `404`.
6. On update and delete, the stored record outside every `update` or
   `delete` filter, `403`.
7. The content type, `415`.
8. The query of a list, or the body, `400`.
9. Fields the user may not write, `403`.
10. The computed fields and the validations, `400`, then storage.
11. On create and update, the new record outside every `create` or `update`
    filter, `403`.

So a caller learns that a record exists only once they may read it. The field
and value checks need the parsed body, so they come after it. The new record
is checked as it is stored, inside the write transaction and after storage,
so its creator, sequence and computed fields are set. When it is outside the
filters, the write is rolled back and nothing is stored. On update, the
`version` in the body must be the version the checks in steps 5 and 6 saw,
otherwise the answer is the stale-version `409`. See
[Authorization](../architecture.md#authorization).

- **`403` for the CSRF header.** A `POST`, `PUT`, `PATCH` or `DELETE`
  without `X-Axis-Csrf: 1`.
- **`401`.** A request without a session, or with a session from another
  tenant. It comes before the path, so it is a `401` even for an unknown
  application. The title is fixed.
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
  `/values/<collection>/<index>/<field>`. The field is found by matching the
  violated constraint against the names the model declares.
- **`409` for a stale version.** An update whose `version` is not the stored
  one.
- **`409` for a referenced record.** A delete of a record that another record
  references. Any foreign-key violation on delete maps to it. The title is
  fixed, and the response never names the referencing entity, table or
  constraint.
- **`409` for a schema conflict.** The table has a constraint the active model
  does not declare, such as a `NOT NULL` column left by an activation that
  failed after provisioning committed. The title is fixed, and the response
  never names a table, column or constraint.
- **`500`.** An unexpected error on any path is caught by the exception
  handler. The response has no exception type, message or stack trace.

**No leaks.** No response body, including `title`, `detail` and the `errors`
messages, ever contains SQL, the `entities` schema, a table, column or
constraint name, an exception type or a stack trace. The `errors` keys repeat
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
- **Duplicates.** A property name repeated in the body or in `values` is an
  error at that property's pointer; the repeated value is not parsed.
- **Required fields.** On create, a required field that is missing or `null`
  is an error at `/values/<field>`. On update, missing fields are left
  untouched; `null` clears a field that is not required and is an error for a
  required one. `null` is SQL `NULL`.
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

**Errors** are a dictionary from RFC 6901 JSON Pointer into the body to its
messages, in ordinal key order, usable as is for a validation problem
response. The keys are `""` for the body, `/<property>` for a body property,
`/values`, `/values/<field>`, `/values/<collection>/<index>`,
`/values/<collection>/<index>/<field>` and `/version`. Pointers escape `~` as `~0` and
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
  next write.
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
   record is read first, with its rows. An unknown record is still a `404`,
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
   checked again as rows, but the owner's aggregates still read them. Any
   failure is a `400`.
4. Storage runs. It answers as described above, for example `409` for a stale
   `version`.

A failure is keyed `/values/<field>` or by a row path such as
`/values/lineItems/1/quantity`. Each key holds one message. When several
validations fail on one key, it reports the first in declaration order. Every
other key is reported in the same response. The message is the validation's
text key, and the client shows the text in the user's locale. Validation on
the client as the user types comes later
([D20](../decisions.md#d20-human-tasks-task-inbox-and-forms--agreed)).

A run-time error in an expression, or a computed value that does not fit its
field, such as one with more fraction digits than `scale`, rejects the write
with a `400` at that field's pointer. A run-time error in a computed field has
the message "Could not be computed.". A value that does not fit gets the
message a body value would get, such as "Must have at most 2 fraction
digits.". Values are never rounded. So on an entity with computed fields or
validations, a `decimal` value that the interpreter cannot hold exactly
(see [expression types](expressions.md#types)) is a `400` at its own
pointer, with the message "Cannot be evaluated exactly.", and no validation
of that record runs.

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

## Inbound endpoints

This section follows [D27](../decisions.md#d27-inbound-endpoints--agreed).
The bearer token check, its `401`, the CSRF exemption and the call are built.
The call creates the record and starts the process, with the order of checks,
the responses, `output` with `wait`, the audit records,
[idempotency](#idempotency) and the [status location](#status-location)
below. An
[`endpoint` resource](configuration.md#endpoint-resource) lets
another system create a record and start a process on it in one call. The
caller signs in with a bearer token, as
[Machine callers](../architecture.md#machine-callers) describes. Endpoints
are the one exception to the session and CSRF rules above.

### The call

The purchasing system submits a purchase request through the sample's
`SubmitPurchaseRequest` endpoint:

```http
POST /api/apps/PurchaseRequests/endpoints/SubmitPurchaseRequest
Authorization: Bearer eyJhbGciOiJSUzI1NiIs…
Content-Type: application/json
Idempotency-Key: po-import-20261012-0042

{
  "input": {
    "title": "Standing desks",
    "department": "0192f4a7-3c1d-7e2f-9a4b-5c6d7e8f9a0b",
    "currency": "VND",
    "lineItems": [
      { "description": "Desk", "quantity": 2, "unitPrice": 1250.45 }
    ]
  }
}
```

- **Address.** `{app}` and `{name}` match ignoring letter case, as `{app}`
  and `{entity}` do on the record routes.
- **Bearer token.** `Authorization: Bearer <token>` is required. A session
  cookie is ignored, so it neither signs the caller in nor causes an error.
- **No CSRF header.** The call needs no `X-Axis-Csrf` header. A browser
  cannot send a bearer token on its own, so a cross-site page cannot make the
  call for a signed-in user.
- **Content type.** `Content-Type: application/json`, by the
  [record API's rule](#create-update-and-delete).
- **Body.** `{ "input": { … } }`. `input` is parsed against the endpoint's
  input fields by the [record API's body rules](#request-bodies-and-values),
  with `input` in place of `values`. Any other body property is an error at
  `/<property>`.
- **`Idempotency-Key`.** The header is optional. See
  [Idempotency](#idempotency).
- **No other grants.** The caller needs the `invoke` grant on the endpoint.
  Entity and process grants are not checked, because the record create and
  the process start are part of the endpoint's contract.

### Order of checks

A call is checked in this order, and the first failure is the response:

1. The bearer token, `401` with `WWW-Authenticate: Bearer`. It comes before
   the path, so a caller without a valid token cannot learn which
   applications or endpoints exist.
2. The application and the endpoint, `404`.
3. The `invoke` grant on the endpoint, `403`.
4. The content type, `415`.
5. The body, `400` keyed `/input/…`.
6. The `Idempotency-Key` header, `400`. A value must be non-empty and at
   most 255 characters.

One transaction then does the rest:

1. With a key, it stores the receipt first. When the key is taken, it rolls
   back and answers as [Idempotency](#idempotency) describes.
2. It maps the input to values and creates the record through the record
   create command. So references, sequences, computed fields and validations
   apply, as in a [record API create](#create-update-and-delete). The
   creator field gets the caller's id. Errors are a `400` keyed
   `/values/<field>`, and a unique-value conflict is a `409`.
3. It starts the processes of the [data change triggers](processes.md#starts-from-triggers)
   on `create` for the entity, as `system`, in the order of the release. A
   skipped trigger start does not fail the call.
4. It evaluates the process's `startCondition` on the new record. A failure
   is a `400` keyed `/input`, with the condition's message text key.
5. It inserts the instance, pinned to the active release, with its first
   work item, as the [process start](processes.md#start-endpoint) does. An
   instance already running for the record is a `409`.
6. It writes the audit records `record.created` and `process.started`.

When any part fails, nothing is written. After the commit, an endpoint with
`wait` checks the instance's state until it completes or `wait` runs out.

- **No connection held.** The call holds no transaction and no database
  connection while it waits. It reads the state every 200 ms, each time over
  a pooled connection that it returns at once.
- **Failure ends the wait.** A failed instance never completes, so the call
  stops waiting as soon as it reads `failed`.

- **Actor.** The actor of both audit records is the caller's id, from the
  token's user id claim (`sub`). The display name is stored with them, see
  [Machine callers](../architecture.md#machine-callers).
- **Details.** Both details add `"endpoint": "<name>"`. So
  `record.created` holds `{ "version": 1, "endpoint": "SubmitPurchaseRequest" }`,
  and `process.started` holds
  `{ "processId", "releaseId", "endpoint": "SubmitPurchaseRequest" }`.

### Responses

Errors are problem details (`application/problem+json`), and their titles are
fixed, as in [Errors](#errors).

**`201` without output.** The record and the instance are stored. An
endpoint without `output` answers at once, and the instance is still
`running`:

```json
{
  "recordId": "0192f5b1-7a2c-7d3e-8f4a-5b6c7d8e9f01",
  "instanceId": "0192f5b1-7a40-7b1c-9d2e-3f4a5b6c7d8e",
  "state": "running"
}
```

**`201` with output.** The endpoint declares `output` and `wait`, and the
instance completed within `wait`. The fixture's `ImportClosedNote` returns
the note's `code` and `status`:

```json
{
  "recordId": "0192f5c0-1b2c-7d3e-8f4a-5b6c7d8e9f02",
  "instanceId": "0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f",
  "state": "completed",
  "output": { "code": "N-1001", "status": "closed" }
}
```

**`202`.** The endpoint declares `output` and `wait`, and the instance did
not complete within `wait`. The body has no `output`, and `Location` names
the status location. `Location` uses the declared names of the application
and the endpoint, not the letter case of the request. `state` is `running`
or `waiting`, or `failed` when the instance failed:

```http
HTTP/1.1 202 Accepted
Location: /api/apps/E2eApp/endpoints/ImportClosedNote/instances/0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f
Content-Type: application/json

{
  "recordId": "0192f5c0-1b2c-7d3e-8f4a-5b6c7d8e9f02",
  "instanceId": "0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f",
  "state": "running"
}
```

**`400`.** The input does not fit the schema, the record create refuses the
values, or the start condition fails. Nothing is written.

- Input that does not fit the endpoint's input fields is keyed
  `/input/<name>`, or `/input/<collection>/<index>/<field>` for a row.
- A `reference` value that names no record, and a failed validation, are
  keyed `/values/<field>`, or by a row path such as
  `/values/lineItems/0/quantity`. The mapping from input to fields need not
  be one-to-one, so these errors point at the record's own fields.
- A start condition that fails is keyed `/input`.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "/input/title": ["Must be at most 200 characters."],
    "/input/lineItems/0/quantity": ["Must be an integral number within the signed 64-bit range."]
  }
}
```

**`401`.** No `Authorization` header, a token that is not a bearer token,
or a token whose signature, issuer, audience or expiry fails. A tenant
without `Oidc:EndpointAudience` answers `401` to every call. The response
carries `WWW-Authenticate: Bearer`, and it never says which check failed:

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "A valid bearer token is needed to call this endpoint.",
  "status": 401
}
```

**`403`.** The caller has no `invoke` grant on the endpoint. Only roles
claim values that name a declared role of the application count. The title
is the record API's:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "You have no grant for this action.",
  "status": 403
}
```

- **`404`.** An unknown application, an application with no active release,
  or an unknown endpoint.
- **`409`.** A value repeats a `unique` field of another record, keyed
  `/values/<field>` as in the record API. Or an instance of the process
  already runs for the new record.
- **`415`.** A body without `Content-Type: application/json`.
- **`422`.** The `Idempotency-Key` was used before by the same caller on the
  same endpoint with a different input.

### Idempotency

A key is unique per endpoint and caller. Two callers can use the same key
without meeting.

- **Repeat.** A repeat with the same key and the same input returns the same
  record and instance. It creates no record and starts no instance.
- **Never waits.** A repeat follows the instance's state at that moment. For
  an endpoint with `output`, it is a `201` with output when the instance has
  completed, otherwise a `202` with the status location. For an endpoint
  without `output`, it is the `201` the first call gave, with the current
  state.
- **Different input.** The same key with a different input is a `422`, and
  nothing is written. The receipt stores a SHA-256 hash of the input, not a
  copy of it. The same JSON value counts as the same input, whatever its
  whitespace and property order. A number written another way, such as `1`
  and `1.0`, is a different input.
- **Only stored on success.** A call that fails stores no receipt. It can be
  retried with the same key after the input is fixed.
- **Concurrent repeats.** They are serialized by the receipt's unique key, so
  only one of them creates a record.

A repeat of the `202` call above, after the worker closed the note:

```json
{
  "recordId": "0192f5c0-1b2c-7d3e-8f4a-5b6c7d8e9f02",
  "instanceId": "0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f",
  "state": "completed",
  "output": { "code": "N-1001", "status": "closed" }
}
```

A repeat of the same key with a different input:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "This idempotency key was used with a different input.",
  "status": 422
}
```

### Status location

`GET /api/apps/{app}/endpoints/{name}/instances/{instanceId}` reads the
state of an instance that an endpoint call started.

- **Checks.** It needs a bearer token and the `invoke` grant on the
  endpoint, checked in the same order as the call: `401`, then `404` for the
  path, then `403`.
- **Own calls only.** Only the caller who made the call can read it. Another
  caller's instance, an instance of another endpoint, an unknown one and an
  `{instanceId}` that is not a UUID are all `404`, titled "No instance of
  this endpoint exists with this id.". So one system cannot read another
  system's output. The caller and the endpoint come from the instance's
  `process.started` audit record.
- **`200`.** The body is the call's body: `recordId`, `instanceId` and
  `state`. `output` is there only when the instance is `completed`.
- **Output as contract.** The output values are read as the endpoint's
  contract, outside field grants, as the call's own `201` does.

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
  [session](../architecture.md#sign-in-and-the-session). Every write needs a
  session, so the record API never writes `anonymous`. The audit record also
  stores the user's display name as it was at that moment. A process
  [operation step](processes.md#running-a-step) that updates its subject
  record writes `record.updated` with the actor `system`, no display name and
  its instance id. A [task completion](processes.md#completing-a-task) that
  writes values writes no `record.updated`. Its `task.completed` record names
  the fields. An [endpoint call](#inbound-endpoints) writes `record.created`
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
    each step and each [task decision](processes.md#completing-a-task) on it,
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
      "details": { "version": 2, "fields": ["title"] }
    },
    {
      "id": "01926b3a-1c20-7b4d-8e6f-7a8b9c0d1e2f",
      "occurredAt": "2026-10-06T02:00:00.123456Z",
      "actor": "anna",
      "actorName": "Anna Employee",
      "action": "record.created",
      "processInstanceId": null,
      "details": { "version": 1 }
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 2
}
```
