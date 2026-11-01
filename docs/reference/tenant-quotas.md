# Tenant quotas and usage

Detailed reference for the quotas of a tenant in a [cell](cells.md) and the usage that Axis measures.
It follows [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_. A self-hosted installation has no quotas: they are on
only when `Cell:Id` is set. The conventions of [architecture.md](../architecture.md) apply.

The [rate limits](headers-and-rate-limits.md) and the query timeouts stay as they are. A quota limits
what one tenant uses in total or at once. A rate limit limits how fast one caller asks.

## Quotas

| Quota | What it counts | Default | Kind |
| --- | --- | --- | --- |
| `storage` | The tenant database's size plus the bytes of its stored files, in bytes | 10 GiB | Sampled |
| `users` | The enabled accounts of the tenant's realm, invited ones included | 50 | Exact |
| `applications` | The applications of the tenant: folders in its Studio folder and applications with a release | 10 | Exact |
| `emailsPerDay` | Emails sent for the tenant since 00:00 UTC | 1,000 | Exact |
| `concurrentImports` | Imports and exports that run at once | 2 | Lease |
| `concurrentConnectorCalls` | Connector and package calls in flight at once | 10 | Lease |
| `concurrentQueries` | Data source queries and record list reads that run at once | 10 | Lease |
| `workerWeight` | The tenant's share of the worker, see [worker scheduling](worker-scheduling.md) | 1 | Weight |
| `authoringRequests` | AI authoring requests, see [D119](../decisions/d119-ai-authoring-in-studio.md) _(planned for M35)_ | — | Exact |

- **Where they live.** `platform.tenant_quotas` holds one row for each quota a tenant overrides:
  `tenant_id`, `quota`, `value` and `updated_at`. A quota with no row takes the cell's default,
  `Quotas:Defaults:<quota>`, see [cell settings](host-settings.md#cell-settings). A `value` of
  `null` means no limit. M34's plans write rows here, with no schema change.
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
  tenant writes in one sampling interval.
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
  - An import or an export waits in its queue, and the SPA shows it as queued.
  - A connector or package call is not claimed by the worker while the tenant has no lease free.
    The work item stays due, and the worker runs other tenants' work.
  - A data source query or a record list read waits up to 5 seconds for a lease. It then answers
    `503` problem details titled "This tenant is busy. Try again in a moment.", with
    `Retry-After: 1`. The query timeout counts from the start of the query, not of the wait.
- **Cost.** A lease costs two short statements on the platform database. The
  [cell load test](performance.md#cell-load-test) shows whether the objectives still hold with them.

## Emails a day

- The worker counts each email it sends in `platform.tenant_daily_counters`, keyed by tenant, day
  (UTC) and counter, in the transaction that marks the email sent.
- An email past the quota is held, not dropped. Held emails are sent in order after 00:00 UTC, still
  under the next day's quota. In-app notifications are never held.
- The tenant's administrators get one in-app notification the first time a day that emails are held.

## Usage

- **Sampling.** A worker job samples each tenant's storage every hour: `pg_database_size` of its
  database plus the bytes of its stored files. It keeps the newest sample in
  `platform.tenant_usage_latest`, which the storage check reads.
- **Daily rows.** `platform.tenant_usage` holds one row per tenant and day (UTC): the largest
  storage sample of the day, split into database and file bytes, the users and applications at the
  end of the day, the emails sent, the connector and package calls, and the work items run. Rows are
  kept for 13 months, then deleted.
- **Readers.** The [cell console](cell-console.md#views) shows the usage. M34's plans and prices
  read it, as D118 agrees. A tenant administrator sees the tenant's own usage in M33.
- **No business data.** Usage holds counts and sizes only, never a record value, a file name or a
  user name.
