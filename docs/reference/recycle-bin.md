# Recycle bin

_(planned for M41)_

A deleted record waits in the recycle bin for a time, so a wrong delete is undone in a minute instead
of by a restore of the whole tenant, as
[D158](../decisions/d158-history-keeps-old-and-new-values-a-deleted-record-waits-in-a-recycle-bin-and-every-record-has-system-fields.md)
agrees. This page covers deleting into the bin, the period, restore and purge. The routes of the
record API are in [record-api.md](record-api.md), and the history of a record is in
[record-audit.md](record-audit.md). People restore and purge records on the
[recycle bin page](platform-pages.md#recycle-bin-page).

| Route | Response |
| --- | --- |
| `GET /api/tenant/recycle-bin?page=&pageSize=&sort=&filter=` _(planned for M41)_ | `200` with one page of the records in the bin that the caller may restore |
| `GET /api/tenant/recycle-bin/settings` _(planned for M41)_ | `200` with `{ "days": 30 }`, the period |
| `PUT /api/tenant/recycle-bin/settings` _(planned for M41)_ | `204`, with `{ "days": n }` as the body |
| `POST /api/apps/{app}/entities/{entity}/records/{id}/restore` _(planned for M41)_ | `200` with the restored record |
| `POST /api/apps/{app}/entities/{entity}/records/{id}/purge` _(planned for M41)_ | `204` with no body |
| `POST /api/apps/{app}/entities/{module}/{entity}/records/{id}/restore` _(planned for M41)_ | `200` with the restored record of a module entity, see [routes](record-api.md#routes) |
| `POST /api/apps/{app}/entities/{module}/{entity}/records/{id}/purge` _(planned for M41)_ | `204` for a module entity, see [routes](record-api.md#routes) |

## Deleting into the bin

_(planned for M41)_

A `DELETE` of the record API keeps the record. It marks the record deleted and leaves the record's
child rows, files and references where they are, so a restore needs no copy.

- **Marks.** The record gets `deleted_at` and `deleted_by`, see
  [physical names](storage.md#physical-names). The response is `204`, as before, and the audit record
  is `record.deleted`.
- **Shows nowhere else.** A record in the bin is not in a list, a read, a count, a label, a search, a
  data source, an export or an exact lookup, and it is not a target for a new reference. A read of it by
  id is a `404`. The bin list is the one place that shows it.
- **Refusals.** The refusals of a delete stay: a record that is the subject of a running or waiting
  process instance, a record that the lifecycle of a module contract does not let go, and a record that
  another record refers to. Only a record outside the bin counts as another record, so a record in the
  bin never blocks the delete of the record it refers to.
- **Unique values.** A unique value of a record in the bin is free, so a new record may take it, see
  [unique values and the bin](storage.md#indexes).
- **Triggers.** A delete starts the matching
  [delete triggers](process-events.md#delete-triggers) as before. A purge starts none.

## The period

_(planned for M41)_

A record stays in the bin for 30 days, or the period the tenant sets.

- **Setting.** An administrator with a browser session sets the period in days with `PUT`, from 1 to
  365. A value outside the range, or a body of another shape, is a `400`. It is stored as a tenant
  setting in the tenant directory, as the
  [application packages switch](user-administration.md#application-packages) is, see
  [storage](storage.md#platform-database). A bound keeps the bin from becoming permanent storage.
- **Audit.** Each accepted `PUT` writes a platform audit record, `tenant recycle-bin set`, in one
  transaction with the change, with `recycleBinDays` in `changed` and never a record, see
  [when a record is written](tenant-setting-commands.md#when-a-record-is-written). The period is
  not in the tenant database, so no audit record of the tenant's audit holds it.
- **Access.** `GET` and `PUT` of the setting are for an administrator, checked in this order as the
  application packages routes are: a signed-in user (`401`), `Users:AdministratorRole` (`403`), then a
  tenant with a row in the tenant directory that is not a preview tenant (`404`), and for `PUT` the
  body (`400`). A tenant with no directory row, under the configuration source, has no setting to
  read or change, so both routes answer `404` and its records keep the default of 30 days.
- **Expiry.** A record's period ends `days` after `deleted_at`. A new period applies to every record in
  the bin, also to one deleted before the change.
- **The job.** A job of the worker purges the records whose period has ended, in batches, as `system`,
  and writes `record.purged` for each. Batches keep one run from holding the database for long.

## Restore

_(planned for M41)_

A person who holds a `delete` grant on the record's entity, or an administrator, restores a record
with its child rows and its files.

- **Access.** A restore needs a session or a service account token, as the record API does. The caller
  needs a `delete` grant on the entity, or must be an administrator, or the answer is a `403`. A grant
  with a `filter` reaches only the records that match it. A record that is not in the bin, one that
  the grants do not reach, an unknown record and one of another tenant are each a `404`, so a caller
  learns that a record is in the bin only when they may restore it.
- **Refusals.** A restore is a `409` keyed `/values/<field>`, with the first field it finds, in
  two cases. Another record that is outside the bin now holds one of the record's unique values, or
  a record that the record refers to is in the bin. A refused restore changes nothing. The caller
  restores the record that it refers to first, or changes the other record.
- **A change.** A restore raises `version` by one and sets `updatedAt` and `updatedBy`, see
  [system fields](record-api.md#system-fields), so a reader of
  [what changed since a time](record-api.md#changes-since-a-time) finds the record again. It runs no
  default, sequence, computed-field or validation step again.
- **Audit.** A restore writes `record.restored` in its transaction.
- **Triggers.** A restore starts the [data change triggers](process-events.md#starts-from-triggers)
  that list `restore`, in the same transaction, as `system`, with the restored record as the subject.
  A create or an update trigger does not start on it, unless it lists `restore` too.

## Purge

_(planned for M41)_

A purge removes a record, its child rows and its files for good.

- **Who and when.** An administrator purges a record at any time with the purge route, and the worker
  purges it at the end of its period. A caller who is not an administrator, a service account included,
  gets a `403`, and a record that is not in the bin is a `404`.
- **Waits for referrers.** A record in the bin that refers to the record stops its purge. The route
  answers `409`, and the worker leaves the record for its next run. Records in the bin that refer only
  to each other are purged together.
- **History stays.** The audit records of the record stay, and the last of them is `record.purged`. The
  values in history are erased only by [the erasure path](record-audit.md#erasing-values-from-history).
- **Files.** The files of a purged record are removed by the sweep that removes deleted files, see
  [file clean-up](file-fields.md#storage-and-clean-up).

## The bin list

_(planned for M41)_

The bin list serves the recycle bin page, and another system that reads which records were deleted
since a time. It needs a session or a service account token.

- **Who sees what.** An administrator sees every record in the bin. Another caller sees a record of an
  entity on which they hold a `delete` grant, when the grant's filter reaches it.
- **Items.** An item has `id`, `application`, `entity`, `label`, `deletedAt`, `deletedBy` and
  `expiresAt`. `entity` is the entity name, `{module}/{entity}` for a module entity. `label` is the
  display value of the record when the caller may read it, else `null`.
- **Paging and sort.** `page` and `pageSize` follow the [paging rules](record-api.md#paging-and-sorting).
  `sort` is `deletedAt` or `-deletedAt`, and the order is oldest first, then by `id`.
- **Filter.** `filter` has the shape of the record API's filter and may name `application`, `entity`,
  `deletedAt` and `deletedBy`. Any other name is a `400` keyed `filter`. A filter on `deletedAt` with
  `gte` reads the records deleted since a time.
