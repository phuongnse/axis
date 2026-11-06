# Region directory

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

_(planned for M33)_

The directory sends a new tenant to the least full cell of its region that has room.

- **Room.** A new tenant goes to a cell of its region that takes new tenants and is under 80% of its
  [cap](cells.md#cell-size). Of those, it goes to the least full.
- **A full cell.** A cell that answers `409` "This cell is full." is marked full, and the next one is
  tried.
- **Written on acceptance.** When the cell accepts the create, the tenant row is written and the
  cell's count goes up by one, in one transaction.
- **No cell left.** The sign-up waits as `queued`, the operator is alerted to open a cell, and the
  person is told by email when their tenant is ready.
- **Counts stay true.** Every 5 minutes the directory reads each cell's tenant list through the
  provisioning API and corrects its counts and its rows, so a tenant created or deleted from a cell
  console is found. The control plane queues one durable job every 5 minutes. The job reads every
  cell of the region and corrects each one in one transaction: it adds the tenants and hosts the
  directory lacks, removes those the cell no longer lists and sets the cell's count to the length of
  its list. A cell that cannot be reached or answers an error keeps its rows, a warning log names it,
  and the other cells are still corrected. A tenant or host that the directory holds under another
  cell stays where it is, and a warning log counts it. Removing a tenant writes nothing to the held
  ids and hosts. The job runs only when the directory's connection string is set and a cell is
  listed. A tenant that the correction added before the picker wrote it is not written or counted
  twice.

## Routing a host

_(planned for M33)_

A tenant's traffic goes from DNS straight to its cell, so nothing global sits in its path.

- **DNS, not a router.** Each tenant's subdomain gets a `CNAME` record of its own to its cell's
  ingress, `ingress.<cell id>.<tenant domain>`, with a time to live of 300 seconds. The control plane
  writes the record with an RFC 2136 dynamic update over TCP, signed with a TSIG HMAC-SHA256 key, when
  it creates the tenant, and removes it when the tenant is deleted. A set deletes the name's `CNAME`
  record set and adds the new record in one update, and a remove deletes the record set, so both are
  idempotent. The [settings](control-plane.md#settings) `ControlPlane:Dns` name the server, the zone and
  the key.
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
