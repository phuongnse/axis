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
  - Field values are never included, so the audit holds no sensitive data.

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
  filter is a `404`. The `fields` of `details`, in `record.updated` and `task.completed`, leave out
  the fields the user may not read.

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
