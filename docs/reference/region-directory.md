# Region directory

_(planned for M33)_

The region directory is the [control plane](control-plane.md)'s record of the regions, their cells,
and which cell holds each tenant and host. This page covers what it holds, how it picks a cell for a
new tenant, and how a host reaches its cell. Inside a cell, a request is resolved as
[architecture](../architecture.md#request-resolution) describes.

## What it holds

The directory holds ids, hosts and cells, and no personal data.

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
- **Unique everywhere.** A tenant id is unique across every region.

## Picking a cell

The directory sends a new tenant to the least full cell of its region that has room.

- **Room.** A new tenant goes to a cell of its region that takes new tenants and is under 80% of its
  [cap](cells.md#cell-size). Of those, it goes to the least full.
- **A full cell.** A cell that answers `409` "This cell is full." is marked full, and the next one is
  tried.
- **No cell left.** The sign-up waits as `queued`, the operator is alerted to open a cell, and the
  person is told by email when their tenant is ready.
- **Counts stay true.** Every 5 minutes the directory reads each cell's tenant list through the
  provisioning API and corrects its counts and its rows, so a tenant created or deleted from a cell
  console is found.

## Routing a host

A tenant's traffic goes from DNS straight to its cell, so nothing global sits in its path.

- **DNS, not a router.** Each tenant's subdomain gets a `CNAME` record of its own to its cell's
  ingress, `ingress.<cell id>.<tenant domain>`, with a time to live of 300 seconds. The control plane
  writes the record through the DNS provider's API when it creates the tenant, and removes it when the
  tenant is deleted.
- **No shared hop.** A request goes from DNS straight to the cell, so a failure of the directory or
  the control plane never stops a tenant's traffic.
- **No wildcard record.** Every tenant has a record of its own, so the region's DNS zone has no
  wildcard record. The wildcard certificate of
  [hosts and certificates](cells.md#hosts-and-certificates) stays.
- **A host of the tenant's own.** The customer's `CNAME` points at the tenant's subdomain, as the
  cell's [host checks](cells.md#hosts-and-certificates) ask, so it follows the tenant's record.
- **Reserved names.** A tenant id cannot be `account`, `admin`, `api`, `app`, `auth`, `billing`,
  `console`, `docs`, `help`, `id`, `ingress`, `login`, `mail`, `signup`, `sign-up`, `static`,
  `status`, `support`, `www` or a cell id, because the region's own hosts use them.
