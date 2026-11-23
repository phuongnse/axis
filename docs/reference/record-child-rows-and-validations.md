# Record child rows, computed fields and validations

How a record's child rows, computed fields and validations work over the record API. The routes and bodies are in [the record API](record-api.md).

## Child rows, computed fields and validations

A record may hold child rows, computed fields and validations, which the
[record shape](record-api.md#record-shape) and the
[request bodies](record-api.md#request-bodies-and-values) include. Computed fields and validations
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
  row's references come with the rows, as the [record shape](record-api.md#record-shape) describes
  `labels`. A row that names a record that does not exist is a `400` at its cell,
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
  A single read, and the response of an update, return a pending
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

A write runs its computed fields and validations in this order:

1. Computed fields and validations run only on a body that parsed cleanly.
   A body with parse errors is answered with those errors alone.
2. On update of an entity with computed fields or validations, the stored
   record is read first, with its rows and sets. An unknown record is still a `404`,
   never a `400`.
   The server then builds the record as it will be stored: the stored values,
   the changes from the body, the
   [matched references](resource-files.md#matched-references) set from them, the rows it will
   store and the computed fields. A computed field that
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
4. Storage runs. It answers as [the record API](record-api.md) describes, for example `409` for
   a stale `version`.

A failure is keyed `/values/<field>` or by a row path such as
`/values/lineItems/1/quantity`. Each key holds one message. When several
validations fail on one key, it reports the first in declaration order. Every
other key is reported in the same response. The message is the validation's
text key, and the client shows the text in the user's locale. The client gets
validation as the user types from the [form state endpoint](record-form-state.md#form-state).

- **A validation of the whole record** _(planned, #5049)_. A validation with no `field` holds for the record. Its failure is
  keyed `/values`, and the form shows its message at the top of the form, above its fields.

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
