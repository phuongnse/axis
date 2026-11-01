# Cells

Detailed reference for the cell: the installation of Axis that Axis runs as a SaaS for many tenants.
It follows [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_. The conventions of [architecture.md](../architecture.md)
apply: sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

A cell is the same software as a self-hosted installation. It adds the per-tenant
[Studio folders](host-settings.md#studio-settings), [quotas and usage](tenant-quotas.md),
[fair work in the worker](worker-scheduling.md), the [provisioning API](tenant-lifecycle.md), the
[cell console](cell-console.md) and [daily backups](tenant-backup-and-restore.md#backups-in-a-cell).
A host runs as a cell when `Cell:Id` is set, see [cell settings](host-settings.md#cell-settings).

## Regions and cells

- **Region.** A region is a place where customer data is kept, named by a short code such as `vn`.
  Every part of a cell, its backups and its object storage included, runs in the cell's region. A
  tenant's data never leaves its region. The first region is `vn`.
- **Cell.** A cell is one full installation in one region: the server replicas, the worker, the
  publisher, the managed PostgreSQL with PgBouncer, Axis's identity provider and object storage.
  Its id is the region and a number, such as `vn-1`. Every tenant lives in exactly one cell.
- **What a cell shares.** The tenants of a cell share its CPU, memory, database server, identity
  provider and mail domain. They share no database, realm, file, key, backup or Studio folder.
- **One failure, one cell.** A restart or a failure of a cell touches only its tenants. A second
  cell in a region is a second installation, with nothing shared but the region's DNS zone.
- **A dedicated cell.** A customer that needs its own resources gets a cell with only its tenants.
  It is the same operation as any new cell.
- **Moving a tenant between cells or regions.** It is out of scope, as D118 agrees.

## Hosts and certificates

- **Tenant subdomain.** Each region has a domain for its tenants, `Cell:TenantDomain`, such as
  `vn.example.com`. A new tenant gets the host `<tenant id>.<tenant domain>`. The DNS zone has a
  wildcard record `*.<tenant domain>` that points at the cell's ingress. A second cell in the
  region gets its tenants' records one by one, instead of the wildcard.
- **Wildcard certificate.** The cell's ingress serves one certificate for `*.<tenant domain>`.
  cert-manager gets and renews it over ACME with the DNS-01 challenge, because a wildcard needs DNS
  validation. The DNS provider's token is a Kubernetes Secret of the cell, never a setting.
- **A host of the tenant's own.** A tenant administrator, or the operator through the
  [provisioning API](tenant-lifecycle.md#hosts), adds a host such as `approvals.acme.vn`.
  1. The cell answers with a verification token. The customer adds the DNS record
     `_axis-verify.<host>` of type `TXT` with that token, and a `CNAME` of the host to the
     tenant's subdomain.
  2. A cell job looks the record up every 10 minutes for 7 days. When it matches, the host is
     `verified`. After 7 days without a match the request expires.
  3. Only then does the cell create a cert-manager `Certificate` for the host, with the HTTP-01
     challenge, and add the host to the tenant, which keeps the realm's redirect addresses in step
     as `tenant host add` does.
  4. A host that is not verified is never served: the ingress has no route for it, and the server
     answers `404` for it, as for any unknown host.
  - A host that a deleted tenant used is held back for 90 days, see
    [deleting a tenant](tenant-lifecycle.md#deleting-a-tenant).
- **Routing.** Inside the cell, request resolution is the same as today, see
  [request resolution](../architecture.md#request-resolution). The directory of regions and cells
  that sends a sign-up or a host to its cell is M33's, see D118.

## Connections

A cell holds up to 1,000 tenant databases on one managed PostgreSQL server. A pool of each tenant in
each server replica and in the worker would need far more connections than PostgreSQL allows, so the
cell puts PgBouncer in front of it.

- **PgBouncer in transaction mode.** The server and the worker reach tenant databases with the
  runtime role through PgBouncer, which lends a server connection for one transaction and takes it
  back at the commit. Starting values, which the [cell load test](performance.md#cell-load-test)
  confirms or changes:
  - PgBouncer `default_pool_size` 3 for each database and role, `max_db_connections` 10,
    `max_client_conn` 10,000.
  - Npgsql `Maximum Pool Size` 5 and `Minimum Pool Size` 0 for each tenant, with
    `Connection Idle Lifetime` 60 seconds, so an idle tenant holds no connection.
  - PostgreSQL `max_connections` stays at the managed service's size, with 40 kept for direct
    connections.
- **What transaction mode allows.** A transaction may take `pg_advisory_xact_lock` and use
  `SET LOCAL`, which end with it. Axis's run-time locks are of that kind. Nothing on a pooled
  connection may rely on state that outlives a transaction: a session advisory lock, a plain `SET`,
  a prepared statement kept by name, `LISTEN` or a temporary table. Npgsql's automatic preparation
  is off on pooled connections.
- **Direct connections.** These connect to PostgreSQL directly, not through PgBouncer:
  - `Axis.Admin` and the publisher, with the schema role, because activation and provisioning take
    session advisory locks and change tables;
  - the worker's one `LISTEN` connection on the platform database, see
    [waking a tenant](worker-scheduling.md#waking-a-tenant);
  - the platform database connections of the server and the worker, which are few.
- **Settings.** The connection strings name PgBouncer's address for the runtime role and
  PostgreSQL's for the rest. TLS with a full certificate check holds on both legs, as
  [TLS](installation.md#tls) asks.

## Cell size

- **Built for 1,000.** A cell is built to hold 1,000 tenants with their realms, as D118 agrees.
- **The measured number is the cap.** The [cell load test](performance.md#cell-load-test) measures
  how many tenants a cell holds. That number caps the cell, and a new cell opens at 80% of it. If
  the identity provider holds fewer than 1,000 realms, the cap is the number it holds, and each
  tenant still keeps its own realm, as D123 agrees.
- **Counting.** Every tenant in the directory counts, suspended ones and those waiting to be deleted
  included. Preview tenants count too, because they hold a database.
- **Watching it.** The [cell console](cell-console.md#views) shows the count against the cap.
  `Cell:TenantCap` holds the measured cap, and the provisioning API refuses a new tenant past it
  with `409` problem details titled "This cell is full.".
