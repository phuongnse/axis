# Tenant lifecycle in a cell

The tenants of a [cell](cells.md) are created, changed, exported and deleted through the cell's
provisioning API. This page covers that API, then creating a tenant, its hosts, exporting it and
deleting it. A self-hosted installation has no provisioning API and keeps the `Axis.Admin` commands
of [administration](administration.md).

## Provisioning API

One API on the cell provisions its tenants. The [cell console](cell-console.md), the `Axis.Admin`
commands run against a cell and the [control plane](control-plane.md) all call it, so each operation
has one implementation.

- **Where it answers.** Under `/api/cell/` on the cell console's host, `Cell:ConsoleHost`, and on the
  host of the server's internal listener, `Cell:InternalUrl`. Both are platform hosts: they resolve no
  tenant, the health probes still answer there, and every path outside `/api/cell/` answers `404`.
  The console host is the exception, because it also serves the SPA's page, its files,
  `GET /api/site` and `GET /api/texts/{locale}`. The internal listener's host answers `404` for every
  path outside `/api/cell/`, the page at `/` included. A tenant host answers `404` for every path
  under `/api/cell/`, before any sign-in work. A self-hosted installation has no platform host, so
  every `/api/cell/` path answers `404` there.
- **The internal listener.** The server opens a listener of its own for `Cell:InternalUrl`, which only
  the cell's network reaches. It is the Kestrel endpoint `Internal`, with its own URL and server
  certificate. With `Cell:InternalClientCa` set, it requires a client certificate that chains to the
  region's internal certificate authority in that PEM file, and the TLS handshake fails without one.
  The server checks the chain against that authority only and no revocation list, and it refuses a
  certificate whose extended key usage leaves out client authentication. The public listeners never ask
  for a client certificate. The route still needs a bearer token, see the next item. Without
  `Cell:InternalClientCa` the endpoint serves without client certificates, which development uses. See
  [reaching the cells](control-plane.md#reaching-the-cells).
- **Who may call.** A caller signs in through the cell's [operator realm](cell-console.md#sign-in):
  the cell console with the operator's session, and a machine caller, such as the control plane or a
  script, with a bearer token of a client of that realm. The server finds the realm at
  `IdentityProvider:Url` plus `realms/<Cell:OperatorRealm>`, and in a cell with a console or internal
  host it stops at start without that setting. It checks the token's issuer, signature, expiry and
  type, and no audience, because the realm serves only the cell. It reads the caller's roles from the
  `realm_access.roles` claim, where Keycloak puts realm roles. Every route but sign-in and sign-out
  needs the role `Cell:OperatorRole` (`403` without it). A missing token, and a tenant realm's token,
  are refused with `401`.
- **Session or token.** A request with a bearer token uses the token and never reads the session
  cookie. A session counts only on the console host, and a tenant's session never counts there. Every
  write through a session needs the CSRF header, as the other session routes do. A write with a bearer
  token needs none, because a browser cannot attach a bearer token on its own.
- **Idempotency.** Each `POST` that queues a job needs an `Idempotency-Key` header of 1 to 255
  characters, and the answer is `400` without one, with no job queued. The same key with the same
  request answers the first job again for 24 hours. The request is the kind, the tenant and the body,
  hashed with SHA-256, so the body must match byte for byte. The same key with another request
  answers `422`. A key is shared across the cell, not kept for each caller, and a lock on the key
  keeps two parallel retries from queuing two jobs.
- **Audit.** Each job writes one platform audit record when it ends, whether it succeeded or failed,
  in the same transaction as the job's final state. The record names the operator or the machine
  caller as actor, the tenant, the command name of the matching `Axis.Admin` command and `job:<id>`
  in `changed`, and holds no secret.
- **Refused calls** _(planned for M31)_. A refused call writes a log event and no audit record.

### Jobs

A route that changes a tenant answers `202` with a job, `{ "id", "kind", "tenantId", "state",
"createdAt" }`, and a `Location` header of `/api/cell/jobs/{id}`. The
[publisher](administration.md#publisher) runs the job with the same code as the `Axis.Admin` command,
over the schema role.

- **Queuing.** The server only queues the job, over the platform reader role, which may insert into
  `platform.jobs` and nothing else there. The job's actor is the operator's id, the subject of the
  token or the session.
- **State.** `GET /api/cell/jobs/{id}` answers the job, with `state` `queued`, `running`, `succeeded`
  or `failed`, its steps, and the error of a failed step.
- **Claims.** A step that throws ends the claim, and the job runs again in the next round, up to three
  claims. A step that already succeeded is skipped, and a refusal, such as for a tenant that is still
  `provisioning`, fails the job at once.
- **Waiting for a lease.** A step may take a lease of the tenant first, as the `export` step does. A
  wait for a lease uses no claim: it adds to the job's `waits` count, and the limit of three is
  `claims` minus `waits`.
- **Running a failed job again** _(planned for M31)_. A failed job can be run again, and it skips the
  steps already done, as `tenant create` does.

| Kind | Queued by | Steps |
| --- | --- | --- |
| `suspend`, `resume`, `read-only` | Its route | One `status` step |
| `create` | `POST /api/cell/tenants` | The five steps of [creating a tenant](#creating-a-tenant), see the [create job](administration.md#create-job) |
| `export` | `POST /api/cell/tenants/{id}/exports` or `POST /api/tenant/exports` | One `export` step, see [exporting a tenant](#exporting-a-tenant) |
| `delete` | `DELETE /api/cell/tenants/{id}` | One `schedule` step |
| `cancel-deletion` | `POST /api/cell/tenants/{id}/deletion/cancel` | One `cancel` step |
| `purge` | The publisher, never a route | The eight steps of [deleting a tenant](#deleting-a-tenant), see the [purge job](administration.md#purge-job) |

### Routes

| Route | What it does |
| --- | --- |
| `GET /api/cell` | Answers the cell as `{ "id", "region", "tenants", "cap", "restoreCheck" }`. `tenants` counts the cell's tenants whatever their status and leaves out preview tenants. `cap` is `Cell:TenantCap`, see [cell size](cells.md#cell-size), or `null` when it is not set. `restoreCheck` is `{ "ranAt", "tenantId", "passed" }` of the newest [weekly restore check](tenant-backup-and-restore.md#weekly-restore-check), or `null` when no check has run. |
| `GET /api/cell/auth/sign-in` | Sends the operator to the operator realm's login page, see [sign-in](cell-console.md#sign-in). It needs no session and answers `404` on any host but the console host, and when the console offers no sign-in. |
| `POST /api/cell/auth/sign-out` | Deletes the operator's session and answers `204`. It needs the CSRF header. |
| `GET /api/cell/session` | Answers the signed-in operator as `{ "displayName" }`, see [sign-in](cell-console.md#sign-in). Needs the operator role. |
| `GET /api/cell/tenants` | Lists the tenants of the cell as `{ "items": [...] }`, ordered by id, each with its `id`, `status`, `application`, `hosts`, `failedWork`, `usage` and `newestArchiveAt`, read from the platform tables. `status` is `deleting` while a deletion is scheduled, though the stored status stays `suspended`. A preview tenant is left out. `failedWork` is `{ "instances", "unknownCalls", "deadLetters" }`, counts only: the failed instances, the connector and package calls whose outcome is unknown, and the open dead letters. It comes from the worker's hourly [usage sample](tenant-quotas.md#usage) and is `null` until the worker has counted the tenant. `usage` holds `storage`, `users`, `applications` and `emailsPerDay`, each as `{ "limit", "used" }` as [the usage route](tenant-quotas.md#usage) answers it, with the effective limit of the tenant's `quotas` (`null` is no limit). `storage.used` is the newest hourly sample and `null` before the first one. `users.used` and `applications.used` are the newest day row of `platform.tenant_usage` and `null` while nothing fills them. `emailsPerDay.used` is today's `emails` counter, 0 when none was sent. `newestArchiveAt` is the time of the tenant's newest [daily archive](tenant-backup-and-restore.md#daily-backup) in `platform.tenant_backups`, or `null` when the tenant was never backed up. |
| `GET /api/cell/tenants/{id}` | Answers one tenant as an item of the list, `deleting`, `failedWork`, `usage` and `newestArchiveAt` included, with its `quotas`: an object keyed by quota key whose entries hold the effective `value` (`null` is no limit) and `overridden`, which is true when the tenant has a row for the quota. It answers `404` for an unknown id or a preview tenant's id. |
| `POST /api/cell/tenants` | Creates a tenant, see [creating a tenant](#creating-a-tenant). |
| `POST /api/cell/tenants/{id}/suspend` | Suspends a tenant, as [`tenant suspend`](tenant-status-commands.md#tenant-suspend) does, through a job. It answers `404` for an unknown id or a preview tenant's id, then `400` without a valid `Idempotency-Key`. |
| `POST /api/cell/tenants/{id}/resume` | Resumes a tenant, as [`tenant resume`](tenant-status-commands.md#tenant-resume) does, through a job. It answers as `suspend` does. |
| `POST /api/cell/tenants/{id}/read-only` | Makes a tenant read-only, as [`tenant read-only`](tenant-status-commands.md#tenant-read-only) does, through a job. It answers as `suspend` does. `resume` ends it. |
| `GET /api/cell/tenants/{id}/administrators` | Lists the people who hold the [administrator role](user-administration.md#the-administrator-role) as `{ "items": [{ "email", "displayName", "language" }] }`, ordered by email, so the [control plane](control-plane.md) can mail them. The answer holds nothing else of a person, not even an id. The holders are the administrator rows of the tenant's `axis.given_roles`, which the `invite` step and the users page write. A role that a sign-in provider's mapping gives at sign-in is stored nowhere, so it is not listed. Only enabled accounts with an email are listed. `language` is the locale the person chose in the tenant's realm, or `null`. The route answers for an active, a suspended and a `deleting` tenant alike: it opens the tenant database from the tenant's directory row and not from the server's tenant catalog, which holds active tenants only. It answers `404` for an unknown id or a preview tenant's id, `409` for a tenant that has no realm users client, and `502` with the title "The identity provider could not read the administrators." when the identity provider fails. |
| `PUT /api/cell/tenants/{id}/quotas` | Sets or clears the tenant's quota rows, see [quotas](tenant-quotas.md#quotas). The body is `{ "set": { "users": 100, "storage": null }, "remove": ["applications"] }`. A set value of `null` is no limit, a removed key goes back to the cell's default, and a key the body does not name stays as it is. It applies at once, with no job, and answers `200` with the tenant's `quotas` as the tenant read does. An unknown key, a negative value, a key both set and removed, or a body that names no key answers `400` and writes nothing. An unknown tenant id or a preview tenant's id answers `404`. It writes the rows and one [platform audit record](tenant-setting-commands.md#platform-audit-records), `tenant quotas set`, in one transaction, over the schema role: the server opens a connection for the write alone and keeps none in a pool. The hosts apply the new values at their next `Tenancy:RefreshInterval`. |
| `POST /api/cell/tenants/{id}/hosts` | Asks for a host of the tenant's own, see [hosts](#hosts). |
| `POST /api/cell/tenants/{id}/exports` | Exports the tenant, see [exporting a tenant](#exporting-a-tenant), through a job. It answers `404` for an unknown id or a preview tenant's id, then `400` without a valid `Idempotency-Key`, and `202` with a `Location` header. A tenant that is still `provisioning` makes the job fail. |
| `GET /api/cell/tenants/{id}/exports/{exportId}` | Answers the [tenant export](#exporting-a-tenant) as JSON: `id`, `tenantId`, `state`, `createdAt` and `expiresAt`, which is 7 days after the job finished and `null` until it succeeded. The same route streams the ZIP when the request sends `Accept: application/zip`, and answers the JSON to every other request. The server streams the ZIP from the export storage itself and never redirects to a storage address. A download before the job succeeded answers `409`. An export past its `expiresAt`, or whose ZIP the storage no longer holds, answers `410` on both forms. An unknown tenant, a preview tenant or an id that is not an export of that tenant answers `404`. A download on a server without the five `Cell:ExportStorage` settings answers `503`, and the JSON read still works there. Each download writes the platform audit record `tenant export download`, see [platform audit records](tenant-setting-commands.md#platform-audit-records). |
| `DELETE /api/cell/tenants/{id}` | Schedules the deletion through a `delete` job, see [deleting a tenant](#deleting-a-tenant). It answers `202` as `suspend` does. |
| `POST /api/cell/tenants/{id}/deletion/cancel` | Cancels a deletion in its grace window through a `cancel-deletion` job. It answers as `suspend` does. |
| `GET /api/cell/jobs` | Lists the cell's jobs as `{ "items": [...] }`, newest first by `createdAt`, then by id. Each item is a job as the job read answers it, with its `steps` and `error`. The `actor` and the `input` of a job are never returned. `tenant` keeps one tenant's jobs, and an unknown tenant answers an empty list. `limit` is a whole number from 1 to 200 and is 50 when missing. Any other value answers `400`. There is no paging past `limit`. |
| `GET /api/cell/jobs/{id}` | Answers a job as the `202` does, with its `steps`, each `{ name, state, error }`, and the `error` of a failed job. An unknown id answers `404`. |

- **A tenant's application** _(planned for M31)_. The create job records the tenant's application,
  which the tenant reads answer as `application`.
- **Usage history in the tenant read** _(planned for M31)_. The tenant read answers the tenant's usage
  history.

## Creating a tenant

`POST /api/cell/tenants` takes `{ "id", "application", "administrator", "quotas" }` and queues a
`create` job with five steps, all `pending`. `quotas` is optional. The job keeps the request's
`application`, `administrator` and `quotas`, when the request has them, in its `input`, which the job
read never returns.

- **`id`** follows the rule of [`tenant create`](administration.md#tenant-create): 1 to 50 lowercase
  letters, digits and hyphens, and not `master`. A bad id answers `400`. An id that a directory row
  holds, in any status and a preview tenant's included, or that a `queued` or `running` create job
  holds, answers `409`. An id that a deleted tenant has a tombstone for answers `409` too.
- **`application`** names an installed
  [application template](modules-and-templates.md#application-templates), such as the built-in
  `approvals`. An id that is not an installed template answers `400`. The job keeps the id, and its
  `application` step takes the newest version.
- **`administrator`** is the email of the person invited as the tenant's first administrator. A value
  that is not an email answers `400`.
- **`quotas`** is optional and has the shape of the `set` object of
  [`PUT /api/cell/tenants/{id}/quotas`](#routes): a flat object that maps a quota key to a number, or
  to `null` for no limit, such as `{ "users": 10, "storage": null }`. The `tenant` step writes one
  [quota](tenant-quotas.md#quotas) row for each key, in the transaction that writes the directory
  entry, so a tenant of the [sign-up](sign-up.md#provisioning) never runs with the cell's defaults.
  A tenant created without `quotas`, or with `{}`, has no rows and uses the cell's defaults. An
  unknown key or a negative value answers `422` and queues no job. A `quotas` that is not an object
  of numbers is a body that cannot be read and answers `400`. A rerun of the `tenant` step replaces
  the tenant's rows with the given ones when the job has `quotas`, and leaves them alone when it has
  none.
- **The cap.** A cell at its cap answers `409`, see [cell size](cells.md#cell-size).
- **Order of the checks.** The route needs an `Idempotency-Key` header, see
  [idempotency](#provisioning-api). The checks run in this order: the header (`400`), the replay of
  the first job or `422`, the body (`400`), the quotas (`422`), then the id and the cap (`409`). A retry with the same key
  and body answers the first job again, even after the tenant exists.
- **One at a time.** A cell-wide advisory lock covers the id check and the cap check, so two parallel
  creates cannot both pass. `Axis.Admin tenant create` does not take that lock.

The publisher runs the job's steps in order, see the [create job](administration.md#create-job):

1. `tenant`: create the tenant as `tenant create` does, with the host `<id>.<Cell:TenantDomain>`, its
   realm, its keys and its backup key, see
   [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell), and write the quota rows of
   the job's `quotas`.
2. `application`: copy the application into the tenant's Studio folder,
   `<Studio:ApplicationsPath>/<tenant id>/`, as Studio creates one from a template.
3. `activate`: activate the application's first release in the tenant.
4. `invite`: invite the administrator, as `tenant create --administrator` does.
5. `resume`: set the tenant `active`.

## Hosts

`POST /api/cell/tenants/{id}/hosts` takes `{ "host" }` and answers the host's verification token and
the DNS records to add. The checks, the certificate, the route and the rule that a host is never
served before it is verified are in [hosts and certificates](cells.md#hosts-and-certificates).

- **The host.** The server trims the host and keeps it in lower case. It must be a DNS name of at
  least two labels, each of 1 to 63 letters, digits or inner hyphens, 253 characters at most, with no
  trailing dot, and no IP address. A blank or malformed host answers `400`. An unknown tenant or a
  preview tenant answers `404`.
- **The answer.** `201` with `{ host, token, state, expiresAt, txt, cname }`. The `token` is 32
  random bytes as base64url, `txt` is the record `_axis-verify.<host>` that holds the token, `cname`
  points the host to `<id>.<Cell:TenantDomain>`, and `expiresAt` is 7 days after the request.
- **A repeat.** The same tenant asking again for the same pending host answers `200` with the same
  token, so a retry does not invalidate DNS records the customer already added. Once `expiresAt` has
  passed the request is not pending any more, and a new request stores a new row with a new token. A
  transaction lock on the tenant and host keeps two parallel retries from storing two tokens.
- **A host that is held.** A host that `platform.tenant_hosts` holds, for any tenant, answers `409`,
  and the message does not say which tenant holds it. A host that a tenant deleted less than 90 days
  ago had answers `409` too, see [deleting a tenant](#deleting-a-tenant), and its message names no
  tenant either.
- **A pending request holds nothing.** The request is a row of `platform.tenant_host_requests`, see
  [storage](storage.md#platform-database), and another tenant may request the same host. The host is
  not in `platform.tenant_hosts`, so it still answers `404` for every path. The server stores the
  request over the platform reader role, which may insert into that table and nothing else there. The
  row's `actor` is the operator's id. The route queues no job and takes no `Idempotency-Key`.

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
  marked last, so a stop, a failed realm update or a failed certificate write is retried in the next
  round (a `Certificate` that exists counts as written), and a host that the tenant already holds is
  not added or audited twice. A host is never verified without a matching record.
- **Pending.** A wrong token, no record or a DNS failure leaves the request pending, and the failure
  only writes a line. The request also stays pending when another tenant holds the host.

## Exporting a tenant

An export gives a tenant's data to its owner in a form that any tool reads. It is not a backup: a
restore takes an [archive](tenant-backup-and-restore.md), never an export.

- **When.** For a tenant in any status but `provisioning`. A job for a `provisioning` tenant fails
  with the refusal that `suspend` gives. An operator starts the `export` job with the exports route, or
  a tenant administrator with the [tenant route](#exports-by-a-tenant-administrator). One export of a
  tenant runs at a time, because the publisher runs one job of a tenant at a time.
- **A lease.** In a cell the job holds one lease of the tenant's `concurrentImports` quota while it
  reads the tenant and uploads the ZIP, so an import that runs at the same time competes for the same
  leases. When none is free, the job stays `running` with its step `pending`, and the publisher tries
  again about a second later. A wait does not use one of the three claims, so a busy tenant never
  fails an export. A tenant with no `concurrentImports` limit, and a publisher outside a cell, export
  without a lease.
- **Consistent.** The job reads the tenant database in one snapshot, as `tenant backup` does: one
  `RepeatableRead` transaction for the active releases, every record, every stored file and the given
  roles. The Studio folder is read from disk while the job runs, outside the snapshot, so an author's
  edit during the export may or may not be in the ZIP, as in a backup. The accounts of `users.csv`
  come from the realm, which is also outside that snapshot, so an account made or removed during the
  export can disagree with the given roles.
- **Stored.** The job stores the ZIP at `exports/<tenant>/<job id>.zip` in the bucket of the five
  [`Cell:ExportStorage`](host-settings.md#cell-settings) settings, in the cell's object storage, with
  S3 server-side encryption (`AES256`), so the owner can open the downloaded ZIP without a key of the
  cell. The export id is the job id. A publisher without these settings still runs every other job,
  and the `export` job fails with a message that names the missing setting.
- **Kept 7 days.** The [publisher](administration.md#publisher) sweeps the export bucket every hour
  and deletes each ZIP under `exports/` that was stored more than 7 days ago. So a ZIP can outlive its
  `expiresAt` by up to an hour, and the route answers `410` from `expiresAt` in that time.
- **Downloaded.** The download is the [exports route](#routes) with `Accept: application/zip`, or
  `GET /api/tenant/exports/{exportId}` with it, and it answers the ZIP to a caller of the provisioning
  API and to an administrator of the tenant, and to nobody else. Each download writes a platform audit
  record that names that caller as actor and `export:<id>` in `changed`.
- **Sensitive fields.** The export holds their values in clear, because the data is the owner's. The
  ZIP is [restricted](keys-and-data-protection.md#restricted) data.
- **The console's warning.** The cell console asks the operator to accept a warning that the ZIP is
  restricted data before it fetches the ZIP, see [cell console actions](cell-console.md#actions).

### ZIP layout

The ZIP holds the tenant's applications, its records in two forms, its files and its users.

| Entry | What it holds |
| --- | --- |
| `export.json` | `format` (`1`), `tenantId`, `createdAt`, `axisVersion`, and each application, ordered by name, with its `name`, its `folder`, its active `release` number and its `entities`, each `{ name, file }` with the original entity name and the file name without its extension, and its `files`, each `{ entity, record, field, name, file }` with the original entity name, the record id, the stored field path such as `attachments/file`, the original file name and the path of the file in the ZIP. |
| `applications/<path>` | Every file of `<Studio:ApplicationsPath>/<tenant id>/` by its path below that folder, `customizations/` and the `.revisions/` folder that holds the [revisions](revisions-and-drafts.md#the-repository) included, as Studio wrote it and found by the same walk as `tenant backup`. Symbolic links are skipped. With no setting or no folder there are no entries. |
| `data/<application>/<entity>.jsonl` | One JSON object per line for each record, in the shape of the [record API](record-api.md#record-shape), with child rows inside the record. A child entity has no file of its own. |
| `data/<application>/<entity>.csv` | The same records in the CSV form of [export](import-export.md#export), one file per entity. Its first column is `id`, the record id, which entity export does not write. Child rows go in a file of their own named `<entity>.<field>.csv`, whose first column is `recordId`, the id of the record that owns the row, so a row can be joined to its record. |
| `files/<application>/<entity>/<record id>/<field>/<file name>` | Each attached file, read in the snapshot, under its record and field. `<entity>` is the entity's file name in `data/`. A file of a child row goes under its parent record, and its `<field>` is the stored field path made safe, such as `attachments_file`. |
| `users.csv` | One row for each user of the tenant's realm, read as `axis-users`, with the columns `userName,email,displayName,roles,enabled,created`. Service accounts are left out, and brokered users are listed. `displayName` is the first and last name, or the user name when both are empty. `roles` holds the person's [given roles](user-administration.md#roles) in the export's snapshot, joined with `; `: first the platform roles, each by its kind such as `administrator`, then the application roles, each as `<application name>:<role>`, with the application id in place of the name when no active release names the application. `enabled` is `true` or `false`, and `created` is the realm's creation time in UTC, as `yyyy-MM-ddTHH:mm:ssZ`. Rows are ordered by user name. No column or entry holds a password hash or any other credential. A tenant without its own realm, such as one that signs in with an outside identity provider, gets no `users.csv`. A realm that cannot be read fails the `export` step, so a ZIP without the users is never stored. |

An entity name, a field path or a file name that is not a safe file name is written with its unsafe
characters replaced by `_`, and `export.json` lists the original name. The safe characters are
letters, digits, `.`, `_` and `-`, so the module entity `organization/Department` is written as
`organization_Department`. When two names become the same file name in one application, the later one
gets `_2`, `_3` and so on. Two file names that clash in one folder get `_2`, `_3` and so on before the
extension, such as `scan_2.pdf`. The paths under `applications/` are copied as they are.

## Exports by a tenant administrator

A tenant administrator exports their own tenant from the application, with the same job and the same
ZIP as the provisioning API, so that a business can take its data without asking the operator.

- **Who.** A person who holds the [administrator role](user-administration.md#the-administrator-role).
  The users page's [Export data section](platform-pages.md#export-data-and-usage) starts and follows it.
- **Routes.** `POST /api/tenant/exports` on the tenant's host starts it, and
  `GET /api/tenant/exports/{exportId}` answers it and its download, for 7 days. They are platform
  routes, in the session partition, with the CSRF header on the `POST`.
- **Checks.** In this order: no session answers `401`, no administrator role `403`, and a tenant that
  is not a cell tenant, or is a preview tenant, `404`.
- **Start.** A new start queues the `export` job with the administrator as actor and answers `202`. While
  an export of the tenant is queued or running, whoever started it, a start answers `200` with that
  export and queues nothing. Both answers hold the export as the exports route does and a `Location`
  header of `/api/tenant/exports/{exportId}`. The start needs no `Idempotency-Key`.
- **Read.** The `GET` answers as the [exports route](#routes) does: the export's state and expiry, or
  the ZIP with `Accept: application/zip`, and `404`, `409`, `410` and `503` as there.
- **When.** While the tenant is `active` or `readOnly`, so a business whose trial ended or that
  stopped paying can still take its data, see [subscriptions](subscriptions.md#the-timeline). One
  export of a tenant runs at a time, whoever started it.
- **Warning.** Before it starts, the page says that the file holds every value in
  clear, sensitive ones included, and must be kept safe.
- **Audit.** A start that queues a job writes the platform audit record `tenant export start`, and each
  download writes `tenant export download`. Both name the administrator as actor and `export:<id>` in
  `changed`.

## Deleting a tenant

A deletion can be undone during its grace window, and not after.

1. **Schedule.** `DELETE /api/cell/tenants/{id}` suspends the tenant, as `tenant suspend` does, and
   records `delete_after`, 7 days later. Its hosts answer `503`. The tenant shows as `deleting` in the
   console and in the tenant reads, though its stored status stays `suspended`. A rerun of the job
   keeps the first date. A deletion of a tenant that has a preview tenant deletes the preview too.
2. **Grace window.** `POST /api/cell/tenants/{id}/deletion/cancel` clears `delete_after`, and the
   tenant stays `suspended` until it is resumed. A cancel after `delete_after` has passed fails its job
   and keeps the date. A cancel of a tenant with no scheduled deletion succeeds and changes nothing.
   An export can still run in the window.
3. **Purge.** After `delete_after`, the [publisher](administration.md#publisher) in a cell queues a
   `purge` job for each main tenant, never for a preview tenant, and runs it, see the
   [purge job](administration.md#purge-job). The job's actor is the actor of the tenant's latest
   succeeded `delete` job, and the tombstone names them. A tenant whose deletion was cancelled has no
   `delete_after` and is never purged. A purge that failed is queued again no sooner than an hour
   after it ended, and a tenant has one `purge` job at a time. The job's eight steps run in the order
   below, and each step can run again, so a purge that stopped finishes when it runs again. When the
   tenant has a preview tenant, each step removes the preview's part first, and the preview gets a
   tombstone of its own.
   1. **`hosts`.** The tenant's hosts from the directory and its realm redirect addresses. This step
      first writes the tombstone, takes the actor from the job input `{ "actor" }`, and refuses a job
      that has none.
      - **Certificates** _(planned for M31)_. The step also removes the tenant's cert-manager
        certificates.
   2. **`database`.** The tenant database, then its schema and runtime roles.
   3. **`realm`.** Its realm in the identity provider.
   4. **`files`** _(planned for M31)_. Its files in object storage, when a storage binding holds them
      outside the database.
   5. **`studio`.** Its Studio folder.
   6. **`keys`.** Its data keys, lookup keys and backup key, see
      [crypto-shredding](tenant-backup-and-restore.md#backups-in-a-cell).
   7. **`rows`.** Its quota, lease, usage and counter rows.
   8. **`directory`.** Its directory entry, replaced by a tombstone in `platform.deleted_tenants`: the
      id, the time, the actor and the hosts, with no other data.
4. **After.** The id is never used again in the cell. Each host is held back for 90 days, counted from
   the tombstone's `deleted_at` on the database clock, so a new tenant does not receive the old
   tenant's links and emails. The check ignores letter case. `tenant create` refuses the id and the
   hosts with exit code 2, `tenant host add` and `move` refuse the hosts with exit code 2, and
   `POST /api/cell/tenants` and `POST /api/cell/tenants/{id}/hosts` answer `409`. No message names
   the deleted tenant. A host request whose DNS check passed stays pending for the same reason. The
   platform audit records stay.

**What remains, and for how long.**

- **Archives.** The tenant's daily archives stay in object storage until their 30-day lock ends, but
  nothing can read them, because the backup key is gone.
- **Point-in-time recovery.** The managed PostgreSQL's point-in-time recovery holds the tenant
  database and the platform rows, wrapped keys included, for its retention of 7 days. A deletion is
  complete once that window has passed.
- **The date** _(planned for M31)_. The cell console shows the date when the deletion is complete.
