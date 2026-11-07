# Publish request storage

This page gives the tables of publish requests and the release history in each tenant database, and
the audit records of publishing. The other system tables are in [storage](storage.md#system-tables).

## Publish requests and release history

Studio publishes by storing a request that the [publisher](administration.md#publisher) runs, and
every activation adds a row to the release history, from which a release can be activated again.
`Axis.Configuration` owns both tables, in the `axis` schema of each tenant database. The server
inserts `validate`, `publish`, `activate` and `test` requests through the
[authoring API](authoring.md#publishing), and a preview is a `publish` request with the target
`preview`.

### Publish requests

`axis.publish_requests` holds one row per request. The server inserts a row over the runtime role,
and only the publisher changes a row, over the schema role. An index on
`(application_id, status, requested_at, id)` serves the claim.

| Column              | Type                | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ------------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                | `uuid`              | Primary key, a version 7 UUID                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `application_id`    | `uuid`              | The application `id`                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `kind`              | `text`              | `validate`, `publish`, `activate` or `test`, see [publishing](authoring.md#publishing) for `activate`                                                                                                                                                                                                                                                                                                                                                    |
| `target`            | `text`              | `tenant` or `preview`, by default `tenant`. See the preview target below                                                                                                                                                                                                                                                                                                                                                                                 |
| `status`            | `text`              | `queued`, `running`, `succeeded` or `failed`                                                                                                                                                                                                                                                                                                                                                                                                             |
| `snapshot`          | `jsonb`, null       | For `validate`, `publish` and `test`, an array of `{ file, version, content }`, one for every `*.json` file of the folder. Null for `activate`                                                                                                                                                                                                                                                                                                           |
| `release_id`        | `uuid`, null        | For `activate`, the release to activate. For `publish`, set when the release is stored                                                                                                                                                                                                                                                                                                                                                                   |
| `label`             | `text`, null        | The optional label, up to 100 characters                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `note`              | `text`, null        | The optional note, up to 2000 characters                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `requested_by`      | `text`              | The requesting user's id                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `requested_by_name` | `text`, null        | Null in every row that Axis writes, because a view reads the name from the user's profile                                                                                                                                                                                                                                                                                                                                                                |
| `requested_at`      | `timestamptz`       | When the request was stored                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `claimed_by`        | `text`, null        | The publisher that holds the claim                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `lease_until`       | `timestamptz`, null | When the claim runs out unless the publisher renews it                                                                                                                                                                                                                                                                                                                                                                                                   |
| `claims`            | `integer`           | How many times a publisher claimed the request, from 0                                                                                                                                                                                                                                                                                                                                                                                                   |
| `started_at`        | `timestamptz`, null | When it was first claimed                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `finished_at`       | `timestamptz`, null | When it reached `succeeded` or `failed`                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `diagnostics`       | `jsonb`, null       | The diagnostics, as an array                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `scenario_names`    | `jsonb`, null       | For `test`, the names of the scenarios to run, as an array of strings in the order sent. Null means every scenario, and it is null for every other kind. See [test](authoring.md#test)                                                                                                                                                                                                                                                                   |
| `scenario_results`  | `jsonb`, null       | For `test`, an array of `{ name, resourceId, file, outcome, durationMs, step, path, message }`, one for each scenario that ran, in file order, and an empty array when none ran. For `publish` and `validate`, the same form for each contract scenario that ran, and `{ name, file, outcome: "skipped", point }` for each one that a relax entry skipped, or null when none ran. Null for every other kind. See [the request](authoring.md#the-request) |

- **A snapshot.** `content` is the file's text as a JSON string, not an embedded object, so the file
  reads back exactly as the author saw it. `version` is the SHA-256 of the file's bytes, as in
  [versions](authoring.md#versions). A later edit never changes a stored snapshot, and the publisher
  compiles the snapshot from these texts alone.
- **Claim.** The publisher takes the oldest `queued` `publish`, `validate` or `activate` row of an
  application with `FOR UPDATE SKIP LOCKED`, sets `running`, `claimed_by` and `lease_until`, and adds
  1 to `claims`. It skips an application that has a `running` row. A `running` row whose
  `lease_until` has passed is claimed again the same way, and a publisher that lost its connection
  sets `lease_until` to now. A row with `claims` of 3 and an expired lease becomes `failed` with
  `AXC0215`.
- **Test requests.** A `test` row has its own queue for each application: at most one runs at a time,
  oldest first, and it does not wait behind a `publish`, `validate` or `activate` row. The claim of
  each queue skips an application only for a `running` row of the same queue. The row has the target
  `tenant`, no `release_id` and no audit record. See [test](authoring.md#test).
- **Preview target.** A row with `target` `preview` is stored in the session tenant's database and
  run against that tenant's preview tenant, see [preview](authoring.md#preview). Its history row
  lives in the preview tenant's `release_activations` and keeps this row's `id` as `request_id`,
  which points at a request in another database, so no foreign key can hold it. The publisher claims
  only a `publish` row with target `preview`, and never claims a `validate` or `activate` row with
  that target. It sets the row `failed` with `AXC0216` when the main tenant no longer has a preview
  tenant. While the preview tenant exists but the publisher does not serve it, the row stays
  `queued`.
- **Final.** A `succeeded` or `failed` row is never changed again.
- **Size and age.** No limit is set on the size of a snapshot, and nothing deletes old requests, so
  large folders or many requests grow the table.

### Release history

`axis.release_activations` holds one row per activation, and is append-only.

| Column           | Type          | Meaning                                                                                   |
| ---------------- | ------------- | ----------------------------------------------------------------------------------------- |
| `id`             | `uuid`        | Primary key, a version 7 UUID                                                             |
| `release_id`     | `uuid`        | The activated release                                                                     |
| `application_id` | `uuid`        | The application `id`                                                                      |
| `number`         | `integer`     | The release number: 1, 2, 3… per application                                              |
| `content_hash`   | `varchar(64)` | The release's content hash                                                                |
| `activated_at`   | `timestamptz` | When the activation committed                                                             |
| `actor`          | `text`        | A user id, `startup`, `operator`, or `unknown` for a backfilled row                       |
| `actor_name`     | `text`, null  | Null in every row that Axis writes, because a view reads the name from the user's profile |
| `label`          | `text`, null  | The label of the publish, or null                                                         |
| `note`           | `text`, null  | The note of the publish, or null                                                          |
| `request_id`     | `uuid`, null  | The publish request, or null for a startup and `axis-admin activate`                      |

- **Append only.** A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE` statement trigger
  raise SQLSTATE `23001`, as for `audit_records`. The runtime role has `SELECT` only.
- **Numbers.** A release gets its number the first time it is activated in the tenant. Every later
  row of that release holds the same number, and no two releases of one application share one. The
  activation takes the number in the transaction that holds the schema lock, so two activations
  cannot give one number to two releases. Numbers belong to one tenant, so the same content can have
  different numbers in two tenants.
- **No copies.** Activating the release that is already active adds no row and keeps the number, so
  a restart under `scripts/dev.sh` and a request that runs again add nothing.
- **Every path.** Startup activation, `axis-admin activate`, a publish and an `activate` request each
  add a row. The actor is the user for a publish and an `activate`, `startup` for a startup and
  `operator` for `axis-admin activate`. A publish passes its user, label, note and request to the
  activation, which stores them as given. An `activate` passes its user and its request, and no label
  and no note, and the release keeps the number it got the first time.
- **Backfill.** The migration that creates the table adds one row for each application's active
  release, with number 1, the actor `unknown`, the activation time of `axis.active_releases` and no
  request, so the live release has a number and a row to activate again. Releases that were not
  active then are not in the history, so no request can name them for activation.

### Refusals to activate a stored release

An `activate` request activates the stored release as it is and never compiles it again. The publisher
refuses it with one of these diagnostics, the request ends `failed`, and nothing is changed. Every
other refusal of activation applies too, such as `AXC0015` for a field that the release lacks and
the tables hold, because those checks protect stored data. So a stored release can be activated only
across releases that did not change the storage.

| Case                                                                                                                               | Code      |
| ---------------------------------------------------------------------------------------------------------------------------------- | --------- |
| A migration in [`axis.applied_migrations`](migrations.md#applied-migrations) was applied after that release and is missing from it | `AXC0174` |
| An extension package version that the release pins is not installed under `Extensions:Path`                                        | `AXC0146` |

The second check is the start check of [extension packages](extension-packages.md). Both codes have
the meaning they have at activation. See also
[activating a release](authoring.md#activating-a-release).

### Audit records of publishing

Each `publish` and each `activate` writes one record in `axis.audit_records`, in the transaction that
sets the request's final status. The actor is the requesting user, and `application_id` is the
application `id`. A `validate` writes no audit record, and neither does a preview request, whose
publisher the preview tenant's release history records.

| Action                   | When                   |
| ------------------------ | ---------------------- |
| `release.published`      | A `publish` succeeded  |
| `release.publishFailed`  | A `publish` failed     |
| `release.rolledBack`     | An `activate` succeeded |
| `release.rollbackFailed` | An `activate` failed    |

`details` is `{ requestId, releaseId, number }`. `releaseId` and `number` are null when the request
failed before a release was known. `number` is the release's number in the
[release history](#release-history), and it is also null when the release was never activated.
