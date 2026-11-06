# Cell console

The cell console is the web page where the platform operator sees every tenant of a
[cell](cells.md) and provisions them. This page covers its host, how an operator signs in, what it
shows, what it does and what it never shows. The [operator console](operator-console.md) of a tenant
is a different page: it shows one tenant's failed work in detail, inside that tenant.

## Host and route

The cell console answers on a host of its own, which belongs to no tenant.

- **Its own host.** The cell console answers only on `Cell:ConsoleHost`, such as
  `console.vn-1.example.com`. Request resolution treats it as a platform host: it resolves no tenant,
  and a tenant host never serves the console's page or its API.
- **Route.** The page is `/` on that host, and every client path, such as `/tenants/a`, answers the
  SPA's `index.html`. The host also serves the SPA's web-root files, `GET /api/site`, which answers
  `cellConsole: true` there and `false` on a tenant host, and `GET /api/texts/{locale}`. Its API is
  the [provisioning API](tenant-lifecycle.md#provisioning-api) under `/api/cell/`. Every other path
  answers `404`.
- **Network.** `Cell:ConsoleAllowedNetworks`, a list of address ranges, limits who reaches the host.
  It is empty by default, which allows every address, because the second factor guards sign-in. When
  it lists ranges, a request to the console host from an address outside every range gets `403`, on
  every path, before tenant resolution and before any sign-in work. The address is the real client
  address, which is the forwarded one behind a listed proxy. Tenant hosts and the internal listener's
  host are not limited.

## Sign-in

Operators sign in through a realm of their own, with a second factor, and the server holds their
session.

- **Operator realm.** The cell's identity provider has one realm for the people who run the cell,
  `Cell:OperatorRealm`, `axis-operators` by default. It is apart from every tenant realm: an operator
  holds no account in a customer's realm, and no tenant's account reaches the cell console.
- **Accounts only.** The realm has accounts that the identity provider holds, with no brokered
  provider. A new operator account is created by an existing operator in the realm's admin console,
  or by `Axis.Admin cell operator invite <email>` for the first one. That command makes the realm, or
  brings it up to date, and removes any brokered provider that it finds, see
  [cell operator invite](administration.md#cell-operator-invite).
- **Second factor.** Every sign-in needs a password and a code from an authenticator app. The realm's
  required action sets one up at the first sign-in, and an account without one
  cannot finish signing in.
- **Session.** The console uses a BFF session: the server holds the session and the browser holds
  only a cookie that names it. The session is a row of `platform.operator_sessions`, because it
  belongs to no tenant, with an idle timeout of 15 minutes and an absolute one of 8 hours. It holds
  the operator's id, display name and roles, and no token. A session counts only on
  `Cell:ConsoleHost`: a tenant's session never counts there, whatever the cookie's name, and the
  console's session counts on no other host.
- **Paths.** `GET /api/cell/auth/sign-in?returnUrl=` sends the operator to the operator realm's login
  page, and the `returnUrl` must be a local path. The realm returns to `GET /api/cell/auth/callback`,
  which creates the session and redirects to the `returnUrl`, and the browser gets only the cookie
  `__Host-Axis.CellSession` (`Axis.CellSession` in Development and Testing, which run over plain
  http). A callback that fails creates no session and redirects to `/?signIn=failed`.
  `POST /api/cell/auth/sign-out` deletes the session and answers `204`, and does not end the
  operator's session at the identity provider. Like every write under `/api`, it is refused with
  `403` unless it carries the `X-Axis-Csrf` header. These two routes are the only ones under
  `/api/cell/` that need no operator role. Sign-in answers `404` on any other host.
- **Session read.** `GET /api/cell/session` answers `{ displayName }` of the signed-in operator. It
  answers `401` without a session and `403` without the role. For a machine caller with a bearer
  token the display name is the client id.
- **Client.** The console signs in as a confidential client of the operator realm,
  `Cell:ConsoleClientId` (`axis-cell-console` by default) with the secret `Cell:ConsoleClientSecret`,
  using the code flow with PKCE. The client must accept the redirect
  `https://<Cell:ConsoleHost>/api/cell/auth/callback`. `Axis.Admin cell operator invite` does not make
  the client, so the operator makes it in the realm's admin console. Without the secret the server
  offers no console sign-in and `GET /api/cell/auth/sign-in` answers `404`.
- **Roles.** The server reads the operator's roles from the `realm_access.roles` claim of the access
  token that the token endpoint returns, not from the ID token, and stores them in the session at
  sign-in. Removing the role in the realm takes effect at the next sign-in, within 8 hours.
- **Role.** `Cell:OperatorRole`, `cell-operator` by default, is a role of the operator realm. A
  signed-in person without it gets `403` on every route and a forbidden page. A missing, expired or
  tenant session gets `401`.
- **Machine callers.** A client of the operator realm with the role in its service account, such as
  the control plane, calls the API with a bearer token, see
  [provisioning API](tenant-lifecycle.md#provisioning-api). `Axis.Admin cell operator client add <name>`
  makes such a client, see [cell operator client add](administration.md#cell-operator-client-add).

## Views

The console has four views, which read the provisioning API. Its shell has the title Cell console and one
navigation entry for each built view. A person with no session goes to the operator realm's sign-in, and a
person without the operator role sees a forbidden page in place of the views. The header shows the
operator's display name from the session read in place of the app switcher and the account button, and has no
notification bell. The built views are read only, except for the Create tenant button of the Tenants view and the
actions of the Tenant view.

- **Cell.** The cell id, its region, and the number of tenants against [the cap](cells.md#cell-size). With no
  cap it shows the count alone. It also shows the newest [restore check](tenant-backup-and-restore.md#weekly-restore-check):
  its result, its source tenant and its time, or a dash when no check has run.
- **Jobs.** The jobs of the cell, newest first. A row shows the time, the kind, the tenant and the state, each
  step with its state, and the error of a failed job or step.
- **Tenants.** One row per tenant, with a search by id and host, a filter by status and a Create tenant button
  that opens the dialog of [creating a tenant](#actions). The search and the filter run in the browser. A row shows:
  - the id, the status (`active`, `readOnly`, `suspended`, `provisioning` or `deleting`), the application and
    the hosts;
  - the counts of failed instances, calls with an unknown outcome and open dead letters, with a dash for a
    tenant that the worker has not counted yet;
  - the usage: the newest storage sample, the users, the applications and the emails sent today, each against
    its quota, with a dash while a count is missing, see [the tenant list](tenant-lifecycle.md#routes);
  - the time of the newest daily archive, marked when it is more than 26 hours old, with a dash for a tenant
    that has no archive.
- **Tenant.** One tenant's status, application and hosts, its quotas with the rows that override the cell's
  defaults, and its jobs in the same table as the Jobs view. An unknown id shows the not-found page.
  - **Usage** _(planned for M31)_: the usage of the last 13 months by day.
  - **Host verification** _(planned for M31)_: the verification state of each host.
  - **Deletion dates** _(planned for M31)_: the dates of a tenant's deletion.
  - **Actions.** A Suspend, a Resume, an Export, a Delete and a Cancel deletion button, each with a confirmation
    that names the tenant, and a Download button once an export has succeeded, see [actions](#actions).
  - **Other actions** _(planned for M31)_: the other [actions](#actions), each a button with a confirmation
    that names the tenant.

## Actions

Each action calls one route of the [provisioning API](tenant-lifecycle.md#provisioning-api) through the
operator's session and queues a job.

- **Suspend and Resume.** The Tenant view shows Suspend for an `active` or a `readOnly` tenant and Resume for
  a `suspended` or a `readOnly` one, the statuses that [`tenant suspend`](tenant-status-commands.md#tenant-suspend)
  and [`tenant resume`](tenant-status-commands.md#tenant-resume) change. A `provisioning` or `deleting` tenant
  shows neither. A button opens a confirmation that names the tenant and says what the action does, and
  confirming it calls `POST /api/cell/tenants/{id}/suspend` or `/resume`.
- **Export and Download.** The Tenant view shows Export for a tenant in any status but `provisioning`. Its
  confirmation names the tenant, and confirming it calls `POST /api/cell/tenants/{id}/exports` and follows the
  job as [the job](#actions) says. Download shows once the tenant's jobs hold a succeeded `export` job, and
  offers the newest one. It opens a warning that names the tenant and says that the ZIP is
  [restricted](keys-and-data-protection.md#restricted) data. Only accepting the warning calls
  `GET /api/cell/tenants/{id}/exports/{exportId}` with `Accept: application/zip`, and the browser saves the ZIP
  under the name the server gives. Cancelling sends no request. A refusing `4xx`, such as `410` for an export
  past its 7 days, keeps the warning open and shows the server's title. See
  [exporting a tenant](tenant-lifecycle.md#exporting-a-tenant).
- **Delete and Cancel deletion.** The Tenant view shows Delete for an `active`, a `readOnly` or a `suspended`
  tenant. Its confirmation names the tenant, says what is lost and that the deletion can be cancelled for 7
  days, and keeps its Delete button off until the operator types the tenant id exactly, with the same case
  and no spaces added. Confirming it calls `DELETE /api/cell/tenants/{id}`. A `deleting` tenant shows
  Cancel deletion, in place of Suspend, Resume and Delete, and its confirmation calls
  `POST /api/cell/tenants/{id}/deletion/cancel`. A `provisioning` tenant shows neither.
- **Create a tenant.** The Tenants view's Create tenant button opens a dialog with the tenant id, the
  application and the administrator's email. The application is the id of an installed
  [application template](modules-and-templates.md#application-templates), typed as text, such as `approvals`.
  Create calls `POST /api/cell/tenants`, see [creating a tenant](tenant-lifecycle.md#creating-a-tenant). A `400`,
  `409` or `422` answer keeps the dialog open and shows the server's title. A `202` answer replaces the form
  with the queued create job in the same table as the Jobs view. The dialog reads the job at once and then every
  2 seconds, and shows its state and steps until it succeeds or fails. Close is there at any time and stops
  following the job, which keeps running. When the job ends, or when the dialog closes after the job was
  queued, the Tenants view loads the list again.
- **Idempotency key.** Each action sends an `Idempotency-Key`. The console keeps the key in the page's memory
  while the request has no final answer: for each tenant and action, and for a create, for each request body.
  A retry after a network error or a `5xx` sends the same key, so it cannot queue a second job. Any `2xx` or
  `4xx` answer is final and drops the key, and the next action gets a new one. A create whose field changed
  gets a new key, because the server hashes the body into the request that a key names.
- **A failed request.** A network error, a `5xx` or a refusing `4xx` leaves the confirmation or the dialog open with an error. A `401` or `403` goes through
  the console's sign-in and forbidden handling.
- **The job.** The queued job heads the tenant's jobs. The view reads it at once and then every 2 seconds, and
  shows its state and steps. When the job succeeds or fails, the view loads the tenant and its jobs again. The
  buttons are off while it follows a job.
- **Other actions** _(planned for M31)_. Set quotas and add a host.

## What the console never shows

_(planned for M31)_

The console shows what the operator needs to run the cell, and no business data of a tenant.

- A record value, a file, a history input or output, or a call input. Failed work appears as counts
  only. To see the detail, the operator uses the tenant's [operator console](operator-console.md),
  which needs an account in that tenant's realm that the tenant's administrator gives.
- A tenant secret, key, password or binding value.
- A tenant user's name or email, the first administrator's included, because the console needs none
  of them.
