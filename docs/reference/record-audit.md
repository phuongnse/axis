# Record audit records and history

Every record write leaves an audit record, and the history route reads one record's audit records
back, so a person can see who changed a record and when. The routes, record shape, paging and
errors of the record API are in [record-api.md](record-api.md).

## Audit records and history

This section says which writes leave an audit record and what it holds. The
[history route](#the-history-route) reads them back.

- **Writes.** A create, update or delete writes one audit record in the write's transaction. A
  write that fails, such as one with a stale version, a missing reference, a unique violation or a
  delete of a referenced record, writes none. When the audit record cannot be written, the write
  rolls back and the response is a `500`. Seed records applied during activation write no audit
  record.
- **Actions.** The actions are `record.created`, `record.updated` and `record.deleted`.
- **Recycle bin actions** _(planned for M41)_. A delete writes `record.deleted` as above, and the
  [recycle bin](recycle-bin.md) adds `record.restored` and `record.purged`, each with the record's
  version in `details`.
- **Actor.** The actor is the user of the [session](security.md#sign-in-and-the-session). Every
  write needs a session, so the record API never writes `anonymous`, and the audit record stores no
  display name.
  - A process [operation step](process-execution.md#running-a-step) that updates its subject record
    writes `record.updated` with the actor `system` and its instance id.
  - A [task completion](task-api.md#completing-a-task) that writes values writes no
    `record.updated`, and its `task.completed` record names the fields.
  - An [endpoint call](inbound-endpoints.md) writes `record.created` with the caller as the actor.
- **Details.** `details` is `{ "version": n }` with the record's new version, and a delete holds the
  version the record had when it was deleted. An endpoint call's `record.created` also has
  `"endpoint"`, the endpoint's name.
  - On update it also has `"fields"`: the declared names of the fields and child collections the
    body sets, in declaration order. A field sent with its stored value is listed too, and computed
    fields are never listed.
- **Values** _(planned for M41)_. Each write also puts `changes` in `details`, the old and new
  value of what the write changed, see [values in history](#values-in-history).

## The history route

`GET /api/apps/{app}/entities/{entity}/records/{id}/history` returns the audit records of one
record, newest first, with the name of each actor and a link to the process instance behind an
entry when the user may inspect it.

- **Entries.** It lists every audit record that names the record. That covers the record API's
  writes, and the [process start](processes.md#start-endpoint), each step and each
  [task decision](task-api.md#completing-a-task), claim and release on it, including those whose
  actor is `system`.
- **Path.** The path follows the 404 rules of a single read, so an unknown record, a deleted record
  and an id from another tenant are each a `404`.
- **Paging.** `page` and `pageSize` follow the [paging rules](record-api.md#paging-and-sorting), and
  an invalid one is a `400` keyed by its name. There is no `sort` or `search`. The session, the path
  and the grant are checked first, then the paging, then whether the record exists.
- **Order.** Items are ordered by `occurredAt` descending, then by `id` descending. The audit
  records of one transaction share one time, so their order among themselves follows their ids,
  which may differ from the order they were written in.
- **Items.** Each item has its `id`, `occurredAt`, `actor`, `actorName`, `action`,
  `processInstanceId` and `details`, as stored. `actorName` is the display name in the actor's
  [user profile](notifications.md#user-profiles), else the name that an older audit record stored
  with it, else `null`. So it is `null` for a caller with no profile, such as a service account,
  and always `null` for `system` and `anonymous`.
- **Inspect path.** Each item also has `inspectPath`, its last property. It is the
  [instance detail](process-inspection.md#instance-detail) path of the entry's process instance,
  such as `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances/0192f4a9-…`, when the
  user holds an `inspect` grant on the entry's process. It is `null` when the user holds none, when
  the entry has no instance, and when the process is not in the active release. The server decides,
  so a client never guesses it from roles. The path leads to a `404` when the user may not read the
  instance's subject record.
- **Access.** The history needs a session and a `read` grant on the entity. Without a session it is
  a `401`, and without the grant a `403`, before the record is read. A record outside every read
  filter is a `404`.
- **Fields the user may not read** _(planned for M41)_. An entry whose change touches a field the
  user may not read still shows that the field changed, with no value. The `fields` of `details`, in
  `record.updated` and `task.completed`, keep such a field, and its entry in `changes` has no `old`
  and no `new`. The reason is that an auditor and a reader of the record both need to know that
  something changed, and a value is shown only to a person who may read its field.

For example:

```json
{
  "items": [
    {
      "id": "01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7d",
      "occurredAt": "2026-10-06T02:05:00.123456Z",
      "actor": "requester",
      "actorName": "Requester User",
      "action": "record.updated",
      "processInstanceId": null,
      "details": { "version": 2, "fields": ["title"] },
      "inspectPath": null
    },
    {
      "id": "01926b3a-1c20-7b4d-8e6f-7a8b9c0d1e2f",
      "occurredAt": "2026-10-06T02:00:00.123456Z",
      "actor": "requester",
      "actorName": "Requester User",
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

## Values in history

_(planned for M41)_

History keeps what each change was, so an auditor reads what a supplier's bank account was before
someone changed it, as [D158](../decisions/d158-history-keeps-old-and-new-values-a-deleted-record-waits-in-a-recycle-bin-and-every-record-has-system-fields.md)
agrees. The audit record is written in the transaction of the change, as for any other write, and the
record tables stay the source of truth.

`changes` in the `details` of `record.created`, `record.updated` and `record.deleted` is a list with
one entry for each field or child collection that the write changed, in declaration order. A field
that a write sends with its stored value is not an entry, and a computed field never is.

- **Field.** An entry is `{ "field", "old", "new" }`, with the declared name of the field. A create
  has only `new`, a delete only `old`, and an update both. An empty value is `null`.
- **Child rows.** An entry for a child collection is `{ "field", "rows" }`. Each row is
  `{ "id", "change", "changes" }`, with the row's id, `change` one of `added`, `changed` or
  `removed`, and `changes` the entries of the row's fields as above. A row that nobody changed is not
  listed.
- **References.** A `reference` value is `{ "id", "label" }`, with the display value of the target
  at the time of the change. The label never changes later, so history shows what the person saw. A
  `references` field is a list of them.
- **Files.** A `file` value is `{ "id", "name", "version" }`, the file, its name and the
  [version](file-fields.md#field-and-value) that the field held.
- **Sensitive values.** A value of a `sensitive` field is stored as ciphertext under the tenant's
  data key, in the format of [history values](data-encryption.md#sensitive-field-values). History
  keeps user ids and never names, as before.
- **Who sees a value.** A value shows only to a user who may read its field, by the same
  [grants](roles-and-policies.md#policies) as a read. The history route decrypts a sensitive value for
  such a user, and leaves `old` and `new` out for any other, see
  [fields the user may not read](#the-history-route).

For example, an update that changes a title, a department and one line item:

```json
{
  "version": 3,
  "fields": ["title", "department", "lineItems"],
  "changes": [
    { "field": "title", "old": "Desk", "new": "Chair" },
    {
      "field": "department",
      "old": { "id": "0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b", "label": "Finance" },
      "new": { "id": "3c4d5e6f-7a8b-4c9d-8e0f-1a2b3c4d5e6f", "label": "Sales" }
    },
    {
      "field": "lineItems",
      "rows": [
        {
          "id": "5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b",
          "change": "changed",
          "changes": [{ "field": "quantity", "old": 2, "new": 3 }]
        }
      ]
    }
  ]
}
```

## A record at a time

_(planned for M41)_

`GET /api/apps/{app}/entities/{entity}/records/{id}?at=<instant>` answers the record as it was at a
time, rebuilt from its history, so a person sees what a record looked like when a decision was made.
A module entity's route has the same query.

- **The query.** `at` is a UTC instant in the form of a `date-time` value, see
  [reading values](record-api.md#reading-values). A missing value keeps the route a plain read. An
  `at` that is repeated, is not an instant or is later than now is a `400` keyed `at`.
- **The answer.** It has the shape of a [record](record-api.md#record-shape), with the `version` and
  the `values` and `labels` that the history holds for that time, child rows included.
- **Today's rules.** The grants and filters are today's. The record must be one that the user's read
  filters reach now, and only the fields the user may read now are shown, so a record at a time never
  shows what a plain read would not. A record that was deleted, or did not exist yet at that time, is
  a `404`.
- **What is left out.** A field whose value at that time history does not hold is left out of
  `values`: a computed field, a field that changed before history kept values, and a field the
  user may not read.

## Erasing values from history

_(planned for M41)_

History has one path that changes an audit record, so that erasing a person's data, which
[M47](../roadmap.md#milestones) builds, can also clear their values from history. Nothing else
changes or removes an audit record.

- **The path.** A command of the server that acts for an administrator, and no route calls it. It
  writes `history.erased` in the transaction that clears the values, so the erasure is itself in the
  audit. Its `details` count the audit records it changed and hold no value.
- **What it clears.** It removes the person's values from the `old` and `new` of the `changes`
  entries and marks each such entry `"erased": true`, so a reader sees that a value was there. The
  field, the actor and the time stay.
- **A sensitive value.** The path removes the ciphertext from the entry. The tenant's data key is
  never dropped, because one key protects every value of the tenant, see
  [tenant keys](data-encryption.md#tenant-keys).
- **Storage.** The append-only triggers let this path through and nothing else, see
  [audit records](storage.md#audit-records).

## The tenant's audit

_(planned for M41)_

A tenant administrator searches and exports every audit record of the tenant: data changes, sign-ins,
administration, publishes and the commands of an operator. It reads the tenant database's
`axis.audit_records`. The platform audit records of `axis-admin` are in the platform database, which
a tenant cannot reach, so they are not in it. The [audit page](platform-pages.md#audit-page) draws it.

| Route | Response |
| --- | --- |
| `GET /api/tenant/audit-records?page=&pageSize=&sort=&filter=` _(planned for M41)_ | `200` with one page of audit records |
| `GET /api/tenant/audit-records/export?format=` _(planned for M41)_ | `200` with the file, see [export](import-export.md#export) |

- **Access.** The routes are for an administrator with a browser session, as the
  [tenant routes](user-administration.md#application-packages) are. Without a session it is a `401`,
  and for a user who is not an administrator a `403`, before anything is read.
- **Paging and sort.** `page` and `pageSize` follow the [paging rules](record-api.md#paging-and-sorting).
  `sort` is `occurredAt` or `-occurredAt`, and the order is newest first, then by `id` descending.
- **Filter.** `filter` is JSON in the shape of the record API's
  [filter](record-api.md#paging-and-sorting). It may name `actor`, `applicationId`, `entityId`,
  `recordId`, `action` and `occurredAt`, which finds records by person, application, entity, record,
  action and time. Any other name is a `400` keyed `filter`. An index serves the search, see
  [audit records](storage.md#audit-records).
- **Items.** An item is as in [the history route](#the-history-route), with `applicationId`,
  `entityId` and `recordId`, and with no `inspectPath`. Its `details` follow
  [values in history](#values-in-history): a value shows only when the administrator may read its
  field, so the audit shows no more than a record's history does.
- **Export.** The export takes `format` of `csv` or `xlsx`, runs the checks, the timeout, the row limit
  and the file rules of an [entity export](import-export.md#export), and writes the same filter's
  records in the same order. `details` is one text column of JSON. It writes `auditRecords.exported`
  after the file is complete, with the administrator as actor and `{ "format", "rowCount",
  "truncated" }` as `details`, and never a value.
