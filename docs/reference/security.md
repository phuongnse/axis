# Authentication and authorization

This page describes how people and other systems prove who they are to Axis, and what each may then
do: sign-in and the session, machine callers on inbound endpoints, service accounts, and the
authorization of every request. The FAPI 2.0 profile that a tenant can turn on is in
[FAPI 2.0 profile](fapi2-profile.md), the identity provider that Axis ships is in
[Axis's identity provider](identity-provider.md), and the summary is in
[architecture](../architecture.md#authentication-and-authorization).

## Sign-in and the session

People sign in through the tenant's identity provider with OpenID Connect, and the server keeps the
session, so the browser holds only a cookie and never a token. Every tenant that `tenant create` makes
signs in through its realm in [Axis's identity provider](identity-provider.md). This section covers
the sign-in flow, the tenant's sign-in settings, the sign-in routes, their audit records, the session
itself and the routes that need it.

### Sign-in flow

Sign-in uses the OIDC authorization code flow with PKCE. The server is the confidential client: it
exchanges the code for tokens, reads the claims from the ID token and keeps no token after that, as
[D9](../decisions/d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) requires.

- **The request.** The identity provider returns the code in the query string. The request asks for
  the `openid`, `profile` and `email` scopes and carries `max_age` set to 8 hours, the session's
  absolute lifetime, so the identity provider asks a person whose login there is older to log in
  again. The callback refuses an ID token whose `auth_time` is missing or older than 8 hours, see
  [session lifetimes and re-authentication](#session-lifetimes-and-re-authentication).
- **Pushed authorization requests** (PAR) stay off under the `standard` profile and are required
  under a tenant's `fapi2` profile, see [FAPI 2.0 profile](fapi2-profile.md).
- **One client per tenant.** The server registers one OIDC client per tenant. All of them share the
  callback path, and each one ignores a callback that arrives on another tenant's host.
- **A site's client.** A sign-in that starts on a site page goes to the tenant's realm as that site's
  client, `site-<path>`, and the callback redeems the code with the secret that derives from the
  platform client's secret. The ID token's `aud`, and its `azp` when present, must be the client that
  started the sign-in. Otherwise the callback is refused with the reason `invalidToken` and creates no
  session, also for a token issued to another site's client. A sign-in from another page, a preview
  tenant, a tenant without realm details and a `fapi2` tenant use the platform client `axis`. See
  [sites and sign-in](identity-provider.md#sites-and-sign-in).

### Sign-in settings

Each tenant in the [`Tenants` section](../architecture.md#tenant-configuration) has an `Oidc` block
that names its identity provider, its client and the claims that Axis reads. Only the server reads
and checks the block. The worker reads the same `Tenants` section but signs nobody in, so it ignores
`Oidc`.

```json
"default": {
  "Hosts": [ "localhost" ],
  "ConnectionString": "Host=...",
  "Oidc": {
    "Authority": "http://localhost:8080/realms/axis",
    "ClientId": "axis",
    "ClientSecret": "...",
    "Claims": {
      "UserId": "sub",
      "DisplayName": "name",
      "Roles": "roles",
      "Email": "email",
      "Locale": "locale"
    }
  }
}
```

- **Claims.** `Claims` names the claims that give the user id, the display name and the roles, by
  default `sub`, `name` and `roles`, and the email and the locale, by default `email` and `locale`,
  which each sign-in writes to the user's profile, see [user profiles](notifications.md#user-profiles).
  Each claim name is optional.
- **Audiences.** The optional `EndpointAudience` is for [machine callers](#machine-callers), and the
  optional `ServiceAudience` is for [service accounts](#service-accounts).
- **Startup checks.** The server's start fails, naming every problem as for the tenant configuration,
  when a tenant has no `Oidc` block, when its `Authority`, `ClientId` or `ClientSecret` is missing or
  blank, or when its `Profile` is other than `standard` or `fapi2`. Under the `fapi2` profile the
  tenant needs a client key with a key id in place of the client secret. The check confirms that the
  key and its key id are present and does not parse the PEM, and no message holds a key.
- **Preview tenants.** A preview tenant has no `Oidc` block and takes its main tenant's, see
  [preview tenants](../architecture.md#preview-tenants).
- **The tenant directory.** Under `Tenancy:Source=directory` the `Oidc` settings of a tenant come from
  the [tenant directory](../architecture.md#tenant-directory), not from `Tenants`. Each refresh
  registers the OIDC client and the endpoint caller scheme of a new tenant, builds them again for a
  tenant whose `Oidc` settings changed, and removes both for a tenant that is no longer served. The
  identity provider's metadata is fetched again only for the tenants that changed.
- **The client secret** comes from the environment, such as `Tenants__default__Oidc__ClientSecret`. It
  is never in a committed file, except the development fixtures, which are never valid outside
  `Development` and tests. `appsettings.Development.json` and the E2E server use the development
  realm's secret. Under `Tenancy:Source=directory` the client secret is stored encrypted in the tenant
  directory and decrypted only when the sign-in client is set up, see
  [platform database](storage.md#platform-database). `tenant create` and `tenant restore` set it, and
  no command changes it afterwards.

### Sign-in routes

These routes start and end a sign-in, and let a person see and end their own sessions.

- **`GET /api/auth/sign-in?returnUrl=`** redirects to the tenant's identity provider.
  - `returnUrl` must start with a single `/`. A value that does not, or that starts with `//` or `/\`,
    is a `400` problem details response. A missing `returnUrl` means `/`.
  - With `reauthenticate=true` the redirect also carries `prompt=login` and `max_age=0`, so the
    identity provider asks for credentials even when its own session is still open. The new session
    then holds a fresh login time, which
    [ending sessions](#session-lifetimes-and-re-authentication) needs.
  - With `action=` the redirect carries `kc_action` with that value, also under `fapi2`, where it goes
    in the pushed authorization request. The [account page](platform-pages.md#account-page) uses it to
    start the identity provider's own steps. The allowed values are `UPDATE_PASSWORD`,
    `CONFIGURE_TOTP` and `delete_credential:<id>`, where the id is letters, digits and dashes. Any
    other value, an empty one included, is a `400` problem details response.
- **`GET /api/auth/callback`** is where the identity provider returns. It creates the session and
  redirects to `returnUrl`.
  - A callback that fails creates no session and redirects to the shell's sign-in page,
    `/?signIn=failed`, for example when the ID token has no recent `auth_time`. The page says that
    sign-in did not complete and shows no reason, and the address holds none.
  - A person who declines the terms step makes the identity provider answer `error=access_denied`.
    When the server could read the sign-in state, that goes to `/?signIn=declined` instead, a card that
    says signing in needs the terms accepted. See the [home page](frontend.md).
- **`POST /api/auth/sign-out`** ends the session and returns `200` with `{ "signOutUrl" }`.
  1. The server ends the identity provider's session by the stored `sid` through the admin API, as the
     realm's `axis-users` client, where a `404` counts as ended, and answers `/?signedOut`, so the
     browser visits no identity provider page.
  2. When the tenant has no users secret, the row holds no `sid`, or the call fails or takes over 5
     seconds, it answers the end-session address instead, and the identity provider returns the
     browser to `/?signedOut`, see [platform home page](shell.md#platform-home-page).

  The SPA calls sign-out with `fetch` to send the CSRF header, which cannot follow a redirect to the
  identity provider as a page. A successful sign-out also sends
  `Clear-Site-Data: "cache", "storage"`, so the browser drops the site's HTTP cache and storage, and a
  refused one sends no such header. The SPA keeps only the theme and the locale in browser storage,
  under `axis.themeMode` and `axis.locale`, so a sign-out resets both. See
  [headers](headers-and-rate-limits.md#security-headers).
- **Starting a sign-in in the SPA.** The shell header's [account button](frontend.md) offers sign-in
  and sign-out. The home page `/` calls `GET /api/auth/sign-in` at once when `GET /api/me` or
  `GET /api/sites` answers `401`. Any other page shows a sign-in prompt, and only a click on its button
  calls it.
- **`GET /api/me/sessions`, `DELETE /api/me/sessions/{id}` and `DELETE /api/me/sessions`** let a
  person see and end their own sessions. They need a session, not a service account's bearer token.
  The shell's [sessions screen](platform-pages.md) at `/account` calls them.
  - The list answers `{ "endAllowed", "sessions": [{ "id", "signedInAt", "lastRequestAt", "current" }] }`.
    It holds only the caller's live sessions of the host that the request came on, newest sign-in
    first. `current` marks the session that made the request, and `endAllowed` is true when the
    session may end sessions now.
  - A session's `id` is the base64url form of the row's `id_hash`, so it cannot be turned back into a
    cookie.
  - The two delete routes need the CSRF header and answer `204`. The first ends the session with that
    id, which may be the current one. The second ends all the caller's sessions of the request's host
    except the current one. Neither touches a session of another host.
  - Both delete routes answer a `403` problem and delete nothing unless the session's last login at
    the identity provider is no more than 5 minutes old. A session with no stored login time cannot
    end sessions. The check comes before the lookup of the id.
  - An id that is not one of the caller's live sessions of the request's host answers a `404`
    problem, whoever owns it.
- **`GET /api/me/authenticators`** lists the person's own authenticators and needs a session too, see
  the [users page reference](user-administration.md#the-users-page).

### Sign-in audit

Sign-in and sign-out write [audit records](storage.md#audit-records-and-sequence-counters) in the
database of the tenant that the host resolves to, so a tenant can see who signed in and who was
refused.

- **Signed in.** The callback writes `session.signedIn` when it creates a session.
- **Refused.** The callback writes `session.signInRefused`, with a reason code, when it refuses, and
  still redirects to the sign-in page.
- **Signed out.** `POST /api/auth/sign-out` writes `session.signedOut` only when a session existed.
- **Nothing to write.** An expired session writes nothing. A host that resolves to no tenant writes
  nothing, because there is no tenant database.
- **No secrets.** Each record holds the
  [shortened client address](storage.md#audit-records-and-sequence-counters), never a token, a cookie
  or the user agent. The address follows the [forwarded headers](host-settings.md#forwarded-headers)
  rules.

| Reason                | The callback refuses                                                                                                                                                                                                                                                                                                  |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `invalidIssuer`       | Provider metadata whose `issuer` differs from the tenant's `Authority`, compared character for character after one trailing `/` is removed from the `Authority`. The check runs for every tenant before the code is sent to the token endpoint, and the start of sign-in does not run it. Under `fapi2` it also covers the `iss` response parameter, see [FAPI 2.0 profile](fapi2-profile.md) |
| `invalidToken`        | A token with no user id, with an `auth_time` that is missing, unreadable or older than 8 hours, with a nonce that the sign-in did not send, or with an `aud`, or `azp` when present, that is not the client that started the sign-in                                                                                    |
| `providerError`       | An error answer from the identity provider, such as the `access_denied` of a declined terms step                                                                                                                                                                                                                     |
| `providerNotAllowed`  | A valid token, for a sign-in that started on a site, when the site's `signIn` does not allow the provider that signed the person in, or when the site is no longer active, see [sites and sign-in](identity-provider.md#sites-and-sign-in)                                                                              |
| `acrNotAccepted`      | A valid token, for a sign-in that started on a site, when the provider that signed the person in has an `acr` list and the token's `identity_provider_acr` is missing or not in it                                                                                                                                      |
| `providerNotAccepted` | A valid token when the tenant's [platform sign-in](identity-provider.md#platform-sign-in) does not accept the provider that signed the person in, or does not accept accounts, and no active site that shares the tenant's host allows it                                                                               |

The callback checks a valid token for `providerNotAllowed` first, then `acrNotAccepted`, then
`providerNotAccepted`.

### The session

The session state is a row of `axis.sessions` in the tenant database, so sign-out revokes it at once
on every replica, as [D84](../decisions/d84-asvs-level-2-and-a-server-side-session.md) explains. The
browser holds only a cookie with a random session id.

- **The row** holds a hash of the session id, the user id, the display name, the roles, the time of
  sign-in, the time of the last request and the time of the person's last login at the identity
  provider, read from the ID token's `auth_time`. It also holds:
  - the lower-case site path of the host that made the session, null for the tenant's own hosts;
  - the alias of the provider that signed the person in, from the ID token's `identity_provider`
    claim, none for accounts;
  - the roles that each application's provider mapping gave, by application id;
  - the identity provider's session id, the ID token's `sid` claim, which names that session for
    sign-out and grants nothing.

  A row with no site path and no provider alias counts as a sign-in through accounts on the tenant's
  own hosts. Given roles are not in the row: the session reads them from `axis.given_roles` at every
  request, and on a preview tenant it reads those of its main tenant, from the main tenant's database.
  The table never holds the session id or a token, and none reaches the browser.
- **The session cookie** is `__Host-Axis.Session`, set with `HttpOnly`, `SameSite=Lax`, `Secure`,
  `Path=/` and no domain, so scripts cannot read it, the browser refuses it on plain http or from a
  subdomain, and a cross-site request does not carry it on a write. It holds only the random session
  id, 256 bits, wrapped and protected by the key ring below, and no claim. The row's key is the SHA-256
  hash of the id. The cookie has no sliding expiry of its own: the row's lifetimes below decide, and
  every request with a session renews the cookie.
- **Under `Development` and `Testing`,** which run over plain http, the session cookie is named
  `Axis.Session` and is Secure only on https requests, so the name depends only on the environment.
- **The OIDC helper cookies.** The OIDC correlation cookie and the nonce cookie last for one sign-in
  round trip. Outside `Development` and `Testing` they are named `__Secure-Axis.Correlation.*` and
  `__Secure-Axis.Nonce.*` and are always Secure. Their path is the callback path, so the `__Host-`
  prefix is not possible. In `Development` and `Testing` they keep the framework's names and are Secure
  only on https requests.
- **A new id at every sign-in.** Sign-in always creates a new session id, even when the browser already
  sends a valid session, and the old session ends. Sign-out deletes the row, so a copied cookie stops
  working at once, on every replica.
- **Any number of sessions.** A person may hold any number of sessions at once, and each sign-in from
  another browser adds one. There is no limit, so nothing is refused or ended when more are open. A
  person lists and ends their sessions on the sessions screen, and an operator ends one user's sessions
  or all sessions of a tenant with [`tenant session end`](tenant-setting-commands.md#sessions).
- **Lifetimes.** A request reads the row and updates the time of the last request. A row ends 8 hours
  after sign-in, or after 1 hour without requests, whichever comes first. A request past either limit
  counts as no session, and the row is deleted. Every request with a session renews the cookie.
- **Bound to the tenant.** The row has no tenant id, because each tenant has its own database, so a
  cookie taken to another tenant's host finds no row there. The server also checks that the session's
  tenant is the tenant [resolved from the host](../architecture.md#request-resolution), so such a
  cookie counts as no session and signs nobody in.
- **Bound to the host.** A session stays on the host that made it. A session made on a site's own host
  counts as no session on the tenant's own hosts, and the other way round. The refused request leaves
  the row alone, and a request never deletes a row of another host. A row past a limit that another
  host meets is left for the worker's cleanup. Sign-out, a new sign-in and an expired session delete
  only the row of their own host.
- **Cleanup.** The worker deletes the expired rows in each tenant it serves. It uses its own clock,
  runs once when the tenant becomes ready and then at most every 5 minutes, and never touches a tenant
  it does not serve. `tenant restore` empties the table, so no session comes back with an archive.
- **Behind a proxy.** Behind a named proxy, the callback and sign-out addresses and the cookie's Secure
  flag use the forwarded scheme and host, see the
  [forwarded headers settings](host-settings.md#forwarded-headers).
- **The key ring.** The cookie is protected with a key ring that every server replica shares. The keys
  are in `platform.data_protection_keys`, encrypted with `Platform:SecretKey`. They protect the
  cookie's session id wrapper, so every replica reads it, and the session itself survives a restart
  and works on every replica because its row is in the database. During a key rotation a key that
  `Platform:PreviousSecretKey` encrypted still decrypts, and `platform rekey` then re-encrypts it, so
  nobody is signed out. The server fixes the data protection application name to `Axis`, so replicas
  at different paths read the same keys. A key that does not decrypt stops the start.

### Roles and the current user

A session carries the person's roles, and `GET /api/me` tells the SPA who is signed in and which
platform tools they may open.

- **Roles.** The roles come from the roles claim at sign-in and are kept in the session row, so a role
  change at the identity provider applies at the next sign-in. Each application's roles also come from
  its provider's `roles` mapping, kept by application id, see [users](identity-provider.md#users). The
  roles that the users page gives join them, and a change on the users page applies at the person's
  next request, see [roles](user-administration.md#roles).
- **`GET /api/me`** reads the session and answers the fields below. Without a session it is a `401`.

| Field               | Value                                                                                                                                                       |
| ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                | The user id                                                                                                                                                 |
| `displayName`       | The display name                                                                                                                                            |
| `roles`             | The roles                                                                                                                                                   |
| `console`           | True when the user holds the role that `Console:OperatorRole` names, and false for everyone when it has no value, see [console settings](host-settings.md#console-settings) and the [platform home page](shell.md#platform-home-page) |
| `studio`            | True when `Studio:Folders` is not empty or `Studio:ApplicationsPath` is set, and the user holds `Studio:AuthorRole`                                         |
| `studioPublisher`   | True when Studio is on and the user holds both `Studio:AuthorRole` and `Studio:PublisherRole`                                                               |
| `studioPreviewHost` | The host of the tenant's served preview tenant, or null for a user who may not use Studio and for a tenant without a preview tenant                          |
| `studioTest`        | True when `studio` is true and `Studio:Tests` is true                                                                                                       |
| `studioCreate`      | True when `studio` is true and `Studio:ApplicationsPath` is set                                                                                             |

Record writes, process starts and the task API read the current user the same way, and their audit
records store the user's id and no display name.

### Sign-in needed

Application content and a person's own data need a session. Without one, these answer a `401` problem
details response with the title "Sign in to read or write this data.":

- every route under `/api/apps/{app}/`: records, record history, data source rows, process start and
  tasks. The session is checked before any `404`, so a caller without a session cannot learn which
  applications or records exist;
- `GET /api/sites`, `GET /api/sites/{path}`, `GET /api/sites/{path}/texts/{locale}` and
  `GET /api/sites/{path}/pages/{page}`. Site texts belong to an application, so they count as
  application content. The session is checked before any `404`, so a caller without a session cannot
  learn which sites or pages exist;
- `GET /api/me`, `GET /api/about` and `GET /api/licences`;
- `GET /api/me/authenticators`;
- `GET /api/me/sessions`, `DELETE /api/me/sessions/{id}` and `DELETE /api/me/sessions`.

Inbound endpoints under `/api/apps/{app}/endpoints/` are the exception: they take a bearer token and
no session, see [machine callers](#machine-callers). The routes that stay open, because the shell
needs them before anyone signs in, are on [the open list](#authentication-by-default), and the
identity provider's return to `GET /api/auth/callback` is open too. `POST /api/auth/sign-out` needs
the CSRF header but no session.

### CSRF header

Every `POST`, `PUT`, `PATCH` and `DELETE` under `/api` must carry `X-Axis-Csrf: 1`, otherwise it is a
`403`, so a cross-site form cannot write with a person's cookie.

- The server checks the header right after it resolves the tenant and before authentication, so a
  refused write does no work and does not renew the session cookie. On the record API, the data source
  endpoint, the process start endpoint and the task API, the session check follows it.
  `POST /api/auth/sign-out` needs no session, so no session check follows it.
- The SPA sends every `/api` call through one wrapper around `fetch`, which adds the header to its
  writes. See the record API's [CSRF header rule](record-api.md#create-update-and-delete).
- Paths under `/api/apps/{app}/endpoints/` are exempt from the header, because they take a bearer token
  and no session cookie. So are the writes under `/api/cell/` that carry a bearer token, and a
  [service account](#service-accounts) request.

### Session lifetimes and re-authentication

Two sessions exist after a sign-in: Axis's session and the identity provider's session. Axis does not
control the identity provider's session. The two are coordinated only through `max_age` and
`auth_time`, and the sign-out, under both [sign-in profiles](fapi2-profile.md).

| Under either profile | Axis's session                                                                                                     | The identity provider's session                                                                                                                                      |
| -------------------- | ------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| What starts it       | The callback creates the row in `axis.sessions`                                                                    | A login on the identity provider's page                                                                                                                              |
| How long it lasts    | 8 hours after sign-in, or 1 hour without requests                                                                  | Set at the identity provider                                                                                                                                         |
| What ends it         | Sign-out, expiry, a new sign-in, an administrator's disable or authenticator reset, an operator or a tenant restore | Its own expiry, the admin API call by `sid` that Axis sign-out makes (or the `signOutUrl` when that call fails), or an administrator's disable or authenticator reset |

- **Axis's session.** The lifetimes are those of [the session](#the-session). A new sign-in ends the
  old session, an operator can end it with [`tenant session end`](tenant-setting-commands.md#sessions),
  an administrator's disable or authenticator reset of a user ends every session of that user, see
  [user administration](user-administration.md#the-users-page), and `tenant restore` empties the
  table.
- **The identity provider's session.** Its lifetimes are set at the identity provider. Axis sets them
  in every realm that `tenant create` makes: at most 8 hours, the lifetime of Axis's session. The
  development realm sets none, so it keeps Keycloak's defaults. Sign-out from Axis ends both sessions:
  it ends the identity provider's session by `sid` and deletes the row, and only when that call fails
  does it send the browser to the `signOutUrl`, which ends the identity provider's session.
- **When a person signs in again.** When Axis's session ends, the next request has no session, and the
  shell starts a sign-in. At the identity provider, the person logs in again whenever the last login
  there is older than 8 hours, because every sign-in request carries `max_age=28800`. So a still-open
  identity provider session cannot skip the login after Axis's session has ended at its 8-hour limit.
- **Re-authentication before ending sessions.** A person ends sessions only when the session's stored
  `auth_time` is no more than 5 minutes old, with no clock skew allowed. Otherwise the shell sends the
  person to `GET /api/auth/sign-in?reauthenticate=true`, which makes the identity provider ask for
  credentials again. The [sessions screen](platform-pages.md) offers this as its **Sign in again**
  button and returns to `/account`. A session with no stored login time must sign in again first.
- **The check at the callback.** The callback refuses an ID token whose `auth_time`, the time of the
  last login at the identity provider, is missing, unreadable or older than 8 hours. The check allows
  the clock skew of the token validation, 5 minutes by default, and uses the clock of the session
  store. The identity provider must send `auth_time` in the ID token, or every sign-in of that tenant
  is refused. Keycloak sends it by default.
- **No back-channel logout.** Ending the identity provider's session does not end Axis's session,
  because Axis has no back-channel logout. A person who logs out only at the identity provider keeps
  the Axis session until it ends. Sign-out from Axis ends both.
- **Under `fapi2`.** The lifetimes and the rules are the same. The one difference is where `max_age`
  goes: into the pushed authorization request instead of the redirect, see
  [FAPI 2.0 profile](fapi2-profile.md).

### Tests of sign-in

Sign-in is tested against a real identity provider, and the tests of everything else sign in through
a shortcut that only test hosts have.

- Tests of sign-in itself start the development realm in Keycloak through Testcontainers and drive its
  login page. One shared Testcontainers container serves the whole integration run.
- Tests of the session lifetime run on real PostgreSQL, create the session through a test-only hook and
  use a test clock, so they see the row deleted. Server tests that only need some session use an
  in-memory session store.
- Tests of [machine callers](#machine-callers) get client credentials tokens from the same container,
  and expire them with a test clock.
- Integration tests of other endpoints sign in through a test authentication scheme that only test
  hosts register. A request names its user in a header, and the scheme builds the same principal as a
  session of that user.
- The E2E sign-in journey drives the Keycloak login page. For the other E2E journeys, a Playwright
  setup step signs each realm user in on the login page once and saves the browser session, and
  journeys that act as a user load or swap those saved sessions.

### Development realm

Docker Compose, `scripts/dev.sh` and the E2E run start Keycloak with an imported development realm,
`axis`, from `samples/keycloak/axis-realm.json`. Under `scripts/dev.sh` and in the E2E run, the server
signs people in through it. Under Docker Compose it does not, because the server in its container
cannot reach Keycloak at the address the browser uses. So under Compose the site endpoints, the record
API and the data source endpoint answer `401`, and an application page shows the sign-in prompt, whose
button starts a sign-in that cannot complete.

- The realm holds the [development users](#development-users), with fixed ids that seed data and E2E
  journeys name, and every user has the same development-only password.
- The roles are client roles of the confidential client `axis`. A client-role mapper writes them to
  the `roles` claim, so the claim holds only the sample roles.
- The confidential client `purchasing-system` stands in for the purchasing system as a
  [machine caller](#machine-callers), and `reporting-system` is a
  [service account](#service-accounts). Their service accounts get tokens through the client
  credentials grant.

### Development users

These are test fixtures. Docs and GitHub text name roles instead. A new
sample reuses a user that holds the role it needs before it adds a user.
`scripts/lint.sh` checks this table against `samples/keycloak/axis-realm.json`.
Every user signs in with the password `axis` and a 6-digit code. Each user's TOTP secret is the `otp` credential in that
file, and the realm accepts a code more than once. See [development realm and users](../development.md#development-realm-and-users).

| User                | Roles                   | Used by                                                                                                                                       |
| ------------------- | ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `requester`         | `employee`, `requester` | Purchase request, leave request and service desk samples; E2E journeys. Has the locale `vi`, the only user with one                           |
| `employee`          | `employee`              | Purchase request, leave request and asset management samples (holds assets); E2E journeys, E2E approvals journey                              |
| `manager`           | `department-manager`    | Purchase request and leave request samples; E2E journeys, E2E module contract journey, E2E approvals journey                                  |
| `manager-2`         | `department-manager`    | Purchase request and leave request samples; E2E journeys, E2E approvals journey                                                               |
| `finance-reviewer`  | `finance-reviewer`      | Purchase request and leave request sample seeds; E2E journeys                                                                                 |
| `operator`          | `operator`              | Purchase request sample's Approvals inspector; E2E journeys                                                                                   |
| `author`            | `author`, `publisher`   | Studio and the authoring API under `scripts/dev.sh`; E2E Studio journeys                                                                      |
| `agent`             | `agent`                 | Service desk sample; E2E journeys                                                                                                             |
| `lead`              | `service-desk-lead`     | Service desk sample; E2E journeys                                                                                                             |
| `approver`          | `approver`              | E2E journeys, E2E approvals journey; an agent in the service desk E2E                                                                         |
| `lead-2`            | `service-desk-lead`     | Service desk sample; E2E claim and reassign journey                                                                                           |
| `asset-manager`     | `asset-manager`         | Asset management sample; E2E asset management journey                                                                                         |
| `auditor`           | `auditor`               | Asset management sample; E2E asset management journey; the user whose roles the E2E users page journey gives and removes                      |
| `administrator`     | `tenant-administrator`  | "Users" in the shell, E2E shell journey; E2E users page journey                                                                               |
| `platform-operator` | `platform-operator`     | The operator console under `scripts/dev.sh`; E2E operator console journey; E2E module contract journey                                        |

## Machine callers

Other systems call [inbound endpoints](inbound-endpoints.md) with a bearer token. This sits next to
the browser session: the session is the only way in for people, and a bearer token the only way in for
systems.

- **Client credentials.** A caller is a system, not a browser. It gets an OAuth 2.0 access token from
  the tenant's `Oidc` authority through the client credentials grant, and sends it as
  `Authorization: Bearer <token>`. Under a tenant's `fapi2` profile it sends
  `Authorization: DPoP <token>` with a DPoP proof, see
  [machine callers under `fapi2`](fapi2-profile.md).
- **Tenant setting.** The expected audience is the tenant setting `Oidc:EndpointAudience`, which is
  `axis-endpoints` in the development tenant:

  ```json
  "Oidc": {
    "Authority": "http://localhost:8080/realms/axis",
    "EndpointAudience": "axis-endpoints"
  }
  ```

  The setting is optional. A tenant without it accepts no bearer token, so every endpoint call there
  is a `401`. The server's [startup checks](#sign-in-settings) do not require it, and the worker
  ignores it, as it ignores the rest of `Oidc`.

- **Token checks.** The server checks each token against the tenant
  [resolved from the host](../architecture.md#request-resolution). Any failure is a `401` with
  `WWW-Authenticate: Bearer`, which never says which check failed, and the server writes a warning with
  the tenant and the reason, see the [log inventory](log-inventory.md#security-events). Under the
  `fapi2` profile the `401` says `WWW-Authenticate: DPoP algs="ES256 PS256"` and the token must also be
  DPoP-bound, see [machine callers under `fapi2`](fapi2-profile.md). The checks are:
  - the signature, with the keys the authority publishes (its JWKS);
  - the issuer, which must equal the tenant's `Authority`, so another tenant's token is a `401`;
  - the audience, which must contain the tenant's `Oidc:EndpointAudience`;
  - the expiry;
  - the signature algorithm, which must be `RS256`, `RS384`, `RS512`, `PS256`, `PS384`, `PS512`,
    `ES256`, `ES384` or `ES512`, so a token signed with `HS256` or any other algorithm, or with `none`,
    is a `401`;
  - the token type, which must be an access token. A token marked as another type is a `401`: a header
    `typ` other than `at+jwt`, `application/at+jwt` or `JWT`, or a payload `typ` claim other than
    `Bearer` or `DPoP`. So Keycloak's ID token, which has the claim `ID`, is refused even when its
    audience is the expected one. A token with no `typ` is not refused, so identity providers that mark
    no type still work.
- **Clock.** The expiry is checked against the server's clock service, with the usual 5 minutes of
  clock skew, so tests can replace the clock.
- **Roles.** The caller's roles come from the configured roles claim (`Claims:Roles`), matched against
  the declared roles as for users.
- **Caller id.** The caller's id comes from the configured user id claim (`Claims:UserId`, default
  `sub`). It is the audit actor and the new record's creator.
- **Display name.** The display name comes from the configured display name claim, else the token's
  client id (`azp`). Audit records store no display name, so a history shows the caller's id, as a
  caller has no profile.
- **Endpoint routes only.** The endpoint audience is accepted only on
  `POST /api/apps/{app}/endpoints/{name}` and its status location. Everywhere else a bearer token with
  that audience is ignored, so it never reaches the record API, the data source endpoint or the task
  API. The request's default authentication is the bearer token on paths under
  `/api/apps/{app}/endpoints/`, and the session everywhere else. [Service accounts](#service-accounts)
  use another audience for the routes they may call.
- **No session.** On endpoint routes the session cookie is ignored and not renewed, so the CSRF header
  does not apply. A browser cannot attach a bearer token on its own, so a cross-site page cannot call
  an endpoint for a signed-in user.
- **Not bound to the sender.** Under `standard` a leaked token works until it expires. Under a tenant's
  `fapi2` profile only DPoP-bound tokens are accepted, so a stolen token is useless without the
  caller's private key. See [machine callers under `fapi2`](fapi2-profile.md).
- **Keycloak.** The development realm has what the purchase request sample needs:
  - a confidential client `purchasing-system` with a service account, for the client credentials
    grant;
  - the password grant on the `purchasing-system` client, so tests can get a realm user's token with
    the endpoint audience;
  - the `axis` client role `purchasing-system`, held by that service account;
  - a roles mapper on the `purchasing-system` client that writes the `axis` client roles to the `roles`
    claim;
  - an audience mapper on the `purchasing-system` client that adds `axis-endpoints` to the token's
    audience;
  - the service account user with the fixed id `purchasing-system`, which is the token's `sub` and so
    the caller id.

  The client secret is `purchasing-system-development-secret`, for development only.
  `appsettings.Development.json` and the E2E server set `Oidc:EndpointAudience`. The Compose settings
  do not, so endpoint calls under Compose are a `401`.

## Service accounts

A service account lets another system read and write records, read data source rows, and start,
inspect and operate processes, under the same policies as a user. It is how a system reaches the
application's data beyond its inbound endpoints.

- **What it is.** A client of the tenant's identity provider that gets access tokens through the
  client credentials grant. Axis issues no API key. The identity provider gives it roles in the roles
  claim, as it does for people.
- **Tenant setting.** The expected audience is the tenant setting `Oidc:ServiceAudience`, separate
  from `Oidc:EndpointAudience`. In the development tenant, the E2E server and the test hosts its value
  is `axis-services`. A tenant without it accepts no service account token, and neither does a preview
  tenant, which takes neither audience from its main tenant. Under `Tenancy:Source=directory` the
  setting is the `service_audience` column of `platform.tenants`, and
  [`tenant oidc set --service-audience`](tenant-setting-commands.md#sign-in-settings) changes it.
- **Routes.** A service account may call:
  - every route of the [record API](record-api.md), record history included;
  - the [file upload](file-fields.md#upload) routes
    `POST /api/apps/{app}/entities/{entity}/fields/{field}/files` and the child field form
    `.../fields/{collection}/{field}/files`;
  - `GET /api/apps/{app}/data-sources/{dataSource}/rows`;
  - `POST /api/apps/{app}/processes/{process}/instances`;
  - the instance list and one instance of [process inspection](process-inspection.md);
  - the operator commands retry, cancel and resolve.

  The server marks each of these routes, so the choice of scheme, the CSRF exemption and the test of
  every route read the same mark and no path is matched by text.

- **Not reached.** The task API, the form state endpoint, site and page metadata, `GET /api/me`, Studio
  and the authoring API. A task asks a person to decide, so a service account never completes one. A
  bearer token on these routes is ignored, and the request needs a session.
- **Choosing the scheme.** On the routes above, a request with an `Authorization: Bearer` or
  `Authorization: DPoP` header is a service account request. It never falls back to the session
  cookie, and does not renew it. A request without the header uses the session. A tenant under `fapi2`
  accepts only the `DPoP` form.
- **CSRF.** A service account request needs no `X-Axis-Csrf` header. A browser cannot add an
  `Authorization` header to a cross-site request without a CORS preflight, which Axis does not allow.
- **Token checks.** The same as for [machine callers](#machine-callers): the signature and its
  algorithm, the issuer, the expiry, the token type, and an audience that holds `Oidc:ServiceAudience`.
  Any failure is a `401` with `WWW-Authenticate: Bearer`. Under the `fapi2` profile the token must also
  be DPoP-bound, the `401` says `WWW-Authenticate: DPoP algs="ES256 PS256"` and the token comes with a
  proof, see [machine callers under `fapi2`](fapi2-profile.md).
- **Order of checks.** The tenant, the token (`401`), the path (`404`), then the grant (`403`), as for
  a session. The checks that follow are those of each route.
- **Authorization.** The roles come from the configured roles claim. Policies apply unchanged, with
  their record filters and fields. A service account with no grant for the action gets a `403`.
- **Scope.** The decision uses the caller id and the roles claim, as it does for a person, and reads
  no `scope` or `authorization_details` claim. Policies grant by role, and the identity provider gives
  the service account its roles, so the roles claim carries its delegated authority. A token with a
  `scope` claim is not refused, and the claim changes nothing.
- **Actor.** The caller id comes from the configured user id claim, `sub` by default. It is the audit
  actor and the creator of a new record. The display name falls back to the client id, `azp`.
- **Rate limits.** Service account requests count in the bearer partition, by tenant and caller id, see
  [rate limits](headers-and-rate-limits.md#rate-limits).
- **Keycloak.** The development realm has a confidential client `reporting-system` with a service
  account, a role in the roles claim and an audience mapper that adds `axis-services`. The service
  account holds the `axis` client role `reporting-system` and has the fixed user id
  `reporting-system`, which is the token's `sub` and so the caller id. The client secret is
  `reporting-system-development-secret`, for development only. Tests use it for allowed and denied
  calls.

## Authorization

Every request is authenticated by default, and then the application's policies decide what the caller
may do, on the server and nowhere else. The `role` and `policy` resource files are described in
[roles and policies](roles-and-policies.md), and the reasons for server-side, default-deny
authorization in [D12](../decisions/d12-authorization-is-server-side-and-default-deny.md).

### Authentication by default

The server sets an authorization fallback policy, so a route that is not on the open list needs a
session or a valid caller token. A request with neither is refused before the route runs, so it never
opens a tenant database. The policy only requires an authenticated user, because the request's scheme
already picks the session, the service account or the endpoint caller.

- **The open list.** Nine routes need no session: `/health/live`, `/health/ready`, `GET /api/site`,
  `GET /api/texts/{locale}`, `GET /api/auth/sign-in`, `POST /api/auth/sign-out`,
  `GET /api/cell/auth/sign-in`, `POST /api/cell/auth/sign-out` and the SPA's pages. The two `/api/cell`
  routes are the [cell console](cell-console.md#sign-in)'s, and they answer only on its host. On the
  console host the open routes that answer are the health probes, `GET /api/site`,
  `GET /api/texts/{locale}`, the cell console's sign-in and sign-out, and the SPA's pages. The static
  files stay public, on the console host too. The OIDC handlers answer the sign-in callbacks inside
  authentication, so they are outside the routes.
- **The answers.** A session route answers `401` with the title "Sign in to read or write this data.".
  A service account route keeps its bearer answer. An inbound endpoint route keeps its
  `WWW-Authenticate: Bearer` challenge and its title. Every answer is problem details. An anonymous
  request for a file path that matches no route and no file answers `401`, not `404`. A signed-in GET
  to an unknown `/api` path answers `404`, not the page.
- **The test.** `AuthorizationRouteTests` calls every route with no session and no token and expects a
  `401` or `403` problem off the open list, and a bearer challenge on each inbound endpoint route. It
  also checks that the routes marked open are exactly that list, so a route added later is checked
  automatically.

### Policies

The record API and the data source endpoint match the user's roles, grant by action, and answer `403`
without a grant. These rules say how grants combine into what a user may read and write.

- **Roles.** A user's roles are the roles claim values, the roles that the application's provider
  mapping gave, kept in the [session](#the-session), and the roles that an administrator gave, which the
  session reads at every request, see [user administration](user-administration.md#roles). A value
  matches a declared `role` of the application by its exact name, and a value that names no declared
  role is ignored. A user matches a policy when they hold any of its roles.
- **Grants.** A policy holds grants, and each grant gives actions on one target, as
  [roles and policies](roles-and-policies.md) lists them: `read`, `create`, `update`, `delete`,
  `export` and `import` on an entity, `read` and `export` on a data source, `start`, `inspect`,
  `cancel`, `retry`, `resolve` and `reassign` on a process, and `invoke` on an endpoint. `inspect`
  lists a process's instances, see [process inspection](process-inspection.md#process-inspection).
  `cancel` stops a running or waiting instance, see [Cancel](process-inspection.md#cancel). `retry`
  runs a failed instance again, see [Retry](process-inspection.md#retry). `resolve` settles an unknown
  connector call, see [Resolve](process-inspection.md#resolve). Each is checked on its own. An entity
  grant may also have a record `filter` and a `fields` list. A user's grants are the grants of every
  policy they match.
- **Default-deny.**
  - A request without a session is a `401`.
  - A signed-in user with no grant for the action on the resource gets a `403`.
  - A record outside every read filter does not exist for the user, so it is a `404`, as for an
    unknown record.
  - A policy that cannot be evaluated denies.
- **Reads.** A record is readable when it matches the filter of at least one of the user's `read`
  grants on its entity. A grant without a `filter` matches every record. Record lists and data sources
  add the read filters to their SQL. A single record is checked in SQL too, with one statement per
  filtered grant, so reads and writes give the same answer and a filter that fails denies through its
  own grant only. The user id is a parameter, never part of the SQL text. A read filter that reads a
  pending computed field fails closed with a `409`, in the record API, the process endpoints and the
  data sources.
- **Writes.**
  - A create needs the new record to match the filter of a `create` grant.
  - An update needs the stored record to be readable, otherwise `404`. It then needs the stored record
    to match an `update` filter, otherwise `403`. The new record must match an `update` filter too,
    otherwise `403`.
  - A delete needs the stored record to be readable, otherwise `404`, and to match a `delete` filter,
    otherwise `403`.
  - The new record is the record as it will be stored: the stored values with the body on top, after
    the creator, sequence and computed fields are set. So a user cannot move a record out of their
    reach.
- **Fields on reads.** The user may read the union of the `fields` of their `read` grants on the
  entity. A `read` grant without `fields` covers every field. The readable fields belong to the entity,
  not to the record, so they never depend on which grant matched a record. Other fields are left out of
  records, data source rows, history details, task forms and page metadata. A record's `id` and
  `version` are always shown.
- **Fields on writes.** A create may write the union of the `fields` of the user's `create` grants on
  the entity, and an update the union of their `update` grants. Writing any other field is a `403`
  keyed at the field's path, such as `/values/decisionComments`.
- **Child entities.** Grants name only entities that are not child entities. Child rows are read and
  written through their owner's grants. An owner's `fields` entry for a `child-collection` field covers
  its rows as a whole. A grant on a child entity is a compile error.
- **Labels and lookups.** The label of a referenced record shows when the user may read the target
  entity's display field, and the target's record filters do not hide it. Lookups list target records
  through the record API, so through the user's read filters. An import's lookup of a display value in
  a reference cell follows the label rule, not the lookup rule, see [import](import-export.md#import).
- **Data sources.** A data source needs a `read` grant on it and a `read` grant on its root entity. The
  root entity's read filters are added to the data source's own filter in SQL. A data source that
  projects or aggregates a field the user may not read is a `403` as a whole. For a path through
  `reference` fields, every field along the path must be readable. See
  [data sources](data-sources.md#authorization).
- **Processes and tasks.** A process start needs a `start` grant on the process and read access to the
  subject record. An operator cancel needs the `cancel` grant and the same read access, an operator
  retry the `retry` grant and the same, and an operator resolve the `resolve` grant and the same. The
  task assignee check is enough to list, read and complete a task. A role assignee counts only when the
  active release declares the role. The task form leaves out fields the user may not read, and the
  subject label uses the display field of the task's release, see [processes](task-api.md). Listing
  the instances of a process needs an `inspect` grant on the process, and an instance is listed only
  when its subject record is readable. The instance detail needs the same. It also shows a call's input
  only when the user may read every field its mapping reads or writes, and a `sensitive` field is never
  shown. See [process inspection](process-inspection.md#process-inspection).
- **System work.** The worker, seed data and `updateRecord` steps run as `system`, and policies do not
  check them.
- **Failures.** A filter that fails at run time adds no records through its grant. The server logs the
  policy name and the grant's index, never a record value. A single record is checked one grant at a
  time, so another grant can still match it. In a list, PostgreSQL cannot fail one branch of an `or`,
  so the whole query fails with a `400` and returns no rows.
- **Module.** `Axis.Policy` holds the evaluation of the compiled policies of each release. Every
  endpoint and engine command calls it with a resource and an action. It answers allow or deny for an
  action on an entity, a data source, a process or an endpoint, and for an entity and an action the
  read filters to add and the readable and writable field sets. The record API, the data source
  endpoint and the process start endpoint call it and apply the record filters it answers. The record
  command in `Axis.Processes` applies the record filters and field sets for record API writes. The
  record API, the data source endpoint and the task API apply the field sets. The site endpoints call
  it to leave unreadable pages out of navigation, to answer `403` for them, and to list a widget's
  readable fields, allowed actions and writable fields in page metadata.
- **Only the server decides.** Page metadata and navigation follow the policies, but they only shape
  the UI. The server checks every request.

### Combining grants

The grants of all the user's policies combine with OR. A record is readable when any `read` grant's
filter is `true` for it. A filter that is `false`, `null` or fails adds no records through its grant.
So a mistake in one policy can never widen access.

Take the purchase request policies in [roles and policies](roles-and-policies.md), and a user who
holds both `employee` and `finance-reviewer`. The user's read filters on `PurchaseRequest` are
`requester == currentUser()` and `status in ('submitted', 'approved', 'returned', 'rejected')`:

| Request                          | `requester == currentUser()` | `status in ('submitted', 'approved', 'returned', 'rejected')` | Result   |
| -------------------------------- | ---------------------------- | ------------------------------------------------------------- | -------- |
| Their own draft                  | `true`                       | `false`                                                       | Readable |
| Someone else's submitted request | `false`                      | `true`                                                        | Readable |
| Someone else's draft             | `false`                      | `false`                                                       | `404`    |

The `false` filters add nothing. Someone else's draft matches neither filter, so it does not exist for
them.

Equality is [null-safe](expressions.md#null-semantics), so a department manager's filter
`department.head.user == currentUser()` is `false`, not `null`, for a request whose department has no
head. That grant adds nothing for the request.

A `null` filter adds nothing either. Suppose a policy granted `read` with the filter
`submittedAt >= dateTime('2026-01-01T00:00:00Z')`. A draft has no `submittedAt`, so the filter is
`null` for it, and that grant gives no access to the draft. Only another grant can make it readable.
