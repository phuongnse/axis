# D123. How an Axis SaaS cell keeps tenants apart, measured and recoverable

Status: **Agreed**

## Context

[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) puts up to 1,000 tenants in a
cell, each with its own database and realm, and asks the cell to keep them apart, measure them and
recover them. A connection pool for each tenant in every host would need tens of thousands of
PostgreSQL connections, a worker that runs one tenant until it is done lets one backlog hold back
every tenant, and the platform operator must reach every tenant without being a user in any
customer's realm.

## Decision

- **Tenant connections go through PgBouncer in transaction mode.** Work that needs a session of its
  own connects directly. See [connections](../reference/cells.md#connections).
- **The worker shares its time by weighted deficit round-robin,** and a wake signal brings an idle
  tenant back at once, see [worker scheduling](../reference/worker-scheduling.md).
- **Quotas and their leases live in the platform database,** shared by every host. Storage is sampled
  and the other quotas are counted exactly. A refusal is a `403` that names the quota, and `429` stays
  for rate limits. See [tenant quotas](../reference/tenant-quotas.md).
- **Each tenant has a backup key of its own,** which its deletion destroys. A daily archive of each
  tenant, with its realm, goes to object storage in the region under an object lock, and a restore is
  tried every week. See [backups in a cell](../reference/tenant-backup-and-restore.md#backups-in-a-cell).
- **Each tenant has a Studio folder of its own,** which keeps files first, as
  [D5](d5-configuration-is-authored-as-files-first.md) and
  [D38](d38-studio-edits-application-folders-through-an-authoring-api.md) agree, see
  [Studio settings](../reference/host-settings.md#studio-settings).
- **A customer's own host is served only once DNS proves the customer controls it,** see
  [hosts and certificates](../reference/cells.md#hosts-and-certificates).
- **One provisioning API on the cell** creates, exports and deletes tenants and sets their quotas and
  hosts, for the cell console, the admin commands and the control plane alike. An export is a portable
  ZIP, and a deletion waits out a grace window. See [tenant lifecycle](../reference/tenant-lifecycle.md).
- **The platform operator works in a [cell console](../reference/cell-console.md),** signed in through
  an operator realm apart from every tenant realm, with a second factor.
- **A load test sets the cell's cap.** When the identity provider holds fewer than 1,000 realms, the
  cap is lower and each tenant keeps its own realm, as
  [D99](d99-axis-ships-its-own-identity-provider.md) and
  [D100](d100-identity-providers-per-application-and-sign-in-per-site.md) agree. See
  [cell size](../reference/cells.md#cell-size).

## Consequences

- A tenant in a cell is still one database and one realm, so the platform keeps one tenancy model.
- Sampling storage costs nothing on the write path, and a tenant can pass its quota only by what it
  writes between two samples.
- A deleted tenant's archives can no longer be read, one leaked key opens one tenant only, and nobody
  can delete the archives early.
- A wrong deletion can be undone during the grace window.
- The cell console and its realm are a new trust boundary for the
  [threat model](../reference/threat-model.md).
- There are more parts to run: a pooler, a wake signal, a lease table, a key for each tenant, a console
  and its realm, and a load test that needs a real cell.

## Alternatives

- **Every tenant in one realm,** so that a cell holds more tenants. It would undo the isolation that
  D99 and D100 chose, while a smaller cell costs only more cells.
- **Limits counted in each replica,** as the rate limits are. They would multiply with the replicas.
- **One backup key for every tenant.** A deleted tenant would stay readable in its archives, and one
  leaked key would open every tenant.
