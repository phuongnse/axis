# Cell console

Detailed reference for the cell console: the web page where the platform operator sees every tenant
of a [cell](cells.md) and provisions them. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
Everything in this file is _(planned for M31)_, except the [sign-in](#sign-in) of the console host with its session read, the **Network** limit of [host and route](#host-and-route), and the host serving the SPA's page and its shell reads. The console's views and actions stay planned. The [operator console](operator-console.md) of a
tenant stays as it is: it shows one tenant's failed work in detail, inside that tenant. The
conventions of [architecture.md](../architecture.md) apply.

## Host and route

- **Its own host.** The cell console answers only on `Cell:ConsoleHost`, such as
  `console.vn-1.example.com`, a host that belongs to no tenant. Request resolution treats it as a
  platform host: it resolves no tenant, and a tenant host never serves the console's page or its API.
- **Route.** The page is `/` on that host, and every client path, such as `/tenants/a`, answers the SPA's
  `index.html`. The host also serves the SPA's web-root files, `GET /api/site`, which answers `cellConsole: true`
  there and `false` on a tenant host, and `GET /api/texts/{locale}`. Its API is the
  [provisioning API](tenant-lifecycle.md#provisioning-api) under `/api/cell/`. Every other path answers `404`.
- **Network.** `Cell:ConsoleAllowedNetworks`, a list of address ranges, limits who reaches the host.
  It is empty by default, which allows every address, because the second factor guards sign-in. When it lists
  ranges, a request to the console host from an address outside every range gets `403`, on every path, before tenant
  resolution and before any sign-in work. The address is the real client address, which is the forwarded one behind a
  listed proxy. Tenant hosts and the internal listener's host are not limited.

## Sign-in

- **Operator realm.** The cell's identity provider has one realm for the people who run the cell,
  `Cell:OperatorRealm`, `axis-operators` by default. It is apart from every tenant realm: an operator
  holds no account in a customer's realm, and no tenant's account reaches the cell console.
- **Accounts only.** The realm has accounts that the identity provider holds, with no brokered
  provider. A new operator account is created by an existing operator in the realm's admin console,
  or by `Axis.Admin cell operator invite <email>` for the first one. That command makes the realm, or
  brings it up to date, and removes any brokered provider that it finds, see
  [cell operator invite](administration.md#cell-operator-invite).
- **Second factor.** Every sign-in needs a password and a second factor: an authenticator app or a
  passkey. The realm's required action sets one up at the first sign-in, and an account without one
  cannot finish signing in.
- **Session.** The console uses a BFF session: the server holds the session and the browser holds
  only a cookie that names it. The session is a row of `platform.operator_sessions`, because it belongs to no
  tenant, with an idle timeout of 15 minutes and an absolute one of 8 hours. It holds the operator's id,
  display name and roles, and no token. A session counts only on `Cell:ConsoleHost`: a tenant's session
  never counts there, whatever the cookie's name, and the console's session counts on no other host.
- **Paths.** `GET /api/cell/auth/sign-in?returnUrl=` sends the operator to the operator realm's login
  page. The `returnUrl` must be a local path. The realm returns to `GET /api/cell/auth/callback`, which creates the
  session and redirects to the `returnUrl`, and the browser gets only the cookie `__Host-Axis.CellSession`
  (`Axis.CellSession` in Development and Testing, which run over plain http). A callback that fails creates no session
  and redirects to `/?signIn=failed`. `POST /api/cell/auth/sign-out` deletes the session and answers `204`. It does not
  end the operator's session at the identity provider. Like every write under `/api`, it is refused with `403`
  unless it carries the `X-Axis-Csrf` header. These two routes are the only ones under `/api/cell/` that need no
  operator role. Sign-in answers `404` on any other host.
- **Session read.** `GET /api/cell/session` answers `{ displayName }` of the signed-in operator. It answers `401`
  without a session and `403` without the role. For a machine caller with a bearer token the display name is the client id.
- **Client.** The console signs in as a confidential client of the operator realm, `Cell:ConsoleClientId`
  (`axis-cell-console` by default) with the secret `Cell:ConsoleClientSecret`, using the code flow with PKCE. The client
  must accept the redirect `https://<Cell:ConsoleHost>/api/cell/auth/callback`. Without the secret the server offers no
  console sign-in and `GET /api/cell/auth/sign-in` answers `404`. `Axis.Admin cell operator invite` does not make the client
  yet, so a cell needs it made by hand.
- **Roles.** The server reads the operator's roles from the `realm_access.roles` claim of the access
  token that the token endpoint returns, not from the ID token, and stores them in the session at sign-in.
  Removing the role in the realm takes effect at the next sign-in, within 8 hours.
- **Role.** `Cell:OperatorRole`, `cell-operator` by default, is a role of the operator realm. A
  signed-in person without it gets `403` on every route and a forbidden page. A missing, expired or
  tenant session gets `401`.
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
