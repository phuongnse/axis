# Tenant quotas and usage

Detailed reference for the quotas of a tenant in a [cell](cells.md) and the usage that Axis measures.
It follows [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_, except what the next paragraph says is in place. A
self-hosted installation has no quotas: they are on only when `Cell:Id` is set.

In place: the `platform.tenant_quotas` table, the cell's defaults and the reading of both with the
directory. The server and the worker answer a tenant's effective quota from them, and answer no limit
outside a cell. The worker in a cell also samples each tenant's storage and counts the work items it runs, as
[usage](#usage) says. It counts each email it sends in `platform.tenant_daily_counters` and holds an email past
`emailsPerDay` until 00:00 UTC, as [Emails a day](#emails-a-day) says. The refusal answer, the `quota.refused` log
event, the `applications` refusal of Studio and the `users` refusal of the invite and enable routes are in place. So
are the `storage` refusals of record creates and updates, imports, file uploads, inbound endpoint calls and Studio
saves and publishes. So are `platform.tenant_leases`, the `concurrentQueries` lease of a record list read and a data
source rows read, and the `concurrentConnectorCalls` lease of a connector or package call in the worker. The realm's first-login flow holds a deny step, and `RealmUsersQuota.RefreshAsync` switches it on at the `users` quota and off below it. In a cell the server calls it after a successful invite, enable or disable and after each brokered sign-in, so the `users` refusal of a brokered person's first sign-in is in place too. A failed call is logged and never fails the command or the sign-in. The other `users` refusals and the other leases are
still planned, and so are the usage columns that the text marks. The conventions of
[architecture.md](../architecture.md) apply.

The [rate limits](headers-and-rate-limits.md) and the query timeouts stay as they are. A quota limits
what one tenant uses in total or at once. A rate limit limits how fast one caller asks.

## Quotas

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
| `authoringRequests` | AI authoring requests a month, counted when the author sends one, see [budgets](authoring-requests.md#budgets) _(planned for M35)_ | — | Exact |

- **Where they live.** `platform.tenant_quotas` holds one row for each quota a tenant overrides:
  `tenant_id`, `quota`, `value` and `updated_at`. A quota with no row takes the cell's default,
  `Quotas:Defaults:<quota>`, see [cell settings](host-settings.md#cell-settings). A `value` of
  `null` means no limit. _(planned for M34)_ The [control plane](control-plane.md) writes a SaaS
tenant's rows from its [plan](plans.md#from-a-plan-to-the-quotas), with no schema change.
- **Who sets them.** The operator sets a tenant's quotas through the
  [provisioning API](tenant-lifecycle.md#provisioning-api). Each change writes a platform audit
  record. The hosts read the quotas with the directory, at each `Tenancy:RefreshInterval`.
- **Lowering a quota below use.** It refuses new use and removes nothing. A tenant over its storage
  quota keeps its data and can still delete.

## Refusals

A request past a quota answers `403` problem details whose `reason` is `quota-exceeded`:

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

- The title names the quota in words, and `quota` names its key. Neither names the tenant.
  `reason` tells a quota refusal apart from a `403` of a policy, which has no `reason`.
- `used` is the last sample for a sampled quota and the exact count otherwise.
- The check is an endpoint filter that runs after the sign-in check (`401`) and before the path (`404`) and the
  grant (`403`), so a signed-in caller without a grant also sees the tenant's limit and usage.
- The check comes before body validation, validations, the
  [module contract](module-contracts.md#where-the-contract-is-checked)
  check and any write, so a refused request evaluates nothing and writes nothing, not even an audit
  record. It writes a log event `quota.refused` with the tenant and the quota.
- The SPA shows the title, and an administrator sees which quota on the usage page of M33.

**What each quota refuses.**

- **`storage`.** Once the last sample is at or over the quota, these are refused: a record create
  or update through the record API, an import, an inbound endpoint call that writes, a file upload,
  a save in Studio and a publish. A delete is never refused. Process steps, task completions,
  effects and triggers go on, so running work does not fail half-way. The overshoot is what the
  tenant writes in one sampling interval. A tenant with no sample yet is not refused, and neither is a
  write when the platform database cannot be read: the server logs a warning and the write goes on, because a quota
  is a billing limit and not a security boundary. The server reads the last sample on each such write.
- **`users`.** Inviting an account and enabling one on the [users page](user-administration.md) are
  refused when the count would pass the quota. A person who signs in through a brokered provider
  for the first time, and would pass the quota, is refused at the identity provider with a page
  that asks them to contact their administrator.
- **`applications`.** Creating an application in Studio is refused when the count would pass the
  quota. Existing applications keep working.

## Work at once

D118 agrees that work past a concurrency quota waits its turn instead of failing.

- **Leases.** `platform.tenant_leases` holds one row for each piece of work that runs: `tenant_id`,
  `quota`, `lease_id`, `holder` (the host and process) and `expires_at`. To start, a host takes a
  transaction advisory lock on the tenant and quota, counts the unexpired rows and inserts one when
  the count is under the quota. A lease lasts 60 seconds and its holder renews it every 20 seconds.
  It is deleted when the work ends. A host that dies leaves leases that expire within a minute.
- **Shared by every host.** The count is one for the whole cell, whatever the number of replicas and
  workers. A limit kept in each host's memory would multiply with the hosts.
- **What waits, and how.**
  - In a cell, the dry run or the commit of an import takes a `concurrentImports` lease before it
    runs. When none is free it does not run. Its work item is due again in 1 second and the import
    keeps its state, `pending` or `checked`, so any worker can run it once a lease is free. The
    lease is held until the work has committed.
  - A connector or package call is not claimed by the worker while the tenant has no lease free.
    The work item stays due, and the worker runs other tenants' work. The worker retries the claim
    at the tenant's next turn, after one of its items ends or at the next poll. The lease is held
    until the run of the item ends.
  - A data source query, a record list read, a record export or a data source export waits up to
    5 seconds for a lease. It then answers `503` problem details titled "This tenant is busy. Try
    again in a moment.", with `Retry-After: 1`. The query timeout counts from the start of the
    query, not of the wait. A query or a list read takes a `concurrentQueries` lease and an export
    takes a `concurrentImports` lease, so a busy slot of one never blocks the other. An export
    releases its lease when its file is complete, before the file is sent.
- **When the platform database is down.** A read, an export, an import or a connector or package call that cannot reach the
  platform database runs without a lease, and the server or the worker logs a warning. Under `directory` the server keeps serving from the last good
  directory then, and the limit would otherwise turn a platform outage into a cell-wide read outage.
- **Cost.** A lease costs two short statements on the platform database. The
  [cell load test](performance.md#cell-load-test) shows whether the objectives still hold with them.

## Emails a day

- **Counting.** In a cell the worker takes one slot of the tenant's count for the day just before it
  sends an email. `platform.tenant_daily_counters` holds the count, keyed by tenant, day (UTC) and
  counter, and emails use the counter `emails`. One statement on the platform database checks the
  quota and adds one, so workers that send at the same time cannot pass it. It is a short
  transaction of its own, since the platform and the tenant databases cannot commit together.
- **Failure.** A send that fails gives its slot back. A worker that crashes between the send and the
  mark of the row sends the email again and counts it twice. A send cancelled at shutdown keeps its slot.
- **Held emails.** An email past the quota is held, not dropped. Its row stays `pending` and its work
  item is due at the next 00:00 UTC by the worker's clock. After midnight held emails are sent in the
  order their items were queued, to the millisecond, and count against the new day's quota. A quota of
  0 holds every email. A tenant with no limit is counted and never held.
- **In-app notifications** are never held. A `notify` step stores its in-app notification when the
  day's emails are used up.
- **Platform database down.** When a slot cannot be taken, the email is not sent, its item is due again
  after one lease, and the worker logs one warning for the tenant.
- **Outside a cell** nothing is counted or held.
- The platform reader role gets `INSERT` and `UPDATE` on the table, and no `DELETE`. Old rows are kept,
  one small row for each tenant and day.
- The tenant's administrators get one in-app notification the first time a day that emails are held.
  _(planned for M31)_

## Usage

- **Sampling.** A worker job in a cell samples each tenant's storage every hour. It keeps the
  newest sample in `platform.tenant_usage_latest`, which the storage check reads. A worker with no
  `Cell:Id` measures nothing and writes no usage row.
- **Database bytes and file bytes.** File bytes are the on-disk size of the stored file contents,
  the table `axis.file_contents` with its indexes and TOAST data. Database bytes are
  `pg_database_size` of the tenant database minus the file bytes. A stored file sits inside the tenant
  database, so this split counts each byte once. A file kept outside the database would need its own
  measurement. A size on disk does not shrink after a delete until PostgreSQL vacuums the
  table.
- **Daily rows.** `platform.tenant_usage` holds one row per tenant and day (UTC): the largest
  storage sample of the day, split into database and file bytes, the users and applications at the
  end of the day, the emails sent, the connector and package calls, and the work items run. A sample
  replaces the day's pair of sizes only when its total is larger. Rows are
  kept for 13 months. The sampling job deletes the tenant's rows older than that.
  The users, applications and emails-sent columns exist, but nothing fills them yet.
  _(planned for M31)_ Each is filled by its own quota, see [Quotas](#quotas) and
  [Emails a day](#emails-a-day). _(planned for M35)_ Rows also hold the authoring requests sent
  and the tokens of each model, with a tenant's own key counted apart, see
  [authoring requests](authoring-requests.md#audit).
- **Counts.** Each work item the worker runs adds one to the day's work-item count, and a connector
  call or a package call adds one to the day's call count too. A retry that the handler reschedules
  counts, as every attempt is real work. The count is added in a short transaction on the platform database right
  after the work item's transaction in the tenant database ended. The two databases cannot commit
  together, so a worker that stops between them loses one count, and a failed write is logged and
  never fails the work item. An item whose claim another worker took counts nowhere in this worker.
- **Who writes.** The platform role of `ConnectionStrings:Platform` may insert, update and delete
  rows of both usage tables, see [storage](storage.md#database-roles).
- **Readers.** The [cell console](cell-console.md#views) shows the usage. M34's plans and prices
  read it, as D118 agrees. _(planned for M33)_ A tenant administrator sees the tenant's own quotas
  and its usage of the last 30 days on the users page's "Usage" section, and the
  [billing portal](billing-portal.md#pages) shows them to its owner.
- **No business data.** Usage holds counts and sizes only, never a record value, a file name or a
  user name.
