# D118. Axis SaaS in cells, with sign-up, quotas and billing

Status: **Agreed**

## Context

[D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) serves small businesses and
individuals first, and they expect to sign up, try Axis and pay without installing anything or
talking to anyone. A hosted Axis that someone creates by hand, or that never takes a payment, is only
half a product. Many customers must share the same machines, while the platform keeps one model of
an installation, with a database and a realm for each tenant.

## Decision

- **One software, three ways to run:** a shared SaaS, a dedicated SaaS cell for one customer, and
  self-hosted, as the [product brief](../product-brief.md#ways-to-run-axis) says.
- **Cells in regions.** A [cell](../reference/cells.md) is one full installation of Axis, and every
  tenant lives in exactly one cell. A region is where customer data is kept, and a tenant's data,
  backups included, never leaves it. The first region is `vn`, and a new region opens when its first
  customer needs it, with no platform change. A region's cloud offers managed Kubernetes, managed
  PostgreSQL, S3-compatible object storage and backups in a second data centre of the same country.
- **A shared cell keeps tenants apart** in their data, their Studio applications and their resources,
  with [quotas](../reference/tenant-quotas.md), measured usage and
  [fair work in the worker](../reference/worker-scheduling.md).
  [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md) sets how.
- **Sign-up and a trial, with no free plan that never ends.** A person signs up and gets a tenant of
  their own as a 30-day trial. A tenant whose trial ended or that stopped paying becomes read-only,
  then is deleted, and its data is exportable until then.
  [D126](d126-sign-up-trials-and-the-control-plane.md) sets how.
- **A plan for the whole tenant.** A plan sets the tenant's quotas and has one price for the tenant,
  with a number of users in it, never a price for each user. A trial becomes paid without losing
  anything. [D127](d127-plans-payments-and-invoices.md) sets how.
- **Prices follow cost.** A plan's lowest price covers its share of a cell's fixed cost, counted with
  the cell 60% full of paying tenants, the cost of its quotas, the payment and invoice fees, time for
  support, and a margin. The prices come from what a running cell measures, and they are settings.
- **Axis's own business lives apart from the cells.** Sign-up, the directory of regions and cells,
  plans and billing run in a [control plane](../reference/control-plane.md), which provisions tenants
  through an API of the cell.
- **Moving a tenant between regions is out of scope.**

The milestones that build this, and their order, are in the [roadmap](../roadmap.md#milestones).

## Consequences

- A tenant in a cell is the same tenant as on any installation, so the platform needs no second
  tenancy model and its tests still hold.
- One failure, one identity provider restart or one slow neighbour touches only the tenants of one
  cell. A dedicated customer and a new region are the same operation as a new cell.
- Separate databases and realms keep data apart but still share CPU, memory, connections and the mail
  domain, so a shared cell needs quotas and fair work as much as isolation.
- Sign-up and billing never touch the engines, but there is a second service to build and run and a
  cell count to watch.
- Axis stays cheaper than tools priced per user.
- Running the service needs things outside the repository: a domain, a cloud provider, a lawyer's
  check of where Vietnamese customer data must be kept, and a company and an accountant before any
  payment.

## Alternatives

- **Every tenant in one large installation.** One failure, one restart or one slow neighbour would
  reach every customer, and a dedicated customer or a new region would need a different setup.
- **A free plan that never ends.** Abandoned tenants would fill the cells.
- **A price for each user.** A shared cell's cost grows with storage and work, not with users, so it
  would charge for what costs Axis nothing.
