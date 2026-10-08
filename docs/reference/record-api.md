# Record API

Detailed reference for the record API: routes, record shape, paging, writes,
concurrency, errors and request bodies. The conventions of
[architecture.md](../architecture.md) apply: sections marked
*(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Records of an entity are read and written through the record API. It serves
the entities of the active release of an application, in the tenant that the
request host resolves to. A record of another tenant does not exist for the
request, so it is a 404 like any unknown record.

The endpoints have no authorization yet. Policies are checked on every record
endpoint from M4 (see [Authentication and authorization](../architecture.md#authentication-and-authorization)).

## Routes

| Method and path | Response |
| --- | --- |
| `GET /api/apps/{app}/entities/{entity}/records?page=&pageSize=&sort=` | `200` with one page of records |
| `GET /api/apps/{app}/entities/{entity}/records/{id}` | `200` with one record |
| `POST /api/apps/{app}/entities/{entity}/records` | `201` with the new record and a `Location` header |
| `PATCH /api/apps/{app}/entities/{entity}/records/{id}` | `200` with the updated record |
| `DELETE /api/apps/{app}/entities/{entity}/records/{id}` | `204` with no body |

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
    "department": "0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b"
  },
  "labels": {
    "department": "Finance"
  }
}
```

`values` holds every declared field of the entity under its declared name, in
declaration order. A field that is SQL `NULL` is `null`; it is never left out.
List responses leave child collections out *(planned for M2)*. See
[Child rows, computed fields and validations](#child-rows-computed-fields-and-validations).

`labels` maps each `reference` field that is not `null` to the display field
value of the referenced record, so a page can show "Finance" without one more
request per row. It is always present, and it is `{}` when the entity has no
reference or every reference is `null`. A `null` reference has no entry.
`values` keeps the record id of each reference, so a record read and sent back
is unchanged.

The labels come from the same SQL statement as the records, through one left
join per reference field to the target table. They are never read with one
query per row. Create and update return their labels from the same statement
as the write: the write is a data-modifying `WITH` clause, and the labels are
selected from it with the same joins, because `RETURNING` cannot join. Labels
are not filtered by access yet. From M4 the server decides whether a caller may
see the label of a record it cannot read.

A list response is `{ "items": [ ... ], "page": 1, "pageSize": 20,
"totalCount": 42 }`. `items` holds records in the shape above, including
`labels`. `page` and `pageSize` are the values used, and `totalCount` counts
every record of the entity.

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
| `child-collection` *(planned for M2)* | An array of row objects. See [Child rows](#child-rows-computed-fields-and-validations) |

## Paging and sorting

- **`page`.** An integer of at least 1. It defaults to 1.
- **`pageSize`.** An integer from 1 to 100. It defaults to 20.
- **`sort`.** A declared field name, or `-` and the name for descending
  order. The name matches exactly, so letter case matters. A
  `child-collection` field is not a valid `sort` *(planned for M2)*.
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

The parameters are digits only: a sign, a space or a repeated parameter
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
- **Delete.** `DELETE` removes the record by id only. It needs no body and no
  version. The response is `204` with no body. A record that another record
  references through a `reference` field is kept, and the response is `409`.
- **Content type.** Both need `Content-Type: application/json`. The media
  type matches ignoring letter case. The only allowed parameter is `charset`
  with the value `utf-8`, in any letter case. A cross-site page can send a
  `text/plain` or form body without a CORS preflight, so accepting it would
  open a CSRF path once M4 adds the session cookie. SameSite cookies and the
  M4 CSRF protection stay the main defence.
- **References.** Before the write, each non-null `reference` value is looked
  up in the target table by id. The check and the write share the request's
  connection without a transaction. The foreign key is the backstop: a target
  removed in between is a foreign-key violation that maps to the same error.

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
before its body is read. Validations *(planned for M2)* are a step between the
body and storage: see
[Child rows, computed fields and validations](#child-rows-computed-fields-and-validations). A delete has no body, so it is checked for the path
and then in storage (`404` or `409`).

- **`404`.** An unknown application, an application with no active release,
  an unknown entity, an unknown record and an `{id}` that is not a UUID in the
  hyphenated form. The path is resolved before the query is checked, so a
  path that names nothing is a 404 whatever its query.
- **`400`.** An invalid `page`, `pageSize` or `sort` is a validation problem
  whose `errors` is keyed `page`, `pageSize` and `sort`. Every invalid
  parameter is reported in the same response.
- **`400` for a body.** A body the parser rejects (see "Request bodies and
  values") is a validation problem whose `errors` is keyed by JSON Pointer. A
  `reference` value that names no record of the target entity is an error at
  `/values/<field>`.
- **`415`.** A `POST` or `PATCH` without `Content-Type` or with any type other
  than `application/json` with an optional `utf-8` charset.
- **`409` for a unique value.** A value that repeats the value of a `unique`
  field in another record is a validation problem with status `409`, whose
  `errors` is keyed `/values/<field>`. The field is found by matching the
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
`/values/lineItems/1/quantity` *(planned for M2)*. The integration tests check every error response of the record API for
this.

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
`/values`, `/values/<field>` and `/version`. Pointers escape `~` as `~0` and
`/` as `~1`; declared names never need it, but unknown names can. Each key has
one fixed English message for the first problem found there, except that the
message of a failed validation is its text key *(planned for M2)*, such as
"Unknown property." or "Must be at most 200 characters.". Messages may use
field type names and limits from the model. They never contain a table,
column, constraint or schema name, a PostgreSQL type name, exception text, or
text from the request; the key already says where the problem is.

## Child rows, computed fields and validations

Everything in this section is *(planned for M2)* (D17). It changes the record
shape, the bodies and the order of checks described above.

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
  array index is the position. Computed values are included.
- **Reads.** A single read and the response of a create or update include the
  collections. List responses leave them out.
- **Create.** A missing collection means no rows. `null` is an error at
  `/values/<collection>`.
- **Update.** A missing collection keeps its rows. An array replaces all the
  rows, in the same transaction, and increments the owner's `version`. `null`
  is an error at `/values/<collection>`.
- **Parsing.** A row is parsed like a create body, so a required field of the
  child entity must be present in every row. An array element that is not an
  object is an error at its index. A row error is keyed
  `/values/<collection>/<index>/<field>`, with a zero-based index.
- **Computed fields.** A body that sets a computed field, of the owner or of a
  row, is an error at its pointer. The server calculates the value on every
  write of the record. Child rows are calculated before the owner.
- **Delete.** Deleting the owner deletes its rows.

**Validations.** The order for a write is:

1. Validations run only on a body that parsed cleanly. A body with parse
   errors is answered with those errors alone.
2. On update, the stored record is read first. An unknown record is still a
   `404`, never a validation `400`. The server then builds the record as it
   will be stored: the stored values, the changes from the body and the
   computed fields.
3. The validations of the entity and of each row run. Any failure is a `400`.
4. Storage runs. It answers as described above, for example `409` for a stale
   `version`.

A failure is keyed `/values/<field>` or by a row path such as
`/values/lineItems/1/quantity`. Each key holds one message. When several
validations fail on one key, it reports the first in declaration order. Every
other key is reported in the same response. The message is the validation's
text key, and the client shows the text in the user's locale. Validation on
the client as the user types comes in M3.

A run-time error in an expression, or a computed value that does not fit its
field, such as one with more fraction digits than `scale`, rejects the write
with a `400` at that field's pointer. Values are never rounded.

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
