# Tenant lifecycle in a cell

Detailed reference for creating, exporting and deleting the tenants of a [cell](cells.md) through the
cell's provisioning API. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_, except the two read routes, the quotas route, the suspend and resume routes, the routes that schedule and cancel a deletion, the create route that queues its job, the exports route that queues its job, the route that requests a host of a tenant's own, and the job read of the
[provisioning API](#provisioning-api), the job runner for suspend, resume, create, export, delete and cancel-deletion described under it, and the console's sign-in and sign-out, which answer in a cell that has a console or internal host. A
self-hosted installation keeps the `Axis.Admin` commands of [administration](administration.md). The conventions of
[architecture.md](../architecture.md) apply.

## Provisioning API

One API on the cell provisions its tenants. The [cell console](cell-console.md), the `Axis.Admin`
commands run against a cell and M33's control plane all call it, so each operation has one
implementation.

- **Where it answers.** Under `/api/cell/` on the cell console's host, `Cell:ConsoleHost`, and on
  the host of the server's internal listener, `Cell:InternalUrl`, which only the cell's network
  reaches. Both are platform hosts: they resolve no tenant, the health probes still answer there, and
  every path outside `/api/cell/` answers `404`, the page at `/` included. A tenant host answers `404`
  for every path under `/api/cell/`, before any sign-in work. A self-hosted installation has no
  platform host, so every `/api/cell/` path answers `404`. The server opens no separate internal
  listener yet, and `Cell:ConsoleAllowedNetworks` is not enforced yet _(planned for M31)_.
- **Who may call.** A caller signs in through the cell's [operator realm](cell-console.md#sign-in):
  the cell console with the operator's session, and a machine caller, such as the
  control plane or a script, with a bearer token of a client of that realm. The server finds the realm at
  `IdentityProvider:Url` plus `realms/<Cell:OperatorRealm>`, and in a cell with a console or internal
  host it stops at start without that setting. It checks the token's issuer, signature, expiry and type,
  and no audience, because the realm serves only the cell. It reads the caller's roles from
  the `realm_access.roles` claim, where Keycloak puts realm roles. Every route but sign-in and sign-out
  needs the role `Cell:OperatorRole` (`403` without it). A missing token, and a tenant realm's token, are refused with
  `401`. A request with a bearer token uses the token and never reads the session cookie. A session
  counts only on the console host, and a tenant's session never counts there. Every write through a session
  needs the CSRF header, as the other session routes do. A write with a bearer token needs none, because a browser cannot
  attach a bearer token on its own.
- **Jobs.** A route that changes a tenant answers `202` with a job: `{ "id", "kind", "tenantId",
  "state", "createdAt" }`. The [publisher](administration.md#publisher) runs the job with the same
  code as the `Axis.Admin` command, over the schema role. `GET /api/cell/jobs/{id}` answers the job,
  with `state` `queued`, `running`, `succeeded` or `failed`, its steps, and the error of a failed
  step. A failed job can be run again, and it skips the steps already done, as `tenant create` does.
  The `platform.jobs` table and the runner in the publisher are built for the `suspend`, `resume`, `delete` and `cancel-deletion` kinds. The first two have one
  `status` step each, `delete` has one `schedule` step and `cancel-deletion` has one `cancel` step. A step that throws ends the claim, and the job runs again in the next round, up to three claims. A step that
  already succeeded is skipped, and a refusal, such as for a tenant that is still `provisioning`, fails the job at once.
  The `suspend`, `resume`, `DELETE` and `deletion/cancel` routes and the job read are built: the server only queues the job, over the platform reader role,
  which may insert into `platform.jobs` and nothing else there, and answers `202` with a `Location` header of `/api/cell/jobs/{id}`.
  The job's actor is the operator's id, the subject of the token or the session. The create route is built too: it queues a `create` job with the five steps of
  [creating a tenant](#creating-a-tenant), all `pending`, and keeps the request's `application` and `administrator` in the job's
  `input`, which the job read never returns. The publisher runs the `create` job in a cell with the five steps, see the
  [create job](administration.md#create-job). The exports route is built too: it queues an `export` job with the one step `export`, see
  [exporting a tenant](#exporting-a-tenant). The routes for the other kinds and the run again of a failed job are _(planned for M31)_.
- **Idempotency.** Each `POST` that queues a job needs an `Idempotency-Key` header of 1 to 255 characters, and the answer is `400`
  without one, with no job queued. The same key with the same request answers the first job again for 24 hours. The request is the kind,
  the tenant and the body, hashed with SHA-256, so the body must match byte for byte. The same key with another request answers `422`.
  A key is shared across the cell, not kept for each caller. A lock on the key keeps two parallel retries from queuing two jobs.
- **Audit.** Each job writes one platform audit record when it ends, with the operator or the
  machine caller as actor, the tenant and the job kind, and no secret. The runner for `suspend`, `resume`, `delete` and `cancel-deletion` is built: it writes the
  record in the same transaction as the job's final state, whether the job succeeded or failed, with the command name of the
  matching `Axis.Admin` command and `job:<id>` in `changed`. A refused call writes a log
  event, not a record _(planned for M31)_.

| Route | What it does |
| --- | --- |
| `GET /api/cell/auth/sign-in` | Sends the operator to the operator realm's login page, see [sign-in](cell-console.md#sign-in). It needs no session and answers `404` on any host but the console host, and when the console offers no sign-in. |
| `POST /api/cell/auth/sign-out` | Deletes the operator's session and answers `204`. It needs the CSRF header. |
| `GET /api/cell/tenants` | Lists the tenants of the cell as `{ "items": [...] }`, ordered by id, each with its `id`, `status`, `application` and `hosts`, read from the platform tables. `status` is `deleting` while a deletion is scheduled, though the stored status stays `suspended`. A preview tenant is left out. `application` is `null` until the create job records it _(planned for M31)_. Usage, quotas and failed work counts, see [views](cell-console.md#views), are _(planned for M31)_ on the list. |
| `GET /api/cell/tenants/{id}` | Answers one tenant as an item of the list, `deleting` included, with its `quotas`: an object keyed by quota key whose entries hold the effective `value` (`null` is no limit) and `overridden`, which is true when the tenant has a row for the quota. It answers `404` for an unknown id or a preview tenant's id. Its usage history is _(planned for M31)_. |
| `POST /api/cell/tenants` | Creates a tenant, see [creating a tenant](#creating-a-tenant). |
| `POST /api/cell/tenants/{id}/suspend` | Suspends a tenant, as [`tenant suspend`](tenant-status-commands.md#tenant-suspend) does, through a job. It answers `404` for an unknown id or a preview tenant's id, then `400` without a valid `Idempotency-Key`. |
| `POST /api/cell/tenants/{id}/resume` | Resumes a tenant, as [`tenant resume`](tenant-status-commands.md#tenant-resume) does, through a job. It answers as `suspend` does. |
| `POST /api/cell/tenants/{id}/read-only` | _(planned for M33)_ Makes a tenant read-only, as [`tenant read-only`](tenant-status-commands.md#tenant-read-only) does. `resume` ends it. |
| `GET /api/cell/tenants/{id}/administrators` | _(planned for M33)_ Lists the accounts that hold the [administrator role](user-administration.md#the-administrator-role) in the tenant's realm: email, display name and language. The [control plane](control-plane.md) mails them. |
| `PUT /api/cell/tenants/{id}/quotas` | Sets or clears the tenant's quota rows, see [quotas](tenant-quotas.md#quotas). The body is `{ "set": { "users": 100, "storage": null }, "remove": ["applications"] }`. A set value of `null` is no limit, a removed key goes back to the cell's default, and a key the body does not name stays as it is. It applies at once, with no job, and answers `200` with the tenant's `quotas` as the tenant read does. An unknown key, a negative value, a key both set and removed, or a body that names no key answers `400` and writes nothing. An unknown tenant id or a preview tenant's id answers `404`. It writes the rows and one [platform audit record](tenant-setting-commands.md#platform-audit-records), `tenant quotas set`, in one transaction, over the schema role: the server opens a connection for the write alone and keeps none in a pool. The hosts apply the new values at their next `Tenancy:RefreshInterval`. |
| `POST /api/cell/tenants/{id}/hosts` | Asks for a host of the tenant's own, see [hosts](#hosts). |
| `POST /api/cell/tenants/{id}/exports` | Exports the tenant, see [exporting a tenant](#exporting-a-tenant), through a job. It answers `404` for an unknown id or a preview tenant's id, then `400` without a valid `Idempotency-Key`, and `202` with a `Location` header. A tenant that is still `provisioning` makes the job fail. |
| `GET /api/cell/tenants/{id}/exports/{exportId}` | _(planned for M31)_ Answers the export, and its download while it is kept. |
| `DELETE /api/cell/tenants/{id}` | Schedules the deletion through a `delete` job, see [deleting a tenant](#deleting-a-tenant). It answers `202` as `suspend` does. |
| `POST /api/cell/tenants/{id}/deletion/cancel` | Cancels a deletion in its grace window through a `cancel-deletion` job. It answers as `suspend` does. |
| `GET /api/cell/jobs/{id}` | Answers a job as the `202` does, with its `steps`, each `{ name, state, error }`, and the `error` of a failed job. An unknown id answers `404`. |

## Creating a tenant

`POST /api/cell/tenants` takes `{ "id", "application", "administrator" }`, and _(planned for M33)_
an optional `quotas`, which the `tenant` step writes as the tenant's quota rows, so a tenant of the
[sign-up](sign-up.md#provisioning) never runs with the cell's defaults.

- `id` follows the rule of [`tenant create`](administration.md#tenant-create): 1 to 50 lowercase letters,
  digits and hyphens, and not `master`. A bad id answers `400`. An id that a directory row holds, in any
  status and a preview tenant's included, or that a `queued` or `running` create job holds, answers `409`.
  An id that a deleted tenant had answers `409` too _(planned for M31, once deletion adds `platform.deleted_tenants`)_.
- `application` names an installed [application template](modules-and-templates.md#application-templates),
  such as the built-in `approvals`. An id that is not an installed template answers `400`. The job keeps the id, and its `application` step takes the newest version.
- `administrator` is the email of the person invited as the tenant's first administrator. A value that is
  not an email answers `400`.
- A cell at its cap answers `409`, see [cell size](cells.md#cell-size).
- The route needs an `Idempotency-Key` header, see [idempotency](#provisioning-api). The checks run in this
  order: the header (`400`), the replay of the first job or `422`, the body (`400`), then the id and the
  cap (`409`). A retry with the same key and body answers the first job again, even after the tenant exists.
  A cell-wide advisory lock covers the id check and the cap check, so two parallel creates cannot both pass.
  `Axis.Admin tenant create` does not take that lock.

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
is verified are in [hosts and certificates](cells.md#hosts-and-certificates).

The route is built as far as the request:

- **The host.** The server trims the host and keeps it in lower case. It must be a DNS name of at
  least two labels, each of 1 to 63 letters, digits or inner hyphens, 253 characters at most, with
  no trailing dot, and no IP address. A blank or malformed host answers `400`. An unknown tenant or a
  preview tenant answers `404`.
- **The answer.** `201` with `{ host, token, state, expiresAt, txt, cname }`. The `token` is 32
  random bytes as base64url, `txt` is the record `_axis-verify.<host>` that holds the token, `cname`
  points the host to `<id>.<Cell:TenantDomain>`, and `expiresAt` is 7 days after the request.
- **A repeat.** The same tenant asking again for the same pending host answers `200` with the same
  token, so a retry does not invalidate DNS records the customer already added. Once `expiresAt` has
  passed the request is no longer pending, and a new request stores a new row with a new token. A
  transaction lock on the tenant and host keeps two parallel retries from storing two tokens.
- **A host that is held.** A host that `platform.tenant_hosts` holds, for any tenant, answers `409`,
  and the message does not say which tenant holds it. The held back check after a deletion is
  _(planned for M31)_, with tenant deletion.
- **A pending request holds nothing.** The request is a row of `platform.tenant_host_requests`, see
  [storage](storage.md#platform-database), and another tenant may request the same host. The host is
  not in `platform.tenant_hosts`, so it still answers `404` for every path. The server stores the
  request over the platform reader role, which may insert into that table and nothing else there.
  The row's `actor` is the operator's id. The route queues no job and takes no `Idempotency-Key`.

The [publisher](administration.md#publisher) checks the customer's DNS records, under
`Tenancy:Source=directory`, every `Cell:HostCheckInterval` (10 minutes by default):

- **Expired.** A pending request past `expires_at` becomes `expired` without a DNS lookup, and its
  host keeps answering `404`.
- **Checked once for each interval.** The publisher claims the due requests by setting `checked_at`,
  and skips the rows that another publisher holds. A request is not checked twice in one interval,
  also across restarts and several publishers.
- **Verified.** When a `TXT` record of `_axis-verify.<host>` equals the token, the host joins the
  tenant, the realm's redirect addresses gain it, the publisher creates the host's cert-manager
  `Certificate` when the cell has certificates on, and the request becomes `verified`. The
  [platform audit record](tenant-setting-commands.md#platform-audit-records) is named
  `tenant host verify` and names the operator who asked for the host as its actor. The request is
  marked last, so a stop, a failed realm update or a failed certificate write is retried in the
  next round (a `Certificate` that exists counts as written), and a host that the
  tenant already holds is not added or audited twice. A host is never verified without a matching
  record.
- **Pending.** A wrong token, no record or a DNS failure leaves the request pending, and the failure
  only writes a line. The request also stays pending when another tenant holds the host.

The [certificate](cells.md#hosts-and-certificates) of a verified host is built. The route of the
ingress for the host is _(planned for M31)_. Until it exists the host is in the directory and has a
certificate, but the ingress has no route for it.

## Exporting a tenant

An export gives a tenant's data to its owner in a form that any tool reads. It is not a backup: a
restore takes an [archive](tenant-backup-and-restore.md), never an export. It is built so that a
tenant administrator can run it in M33.

- **When.** For a tenant in any status but `provisioning`. A job for a `provisioning` tenant fails with the
  refusal that `suspend` gives. The `export` job of the publisher is built, and an operator starts it with the
  exports route. One export of a tenant runs at a time, and it takes a lease of the tenant's `concurrentImports`
  quota _(planned for M31)_.
- **Consistent.** The job reads the tenant database in one snapshot, as `tenant backup` does: one `RepeatableRead`
  transaction for the active releases and every record.
- **Kept.** The ZIP is stored in the cell's object storage, encrypted at rest, for 7 days, then
  deleted. The download answers it to a caller of the provisioning API, and to nobody else. Each
  download writes a platform audit record. The job stores the ZIP at `exports/<tenant>/<job id>.zip` in the bucket of
  the five [`Cell:ExportStorage`](host-settings.md#cell-settings) settings, with S3 server-side encryption (`AES256`), so that the owner
  can open the downloaded ZIP without a key of the cell. The export id is the job id. A publisher without these
  settings still runs every other job, and the `export` job fails with a message that names the missing setting.
  The deletion after 7 days, the download and its audit record are _(planned for M31)_.
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
| `export.json` | `format` (`1`), `tenantId`, `createdAt`, `axisVersion`, and each application, ordered by name, with its `name`, its `folder`, its active `release` number and its `entities`, each `{ name, file }` with the original entity name and the file name without its extension. |
| `applications/<application>/` | _(planned for M31)_ The application folder from the tenant's Studio folder, as Studio writes it, its `customizations/` included. |
| `data/<application>/<entity>.jsonl` | One JSON object per line for each record, in the shape of the [record API](record-api.md#record-shape), with child rows inside the record. A child entity has no file of its own. |
| `data/<application>/<entity>.csv` | The same records in the CSV form of [export](import-export.md#export), one file per entity. Its first column is `id`, the record id, which entity export does not write. Child rows go in a file of their own named `<entity>.<field>.csv`, whose first column is `recordId`, the id of the record that owns the row, so a row can be joined to its record. |
| `files/<application>/<entity>/<record id>/<field>/<file name>` | _(planned for M31)_ Each stored file, under its record and field. |
| `users.csv` | _(planned for M31)_ The realm's accounts: user name, email, display name, roles, enabled, created. No credential. |

An entity name or a file name that is not a safe file name is written with its unsafe characters
replaced by `_`, and `export.json` lists the original name. The safe characters are letters, digits, `.`, `_` and `-`, so the
module entity `organization/Department` is written as `organization_Department`. When two names become the same file name
in one application, the later one gets `_2`, `_3` and so on.

## Deleting a tenant

A deletion can be undone during its grace window, and not after.

1. **Schedule.** `DELETE /api/cell/tenants/{id}` suspends the tenant, as `tenant suspend` does, and
   records `delete_after`, 7 days later. Its hosts answer `503`. The tenant shows as
   `deleting` in the console and in the tenant reads, though its stored status stays `suspended`. A rerun of the job keeps the first
   date. A deletion of a tenant that has a preview tenant deletes the preview too.
2. **Grace window.** `POST /api/cell/tenants/{id}/deletion/cancel` clears `delete_after`, and the
   tenant stays `suspended` until it is resumed. A cancel after `delete_after` has passed fails its job and keeps the date. A cancel
   of a tenant with no scheduled deletion succeeds and changes nothing. An export can still run in the window.
3. **Purge** _(planned for M31)_. After `delete_after`, a job removes, in this order, and each step can run again:
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
4. **After** _(planned for M31)_. The id is never used again in the cell. Each host is held back for 90 days, so a new
   tenant does not receive the old tenant's links and emails. The platform audit records stay.

**What remains, and for how long.**

- The tenant's daily archives stay in object storage until their 30-day lock ends, but nothing can
  read them, because the backup key is gone.
- The managed PostgreSQL's point-in-time recovery holds the tenant database and the platform rows,
  wrapped keys included, for its retention of 7 days. A deletion is complete once that window has
  passed. The console shows the date.
