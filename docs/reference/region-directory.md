# Region directory

Detailed reference for the directory of regions, cells, tenants and hosts that the
[control plane](control-plane.md) keeps. It follows
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md). Everything in this file is
_(planned for M33)_. Inside a cell, request resolution stays as
[architecture](../architecture.md#request-resolution) describes. The conventions of
[architecture.md](../architecture.md) apply.

## What it holds

| Table | Rows |
| --- | --- |
| `directory.regions` | Each region: its code, such as `vn`, its tenant domain and its control plane's address |
| `directory.cells` | Each cell: its id, such as `vn-1`, its region, its internal address, its cap, its tenant count and whether it takes new tenants |
| `directory.tenants` | Each tenant: its id, its region and its cell |
| `directory.hosts` | Each host: the host name, its tenant and its cell |
| `directory.held` | Tenant ids that are never used again, and hosts held back for 90 days after a deletion |

- **No personal data.** No name, email, plan or payment. A tenant id is the name a person chose for
  their subdomain, so it is checked against reserved words, not against people.
- **One owner.** Only the control plane writes it. A cell never reads or writes it.
- **Unique everywhere.** A tenant id is unique across every region, so a tenant never changes id if
  moving between regions is ever built.

## Picking a cell

- A new tenant goes to a cell of its region that takes new tenants and is under 80% of its cap, as
  D118 agrees. Of those, it goes to the least full.
- A cell that answers `409` "This cell is full." is marked full, and the next one is tried.
- With no cell left, the sign-up waits as `queued`, the operator is alerted to open a cell, and the
  person is told by email when their tenant is ready.
- **Counts stay true.** Every 5 minutes the directory reads each cell's tenant list through the
  provisioning API and corrects its counts and its rows, so a tenant created or deleted from a cell
  console is found.

## Routing a host

- **DNS, not a router.** Each tenant's subdomain gets a `CNAME` record of its own to its cell's
  ingress, `ingress.<cell id>.<tenant domain>`, with a time to live of 300 seconds. The control plane
  writes the record through the DNS provider's API when it creates the tenant, and removes it when the
  tenant is deleted.
- **No shared hop.** A request goes from DNS straight to the cell. Nothing global sits in its path, so
  a failure of the directory or the control plane never stops a tenant's traffic.
- **The wildcard goes.** Once the directory runs, the region's wildcard record of
  [hosts and certificates](cells.md#hosts-and-certificates) is removed, because every tenant has a
  record of its own. The wildcard certificate stays.
- **A host of the tenant's own.** The customer's `CNAME` points at the tenant's subdomain, as the
  cell's [host checks](cells.md#hosts-and-certificates) ask, so it follows the tenant's record.
- **Reserved names.** A tenant id cannot be `account`, `admin`, `api`, `app`, `auth`, `billing`,
  `console`, `docs`, `help`, `id`, `ingress`, `login`, `mail`, `signup`, `sign-up`, `static`,
  `status`, `support`, `www` or a cell id, because the region's own hosts use them.
