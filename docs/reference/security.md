# Authentication and authorization

Detailed reference for sign-in and the session, machine callers, service accounts, the FAPI 2.0
profile, authorization and the development users. The summary is in
[architecture.md](../architecture.md#authentication-and-authorization).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Sign-in and the session

It follows
[D23](../decisions/d23-sign-in-through-oidc-and-the-bff-session.md).
Sign-in, the session, `GET /api/me` and sign-in needed are in place. Every tenant that `tenant create` makes signs in through its realm in
[Axis's identity provider](identity-provider.md).

- **Flow.** Sign-in uses the OIDC authorization code flow with PKCE. The
  server is the confidential client: it exchanges the code for tokens, reads
  the claims from the ID token and keeps no token after that (D9). The
  identity provider returns the code in the query string. The request asks
  for the `openid`, `profile` and `email` scopes. It carries `max_age` set to
  8 hours, the session's absolute lifetime, so the identity provider asks a
  person whose login there is older to log in again. The callback refuses an
  ID token whose `auth_time` is missing or older than 8 hours, see
  [session lifetimes and re-authentication](#session-lifetimes-and-re-authentication). Pushed authorization
  requests (PAR) stay off under the `standard` profile (D9). Under a tenant's
  `fapi2` profile they are required, see
  [FAPI 2.0 profile](#fapi-20-profile).
  - The server registers one OIDC client per tenant. All of them share the
    callback path, and each one ignores a callback that arrives on another
    tenant's host.
  - A sign-in that starts on a site page goes to the tenant's realm as that
    site's client, `site-<path>`, and the callback redeems the code with the
    secret that derives from the platform client's secret. The ID token's `aud`,
    and its `azp` when present, must be the client that started the sign-in.
    Otherwise the callback is refused with reason `invalidToken` and creates no
    session, also for a token issued to another site's client. A sign-in from
    another page, a preview tenant, a tenant without realm details and a
    `fapi2` tenant use the platform client `axis`. See
    [sites and sign-in](identity-provider.md#sites-and-sign-in).
- **Tenant settings.** Each tenant in the [`Tenants` section](../architecture.md#tenant-configuration)
  has an `Oidc` block. `Claims` names the claims that give the user id, the
  display name and the roles. Each claim name is optional, and the defaults
  are `sub`, `name` and `roles`. The optional `EndpointAudience` is for
  [machine callers](#machine-callers), and the optional `ServiceAudience` is
  for [service accounts](#service-accounts). `Claims` also names `Email` and
  `Locale`, by default `email` and `locale`, which each sign-in writes to the
  user's profile, see [User profiles](notifications.md#user-profiles).

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

- **Startup checks.** The server's startup fails, naming every problem as
  for the tenant configuration, when a tenant has no `Oidc` block, or when
  its `Authority`, `ClientId` or `ClientSecret` is missing or blank. Under the
  `fapi2` profile the client key replaces the client secret: the tenant needs a
  client key with a key id, and no client secret. A `Profile` other than
  `standard` or `fapi2` fails the start too. The check confirms that the key and
  its key id are present and does not parse the PEM. No message holds a key.
  A preview tenant has no `Oidc` block, and takes its
  main tenant's, see [preview tenants](../architecture.md#preview-tenants). Only the
  server reads and checks the block. The worker reads the same `Tenants`
  section but signs nobody in, so it ignores `Oidc`. Under
  `Tenancy:Source=directory`, the `Oidc` settings of a tenant come from the
  [tenant directory](../architecture.md#tenant-directory), not from `Tenants`. Under `directory`,
  each refresh registers the OIDC client and the endpoint caller scheme of a new
  tenant, builds them again for a tenant whose `Oidc` settings changed, and
  removes both for a tenant that is no longer served. The identity provider's
  metadata is fetched again only for the tenants that changed.
- **Secret.** The client secret comes from the environment, such as
  `Tenants__default__Oidc__ClientSecret`. It is never in a committed file,
  except the development fixtures, which are never valid outside
  `Development` and tests. `appsettings.Development.json` and the E2E server
  use the development realm's secret. Under
  `Tenancy:Source=directory`, the client secret is stored encrypted in the
  tenant directory and decrypted only when the sign-in client is set up, see
  [platform database](storage.md#platform-database). `tenant create` and `tenant restore` set it,
  and no command changes it afterwards.
- **Endpoints.**
  - `GET /api/auth/sign-in?returnUrl=` redirects to the tenant's identity
    provider. `returnUrl` must start with a single `/`. A value that does
    not, or that starts with `//` or `/\`, is a `400` problem details
    response. A missing `returnUrl` means `/`. With `reauthenticate=true`
    the redirect also carries `prompt=login` and `max_age=0`, so the identity
    provider asks for credentials even when its own session is still open.
    The new session then holds a fresh login time, which
    [ending sessions](#session-lifetimes-and-re-authentication) needs.
  - `GET /api/auth/callback` is where the identity provider returns. It
    creates the session and redirects to `returnUrl`. A callback that fails
    creates no session and redirects to the shell's sign-in page, `/?signIn=failed`,
    for example when the person cancels at the identity provider, or when the
    ID token has no recent `auth_time`. The page says
    that sign-in did not complete and shows no reason, and the address holds
    none. See the [home page](frontend.md) and
    [D84](../decisions/d84-asvs-level-2-and-a-server-side-session.md).
  - `POST /api/auth/sign-out` ends the session and returns `200` with
    `{ "signOutUrl" }`. The SPA sends the browser there, and the identity
    provider returns it to `/?signedOut`, see
    [platform home page](shell.md#platform-home-page). The SPA calls sign-out
    with `fetch` to send the CSRF header, which cannot follow a redirect to the
    identity provider as a page. A successful sign-out also
    sends `Clear-Site-Data: "cache", "storage"`, so the browser drops the
    site's HTTP cache and storage, and a refused one sends no such header.
    The SPA keeps only the theme and the locale in browser storage, under
    `axis.themeMode` and `axis.locale`, so a sign-out resets both. See
    [headers](headers-and-rate-limits.md#security-headers). The shell header's
    [account button](frontend.md) offers sign-in and
    sign-out. The home page `/` calls `GET /api/auth/sign-in` at once when
    `GET /api/me` or `GET /api/sites` answers `401`. Any other page shows a
    sign-in prompt, and only a click on its button calls it.
  - `GET /api/me/sessions`, `DELETE /api/me/sessions/{id}` and
    `DELETE /api/me/sessions` let a person see and end their own sessions.
    They need a session, not a service account's bearer token. The shell's
    [sessions screen](platform-pages.md) at `/account` calls them.
    - The list answers `{ "endAllowed", "sessions": [{ "id", "signedInAt",
      "lastRequestAt", "current" }] }`. It holds only the caller's live
      sessions of the host that the request came on, newest sign-in first. `current` marks the session that made
      the request. `endAllowed` is true when the session may end sessions now.
    - A session's `id` is the base64url form of the row's `id_hash`. It
      cannot be turned back into a cookie.
    - The two delete routes need the CSRF header and answer `204`. The first
      ends the session with that id, which may be the current one. The second
      ends all the caller's sessions of the request's host except the current
      one. Neither touches a session of another host.
    - Both delete routes answer a `403` problem and delete nothing unless the
      session's last login at the identity provider is no more than 5 minutes
      old. A session with no stored login time cannot end sessions. The check
      comes before the lookup of the id.
    - An id that is not one of the caller's live sessions of the request's
      host answers a `404` problem, whoever owns it.
- **Audit.** Sign-in writes
  [audit records](storage.md#audit-records-and-sequence-counters)
  in the database of the tenant that the host resolves to, see
  [D50](../decisions/d50-sign-in-audit-records-and-security-scanning-in-ci.md).
  - The callback writes `session.signedIn` when it creates a session. It writes
    `session.signInRefused`, with a reason code, when it refuses. A refused
    callback still redirects to the sign-in page. A token with no user id, or
    with an `auth_time` that is missing or older than 8 hours, is refused with
    the reason `invalidToken`. So is an ID token with a nonce that the sign-in
    did not send, or with an `aud` that is not the tenant's client id.
  - A token that is valid is refused with the reason `providerNotAccepted` when
    the tenant's [platform sign-in](identity-provider.md#platform-sign-in) does
    not accept the provider that signed the person in, or does not accept
    accounts, and no active site that shares the tenant's host allows it.
  - A token that is valid, for a sign-in that started on a site, is refused with the reason `providerNotAllowed`
    when the site's `signIn` does not allow the provider that signed the person in, or when the site is no longer
    active. It is checked before `acrNotAccepted` and `providerNotAccepted`. See
    [sites and sign-in](identity-provider.md#sites-and-sign-in).
  - A token that is valid, for a sign-in that started on a site, is refused with the reason `acrNotAccepted` when the
    provider that signed the person in has an `acr` list and the token's `identity_provider_acr` is missing or not in
    it. It is checked after `providerNotAllowed` and before `providerNotAccepted`.
  - The callback also compares the `issuer` of the provider's metadata with the
    tenant's `Authority`, character for character, after one trailing `/` is
    removed from the `Authority`. Metadata with another issuer creates no
    session and is refused as `invalidIssuer`. The check runs before the code is
    sent to the token endpoint, for every tenant. The start of sign-in does not
    run it.
  - `POST /api/auth/sign-out` writes `session.signedOut` only when a session
    existed.
  - An expired session writes nothing. A host that resolves to no tenant writes
    nothing, because there is no tenant database.
  - Each record holds the [shortened client address](storage.md#audit-records-and-sequence-counters),
    never a token, a cookie or the user agent. The address follows the
    [forwarded headers](host-settings.md#forwarded-headers) rules of
    [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
- **Session.** The session state is a row of `axis.sessions` in the tenant
  database, see [D84](../decisions/d84-asvs-level-2-and-a-server-side-session.md).
  - The row holds a hash of the session id, the user id, the display name, the
    roles, the time of sign-in, the time of the last request and the time of the
    person's last login at the identity provider, read from the ID token's
    `auth_time`. It also holds the lower-case site path of the host that made the
    session, null for the tenant's own hosts, and the alias of the provider that
    signed the person in, from the ID token's `identity_provider` claim, none for
    accounts, and the roles that each application's provider mapping gave, by application id. Given roles are not in the row, the session reads them from `axis.given_roles` at every request. On a preview tenant it reads those of its main tenant, from the main tenant's database. A session from before a column existed holds none of it and counts
    as a sign-in through accounts on the tenant's own hosts. The table never holds the session id or a token, and none reaches the browser.
  - The cookie is one HttpOnly, SameSite=Lax cookie. It holds only the random
    session id, 256 bits, wrapped and protected by the key ring below. It
    holds no claim. The row's key is the SHA-256 hash of the id.
  - Sign-in always creates a new session id, even when the browser already
    sends a valid session, and the old session ends. Sign-out deletes the row,
    so a copied cookie stops working at once, on every replica.
  - A person may hold any number of sessions at once. Each sign-in from
    another browser adds one. There is no limit, so nothing is refused or
    ended when more are open. A sign-in from a browser that already holds a
    session ends that session instead, as above. A person lists and ends
    their sessions on the sessions screen, which uses `GET /api/me/sessions`.
    An operator ends a person's sessions with
    [`tenant session end --user`](tenant-setting-commands.md#sessions).
  - A request reads the row and updates the time of the last request. A row
    ends 8 hours after sign-in, or after 1 hour without requests, whichever
    comes first. A request past either limit counts as no session, and the
    row is deleted. Every request with a session renews the cookie.
  - The row has no tenant id, because each tenant has its own database. A
    cookie on another tenant's host finds no row there, so it counts as no
    session.
  - A session stays on the host that made it. A session made on a site's own
    host counts as no session on the tenant's own hosts, and the other way
    round. The refused request leaves the row alone, and a request never
    deletes a row of another host. A row past a limit that another host meets
    is left for the worker's cleanup. Sign-out, a new sign-in and an expired
    session delete only the row of their own host.
  - The cookie is named `__Host-Axis.Session`, with `Secure`, `Path=/` and no
    domain, so the browser refuses it on plain http or from a subdomain. In
    `Development` and `Testing`, which run over plain http, it keeps the name
    `Axis.Session`. The name depends only on the environment.
  - The OIDC correlation cookie and the nonce cookie last for one sign-in
    round trip. Outside `Development` and `Testing` they are named
    `__Secure-Axis.Correlation.*` and `__Secure-Axis.Nonce.*`, and they are
    always Secure. Their path is the callback path, so the `__Host-` prefix is
    not possible. In `Development` and `Testing` they keep the framework's
    names.
  - The worker deletes the expired rows in each tenant it serves. It uses its
    own clock, runs once when the tenant becomes ready and then at most every
    5 minutes, and never touches a tenant it does not serve. `tenant restore`
    empties the table, so no session comes back with an archive.
  - An operator ends one user's sessions or all sessions of a tenant with
    [`tenant session end`](tenant-setting-commands.md#sessions).
  - In `Development` and `Testing`, which run over plain http, the session
    cookie and the OIDC helper cookies are Secure only on https requests.
  - Behind a named proxy, the callback and sign-out addresses and the cookie's
    Secure flag use the forwarded scheme and host. See
    [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md) and the
    [forwarded headers settings](host-settings.md#forwarded-headers).
  - The cookie is protected with a key ring that every server replica shares.
    The keys are in `platform.data_protection_keys`, encrypted with
    `Platform:SecretKey`. It protects the cookie's session id wrapper, so
    every replica reads it. The session itself survives a restart and works
    on every replica because its row is in the database. During a key
    rotation a key that `Platform:PreviousSecretKey` encrypted still
    decrypts, and `platform rekey` then re-encrypts it, so nobody is signed
    out. The server fixes the data protection application name to `Axis`,
    so replicas at different paths read the same keys. A key that does not
    decrypt stops the start.
- **Tenant binding.** A session whose tenant id differs from the tenant
  [resolved from the host](../architecture.md#request-resolution) counts as no session. So a
  cookie taken to another tenant's host signs nobody in there.
- **Roles.** The roles come from the roles claim at sign-in and are kept in
  the session row. A role change at the identity provider applies at the next
  sign-in. Each application's roles also come from its provider's `roles` mapping, kept by application
  id, see [users](identity-provider.md#users). The roles the users page gives join them, and a
  change on the users page applies at the person's next request, see [roles](user-administration.md#roles).
- **Current user.** `GET /api/me` keeps its shape,
  `{ "id", "displayName", "roles" }`, and reads the session. It adds a boolean `console`, true when the user holds
  the role that `Console:OperatorRole` names. With no value set it is false for everyone. See the
  [platform home page](shell.md#platform-home-page) and the
  [console settings](host-settings.md#console-settings). It adds a boolean
  `studio`, true when `Studio:Folders` is not empty or `Studio:ApplicationsPath` is set, and the user holds
  `Studio:AuthorRole`. See [D38](../decisions/d38-studio-edits-application-folders-through-an-authoring-api.md).
  It adds a boolean `studioPublisher`, true when
  Studio is on and the user holds both `Studio:AuthorRole` and
  `Studio:PublisherRole`. See [D47](../decisions/d47-publishing-from-studio.md).
  It adds `studioPreviewHost`, the host of the tenant's served preview tenant. It
  is null for a user who may not use Studio and for a tenant without a preview
  tenant. See [D48](../decisions/d48-preview-tenants.md).
  It adds a boolean `studioTest`, true when `studio` is true
  and `Studio:Tests` is true. See
  [D55](../decisions/d55-running-test-scenarios-from-studio.md).
  It adds a boolean `studioCreate`, true when `studio` is true and `Studio:ApplicationsPath` is set. See
  [D51](../decisions/d51-creating-applications-and-resources-from-studio.md).
  Without a session it is a `401`, as today. Record writes, process starts and the
  task API read the current user the same way, and their audit records store
  the user's id and no display name.
- **Sign-in needed.** Without a session, these answer a `401` problem
  details response:
  - every route under `/api/apps/{app}/`: records, record history, data
    source rows, process start and tasks. The session is checked before any
    `404`, so a caller without a session cannot learn which applications or
    records exist;
  - `GET /api/sites`, `GET /api/sites/{path}`,
    `GET /api/sites/{path}/texts/{locale}` and
    `GET /api/sites/{path}/pages/{page}`. Site texts belong to an
    application, so they count as application content. The session is checked
    before any `404`, so a caller without a session cannot learn which sites
    or pages exist;
  - `GET /api/me` and `GET /api/about`;
  - `GET /api/me/sessions`, `DELETE /api/me/sessions/{id}` and
    `DELETE /api/me/sessions`.

  Every one of these `401`s has the title "Sign in to read or write this data.".

  Inbound endpoints under `/api/apps/{app}/endpoints/` are the exception.
  They take a bearer token and no session, see
  [Machine callers](#machine-callers).

  These stay open, because the shell needs them before anyone signs in:
  `GET /api/site`, `GET /api/texts/{locale}`, `GET /api/auth/sign-in`,
  `GET /api/auth/callback`, every path under `/health` and the SPA's static
  files. `POST /api/auth/sign-out` needs the CSRF header but no
  session.

- **CSRF.** Every `POST`, `PUT`, `PATCH` and `DELETE` under `/api` must
  carry `X-Axis-Csrf: 1`, otherwise `403`. The server checks the header
  right after it resolves the tenant and before authentication, so a refused
  write does no work and does not renew the session cookie. On the record
  API, the data source endpoint, the process start endpoint and the task
  API, the session check follows it. `POST /api/auth/sign-out` needs no
  session, so no session check follows it. The SPA sends every `/api`
  call through one wrapper around `fetch`, which adds the header to its
  writes. See the record API's
  [CSRF header rule](record-api.md#create-update-and-delete).
  Paths under `/api/apps/{app}/endpoints/` are exempt from the header,
  because they take a bearer token and no session cookie. See
  [Machine callers](#machine-callers).
- **Tests.** Tests of sign-in itself start the development realm in Keycloak
  through Testcontainers, and drive its login page. Tests of the session
  lifetime run on real PostgreSQL, create the session through a test-only hook
  and use a test clock, so they see the row deleted. Server tests that only
  need some session use an in-memory session store.
  Tests of [machine callers](#machine-callers) get client credentials tokens
  from the same container, and expire them with a test clock.
  Integration tests of other endpoints sign in through a test authentication
  scheme that only test hosts register. A request names its user in a
  header, and the scheme builds the same principal as a session of that user.
  The E2E sign-in journey drives the Keycloak login page. For the other E2E
  journeys, a Playwright setup step signs each realm user in on the login page
  once and saves the browser session. Journeys that act as a user load or swap
  those saved sessions.
- **Keycloak realm.** Docker Compose, `scripts/dev.sh` and the E2E run start
  Keycloak with an imported development realm, `axis`. This part is in place:
  the realm is `samples/keycloak/axis-realm.json`, and one shared
  Testcontainers container serves the whole integration run. Under `scripts/dev.sh` and in
  the E2E run, the server signs people in through it. Under Docker Compose
  it does not yet, because the server in its container cannot reach Keycloak
  at the address the browser uses. So under Compose the site endpoints, the
  record API and the data source endpoint answer `401`, and an application
  page shows the sign-in prompt, and its button starts a sign-in that cannot complete.
  - It holds the development users listed in
    [development users](#development-users), with fixed ids that seed data
    and E2E journeys name.
  - Every user has the same development-only password.
  - The roles are client roles of the confidential client `axis`. A
    client-role mapper writes them to the `roles` claim, so the claim holds
    only the sample roles.
  - The confidential client `purchasing-system` stands in for the
    purchasing system as a [machine caller](#machine-callers). Its service
    account gets tokens through the client credentials grant.

### Session lifetimes and re-authentication

Two sessions exist after a sign-in: Axis's session and the identity
provider's session. Axis does not control the identity provider's session.
The two are coordinated only through `max_age` and `auth_time`, and the
sign-out address. This holds under both
[sign-in profiles](#fapi-20-profile).

| Under either profile | Axis's session                                                   | The identity provider's session                             |
| -------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------- |
| What starts it       | The callback creates the row in `axis.sessions`                  | A login on the identity provider's page                     |
| How long it lasts    | 8 hours after sign-in, or 1 hour without requests                | Set at the identity provider                                |
| What ends it         | Sign-out, expiry, a new sign-in, an administrator's disable or authenticator reset, an operator or a tenant restore | Its own expiry, the `signOutUrl` that Axis sign-out uses, or an administrator's disable or authenticator reset |

- **Axis's session.** The lifetimes are those of the
  [session](#sign-in-and-the-session). A new sign-in ends the old session, an
  operator can end it with
  [`tenant session end`](tenant-setting-commands.md#sessions), an administrator's
  disable or authenticator reset of a user ends every session of that user, see
  [user administration](user-administration.md#the-users-page), and
  `tenant restore` empties the table.
- **The identity provider's session.** Its lifetimes are set at the identity
  provider. The development realm sets none, so it keeps Keycloak's defaults.
  Axis sets them in every realm that `tenant create` makes: at most 8 hours, the lifetime of Axis's session.
  Sign-out from Axis ends both sessions: it deletes the row and sends the
  browser to the `signOutUrl`, which ends the identity provider's session.
- **When a person signs in again.** When Axis's session ends, the next request
  has no session, and the shell starts a sign-in. At the identity provider,
  the person logs in again whenever the last login there is older than 8 hours.
  This holds because every sign-in request carries `max_age=28800`. So a
  still-open identity provider session cannot skip the login after Axis's
  session has ended at its 8-hour limit.
- **Re-authentication before ending sessions.** A person ends sessions only
  when the session's stored `auth_time` is no more than 5 minutes old, with
  no clock skew allowed. Otherwise the shell sends the person to
  `GET /api/auth/sign-in?reauthenticate=true`, which makes the identity
  provider ask for credentials again. The [sessions screen](platform-pages.md)
  offers this as its **Sign in again** button and returns to `/account`. A person who
  signed in before the column existed has no stored time and must sign in again first.
- **The check at the callback.** The callback refuses an ID token whose
  `auth_time`, the time of the last login at the identity provider, is missing,
  unreadable or older than 8 hours. The check allows the clock skew of the token
  validation, 5 minutes by default, and uses the clock of the session store. The
  identity provider must send `auth_time` in the ID token. If it leaves the
  claim out, every sign-in of that tenant is refused. Keycloak sends it by
  default.
- **No back-channel logout.** Ending the identity provider's session does not
  end Axis's session, because Axis has no back-channel logout. A person who
  logs out only at the identity provider keeps the Axis session until it ends.
  Sign-out from Axis ends both.
- **Under `fapi2`.** The lifetimes and the rules are the same. The one
  difference is where `max_age` goes: into the pushed authorization request
  instead of the redirect, see [FAPI 2.0 profile](#fapi-20-profile).

### Development users

These are test fixtures. Docs and GitHub text name roles instead. A new
sample reuses a user that holds the role it needs before it adds a user.
`scripts/lint.sh` checks this table against `samples/keycloak/axis-realm.json`.
Every user signs in with the password `axis` and a 6-digit code. Each user's TOTP secret is the `otp` credential in that
file, and the realm accepts a code more than once. See [development realm and users](../development.md#development-realm-and-users).

| User               | Roles                   | Used by                                                                                                                                       |
| ------------------ | ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `requester`        | `employee`, `requester` | Purchase request, leave request and service desk samples; E2E journeys, request approval journey. Has the locale `vi`, the only user with one |
| `employee`         | `employee`              | Purchase request, leave request and asset management samples (holds assets); E2E journeys                                                     |
| `manager`          | `department-manager`    | Purchase request and leave request samples; E2E journeys                                                                                      |
| `manager-2`        | `department-manager`    | Purchase request and leave request samples; E2E journeys                                                                                      |
| `finance-reviewer` | `finance-reviewer`      | Purchase request sample seeds; E2E journeys                                                                                                   |
| `operator`         | `operator`              | Purchase request sample's Approvals inspector; E2E journeys                                                                                   |
| `author`           | `author`, `publisher`   | Studio and the authoring API under `scripts/dev.sh`; E2E Studio journeys                                                                      |
| `agent`            | `agent`                 | Service desk sample; E2E journeys                                                                                                             |
| `lead`             | `service-desk-lead`     | Service desk sample; E2E journeys                                                                                                             |
| `approver`         | `approver`              | Request approval template journey; E2E journeys                                                                                               |
| `lead-2`           | `service-desk-lead`     | Service desk sample; E2E claim and reassign journey                                                                                           |
| `asset-manager`    | `asset-manager`         | Asset management sample; E2E asset management journey                                                                                         |
| `auditor`          | `auditor`               | Asset management sample; E2E asset management journey; the user whose roles the E2E users page journey gives and removes                       |
| `administrator`    | `tenant-administrator`  | "Users" in the shell, E2E shell journey; E2E users page journey                                                                              |

## Machine callers

It follows [D27](../decisions/d27-inbound-endpoints.md). The token
checks, the tenant binding, the endpoint-only acceptance, the CSRF exemption
and the development realm's client are built. The `endpoint` resource and its
compile checks are built too, and so are the endpoint call with its `output`
and `wait`, its `Idempotency-Key`, and the status location. Other systems call [inbound endpoints](inbound-endpoints.md)
with a bearer token. This sits next to the browser session: the session
stays the only way in for people, and a bearer token is the only way in for
systems.

- **Client credentials.** A caller is a system, not a browser. It gets an
  OAuth 2.0 access token from the tenant's `Oidc` authority through the
  client credentials grant, and sends it as `Authorization: Bearer <token>`.
  Under a tenant's `fapi2` profile it sends `Authorization: DPoP <token>` with
  a DPoP proof, see [machine callers under `fapi2`](#fapi-20-profile).
- **Tenant setting.** The expected audience is a new tenant setting,
  `Oidc:EndpointAudience`. In the development tenant its value is
  `axis-endpoints`:

  ```json
  "Oidc": {
    "Authority": "http://localhost:8080/realms/axis",
    "EndpointAudience": "axis-endpoints"
  }
  ```

  The setting is optional. A tenant without it accepts no bearer token, so
  every endpoint call there is a `401`. The server's
  [startup checks](#sign-in-and-the-session) do not change. The worker
  ignores it, as it ignores the rest of `Oidc`.

- **Token checks.** The server checks each token against the tenant
  [resolved from the host](../architecture.md#request-resolution). Any failure is a `401`
  with `WWW-Authenticate: Bearer`, which never says which check failed. The
  server writes a warning with the tenant and the reason, see the
  [log inventory](log-inventory.md#security-events). Under
  the `fapi2` profile the `401` says `WWW-Authenticate: DPoP algs="ES256 PS256"`
  and the token must also be DPoP-bound, see
  [machine callers under `fapi2`](#fapi-20-profile).
  - The signature, with the keys the authority publishes (its JWKS).
  - The issuer, which must equal the tenant's `Authority`. So another
    tenant's token is a `401`.
  - The audience, which must contain the tenant's `Oidc:EndpointAudience`.
  - The expiry.
  - The signature algorithm, which must be `RS256`, `RS384`, `RS512`, `PS256`,
    `PS384`, `PS512`, `ES256`, `ES384` or `ES512`. A token signed with `HS256`
    or any other algorithm, or with `none`, is a `401`.
  - The token type, which must be an access token. A token marked as another
    type is a `401`: a header `typ` other than `at+jwt`,
    `application/at+jwt` or `JWT`, or a payload `typ` claim other than
    `Bearer` or `DPoP`. So Keycloak's ID token, which has the claim `ID`, is
    refused even when its audience is the expected one. A token with no `typ`
    is not refused, so identity providers that mark no type still work.
- **Roles.** The caller's roles come from the configured roles claim
  (`Claims:Roles`), matched against the declared roles as for users.
- **Caller id.** The caller's id comes from the configured user id claim
  (`Claims:UserId`, default `sub`). It is the audit actor and the new
  record's creator.
- **Display name.** The display name comes from the configured display
  name claim, else the token's client id (`azp`). Audit records store no
  display name, so a history shows the caller's id, as a caller has no profile.
- **Endpoint routes only.** The endpoint audience is accepted only on
  `POST /api/apps/{app}/endpoints/{name}` and its status location. Everywhere
  else a bearer token with that audience is ignored, so it never reaches the
  record API, the data source endpoint or the task API. The request's default
  authentication is the bearer token on paths under
  `/api/apps/{app}/endpoints/`, and the session everywhere else.
  [Service accounts](#service-accounts) send bearer tokens with another
  audience to the record API, to data source rows, and to process start and
  inspection, and to the operator commands. The task API
  stays closed to bearer tokens.
- **Clock.** The expiry is checked against the server's clock service, with
  the usual 5 minutes of clock skew, so tests can replace the clock.
- **No session.** On endpoint routes the session cookie is ignored and not
  renewed, so the CSRF header does not apply. A browser cannot attach a bearer token on its
  own, so a cross-site page cannot call an endpoint for a signed-in user.
- **Not bound to the sender.** Under `standard` a leaked token works until it
  expires. Under a tenant's `fapi2` profile only DPoP-bound tokens are
  accepted, so a stolen token is useless without the caller's private key. See
  [machine callers under `fapi2`](#fapi-20-profile).
- **Keycloak.** The development realm gets what the purchase request sample
  needs:
  - a confidential client `purchasing-system` with a service account, for
    the client credentials grant;
  - the password grant on the `purchasing-system` client, so tests can get a
    realm user's token with the endpoint audience;
  - the `axis` client role `purchasing-system`, held by that service account;
  - a roles mapper on the `purchasing-system` client that writes the `axis`
    client roles to the `roles` claim;
  - an audience mapper on the `purchasing-system` client that adds
    `axis-endpoints` to the token's audience;
  - the service account user with the fixed id `purchasing-system`, which is
    the token's `sub` and so the caller id.

  The client secret is `purchasing-system-development-secret`, for
  development only. `appsettings.Development.json` and the E2E server set
  `Oidc:EndpointAudience`. The Compose settings do not, so endpoint calls
  under Compose are a `401`.

## Service accounts

It follows
[D86](../decisions/d86-service-accounts-for-the-record-and-process-apis.md).
A service account lets another system read and write records, read data
source rows, start, inspect and operate processes, under the same policies as a
user. It replaces the point of D27 that accepts bearer tokens only on endpoint
routes. The record API, data source rows, process start, process inspection and
the operator commands take service accounts now.

- **What it is.** A client of the tenant's identity provider that gets
  access tokens through the client credentials grant. Axis issues no API key.
  The identity provider gives it roles in the roles claim, as it does for
  people.
- **Tenant setting.** The expected audience is a new tenant setting,
  `Oidc:ServiceAudience`, separate from `Oidc:EndpointAudience`. In the
  development tenant, the E2E server and the test hosts its value is
  `axis-services`. A tenant without it accepts no service account token, and
  neither does a preview tenant, which takes neither audience from its main
  tenant. Under `Tenancy:Source=directory` the setting is the
  `service_audience` column of `platform.tenants`.
  [`tenant oidc set --service-audience`](tenant-setting-commands.md#sign-in-settings)
  changes it.
- **Routes.** A service account may call:
  - every route of the [record API](record-api.md), record history included;
  - the [file upload](file-fields.md#upload) routes
    `POST /api/apps/{app}/entities/{entity}/fields/{field}/files` and the child
    field form `.../fields/{collection}/{field}/files`;
  - `GET /api/apps/{app}/data-sources/{dataSource}/rows`;
  - `POST /api/apps/{app}/processes/{process}/instances`;
  - the instance list and one instance of
    [process inspection](process-inspection.md);
  - the operator commands retry, cancel and resolve.

  The server marks each of these routes, so the choice of scheme, the CSRF
  exemption and the test of every route read the same mark and no path is
  matched by text.

- **Not reached.** The task API, the form state endpoint, site and page
  metadata, `GET /api/me`, Studio and the authoring API. A task asks a person
  to decide, so a service account never completes one. A bearer token on these
  routes is ignored, as today, and the request needs a session.
- **Choosing the scheme.** On the routes above, a request with an
  `Authorization: Bearer` or `Authorization: DPoP` header is a service account
  request. It never falls back to the session cookie, and does not renew it. A
  request without the header uses the session. A tenant under `fapi2` accepts
  only the `DPoP` form.
- **CSRF.** A service account request needs no `X-Axis-Csrf` header. A
  browser cannot add an `Authorization` header to a cross-site request
  without a CORS preflight, which Axis does not allow.
- **Token checks.** The same as for [machine callers](#machine-callers):
  the signature and its algorithm, the issuer, the expiry, the token type, and
  an audience that holds `Oidc:ServiceAudience`. Any failure is a `401` with
  `WWW-Authenticate: Bearer`. Under the `fapi2` profile the token must also
  be DPoP-bound, the `401` says `WWW-Authenticate: DPoP algs="ES256 PS256"` and
  the token comes with a proof, see
  [machine callers under `fapi2`](#fapi-20-profile).
- **Order of checks.** The tenant, the token (`401`), the path (`404`), then
  the grant (`403`), as for a session. The checks that follow are those of
  each route.
- **Authorization.** The roles come from the configured roles claim. Policies
  apply unchanged, with their record filters and fields. A service account
  with no grant for the action gets a `403`.
- **Scope.** The decision uses the caller id and the roles claim, as it does
  for a person, and reads no `scope` or `authorization_details` claim. Policies
  grant by role, and the identity provider gives the service account its roles,
  so the roles claim carries its delegated authority. A token with a `scope`
  claim is not refused, and the claim changes nothing.
- **Actor.** The caller id comes from the configured user id claim, `sub` by
  default. It is the audit actor and the creator of a new record. The display
  name falls back to the client id, `azp`.
- **Rate limits.** Service account requests count in the bearer partition,
  by tenant and caller id, see
  [rate limits](headers-and-rate-limits.md#rate-limits).
- **Keycloak.** The development realm has a confidential client
  `reporting-system` with a service account, a role in the roles claim and an
  audience mapper that adds `axis-services`. The service account holds the
  `axis` client role `reporting-system` and has the fixed user id
  `reporting-system`, which is the token's `sub` and so the caller id. The
  client secret is `reporting-system-development-secret`, for development only.
  Tests use it for allowed and denied calls.

## FAPI 2.0 profile

It follows [D83](../decisions/d83-the-fapi-20-profile-per-tenant.md). A tenant
can turn on the FAPI 2.0 Security Profile for sign-in and for machine callers.
The tenant setting, the client key, the startup check, the machine callers, and
PAR, `private_key_jwt`, the `iss` check, DPoP at sign-in and the client key rotation are built. The
verification bullet below is _(planned for M32)_.

- **Tenant setting.** `Oidc:Profile` is `standard` or `fapi2`, and
  `standard` when unset. Any other value stops the server's start, naming the
  tenant. A preview tenant takes its main tenant's profile.
- **Sign-in under `fapi2`.**
  - The server sends the authorization request through pushed authorization
    requests (PAR), PKCE `S256` included. The browser goes to the identity
    provider with only the `request_uri` that comes back. A provider whose
    metadata has no PAR endpoint fails the tenant's sign-in: the server logs a
    warning that names the tenant, and the browser goes to `/?signIn=failed`.
    No audit record is written, as no callback happened.
  - The client authenticates at the PAR endpoint and at the token endpoint with
    `private_key_jwt`: a JWT of 60 seconds, signed with the tenant's newest
    client key, with the `kid` of that key. Its `iss` and `sub` are the client
    id, its `aud` is the tenant's `Authority`, and each request has its own
    `jti`. The server sends no client secret, even when the tenant has one.
    Client keys are EC P-256 keys and the assertion is signed with ES256. RSA
    keys are not supported.
  - The server checks the `iss` parameter of the authorization response
    (RFC 9207). It must equal the tenant's `Authority` character for
    character, after one trailing `/` is removed from the `Authority`. A
    callback with no `iss`, or a different one, creates no session and writes
    `session.signInRefused` with the reason `invalidIssuer`. The check runs
    after the state checks, so a callback with a bad state is still refused as
    `invalidState` or `otherTenant`. The code is never sent to the token
    endpoint. An error response from the provider is refused as
    `providerError`, whatever its `iss`.
  - The token request carries a DPoP proof (RFC 9449), signed with a new P-256
    key that is made for each sign-in and thrown away after it. The proof has
    `typ` `dpop+jwt`, the `htm` `POST` and the token endpoint as `htu`, and
    holds only the public key. If the provider answers `use_dpop_nonce`, the
    server sends the request once more, with the nonce in a new proof signed
    with the same key and with a new client assertion. A second refusal fails
    the sign-in as `providerError`. The authorization code is not bound to the
    key, and the PAR request carries no proof. The server keeps no token after
    sign-in, so the DPoP-bound access token is discarded.
  - A `standard` tenant is unchanged: no PAR, no check of the `iss` response
    parameter, no DPoP, and a client secret.
- **Machine callers under `fapi2`.** Inbound endpoints and service accounts
  accept only access tokens bound to a DPoP key (RFC 9449). A DPoP proof is a
  short signed JWT that the caller makes for each request, so a stolen token is
  useless without the caller's private key. Mutual TLS is not offered, because
  the TLS proxy ends the client's TLS connection. The same rule holds on every
  route that takes a bearer token, whichever is built first, because it sits
  in the token check itself.
  - The token arrives as `Authorization: DPoP <token>`, with one `DPoP` header
    that holds the proof. The same token under `Authorization: Bearer` is
    refused, and so is a plain bearer token.
  - The proof's `typ` is `dpop+jwt` and its `alg` is `ES256` or `PS256`. Its
    `jwk` header holds the public key, and a `jwk` with a private key is
    refused. A `PS256` key must be an RSA key of at least 2048 bits, the floor
    of FAPI 2.0. The signature is checked with that key.
  - The proof's `htm` is the request's method. Its `htu` is the request's scheme,
    host and path, without the query and fragment, after the forwarded headers
    rules of
    [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md). So a
    proof made for the address the caller used passes behind a listed proxy,
    and one made for the proxy's internal address does not.
  - The proof's `ath` is the hash of the access token, so a proof made for one
    token never works with another.
  - The token's `cnf.jkt` must equal the thumbprint of the proof's key.
  - The proof's `iat` must be within 60 seconds of the server's clock, on
    either side, and its `jti` must not be blank.
  - The server keeps no store of seen `jti` values and asks for no `nonce`. A
    store on one replica would not stop a replay on another. So the same proof
    can be replayed for up to 60 seconds, but only to the same method and
    address and with the same token.
  - Any failure is a `401` with `WWW-Authenticate: DPoP algs="ES256 PS256"`,
    which never says which check failed. A tenant under `standard` answers
    `WWW-Authenticate: Bearer`, as before.
- **The client key.** A tenant under `fapi2` has a private key for
  `private_key_jwt`, an EC P-256 key as a PEM (PKCS#8). Under `configuration` it comes from the environment,
  such as `Tenants__default__Oidc__ClientKey`, as a PEM, with its key id in
  `Oidc:ClientKeyId`. Under `directory` it is stored encrypted in the tenant
  directory, with the field name `oidc_client_key` and its key id, see
  [platform database](storage.md#platform-database). A tenant can hold two keys
  under `directory`, the current one and the previous one, and one under
  `configuration`. A tenant under `fapi2` without a key, or with a key without a
  key id, stops the server's start.
- **Rotation.** The tenant can hold a second key while the identity provider changes over. The server
  signs with the newest key after its next refresh. `tenant oidc set --client-key` and `--retire-client-key`
  change the keys, see [tenant setting commands](tenant-setting-commands.md#sign-in-settings). For a tenant
  that signs in through its own realm, the command registers each key in the realm's clients and removes a
  retired one, so the operator only adds, waits for the refresh and retires. A tenant that still signs in
  through an outside identity provider, such as a restored one, has the operator register the public keys there.
- **Verification** _(planned for M32)_. The OpenID Foundation's conformance suite runs by hand
  against the development realm under `fapi2`. Its result is recorded in
  [ASVS](asvs.md). Axis seeks no certification.
- **Keycloak.** The development realm has a second client, `axis-fapi`, so
  tests cover both profiles. Only an integration test host uses it. It has no
  secret and authenticates with `private_key_jwt`, and it requires PAR and
  PKCE `S256`. A client policy applies to a client that has a `fapi2` role, and
  only `axis-fapi` has one, so it never touches the other clients. Its profile `axis-fapi2`
  has three executors: the PKCE enforcer, the secure client authenticator that
  allows only `client-jwt`, and the signature algorithm check for signed JWT
  client assertions. The client's JWKS holds the public keys of two EC P-256
  keys, `key-1` and `key-2`. It lists each key under that name and under its key id from
  `tenant oidc set --client-key`, the JWK thumbprint, so Keycloak accepts both. Their private keys are committed as
  `samples/keycloak/axis-fapi-key-1.pem` and `axis-fapi-key-2.pem`, for
  development and tests only. They are registered on this client of the
  development realm and nowhere else.

## Authorization

The server sets an authorization fallback policy, so a route that is not on the
open list needs a session or a valid caller token. A request with neither is
refused before the route runs, so it never opens a tenant database. The policy
only requires an authenticated user, because the request's scheme already picks
the session, the service account or the endpoint caller. It follows
[D111](../decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md).

- **The open list.** Seven routes need no session: `/health/live`,
  `/health/ready`, `GET /api/site`, `GET /api/texts/{locale}`,
  `GET /api/auth/sign-in`, `POST /api/auth/sign-out` and the SPA's pages. The
  static files stay public. The OIDC handler answers the sign-in callback inside
  authentication, so it is outside the routes.
- **The answers.** A session route answers `401` with the title "Sign in to read
  or write this data.". A service account route with a token keeps its bearer
  answer. An inbound endpoint route keeps its `WWW-Authenticate: Bearer`
  challenge and its title. The answer is problem details, never an empty body.
  An anonymous request for a file path that matches no route and no file answers
  `401`, not `404`.
- **The test.** `AuthorizationRouteTests` calls every route with no session and
  no token and expects a `401` or `403` problem off the open list, and a bearer
  challenge on each inbound endpoint route. It also checks that the routes marked
  open are exactly that list, so a route added later is checked automatically.

The `role` and `policy` resources and their compile checks are built. For
the record API and the data source endpoint, so are the session `401`, the
matching of roles, grants by action and the `403` without a grant. The record
API also enforces record filters and field lists: reads, writes and their
failures as described below. The data source endpoint adds the root entity's
read filters to its SQL and checks every projected field. A process start
checks the session, the `start` grant and read access to the subject record.
The task API keeps the assignee check and leaves out the fields the user may
not read. Navigation and page metadata follow the user's grants, and page
metadata leaves out the fields the user may not read. Everything in this
section is built. It follows
[D24](../decisions/d24-roles-and-policies.md) and
[D12](../decisions/d12-authorization-is-server-side-and-default-deny.md).
The `role` and `policy` resource files are described in
[configuration](roles-and-policies.md).

- **Roles.** A user's roles are the roles claim values, the roles that the application's provider mapping gave, kept in the
  [session](#sign-in-and-the-session), and the roles that an administrator gave, which the session reads at every request, see
  [user administration](user-administration.md#roles). A value matches a declared `role` of the application by its exact name. A value that names no declared role is
  ignored. A user matches a policy when they hold any of its roles.
- **Grants.** A policy holds grants. Each grant gives actions on one target:
  `read`, `create`, `update`, `delete`, `export` and `import` on an entity,
  `read` and `export` on a data source, or `start`, `inspect`, `cancel`, `retry`, `resolve` and `reassign` on a process. `inspect` lists a process's
  instances, see [process inspection](process-inspection.md#process-inspection).
  `cancel` stops a running or waiting instance, see
  [Cancel](process-inspection.md#cancel). `retry` runs a failed instance
  again, see [Retry](process-inspection.md#retry). `resolve`
  settles an unknown connector call, see
  [Resolve](process-inspection.md#resolve). Each is checked on its own. An entity grant may also have a record
  `filter` and a `fields` list. A user's grants are the grants of every
  policy they match.
- **Default-deny.**
  - A request without a session is a `401`.
  - A signed-in user with no grant for the action on the resource gets a
    `403`.
  - A record outside every read filter does not exist for the user, so it is
    a `404`, as for an unknown record.
  - A policy that cannot be evaluated denies.
- **Reads.** A record is readable when it matches the filter of at least one
  of the user's `read` grants on its entity. A grant without a `filter`
  matches every record. Record lists and data sources add the read filters
  to their SQL. A single record is checked in SQL too, with one statement
  per filtered grant, so reads and writes give the same answer and a filter
  that fails denies through its own grant only. The user id is a parameter,
  never part of the SQL text. A read filter that reads a pending computed
  field fails closed with a `409`, in the record API, the process endpoints and
  the data sources.
- **Writes.**
  - A create needs the new record to match the filter of a `create` grant.
  - An update needs the stored record to be readable, otherwise `404`. It
    then needs the stored record to match an `update` filter, otherwise
    `403`. The new record must match an `update` filter too, otherwise
    `403`.
  - A delete needs the stored record to be readable, otherwise `404`, and to
    match a `delete` filter, otherwise `403`.
  - The new record is the record as it will be stored: the stored values
    with the body on top, after the creator, sequence and computed fields
    are set. So a user cannot move a record out of their reach.
- **Fields on reads.** The user may read the union of the `fields` of their
  `read` grants on the entity. A `read` grant without `fields` covers every
  field. The readable fields belong to the entity, not to the record, so
  they never depend on which grant matched a record. Other fields are left
  out of records, data source rows, history details, task forms and page
  metadata. A record's `id` and `version` are always shown.
- **Fields on writes.** A create may write the union of the `fields` of the
  user's `create` grants on the entity, and an update the union of their
  `update` grants. Writing any other field is a `403` keyed at the field's
  path, such as `/values/decisionComments`.
- **Child entities.** Grants name only entities that are not child
  entities. Child rows are read and written through their owner's grants. An
  owner's `fields` entry for a `child-collection` field covers its rows as a
  whole. A grant on a child entity is a compile error.
- **Labels and lookups.** The label of a referenced record shows when the
  user may read the target entity's display field. The target's record
  filters do not hide it. Lookups list target records through the record
  API, so through the user's read filters. An import's lookup of a display
  value in a reference cell follows the label rule, not the lookup rule, see
  [import](import-export.md#import).
- **Data sources.** A data source needs a `read` grant on it and a `read`
  grant on its root entity. The root entity's read filters are added to the
  data source's own filter in SQL. A data source that projects or aggregates
  a field the user may not read is a `403` as a whole. For a path through
  `reference` fields, every field along the path must be readable. See
  [data sources](data-sources.md#authorization).
- **Processes and tasks.** A process start needs a `start` grant on the
  process and read access to the subject record. An operator cancel needs
  the `cancel` grant and the same read access, and so does an operator retry
  with the `retry` grant, and so does an operator resolve with the
  `resolve` grant. The task assignee check
  stays, and it is enough to list, read and complete a task. A role assignee
  counts only when the active release declares the role. The task form
  leaves out fields the user may not read, and the subject label uses the
  display field of the task's release. See
  [processes](task-api.md). Listing the instances of a
  process needs an `inspect` grant on the process, and an instance is listed
  only when its subject record is readable. The instance detail needs the
  same. It also shows a call's input only when the user may read every field
  its mapping reads or writes. A `sensitive` field is never shown. See
  [process inspection](process-inspection.md#process-inspection).
- **System work.** The worker, seed data and `updateRecord` steps run as
  `system`. Policies do not check them.
- **Failures.** A filter that fails at run time adds no records through its
  grant. The server logs the policy name and the grant's index, never a
  record value. A single record is checked one grant at a time, so another
  grant can still match it. In a list, PostgreSQL cannot fail one branch of
  an `or`, so the whole query fails with a `400` and returns no rows.
- **Module.** `Axis.Policy` holds the evaluation of the compiled policies of
  each release. Every endpoint and engine command calls it with a resource
  and an action. It answers allow or deny, the read filters to add, and the
  readable and writable field sets. Today it answers allow or deny for an
  entity, a data source or a process start, and the record filters and the
  field set of an entity and an action. The record API, the data source
  endpoint and the process start endpoint call it, and apply the record
  filters it answers. The record command in `Axis.Processes` applies the
  record filters and field sets for record API writes. The record API, the
  data source endpoint and the task API apply the field sets. The site endpoints call it to leave unreadable
  pages out of navigation, to answer `403` for them, and to list a widget's
  readable fields, allowed actions and writable fields in page metadata.
- **Only the server decides.** Page metadata and navigation follow the
  policies, but they only shape the UI. The server checks every request.

### Combining grants

The grants of all the user's policies combine with OR. A record is readable
when any `read` grant's filter is `true` for it. A filter that is `false`,
`null` or fails adds no records through its grant. So a mistake in one
policy can never widen access.

Take the purchase request policies in
[configuration](roles-and-policies.md), and a user
who holds both `employee` and `finance-reviewer`. The user's read filters on
`PurchaseRequest` are `requester == currentUser()` and
`status in ('submitted', 'approved', 'returned', 'rejected')`:

| Request                          | `requester == currentUser()` | `status in ('submitted', 'approved', 'returned', 'rejected')` | Result   |
| -------------------------------- | ---------------------------- | ------------------------------------------------------------- | -------- |
| Their own draft                  | `true`                       | `false`                                                       | Readable |
| Someone else's submitted request | `false`                      | `true`                                                        | Readable |
| Someone else's draft             | `false`                      | `false`                                                       | `404`    |

The `false` filters add nothing. Someone else's draft matches neither
filter, so it does not exist for them.

Equality is [null-safe](expressions.md#null-semantics), so a
department manager's filter `department.manager == currentUser()` is
`false`, not `null`, for a request whose department has no manager. That
grant adds nothing for the request.

A `null` filter adds nothing either. Suppose a policy granted `read` with the
filter `submittedAt >= dateTime('2026-01-01T00:00:00Z')`. A draft has no
`submittedAt`, so the filter is `null` for it, and that grant gives no access
to the draft. Only another grant can make it readable.
