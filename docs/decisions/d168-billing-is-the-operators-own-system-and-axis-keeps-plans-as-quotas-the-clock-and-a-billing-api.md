# D168. Billing is the operator's own system, and Axis keeps plans as quotas, the clock and a billing API

Status: **Agreed**

## Context

Axis is open source. An operator, such as a hosting company or an organization's IT team, can run
Axis as a service for its own customers.
[D166](d166-axis-is-designed-for-small-businesses-first-and-the-milestones-in-three-phases.md)
designs Axis first for small businesses and individuals, and they expect to sign up, try Axis and
use it without installing anything or talking to anyone. Many customers must share the same machines,
while the platform keeps one model of an installation, with a database and a realm for each tenant.

Billing is the operator's own business: its prices, its currencies, its invoices, its payment
providers and its tax rules. Built into Axis, it would tie every operator to one country's providers
and rules. It would also put the operator's billing under the AGPL, so that an operator who changed its
prices or its payment provider would have to publish that change.

This decision replaces D126, D127 and D167. It keeps what still holds of D126 and D167, and it drops
D127 whole.

## Decision

- **One software, three ways to run:** a shared SaaS, a dedicated SaaS cell for one customer, and
  self-hosted, as the [product brief](../product-brief.md#ways-to-run-axis) says.
- **An operator runs Axis SaaS.** It runs the cells, the regions and the control plane, and its tenants
  are its customers.
- **Cells in regions.** A [cell](../reference/cells.md) is one full installation of Axis, and every
  tenant lives in exactly one cell. A region is where customer data is kept, and a tenant's data,
  backups included, never leaves it. The operator opens each region with no platform change, and `vn`
  is an example. A region's cloud offers managed Kubernetes, managed PostgreSQL, S3-compatible object
  storage and backups in a second data centre of the same country.
- **A shared cell keeps tenants apart** in their data, their Studio applications and their resources,
  with [quotas](../reference/tenant-quotas.md), measured usage and
  [fair work in the worker](../reference/worker-scheduling.md).
  [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md) sets how.
- **The control plane is a .NET service of its own,** never in a cell. It reaches a cell only through
  the provisioning API, over a private network with mutual TLS, and a cell never calls it. The
  [control plane](../reference/control-plane.md) holds sign-up, the directory of regions and cells,
  provisioning, subscriptions and plans.
- **Only routing is global.** A global [directory](../reference/region-directory.md) holds which cell
  holds each tenant and host, with no personal data, and DNS routes each tenant's host to its cell. A
  control plane in each region holds that region's accounts and subscriptions.
- **Sign-up and a trial, with no free plan that never ends.** A person signs up and gets a tenant of
  their own as a 30-day trial. No tenant exists until the person verifies their email, and
  [sign-up](../reference/sign-up.md) serves its own proof of work. A tenant that no billing system
  has paid through a date becomes read-only when its trial ends, then is deleted, and its data is
  exportable until then.
- **One clock, in the control plane.** A [subscription](../reference/subscriptions.md)'s state machine
  owns every date and calls the cell, which knows no commerce.
- **Read-only, not suspended.** A tenant whose trial ended or whose paid-through date and grace have
  passed is read-only: people sign in, read and export, and nothing is written. A full suspension stays
  for the operator.
- **Moving a tenant between regions is out of scope.**

What Axis keeps of the commercial life of a tenant:

- **Axis keeps** sign-up, the region directory, provisioning, the acceptance of the terms at sign-up,
  the subscription and its clock, the emails of the trial, of read-only and of deletion,
  [quotas](../reference/tenant-quotas.md) and the platform operator's overrides.
- **A plan is a named set of quotas.** It has a version and the users included in it, and no price and
  no currency. A plan is for the whole tenant, never for each user. A user pack is an add-on that adds
  users to the quota. The prices are in the operator's billing system, see [plans](../reference/plans.md).
- **A billing API for the operator's billing system.** It has a client and a scope of its own. The
  billing system sets a tenant's plan and add-ons and the date the tenant is paid through, ends a
  subscription at its paid-through date, and reads a tenant's state and usage. Each call is idempotent
  and audited, and the API is a public contract with a version. See [billing API](../reference/billing-api.md).
- **The clock stays in Axis.** A tenant paid through a date is `active`. Past that date Axis moves it
  to past due, then to read-only after the grace, then to deleting, as it does for an ended trial. A new
  paid-through date brings it back, see [subscriptions](../reference/subscriptions.md).
- **A downgrade is refused** while the tenant's use is over the smaller plan.
- **Axis sends signed events** to the operator's billing system: a tenant is created, a subscription
  changes state, and each tenant's daily usage. Deliveries retry, and each event has an id that stays
  the same on every retry. See [events](../reference/billing-api.md#events).
- **A tenant administrator sees** the plan, each quota with its use, the subscription's state and its
  next date in the tenant's administration. A link leads to the operator's billing page, which is a
  setting.
- **A tenant administrator accepts a new version** _(planned for M34)_ of the terms or of the data
  processing agreement in the shell, before anything else, and Axis records it as at sign-up.
- **Axis drops** prices, currencies, proration, billing invoices, the ledger, payments, payment
  providers, reconciliation, e-invoices, the billing portal with its accounts, owners and billing
  contacts, payment reminders and the operator's billing commands.
- **Connectors are open.** Anyone may write a connector between the billing API and a billing system,
  under any licence, as a service of its own.

The milestones that build this, and their order, are in the [roadmap](../roadmap.md#milestones).

## Consequences

- An operator in any country brings its own billing system, and Axis holds no payment rule and no tax
  rule.
- The operator's billing stays outside the AGPL code, so changing a price or a payment provider
  publishes nothing.
- An operator needs a connector between the billing API and its billing system, written by itself or
  by anyone else.
- Payment data never reaches Axis, so no cell and no control plane holds a card, an account or an
  invoice.
- A tenant in a cell is the same tenant as on any installation, so the platform needs no second
  tenancy model and its tests still hold.
- One failure, one identity provider restart or one slow neighbour touches only the tenants of one
  cell. A dedicated customer and a new region are the same operation as a new cell.
- Separate databases and realms keep data apart but still share CPU, memory, connections and the mail
  domain, so a shared cell needs quotas and fair work as much as isolation.
- A failing cell never stops sign-up, and nothing global sits in a request's path.
- Personal data stays in its region, with no third party that holds it, and a proof of work served by
  Axis keeps sign-up data there, which a third-party captcha would not.
- A read-only tenant costs the operator nothing, because nothing grows.
- The sign-up page, the billing API, the calls to the cells and the directory's DNS writes are trust
  boundaries for the [threat model](../reference/threat-model.md).
- There is a second service to run, a DNS provider to integrate, a cell count to watch and a new tenant
  state in the cell.
- Running the service needs things outside the repository. An operator brings a domain, a cloud
  provider in each region and a billing system, and makes its own checks, such as where its customers'
  data must be kept.

## Alternatives

- **Billing inside Axis,** with prices, a ledger, invoices and a portal. It ties every operator to one
  country's rules, puts the operator's billing under the AGPL and puts payment data in Axis.
- **One built-in payment provider.** It would still tie an operator to that provider's countries and
  contracts.
- **Every tenant in one large installation.** One failure, one restart or one slow neighbour would
  reach every customer, and a dedicated customer or a new region would need a different setup.
- **A free plan that never ends.** Abandoned tenants would fill the cells.
