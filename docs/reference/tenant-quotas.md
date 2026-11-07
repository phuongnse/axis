# Tenant quotas and usage

A quota limits what one tenant of a [cell](cells.md) uses in total or at once, so that one tenant
cannot take what the others need. This page covers the quotas, how a request past one is refused, how
work past a concurrency quota waits, the daily email count, and the usage that Axis measures for each
tenant. Quotas are on only when `Cell:Id` is set, so a self-hosted installation has none: its server
and worker answer no limit for every quota.

A quota is not a [rate limit](headers-and-rate-limits.md), and the query timeouts are apart from both.
A quota limits what one tenant uses in total or at once, and a rate limit limits how fast one caller
asks.

## Quotas

Each quota has a key, a cell-wide default and a kind, which says how it is counted.

| Quota | What it counts | Default | Kind |
| --- | --- | --- | --- |
| `storage` | The tenant database's size plus the bytes of its stored files, in bytes | 10 GiB | Sampled |
| `users` | The enabled users of the tenant's realm, invited and brokered ones included, without service accounts | 50 | Exact |
| `applications` | The applications of the tenant: folders in its Studio folder and applications with a release | 10 | Exact |
| `emailsPerDay` | Emails sent for the tenant since 00:00 UTC | 1,000 | Exact |
| `concurrentImports` | Imports and exports that run at once | 2 | Lease |
| `concurrentConnectorCalls` | Connector and package calls in flight at once | 10 | Lease |
| `concurrentQueries` | Data source queries and record list reads that run at once | 10 | Lease |
| `workerWeight` | The tenant's share of the worker, see [worker scheduling](worker-scheduling.md) | 1 | Weight |
| `authoringRequests` | AI authoring requests sent in a calendar month in UTC, see [budgets](authoring-requests.md#budgets) | None | Exact |

- **Where they live.** `platform.tenant_quotas` holds one row for each quota that a tenant overrides:
  `tenant_id`, `quota`, `value` and `updated_at`. A quota with no row takes the cell's default,
  `Quotas:Defaults:<quota>`, see [cell settings](host-settings.md#cell-settings). A `value` of `null`
  means no limit. The server and the worker read the rows with the directory, at each
  `Tenancy:RefreshInterval`, and answer a tenant's effective quota from them.
- **Who sets them.** The operator sets a tenant's quotas through the
  [provisioning API](tenant-lifecycle.md#provisioning-api), and each change writes a platform audit
  record that names the keys of the request. The [create route](tenant-lifecycle.md#creating-a-tenant)
  can also set the first rows.
- **The operator as actor** _(planned for M31)_. The audit record of a quota change names the operator
  or the machine caller in its `actor` column.
- **From a plan** _(planned for M34)_. The [control plane](control-plane.md) writes a SaaS tenant's
  rows from its [plan](plans.md#from-a-plan-to-the-quotas), with no schema change.
- **Authoring requests.** `authoringRequests` counts the requests sent since 00:00 UTC on the first
  day of the month, once each, when the author sends it. A dropped or failed request still counts,
  and a change of plan does not count again. Its default is no limit.
- **Lowering a quota below use.** It refuses new use and removes nothing. A tenant over its storage
  quota keeps its data and can still delete.

## Refusals

A request past a quota answers `403` problem details whose `reason` is `quota-exceeded`, and writes
nothing:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "This tenant has reached its storage quota.",
  "status": 403,
  "reason": "quota-exceeded",
  "quota": "storage",
  "limit": 10737418240,
  "used": 10790000000
}
```

- **The answer.** The title names the quota in words, and `quota` names its key. Neither names the
  tenant. `reason` tells a quota refusal apart from a `403` of a policy, which has no `reason`. `used`
  is the last sample for a sampled quota and the exact count otherwise.
- **Order.** The check is an endpoint filter that runs after the sign-in check (`401`) and before the
  path (`404`) and the grant (`403`), so a signed-in caller without a grant also sees the tenant's
  limit and usage.
- **Nothing evaluated.** The check comes before body validation, validations, the
  [module contract](module-contracts.md#where-the-contract-is-checked) check and any write, so a
  refused request evaluates nothing and writes nothing, not even an audit record. It writes a log
  event `quota.refused` with the tenant and the quota.
- **What a person sees.** The SPA shows the title.
- **The usage page.** An administrator sees which quota was reached in the users page's "Usage" section, see
  [readers](#usage).

**What each quota refuses.**

- **`storage`.** Once the last sample is at or over the quota, these are refused: a record create or
  update through the record API, an import, an inbound endpoint call that writes, a file upload, a
  save, reset or undo in Studio and a publish. A delete is never refused, and neither is a change of an
  [application setting](authoring.md#application-settings), which stores one flag. Process steps, task completions,
  effects and triggers go on, so running work does not fail half-way. The overshoot is what the
  tenant writes in one sampling interval. The server reads the last sample on each such write. A
  tenant with no sample yet is not refused, and neither is a write when the platform database cannot
  be read: the server logs a warning and the write goes on, because a quota is a billing limit and not
  a security boundary.
- **`users`.** Inviting an account and enabling one on the [users page](user-administration.md) are
  refused when the count would pass the quota. A person who signs in through a brokered provider for
  the first time, and would pass the quota, is refused at the identity provider with a page that asks
  them to contact their administrator. The realm's first-login flow holds a deny step for this, which
  `RealmUsersQuota.RefreshAsync` switches on at the quota and off below it. In a cell the server calls
  it after a successful invite, enable or disable and after each brokered sign-in, and the worker's
  hourly [sampling](#usage) calls it for each tenant with realm details, so a change of the quota, or
  of the users outside Axis, takes effect within about an hour of the worker's next directory
  refresh. A failed call is logged and never fails the command or the sign-in.
- **`applications`.** Creating an application in Studio is refused when the count would pass the
  quota. Existing applications keep working.
- **`authoringRequests`.** Sending an [authoring request](authoring-requests.md#routes) is refused
  once the month's count is at the quota. The check comes after the author role and before the
  application and the body, and the server counts again under a lock just before it stores the
  request, so sends at the same time cannot pass it. Requests already sent go on, and the author can
  still change their plans and drop them.
- **The provisioning API.** The platform operator's `PUT /api/cell/tenants/{id}/quotas` is never
  refused over the `storage` quota. It writes platform rows and no tenant data, so the operator can
  raise a quota that a tenant has reached.

## Work at once

Work past a concurrency quota waits its turn instead of failing. The count of work that runs is kept
in the platform database, so it holds for the whole cell.

- **Leases.** `platform.tenant_leases` holds one row for each piece of work that runs: `tenant_id`,
  `quota`, `lease_id`, `holder` (the host and process) and `expires_at`. To start, a host takes a
  transaction advisory lock on the tenant and quota, counts the unexpired rows and inserts one when
  the count is under the quota. A lease lasts 60 seconds and its holder renews it every 20 seconds.
  It is deleted when the work ends, and a host that dies leaves leases that expire within a minute.
- **Shared by every host.** The count is one for the whole cell, whatever the number of replicas and
  workers, because a limit kept in each host's memory would multiply with the hosts.
- **Cost.** A lease costs two short statements on the platform database. The
  [cell load test](performance.md#cell-load-test) shows whether the objectives still hold with them.

What waits, and how:

| Work | Lease | When none is free |
| --- | --- | --- |
| The dry run or the commit of an import, in the worker | `concurrentImports`, held until the work has committed | It does not run. Its work item is due again in 1 second and the import keeps its state, `pending` or `checked`, so any worker can run it once a lease is free. |
| The `export` job of the publisher, see [exporting a tenant](tenant-lifecycle.md#exporting-a-tenant) | `concurrentImports`, held until the ZIP is stored | The job stays `running` and the publisher tries again about a second later. The wait uses none of the job's three claims, so a busy tenant never fails an export. |
| A connector or package call, in the worker | `concurrentConnectorCalls`, held until the run of the item ends | The worker does not claim it. The work item stays due, the worker runs other tenants' work, and it retries the claim at the tenant's next turn, after one of its items ends or at the next poll. |
| A data source query or a record list read | `concurrentQueries` | It waits up to 5 seconds, then answers `503`, see below. |
| A record export or a data source export | `concurrentImports`, released when the file is complete, before it is sent | It waits up to 5 seconds, then answers `503`, see below. |

- **The busy answer.** A read or an export that waited 5 seconds answers `503` problem details titled
  "This tenant is busy. Try again in a moment.", with `Retry-After: 1`. The query timeout counts from
  the start of the query, not of the wait. A query takes a different lease from an export, so a busy
  slot of one never blocks the other.
- **The publisher's limit.** The `export` job reads the tenant's own row in `platform.tenant_quotas`
  when it takes the lease, or the publisher's `Quotas:Defaults` when the tenant has no row.
- **When the platform database is down.** A read, an export, an import or a connector or package call
  that cannot reach the platform database runs without a lease, and the server or the worker logs a
  warning. Under `directory` the server keeps serving from the last good directory then, and the limit
  would otherwise turn a platform outage into a cell-wide read outage. The `export` job of the
  publisher is the exception: it needs the platform database to renew and finish the job anyway, so a
  lease that cannot be taken ends its claim, and the export does not run without one.

## Emails a day

In a cell the worker counts each email it sends for a tenant, and holds an email past the tenant's
`emailsPerDay` until the next day. Outside a cell nothing is counted or held.

- **Counting.** The worker takes one slot of the tenant's count for the day just before it sends an
  email. `platform.tenant_daily_counters` holds the count, keyed by tenant, day (UTC) and counter, and
  emails use the counter `emails`. One statement on the platform database checks the quota and adds
  one, so workers that send at the same time cannot pass it. It is a short transaction of its own,
  because the platform and the tenant databases cannot commit together.
- **Failure.** A send that fails gives its slot back. A worker that crashes between the send and the
  mark of the row sends the email again and counts it twice. A send cancelled at shutdown keeps its
  slot.
- **Held emails.** An email past the quota is held, not dropped. Its row stays `pending` and its work
  item is due at the next 00:00 UTC by the worker's clock. After midnight held emails are sent in the
  order their items were queued, to the millisecond, and count against the new day's quota. A quota
  of 0 holds every email. A tenant with no limit is counted and never held.
- **In-app notifications** are never held. A `notify` step stores its in-app notification when the
  day's emails are used up.
- **Platform database down.** When a slot cannot be taken, the email is not sent, its item is due
  again after one lease, and the worker logs one warning for the tenant.
- **Rows.** The platform reader role gets `INSERT` and `UPDATE` on the table, and no `DELETE`. Old
  rows are kept, one small row for each tenant and day.
- **The notice.** The tenant's administrators get one in-app notice the first time in a day that an
  email is held. The worker takes the counter `emailsHeldNotice` for the day with a limit of 1. When
  it gets it, it inserts the notice row in the same tenant transaction that makes the item due at
  00:00 UTC, and later holds that day store nothing. A transaction that fails or loses its claim
  gives the counter back. A worker that crashes between taking the counter and the commit loses that
  day's notice. When the platform database cannot be reached for the counter, the email is still
  held, no notice is stored, and the worker logs one warning for the tenant. A tenant with no limit
  stores no notice.
- **The notice's text.** The notice is a row with no application and no release, whose texts are the
  platform texts `notices.emailsHeld.subject` and `notices.emailsHeld.body` with the value `limit`,
  the tenant's `emailsPerDay`. The administrators read it on the
  [notification routes](notifications.md#in-app-notifications), and the bell of the platform site
  shows it.

## Usage

The worker of a cell measures each tenant's usage: its storage every hour and its counts as it runs
work. A worker with no `Cell:Id` measures nothing and writes no usage row. Usage holds counts and
sizes only, never a record value, a file name or a user name.

- **Sampling.** A worker job samples each tenant's storage every hour and keeps the newest sample in
  `platform.tenant_usage_latest`, which the storage check reads. The same pass counts the tenant's
  failed instances, its calls with an unknown outcome and its open dead letters, the failed work that
  its [operator console](operator-console.md) lists, and stores the three counts in that row, which
  the provisioning API answers as `failedWork`. The same pass brings the deny step of each tenant
  with realm details in step with its `users` quota, and skips a tenant without realm details, such
  as a preview tenant. A failure for one tenant is logged as a warning that names it, and the pass
  goes on with the other tenants.
- **Database bytes and file bytes.** File bytes are the on-disk size of the stored file contents, the
  table `axis.file_contents` with its indexes and TOAST data. Database bytes are `pg_database_size`
  of the tenant database minus the file bytes. A stored file sits inside the tenant database, so this
  split counts each byte once. A size on disk does not shrink after a delete until PostgreSQL vacuums
  the table.
- **Daily rows.** `platform.tenant_usage` holds one row per tenant and day (UTC): the largest storage
  sample of the day, split into database and file bytes, the users and applications at the end of
  the day, the emails sent, the connector and package calls, and the work items run. A sample
  replaces the day's pair of sizes only when its total is larger. Rows are kept for 13 months, and
  the sampling job deletes the tenant's rows older than that.
- **Users, applications and emails sent** _(planned for M31)_. The daily row's users, applications
  and emails-sent columns are filled from the counts of their quotas, see [quotas](#quotas) and
  [emails a day](#emails-a-day).
- **Authoring requests** _(planned for M35)_. The daily row also holds the authoring requests sent
  and the tokens of each model, with a tenant's own key counted apart, see
  [authoring requests](authoring-requests.md#audit).
- **Counts.** Each work item the worker runs adds one to the day's work-item count, and a connector
  call or a package call adds one to the day's call count too. A retry that the handler reschedules
  counts, because every attempt is real work. The count is added in a short transaction on the
  platform database right after the work item's transaction in the tenant database ended. The two
  databases cannot commit together, so a worker that stops between them loses one count, and a failed
  write is logged and never fails the work item. An item whose claim another worker took counts
  nowhere in this worker.
- **Who writes.** The platform role of `ConnectionStrings:Platform` may insert, update and delete
  rows of both usage tables, see [storage](storage.md#database-roles).
- **Readers.** The [cell console](cell-console.md#views) shows the usage through the
  [provisioning API's tenant reads](tenant-lifecycle.md#routes), which answer `usage`. The single tenant
  read also answers the day rows of the last 13 months. A tenant's administrator reads it through
  [the usage route](#usage).
- **The usage route.** `GET /api/tenant/usage` on a tenant's own host answers
  `{ quotas, days }` to an administrator. `quotas` holds every quota by its key as `{ limit, used }`,
  with a `null` limit for no limit. `used` is the latest storage sample, the realm's enabled users,
  Studio's application count and the emails sent today. `storage.used` is `null` until the worker has
  taken the tenant's first sample, and `emailsPerDay.used` is 0 when no email was sent today. A
  concurrency quota, `workerWeight` and `authoringRequests` answer `used: null`, and so does `users`
  when the identity provider cannot count the users, with a warning in the server's log. `days` holds
  the tenant's own rows of today and the 29 days before it, newest first, with the columns of
  `platform.tenant_usage` and nothing else. The route checks in the order of the
  [read routes](user-administration.md#read-routes) and answers `503` when the platform database
  cannot be read.
- **Plans** _(planned for M34)_. The [plans](plans.md) and their prices read the usage.
- **The tenant's own usage.** A tenant administrator sees the tenant's own quotas and its usage of the last 30 days
  in the users page's "Usage" section, see [platform pages](platform-pages.md#export-data-and-usage).
- **The billing portal's usage** _(planned for M33)_. The [billing portal](billing-portal.md#pages) shows the tenant's
  quotas and usage to its owner.

A tenant's administrator reads the usage through one route of the tenant's own host.

| Route                   | What it does                                              |
| ----------------------- | --------------------------------------------------------- |
| `GET /api/tenant/usage` | Answers `{ quotas, days }`, see [the usage route](#usage) |
