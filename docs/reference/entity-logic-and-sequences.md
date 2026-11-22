# Entity logic and sequences

This page holds an entity's logic and sequences, the application time zone and the fiscal year. The rest of a
resource file is in [resource files](resource-files.md).

## Entity logic

An entity may hold validations, computed fields and child collections, so that its own rules live
with its data. Expressions use the syntax of the [expression language](expressions.md). A validation
can call a named [rule](resource-files.md#rules), such as `IsPositive(quantity)` below, and both examples below
compile, given that rule. A module entity's validation calls the rules of its own module, see
[modules](modules-and-templates.md#modules).

```json
{
  "id": "0ec02f4a-bf31-4409-8eb9-bfb9c39b6756",
  "kind": "entity",
  "name": "PurchaseRequest",
  "formatVersion": 1,
  "label": { "textKey": "purchaseRequest.label" },
  "displayField": "title",
  "fields": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "lineItems", "type": "child-collection", "target": "LineItem" },
    {
      "name": "total",
      "type": "decimal",
      "precision": 18,
      "scale": 2,
      "expression": "sum(lineItems, amount)"
    }
  ],
  "validations": [
    {
      "name": "needsLineItem",
      "expression": "count(lineItems) >= 1",
      "message": { "textKey": "purchaseRequest.needsLineItem" },
      "field": "lineItems"
    }
  ]
}
```

```json
{
  "id": "b6c15e88-82e5-43e7-a589-c48ba8a56bc3",
  "kind": "entity",
  "name": "LineItem",
  "formatVersion": 1,
  "label": { "textKey": "lineItem.label" },
  "fields": [
    { "name": "description", "type": "text", "maxLength": 200 },
    { "name": "quantity", "type": "integer" },
    { "name": "unitPrice", "type": "decimal", "precision": 18, "scale": 2 },
    {
      "name": "amount",
      "type": "decimal",
      "precision": 18,
      "scale": 2,
      "expression": "quantity * unitPrice"
    }
  ],
  "validations": [
    {
      "name": "quantityPositive",
      "expression": "IsPositive(quantity)",
      "message": { "textKey": "lineItem.quantityPositive" },
      "field": "quantity"
    }
  ]
}
```

- **Validations.** `validations` is a list on the entity. Each entry has:
  - `name`, which is required, so a [customization layer](customization-layers.md#list-keys) can
    find the validation. It is a name like a field's, and it is unique in the entity's validations,
    ignoring letter case (`AXC0343`, at `/validations/{i}/name`). A missing `name` breaks the JSON
    Schema (`AXC0004`).
  - `expression`, which must be boolean.
  - `message`, a label that is checked like other labels (`AXC0028`). Its text key is the message of
    a failure.
  - **`field`** _(planned)_. It is optional. It names a field of the same entity, and with a field the message shows at
    that field. With none, the validation holds for the whole record, and its failure is reported at `/values`.

  The expression reads the entity's own fields by name, and can call named rules. It can also
  aggregate over the entity's child collections, such as `count(lineItems) >= 1`. An aggregate reads
  the rows as they will be stored, so `count(lineItems) >= 1` refuses a create without rows and an
  update that removes the last row. The server runs the validations on create and update, on the
  record as it will be stored and after computed fields are calculated. A validation fails when its
  condition is `false` or `null`, or stops with a run-time error. The validations of a child entity
  run on each row that the body sends. See
  [record child rows](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations).

- **Computed fields.** A field with an `expression` is a computed field.
  - The expression type must match the field's type, under the
    [result type rule](expressions.md#types).
  - The type must be a scalar type. A `reference` or a `child-collection` cannot be computed.
  - `required` is not allowed. `unique`, `maxLength`, `precision`, `scale` and `values` keep their
    usual meaning, because they describe the column that stores the value.
  - It reads only the record's own fields and its child rows, never a reference path. It reads the
    entity's own fields that are not computed. Naming a computed field, itself included, is
    `AXC0046`.
  - It cannot call `now()`, `today()` or `toDate`, because its stored value would go stale. A call,
    also inside an aggregate's item expression, is `AXC0086`.
  - It cannot call a rule (`AXC0050`).
  - It reads child rows only through an [aggregate](expressions.md#aggregates), such as
    `sum(lineItems, amount)`. Inside the item expression, names are the child row's fields, computed
    ones included, and nothing else.
  - The rows of a child collection are computed before the owner, so the owner's aggregates read the
    rows' computed values. An update that leaves a collection out aggregates its stored rows.
  - Clients cannot write it. A body that sets it, even to `null`, is a `400` at its pointer with the
    message "Cannot be set.". A run-time error is a `400` at its pointer with "Could not be
    computed.". A value that does not fit the column gets the message that a body value would get.
    See [record child rows](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations) and
    [child tables and computed columns](child-tables-and-computed-columns.md#child-tables-and-computed-columns).
- **Child collections.** The field type `child-collection` needs a `target` that names the child
  entity. Its rows are read and written only through the owner record, as
  [record child rows](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations) describes.
- **Limits on a child entity.** A child entity:
  - is owned by exactly one `child-collection` field (`AXC0039`);
  - is never the `target` of a `reference` or `references` field (`AXC0040`);
  - is never the `entity` of a data source (`AXC0064`) or of a process (`AXC0068`);
  - has no record routes of its own;
  - has no `child-collection` or `references` fields (`AXC0041`). It may have `reference` fields, as
    real line items point to a product. A reference from a child row gets a foreign key, and its
    labels follow the rows, see
    [child rows](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations). Its index comes with the
    platform indexes on every reference column.

## Sequences

A `sequence` resource hands out business numbers, such as `PR-2026-00042`. The compiler checks
sequences and the fields that name them, and the record create fills a field that names a sequence
with the next number.

```json
{
  "id": "5d2f8a61-3c4b-4e7d-9a0f-6b1c2d3e4f09",
  "kind": "sequence",
  "name": "PurchaseRequestNumber",
  "formatVersion": 1,
  "format": "PR-{yyyy}-{n:5}"
}
```

- **Tokens.** `{yyyy}` is the four-digit year of the create's transaction time in the
  [application time zone](#application-time-zone), which is `UTC` when the application names none.
  `{n}` is the number, and `{n:k}` is the number zero-padded to `k` digits, with `k` from 1 to 18.
  Every other character is literal.
- **One number.** The format holds exactly one `{n}` or `{n:k}` token. It may hold `{yyyy}`, even
  more than once, and no other braces. Any other format is `AXC0004` at `/format`.
- **Yearly restart.** The counter restarts each year in the
  [application time zone](#application-time-zone), but only when the format holds `{yyyy}`, because
  without a year a restart would hand out the same number twice. In `Asia/Ho_Chi_Minh`, a record
  created at 18:00 UTC on 31 December gets the first number of the next year.

An entity `text` field names the sequence in its `sequence` property:

```json
{
  "id": "0ec02f4a-bf31-4409-8eb9-bfb9c39b6756",
  "kind": "entity",
  "name": "PurchaseRequest",
  "formatVersion": 1,
  "label": { "textKey": "purchaseRequest.label" },
  "displayField": "title",
  "fields": [
    { "name": "number", "type": "text", "maxLength": 20, "unique": true, "sequence": "PurchaseRequestNumber" },
    { "name": "title", "type": "text", "required": true, "maxLength": 200 }
  ]
}
```

- `sequence` names a loaded `sequence` resource, ignoring letter case.
- The field gets its number on create, in the create's transaction, and clients cannot write it. See
  [the record API](record-api.md#create-update-and-delete) and
  [storage](audit-records-and-sequence-counters.md#audit-records-and-sequence-counters).
- A field with `sequence` cannot be `required` or have an `expression`, because the server always
  assigns its value. A child entity's field cannot have a `sequence` either. Each of these is
  `AXC0013`.

## Application time zone

The `application` manifest may name the application's time zone, which decides the calendar date of
an instant wherever Axis needs one:

```json
{
  "id": "6ec71db1-5b21-43d9-95d2-dcc45055eb0e",
  "kind": "application",
  "name": "ServiceDesk",
  "formatVersion": 1,
  "timeZone": "Asia/Ho_Chi_Minh"
}
```

- **`timeZone`** is optional. It is an IANA time zone id, and without it the time zone is `UTC`. An
  id is known when .NET finds it as an IANA id written with its exact letter case, as for a schedule
  trigger. An unknown id is `AXC0277` at `/timeZone`. Studio's application form offers `timeZone`.
- **Part of the release.** A change applies to work that runs on the new release.
- **What uses it.** [`today()` and `toDate`](expressions.md#functions), a sequence's
  [`{yyyy}`](#sequences) and its yearly restart, the dates in
  [notifications](notifications.md#placeholders) and the date buckets of data sources.
- **What does not.** `now()` stays an instant. A schedule trigger keeps its own `timeZone`. The SPA
  shows a `date-time` in the browser's time zone. Calendar durations such as `P1M` stay `AXC0084`.
- **Due times** _(planned for M39)_. A due time counted on a calendar shows in that calendar's time zone,
  see [durations in working time](working-time.md#durations-in-working-time).

## Fiscal year

_(planned for M39)_

The `application` manifest may name the month in which the application's fiscal year starts, so that a
[data source](data-sources.md#aggregates) can total by fiscal period:

```json
{
  "id": "6ec71db1-5b21-43d9-95d2-dcc45055eb0e",
  "kind": "application",
  "name": "ServiceDesk",
  "formatVersion": 1,
  "timeZone": "Asia/Ho_Chi_Minh",
  "fiscalYearStart": 4
}
```

- **`fiscalYearStart`.** It is optional. It is a month from 1 to 12, and without it the fiscal year starts
  in January. A value that is not a whole number from 1 to 12 is refused by the compiler.
- **Part of the release.** A change applies to work that runs on the new release.
- **What uses it.** The `fiscalQuarter` and `fiscalYear` buckets of
  [aggregates](data-sources.md#aggregates).
