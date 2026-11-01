# D123. How an Axis SaaS cell keeps tenants apart, measured and recoverable

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets the mechanics that
[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) left open, so that M31 can be
broken into issues. It adds to D118 and changes none of its points. Everything here is
_(planned for M31)_. The detail is in [cells](../reference/cells.md),
[tenant quotas](../reference/tenant-quotas.md), [worker scheduling](../reference/worker-scheduling.md),
[tenant lifecycle](../reference/tenant-lifecycle.md), [cell console](../reference/cell-console.md) and
[backups in a cell](../reference/tenant-backup-and-restore.md#backups-in-a-cell).

- **Connections:** the hosts reach the cell's PostgreSQL through PgBouncer in transaction mode, with
  small Npgsql pools for each tenant. Work that needs a session of its own, such as a session
  advisory lock, `LISTEN` or a schema change, connects directly.
  - _Why:_ 1,000 tenant databases with a pool each in every replica and the worker would need tens
    of thousands of PostgreSQL connections. A pooler in transaction mode is the standard answer for
    many databases on one server. Axis already takes its run-time locks per transaction.
- **Fair work in the worker:** the worker runs weighted deficit round-robin over the tenants that
  have work due, with several loops that claim at once. A tenant with no work is polled less and
  less often, and a wake signal brings it back at once when new work arrives.
  - _Why:_ today the worker runs one tenant until it has no work left, so one backlog holds back
    every other tenant. Polling 1,000 idle databases every second would cost 1,000 queries a second
    for nothing. A weight lets a plan of M34 buy a larger share without a new algorithm.
- **Quotas and usage:**
  - Quotas live in `platform.tenant_quotas`. The cell's settings hold the defaults, and a row
    overrides one quota for one tenant, so M34's plans write rows without a schema change.
  - A request past a quota is refused with `403` problem details, with the reason `quota-exceeded`, that name
    the quota. `429` stays for rate limits.
  - Work-at-once quotas count leases in the platform database, shared by every replica and worker.
  - Storage is sampled by a job and kept as one usage row a day for 13 months. A write is refused
    once the last sample is over the quota, a soft limit. Users, applications and emails a day are
    counted exactly when they change.
  - The quota check comes before validations and before the module contract check of
    [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md), so a write over a quota
    is refused without evaluating anything.
  - _Why:_ this is how large services, such as GitHub and Google Cloud, answer a quota. A limit for
    each replica multiplies with the replicas, the flaw the rate limits have today. Sampling storage
    costs nothing on the write path, and the overshoot is bounded by the sampling interval.
- **Backups:**
  - Each tenant has a backup key of its own, wrapped by the cell's backup key. Deleting the tenant
    destroys its key, so its archives can no longer be read (crypto-shredding).
  - The managed PostgreSQL keeps point-in-time recovery for the whole cluster. A scheduled job also
    writes each tenant's encrypted archive every day to object storage in the region, with an object
    lock and a lifecycle rule of 30 days.
  - The archive also holds the tenant's realm, with its users, role mappings and credentials, so a
    restore gives the tenant back as it was.
  - Each week a restore of a random tenant's newest archive is tried in a scratch tenant, checked,
    and deleted. The result is a metric.
  - _Why:_ with one key for every tenant, a deleted tenant stays readable in 30 days of archives, and
    one leaked key opens every tenant. Object lock keeps an attacker or a mistake from deleting the
    archives. A restore that drops the users is not a restore a small business can use. A backup
    that is never restored is not known to work.
- **Studio folders:** in a cell each tenant's applications are under
  `<Studio:ApplicationsPath>/<tenant id>/`, `Studio:Folders` is refused, and the folder joins the
  tenant's backup, export and deletion.
  - _Why:_ it keeps files first, as [D5](d5-configuration-is-authored-as-files-first.md) and
    [D38](d38-studio-edits-application-folders-through-an-authoring-api.md) agree, and gives each
    tenant a place no other tenant's author reaches.
- **Hosts:** each region has a wildcard certificate for its tenant subdomains. A host of a
  customer's own is served only after a DNS `TXT` record proves that the customer controls it, and
  then gets its certificate from cert-manager over ACME.
  - _Why:_ a host that is not verified could serve a domain the customer does not own.
- **Tenant lifecycle:**
  - A provisioning API on the cell creates, suspends, resumes, exports and deletes tenants and sets
    their quotas and hosts. The cell console, the `Axis.Admin` commands of a cell and M33's control
    plane all call it, so each operation has one implementation.
  - An export is a portable ZIP: JSON Lines and CSV for each entity, the files, the application
    folders and the list of users. It is built so a tenant administrator can run it in M33.
  - A deletion suspends the tenant, waits out a grace window, then removes the database, its roles,
    the realm, the files, the Studio folder and the keys, and leaves a tombstone. A deleted tenant's
    id is never used again, and its hosts are held back for 90 days.
  - _Why:_ D118 says the control plane provisions through an API. An export that only Axis can read
    is a backup, not an export. A grace window turns a wrong deletion into a mistake that can be
    undone. Reusing a host at once lets a new tenant receive the old tenant's links and emails.
- **Cell console:** the platform operator sees every tenant of the cell with its status, usage and
  failed work on a console of its own, on a host of its own. The operator signs in through an
  operator realm that is apart from every tenant realm and needs a second factor.
  - _Why:_ the owner must not be a user in every customer's realm, and a customer's realm must not
    reach other tenants. This is a new trust boundary for the
    [threat model](../reference/threat-model.md).
- **Cell size:** a load test of 1,000 tenants with their realms, of mixed sizes, measures what a
  cell holds. The measured number is the cell's cap, and a new cell opens at 80% of it, as D118
  agrees. If the identity provider holds fewer realms than 1,000, the cap is that number, and each
  tenant keeps its own realm, as [D99](d99-axis-ships-its-own-identity-provider.md) and
  [D100](d100-identity-providers-per-application-and-sign-in-per-site.md) agree.
  - _Why:_ moving every tenant into one realm would undo the isolation that D99 and D100 chose. A
    smaller cell costs only more cells.

_Why:_ D118 chose what a cell must do and left how to the milestone. Each choice here is the common
practice for a multi-tenant service on PostgreSQL and Kubernetes, and each keeps the platform as it
is built: a tenant in a cell is still one database and one realm. The cost is more parts to run: a
pooler, a wake signal, a lease table, a key per tenant, a cell console and its realm, and a load
test that needs a real cell to run.
