# Cell console

Detailed reference for the cell console: the web page where the platform operator sees every tenant
of a [cell](cells.md) and provisions them. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_. The [operator console](operator-console.md) of a
tenant stays as it is: it shows one tenant's failed work in detail, inside that tenant. The
conventions of [architecture.md](../architecture.md) apply.

## Host and route

- **Its own host.** The cell console answers only on `Cell:ConsoleHost`, such as
  `console.vn-1.example.com`, a host that belongs to no tenant. Request resolution treats it as a
  platform host: it resolves no tenant, and a tenant host never serves the console's page or its API.
- **Route.** The page is `/` on that host. Its API is the [provisioning API](tenant-lifecycle.md#provisioning-api)
  under `/api/cell/`.
- **Network.** `Cell:ConsoleAllowedNetworks`, a list of address ranges, limits who reaches the host.
  It is empty by default, which allows every address, because the second factor guards sign-in.

## Sign-in

- **Operator realm.** The cell's identity provider has one realm for the people who run the cell,
  `Cell:OperatorRealm`, `axis-operators` by default. It is apart from every tenant realm: an operator
  holds no account in a customer's realm, and no tenant's account reaches the cell console.
- **Accounts only.** The realm has accounts that the identity provider holds, with no brokered
  provider. A new operator account is created by an existing operator in the realm's admin console,
  or by `Axis.Admin cell operator invite <email>` for the first one.
- **Second factor.** Every sign-in needs a password and a second factor: an authenticator app or a
  passkey. The realm's required action sets one up at the first sign-in, and an account without one
  cannot finish signing in.
- **Session.** The console uses the BFF session of the server, as the SPA does, with an idle timeout
  of 15 minutes and an absolute one of 8 hours.
- **Role.** `Cell:OperatorRole`, `cell-operator` by default, is a role of the operator realm. A
  signed-in person without it gets `403` on every route and a forbidden page.
- **Machine callers.** A client of the operator realm with the role in its service account, such as
  the control plane, calls the API with a bearer token, see
  [provisioning API](tenant-lifecycle.md#provisioning-api).

## Views

- **Cell.** The cell id, its region, the number of tenants against [the cap](cells.md#cell-size),
  and the cell's newest restore check with its result, see
  [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell).
- **Tenants.** One row per tenant, with a search by id and host and a filter by status:
  - the id, the status (`active`, `suspended`, `provisioning` or `deleting`), the application and
    the hosts;
  - the newest storage sample against its quota, the users and applications against theirs, and
    the emails sent today;
  - the counts of failed instances, calls with an unknown outcome and open dead letters;
  - the time of the newest daily archive, marked when it is more than 26 hours old.
- **Tenant.** One tenant's details: its quotas and the rows that override the defaults, the usage of
  the last 13 months by day, its hosts with their verification state, its jobs, and a deletion's
  dates. Each action below is a button with a confirmation that names the tenant.
- **Jobs.** The jobs of the cell, newest first, with their steps and errors.

## Actions

Each action calls one route of the [provisioning API](tenant-lifecycle.md#provisioning-api): create a
tenant, suspend, resume, set quotas, add a host, export, download an export, delete and cancel a
deletion. Delete asks the operator to type the tenant id.

## What the console never shows

- A record value, a file, a history input or output, or a call input. Failed work appears as counts
  only. To see the detail, the operator uses the tenant's [operator console](operator-console.md),
  which needs an account in that tenant's realm that the tenant's administrator gives.
- A tenant secret, key, password or binding value.
- A tenant user's name or email, except the first administrator's email on the create job.
