# D118. Axis SaaS in cells, with sign-up, quotas and billing

Status: **Agreed**

The owner agreed these points on 2026-10-31.
[D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) brought Axis-hosted
operations into scope as M31 and left its design to a decision of its own. Small businesses and
individuals sign up, pay and use Axis without installing anything, so a hosted Axis that a person
creates by hand, or that never takes a payment, is only half a product. This decision designs the
whole service, splits it into three milestones that each finish what they build, and opens sign-up
to everyone only when all three are done. Everything here is _(planned for M31, M33 and M34)_, see
the [roadmap](../roadmap.md#milestones).

- **Three ways to run Axis, one software:**
  - **SaaS, shared:** many customers share a cell that the owner runs. This is phase 1.
  - **SaaS, dedicated:** one customer has a cell of its own that the owner runs, for a customer
    that needs more resources or wants to be apart.
  - **Self-hosted:** the customer installs Axis on premises or in its own cloud, as
    [installation](../reference/installation.md) describes. It is built, and it serves large
    organizations, banks and government in phase 3.
- **Cells and regions:**
  - A cell is one full installation: the server, the worker, the publisher, PostgreSQL, Axis's
    identity provider and file storage. Every tenant lives in exactly one cell.
  - A region is a place where customer data is kept, such as `vn` or `sg`. A region holds one or
    more cells. A tenant's data, backups included, never leaves its region.
  - Each tenant keeps its own database and its own realm, as
    [D99](d99-axis-ships-its-own-identity-provider.md) agrees. A cell holds as many tenants as
    its load test shows it can, and a new cell opens before a cell is full. A restart or a
    failure of a cell touches only its tenants.
  - The owner names the first region before M31 is broken down. A second region opens when its
    first customer needs it, with no platform change.
- **What a shared cell keeps apart:**
  - **Data:** each tenant's database, realm, files, keys and backups, as today.
  - **Studio:** each tenant's application folders, so an author sees and changes only their own
    tenant's applications. Today `Studio:ApplicationsPath` and `Studio:Folders` are set for the
    whole installation, which is safe only with one tenant.
  - **Resources:** each tenant has quotas, and one tenant never slows the others down.
- **Quotas per tenant:**
  - Storage: database size and file size.
  - Users and applications.
  - Emails a day, so one tenant cannot get the cell's mail domain marked as spam.
  - Work at once: imports, exports, connector calls and data source queries.
  - The worker shares its time between tenants, so a tenant with a large backlog does not hold
    back the others.
  - A write past a storage, user or application quota is refused with a clear message, and work
    past a concurrency quota waits its turn. The existing rate limits and query timeouts stay.
  - Each tenant's usage is measured and kept, for the operator and for plans.
- **M31 Axis SaaS** builds the cell: Studio folders per tenant, quotas and usage, fair work in the
  worker, a subdomain of the region's domain for each new tenant and a host of its own on demand,
  encrypted backups of every tenant each day with a retention period and a tested restore,
  exporting a tenant's data and deleting a tenant, and an overview of every tenant of the cell for
  the platform operator with its status, usage and failed work. The operator creates tenants,
  from a built-in application, for the first customers. A load test sets how many tenants a cell
  holds.
- **M33 Sign-up and trials:**
  - A person signs up on Axis's public page, chooses a region and a built-in application, and
    verifies their email.
  - Axis picks a cell of the region with room, creates the tenant, its realm and its subdomain,
    activates the application and invites the person as the first administrator.
  - A new tenant is a trial for a set period. Before the end, its administrators get emails. At the
    end it is suspended, and after a grace period it is deleted.
  - Sign-up has rate limits and a bot check, and the operator can still create a tenant by hand.
  - A directory of regions and cells, apart from the cells, routes each sign-up, and each tenant
    host, to its cell.
- **M34 Plans and billing:**
  - Each tenant has a plan, and the plan sets its quotas.
  - A tenant administrator chooses a plan and pays. In Vietnam, by bank transfer through VietQR or
    a payment gateway; abroad, through a merchant of record.
  - Each payment gets an electronic invoice from an e-invoice provider, as Vietnamese law asks.
  - A trial becomes paid without losing anything. A failed or late payment sends reminders, then
    a grace period, then a suspension.
  - Which providers, and the plans and their prices, are set when M34 is broken down.
- **Where it lives:** the cells run the platform as it is, with quotas, usage and the per-tenant
  Studio folders added. Sign-up, the directory of regions and cells, plans and billing are Axis's
  own business, not a customer's domain, and live in a control plane apart from the cells. The
  control plane provisions a tenant in a cell through an API of the admin commands, which
  [administration](../reference/administration.md#not-built-yet) lists as not built yet.
- **Order:** M31 runs beside M24 to M26. M33 follows M31 and M26, because a sign-up starts from a
  built-in application. M34 follows M33. All three are in phase 1, and phase 1 ends when M34's
  journey passes.
- **Outside the repository:** before M33 opens sign-up, the owner has a lawyer check where
  Vietnamese customer data must be kept, the terms of service and the data processing agreement.
  Migrating a tenant between regions stays out of scope.

_Why:_ a person who finds Axis expects to sign up, try it and pay without talking to anyone, so
sign-up, trials and billing are part of the product, not extras. One installation per cell keeps
the platform as it is built and tested: a tenant in a cell is the same tenant as today. Cells also
bound what one failure, one Keycloak restart or one slow neighbour can touch, and they make a
dedicated customer and a new region the same operation as a new cell. Separate databases and realms
keep data apart but share CPU, memory, connections and the mail domain, so a shared cell needs
quotas and fair work as much as it needs isolation. Kept outside the cells, sign-up and billing
never touch the engines. The cost is a control plane to build and run, a cell count to watch, and
three milestones before sign-up is open to everyone.
