# Form state endpoint

The form state endpoint is the one route of the record API that writes nothing: it answers for a
draft. The other routes, the record shape and the errors are in [record-api.md](record-api.md).

## Form state

The form state endpoint answers what a form shows for a draft record, and which messages the
user's input earns so far, and writes nothing. The conditions are in
[Form conditions and steps](form-conditions.md#form-conditions-and-steps).

`POST /api/apps/{app}/forms/{form}/state` needs `Content-Type: application/json`
and the [CSRF header](record-api.md#create-update-and-delete). `{form}` is a form name in
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
  [value format](record-api.md#request-bodies-and-values). Child rows and their keys work
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
  are in the [value format](record-api.md#request-bodies-and-values), so a date-time is UTC
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

The problem details are as in [Errors](record-api.md#errors).
