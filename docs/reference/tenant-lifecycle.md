# Tenant lifecycle in a cell

Detailed reference for creating, exporting and deleting the tenants of a [cell](cells.md) through the
cell's provisioning API. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_. A self-hosted installation keeps the `Axis.Admin`
commands of [administration](administration.md). The conventions of
[architecture.md](../architecture.md) apply.

## Provisioning API

One API on the cell provisions its tenants. The [cell console](cell-console.md), the `Axis.Admin`
commands run against a cell and M33's control plane all call it, so each operation has one
implementation.

- **Where it answers.** Under `/api/cell/` on the cell console's host, `Cell:ConsoleHost`, and on
  the server's internal listener, `Cell:InternalUrl`, which only the cell's network reaches. A tenant
  host answers `404` for every path under `/api/cell/`.
- **Who may call.** A caller signs in through the cell's [operator realm](cell-console.md#sign-in):
  the cell console with the operator's session, and a machine caller, such as the control plane or a
  script, with a bearer token of a client of that realm. Every route needs the role
  `Cell:OperatorRole` (`403` without it). A tenant realm's token is refused with `401`. Every write
  through a session needs the CSRF header, as the other session routes do.
- **Jobs.** A route that changes a tenant answers `202` with a job: `{ "id", "kind", "tenantId",
  "state", "createdAt" }`. The [publisher](administration.md#publisher) runs the job with the same
  code as the `Axis.Admin` command, over the schema role. `GET /api/cell/jobs/{id}` answers the job,
  with `state` `queued`, `running`, `succeeded` or `failed`, its steps, and the error of a failed
  step. A failed job can be run again, and it skips the steps already done, as `tenant create` does.
- **Idempotency.** Each `POST` needs an `Idempotency-Key` header. The same key with the same body
  answers the first job again for 24 hours. The same key with another body answers `422`.
- **Audit.** Each job writes one platform audit record when it ends, with the operator or the
  machine caller as actor, the tenant and the job kind, and no secret. A refused call writes a log
  event, not a record.

| Route | What it does |
| --- | --- |
| `GET /api/cell/tenants` | Lists the tenants of the cell with their status, application, hosts, usage, quotas and failed work counts, see [views](cell-console.md#views). |
| `GET /api/cell/tenants/{id}` | Answers one tenant, with its usage history. |
| `POST /api/cell/tenants` | Creates a tenant, see [creating a tenant](#creating-a-tenant). |
| `POST /api/cell/tenants/{id}/suspend` | Suspends a tenant, as [`tenant suspend`](tenant-status-commands.md#tenant-suspend) does. |
| `POST /api/cell/tenants/{id}/resume` | Resumes a tenant, as [`tenant resume`](tenant-status-commands.md#tenant-resume) does. |
| `POST /api/cell/tenants/{id}/read-only` | _(planned for M33)_ Makes a tenant read-only, as [`tenant read-only`](tenant-status-commands.md#tenant-read-only) does. `resume` ends it. |
| `GET /api/cell/tenants/{id}/administrators` | _(planned for M33)_ Lists the accounts that hold the [administrator role](user-administration.md#the-administrator-role) in the tenant's realm: email, display name and language. The [control plane](control-plane.md) mails them. |
| `PUT /api/cell/tenants/{id}/quotas` | Sets or clears the tenant's quota rows, see [quotas](tenant-quotas.md#quotas). It applies at once, with no job. |
| `POST /api/cell/tenants/{id}/hosts` | Asks for a host of the tenant's own, see [hosts](#hosts). |
| `POST /api/cell/tenants/{id}/exports` | Exports the tenant, see [exporting a tenant](#exporting-a-tenant). |
| `GET /api/cell/tenants/{id}/exports/{exportId}` | Answers the export, and its download while it is kept. |
| `DELETE /api/cell/tenants/{id}` | Schedules the deletion, see [deleting a tenant](#deleting-a-tenant). |
| `POST /api/cell/tenants/{id}/deletion/cancel` | Cancels a deletion in its grace window. |
| `GET /api/cell/jobs/{id}` | Answers a job. |

## Creating a tenant

`POST /api/cell/tenants` takes `{ "id", "application", "administrator" }`, and _(planned for M33)_
an optional `quotas`, which the `tenant` step writes as the tenant's quota rows, so a tenant of the
[sign-up](sign-up.md#provisioning) never runs with the cell's defaults.

- `id` follows the rule of [`tenant create`](administration.md#tenant-create). An id that a tenant
  of the cell has, or that a deleted tenant had, answers `409`.
- `application` names a built-in application installed in the cell _(planned for M26)_. Until M26 it
  names an [application template](modules-and-templates.md#application-templates).
- `administrator` is the email of the person invited as the tenant's first administrator.
- A cell at its cap answers `409`, see [cell size](cells.md#cell-size).

The job's steps, in order:

1. `tenant`: create the tenant as `tenant create` does, with the host `<id>.<Cell:TenantDomain>`, its
   realm, its keys and its backup key, see [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell).
2. `application`: copy the application into the tenant's Studio folder,
   `<Studio:ApplicationsPath>/<tenant id>/`, as Studio creates one from a template.
3. `activate`: activate the application's first release in the tenant.
4. `invite`: invite the administrator, as `tenant create --administrator` does.
5. `resume`: set the tenant `active`.

## Hosts

`POST /api/cell/tenants/{id}/hosts` takes `{ "host" }` and answers the host's verification token and
the DNS records to add. The checks, the certificate and the rule that a host is never served before it
is verified are in [hosts and certificates](cells.md#hosts-and-certificates). A host already held by a
tenant of the cell, or held back after a deletion, answers `409`, and the message does not say which
tenant holds it.

## Exporting a tenant

An export gives a tenant's data to its owner in a form that any tool reads. It is not a backup: a
restore takes an [archive](tenant-backup-and-restore.md), never an export. It is built so that a
tenant administrator can run it in M33.

- **When.** For a tenant in any status but `provisioning`. One export of a tenant runs at a time, and
  it takes a lease of the tenant's `concurrentImports` quota.
- **Consistent.** The job reads the tenant database in one snapshot, as `tenant backup` does.
- **Kept.** The ZIP is stored in the cell's object storage, encrypted at rest, for 7 days, then
  deleted. The download answers it to a caller of the provisioning API, and to nobody else. Each
  download writes a platform audit record.
- **Sensitive fields.** The export holds their values in clear, because the data is the owner's. The
  ZIP is [restricted](keys-and-data-protection.md#restricted) data, and the console says so before
  the download.

## Exports by a tenant administrator

_(planned for M33)_ A tenant administrator exports their own tenant from the application, with the
same job and the same ZIP as the provisioning API, as
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md) agrees.

- **Who.** A person who holds the [administrator role](user-administration.md#the-administrator-role),
  on the users page's "Export data" section.
- **Routes.** `POST /api/tenant/exports` on the tenant's host starts it, and
  `GET /api/tenant/exports/{exportId}` answers it and its download, for 7 days. They are platform
  routes, in the session partition, with the CSRF header on the `POST`.
- **When.** While the tenant is `active` or `readOnly`, so a business whose trial ended or that
  stopped paying can still take its data, see [subscriptions](subscriptions.md#the-timeline). One
  export of a tenant runs at a time, whoever started it.
- **Warning.** Before it starts, the page says that the file holds every value in clear, sensitive
  ones included, and must be kept safe.
- **Audit.** The start and each download write a platform audit record with the administrator as
  actor.

**ZIP layout.**

| Entry | What it holds |
| --- | --- |
| `export.json` | `format` (`1`), `tenantId`, `createdAt`, `axisVersion`, and each application with its name, its active release number and its entities. |
| `applications/<application>/` | The application folder from the tenant's Studio folder, as Studio writes it, its `customizations/` included. |
| `data/<application>/<entity>.jsonl` | One JSON object per line for each record, in the shape of the [record API](record-api.md#record-shape), with child rows inside the record. |
| `data/<application>/<entity>.csv` | The same records in the CSV form of [export](import-export.md#export), one file per entity, child rows in a file of their own named `<entity>.<field>.csv`. |
| `files/<application>/<entity>/<record id>/<field>/<file name>` | Each stored file, under its record and field. |
| `users.csv` | The realm's accounts: user name, email, display name, roles, enabled, created. No credential. |

An entity name or a file name that is not a safe file name is written with its unsafe characters
replaced by `_`, and `export.json` lists the original name.

## Deleting a tenant

A deletion can be undone during its grace window, and not after.

1. **Schedule.** `DELETE /api/cell/tenants/{id}` suspends the tenant, as `tenant suspend` does, and
   records `delete_after`, 7 days later. Its hosts answer `503`. The tenant shows as
   `deleting` in the console. A deletion of a tenant that has a preview tenant deletes the preview too.
2. **Grace window.** `POST /api/cell/tenants/{id}/deletion/cancel` clears `delete_after`, and the
   tenant stays `suspended` until it is resumed. An export can still run in the window.
3. **Purge.** After `delete_after`, a job removes, in this order, and each step can run again:
   1. the tenant's hosts from the directory, its realm redirect addresses and its cert-manager
      certificates;
   2. the tenant database, then its schema and runtime roles;
   3. its realm in the identity provider;
   4. its files in object storage, when a storage binding holds them outside the database;
   5. its Studio folder;
   6. its data keys, lookup keys and backup key, see [crypto-shredding](tenant-backup-and-restore.md#backups-in-a-cell);
   7. its quota, lease, usage and counter rows;
   8. its directory entry, replaced by a tombstone in `platform.deleted_tenants`: the id, the time,
      the actor and the hosts, with no other data.
4. **After.** The id is never used again in the cell. Each host is held back for 90 days, so a new
   tenant does not receive the old tenant's links and emails. The platform audit records stay.

**What remains, and for how long.**

- The tenant's daily archives stay in object storage until their 30-day lock ends, but nothing can
  read them, because the backup key is gone.
- The managed PostgreSQL's point-in-time recovery holds the tenant database and the platform rows,
  wrapped keys included, for its retention of 7 days. A deletion is complete once that window has
  passed. The console shows the date.
