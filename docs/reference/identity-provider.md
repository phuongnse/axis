# Axis's identity provider

Axis ships an identity provider of its own, so people sign in on one standard Axis page, whatever identity provider
their organization uses. [D99](../decisions/d99-axis-ships-its-own-identity-provider.md) agrees it, and
[D100](../decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) lets each application declare how
its people sign in and each site pick from that. M17 builds it, and each section says whether it is built. The
`identityProvider` resource, a site's `signIn` and their compile checks are built, and so is the tenant's realm that
`tenant create` makes. Activation checks the bindings of `oidc` providers and refuses providers that would split a person. Sign-in does
not read them yet, activation updates the realm, and a new realm has no accounts, no redirect addresses and no role mappings, so until they do, a tenant signs in through an identity provider that runs outside
Axis, which `tenant oidc set` points its `Oidc` block at, as [security](security.md#sign-in-and-the-session) describes. Accounts, roles given by an administrator and the
users page are in [user administration](user-administration.md).

## What it is

_(planned for M17)_

- Axis's identity provider is Keycloak with the Axis theme. Every installation runs it beside the server and the
  worker.
- The server's sign-in does not change: it stays the confidential OIDC client of
  [D23](../decisions/d23-sign-in-through-oidc-and-the-bff-session.md), and the session stays on the server. Each
  tenant's `Oidc` block points at that tenant's realm in Axis's identity provider, and nowhere else.
- The realm brokers to the identity providers that the tenant's applications declare. A person of an organization that
  uses Microsoft Entra ID clicks "Continue with Microsoft" on the Axis page, signs in there, and comes back through
  Axis's identity provider to Axis.

## Realms

- Each tenant has one realm of its own, named after the tenant id, with a client for its platform sign-in. Tenants
  share nothing in it, as they share no database. `tenant create` makes it, see
  [keeping the realm in step](#keeping-the-realm-in-step).
- The realm's sessions last at most 8 hours, the lifetime of Axis's session, see
  [session lifetimes](security.md#session-lifetimes-and-re-authentication).
- The realm holds the account rules of [accounts](user-administration.md#accounts): an authenticator code at every
  sign-in, passwords of 12 to 128 characters that are not on the breached-password list, a wait after 5 failed
  sign-ins, a password reset link of 30 minutes and no self-registration.
- The realm's login theme is the Axis theme `axis`, see [theme](#theme).
- _(planned for M17)_ The realm also has its people, its brokered identity providers and a client for each site.
- _(planned for M17)_ The realm's display name is the organization's name, which every sign-in page shows under its
  title. Until `tenant create` takes that name, the display name is the tenant id.
- A preview tenant has no realm. It signs people in through its main tenant's realm, see
  [preview tenants](#preview-tenants).

## Identity provider resource

An `identityProvider` resource declares one way for the application's people to sign in. It is part of the release,
so Studio edits it, with the identity provider editor of [Studio](studio.md), and test scenarios check it.

```json
{
  "id": "5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e51",
  "kind": "identityProvider",
  "name": "company",
  "formatVersion": 1,
  "type": "oidc",
  "label": { "textKey": "signIn.company" },
  "issuer": { "binding": "CompanyIssuer" },
  "clientId": { "binding": "CompanyClientId" },
  "clientSecret": { "binding": "CompanyClientSecret" },
  "claims": { "displayName": "name", "email": "email", "locale": "locale" },
  "roles": {
    "claim": "groups",
    "values": {
      "purchasing-managers": "manager",
      "finance": "finance-reviewer"
    }
  }
}
```

- **`type`:**
  - `accounts`: accounts that the realm holds, see [accounts](user-administration.md#accounts). It has no `label`,
    bindings or `claims`, because the account form is its sign-in. An application has at most one.
  - `oidc`: a provider that the realm brokers to over OpenID Connect. `issuer` is a `url` binding, the address its
    discovery document is found at. `clientId` is a `secret` binding. The client credential is either `clientSecret`
    or `clientKey`, a `secret` binding with a private key for `private_key_jwt`, as many national providers require.
  - `saml`: a provider that the realm brokers to over SAML 2.0. `metadata` is a `url` binding, the address of the
    provider's metadata. The realm signs its requests with a key of its own and publishes its own metadata for the
    organization to register.
- **`label`:** the text of the "Continue with" button, as a text key of the application.
- **`claims`:** the claims that give the display name, the email and the locale, by default `name`, `email` and
  `locale`. The realm links a person to the provider by the provider's own subject, and the user id that Axis keeps is
  always the realm's `sub`, see [users](#users).
- **`roles`:** how the provider's claims give the application's roles. `claim` names the claim, by default `roles`.
  `values` maps a claim value to a role. Without `values`, a claim value gives the role of the same name, as the roles
  claim does today, see [claim values](roles-and-policies.md). A value that gives no declared role is ignored.
- **`acr`:** optional, the assurance levels the provider must sign the person in at. The sign-in request asks for
  them, and the server refuses an ID token whose `acr` is not one of them.
- **Checks:** these are compile errors, see [diagnostic codes](diagnostic-codes.md).
  - An unknown `type` is `AXC0284`. A missing binding is `AXC0286`: an `oidc` provider needs `issuer`, `clientId` and
    `clientSecret` or `clientKey`, and a `saml` provider needs `metadata`.
  - A binding of the wrong kind is `AXC0285`. No resource declares the kind of a binding, so the compiler compares
    uses: a provider's binding name that the application also uses as another kind, ignoring letter case, fails. The
    other uses are a connector's `baseUrl` (`url`) and `auth` (`secret`), the manifest's `fileStorage` (`storage`) and
    the bindings of other providers. A clash with a package operation's binding shows up at activation.
  - A role in `values` that the application does not declare is `AXC0287`, and a second `accounts` provider is
    `AXC0288`.
  - A `label` is a text key like any other label, so a key that no locale has is `AXC0028`.
  - The schema refuses, as `AXC0004`, what a type does not take: a `label`, bindings or `claims` on `accounts`, a
    `metadata` on `oidc`, an `issuer` or client binding on `saml`, and both `clientSecret` and `clientKey`. It also
    requires a `label` for `oidc` and `saml`.
  - A module holds no identity provider, because it holds no bindings, so a module file of this kind is `AXC0202`.
  - A site's `signIn` entry that names no provider is `AXC0289`, see
    [sites and sign-in](#sites-and-sign-in).
- **Read only by activation:** activation checks the bindings of `oidc` providers against the tenant's, and refuses
  providers that would split a person into two users, see [users](#users). It plans the realm it wants.
  Activation updates the realm, through `Axis.Admin` and the server's startup activation, see
  [keeping the realm in step](#keeping-the-realm-in-step). Sign-in does not read the resource yet. Studio's blank application creates an `accounts` provider, and New resource creates
  more from an `accounts` starter. Studio edits the kind with the identity provider editor, which does not show a
  binding's value, and a site's `signIn` with a picker of the application's providers.

## Sites and sign-in

- **`signIn`:** a site lists the providers it allows, by name, as `"signIn": ["company"]`. A site with no `signIn`
  allows its application's providers, all of them. Built, with the compile checks below. Sign-in does not read it yet.
  - Names are compared ignoring letter case. The list has at least one entry and no repeated entry, or it is
    `AXC0004`. To allow every provider, leave `signIn` out.
  - An entry that names no provider of the application is `AXC0289`, at `/signIn/{i}` of the site file.
  - An application with sites and no provider is `AXC0290`, at the root of each site file. The samples and the
    application that Studio creates each declare an `accounts` provider for this.
  - A name that matches a provider file that failed the schema is not reported again, and such a file also prevents
    `AXC0290`.
  - The compiled site carries the providers it allows.
- _(planned for M17)_ **A client for each site:** activation gives each site a client of the tenant's realm. A sign-in starts for the site
  that `returnUrl` opens, through that site's client, and the realm's page shows only that site's providers. With one
  provider and no accounts, the server sends the person straight to it. The page's filter is built, see the theme below.
  Starting a sign-in through the site's client is not.
- _(planned for M17)_ **The callback checks again:** the realm tells the server which provider signed the person in. The server refuses a
  callback whose provider is not in the site's `signIn`, or whose `acr` the provider does not accept, as any refused
  callback, see [security](security.md#sign-in-and-the-session). The realm's page is not the control.
- _(planned for M17)_ **Sign-in once:** a person signed in through a provider at the realm opens another site that allows it without
  being asked again. A site that does not allow it asks the person to sign in with one it does.
- **A host of its own:** `tenant host add <id> <host> --site <path>` gives a tenant host to one site, see
  [hosts](tenant-setting-commands.md#hosts). Both the command and the serving are built. Only the `directory` tenant
  source has site hosts, so hosts from the `configuration` source serve the whole tenant.
  - That host serves only the site: the SPA under the site's path, the web root's static files, sign-in
    (`/api/auth/*`), `GET /api/me`, the SPA shell's `/api/site`, `/api/texts/{locale}` and `/api/about`,
    `/api/sites/{path}` and below for that site, and `/api/apps/{app}` for the site's application. Everything else is
    a `404` problem there, such as `/api/sites`, other sites and applications, Studio, the users page, the account
    page and `/api/me/sessions`. The refusal comes before authentication, so it renews no session.
  - The site keeps its path on its own host, so it opens at `shop.example.test/shop`. `/` on that host redirects to
    `/{path}`. Serving the site at `/` comes later and needs a change in the SPA.
  - The tenant's other hosts redirect the site's path and anything below it with `302` to the same path and query on
    the site's host, so the site has one origin. The site's API under `/api` still answers on the other hosts.
  - A site for people outside the organization, such as citizens or customers, should get a host of its own. Sites
    that share a host share an origin, so a script that runs in one could act in the other for a person signed in to
    both.

## Platform sign-in

`tenant sign-in set` stores the platform sign-in in the tenant directory, and `tenant create` and `tenant restore`
set accounts on. Both are built. The server's callback for the platform pages reads the stored platform sign-in, see
below. The realm sync does not add the platform's providers to the realm yet, so no real person can sign in through one
until it does _(planned for M17)_.

- The home page, Studio, the operator console, the account page and the users page belong to no site. The tenant's
  platform sign-in says which providers they accept.
- `tenant sign-in set <id>` sets it, with `--accounts true|false` and one `--provider <name>` for each brokered
  provider, each given its type and bindings as an `identityProvider` resource names them, see
  [sign-in settings](tenant-setting-commands.md#sign-in-settings).
- The author, publisher, operator and administrator roles are roles claim values of the platform sign-in, mapped as
  `roles` maps them, or roles that the users page gives.
- The sign-in callback of a platform page checks the platform sign-in. It refuses a provider that the platform sign-in
  does not accept, and an account sign-in when accounts are off. The refused callback writes `session.signInRefused`
  with the reason `providerNotAccepted`, see [sign-in and the session](security.md#sign-in-and-the-session). A provider
  matches the `identity_provider` claim of the ID token by its alias, `oidc-` and the lower-case issuer binding. A
  `saml` provider has no alias yet, so its sign-ins are refused. A token with no `identity_provider` claim is an
  account sign-in.
- For an accepted provider, the callback reads the values of the ID token claim `axis_roles_<roles claim>`, the user
  attribute that the realm sync writes for each roles claim. It maps each value to author, publisher, operator or
  administrator, and gives the role names that `Studio:AuthorRole`, `Studio:PublisherRole`, `Console:OperatorRole` and
  `Users:AdministratorRole` set. The session keeps the roles that the tenant's roles claim gives, which include the
  roles of the users page, and the mapped roles are added. The claims come from the `axis` client's token, which the
  realm sync sets up.
- A tenant with accounts off and no provider stored has no platform sign-in. It accepts every sign-in and takes roles as
  the tenant's roles claim gives them. So does a tenant of the `configuration` source. A preview tenant uses its main
  tenant's platform sign-in.
- A change made with `tenant sign-in set` reaches the server at the next refresh of the tenant directory, like any other
  sign-in setting.
- `tenant create` sets accounts on, so a new tenant can sign in its first administrator before any application is
  published, see [the first administrator](user-administration.md#the-first-administrator).
- On the tenant's host the home page's sign-in also offers the providers of the sites that share the host. After
  sign-in the home page lists only the sites that allow the provider the person used.

## Users

_(planned for M17)_

- **The user id is the realm's `sub`.** The issuer is always the tenant's realm, so a user is one realm user.
- **One provider per binding:** the realm holds one brokered provider for each set of bindings. Two applications
  whose providers name the same bindings use the same provider in the realm, and the platform sign-in shares it too,
  so a person is one user across the tenant. Two providers that name the same issuer or metadata binding with other
  client bindings fail activation, because the same person would become two users. Two with the same bindings and
  other `claims` fail too. Both are `AXC0291`, reported before provisioning at `/issuer/binding` or `/claims` of
  the provider that is activated, and built for `oidc` providers. Activation compares binding names ignoring letter
  case, reads the providers of the tenant's other active releases, and gives each provider the alias `oidc-` and its
  lower-case issuer binding.
- **No automatic linking:** a person who signs in through two providers is two users, even when both give the same
  email. The realm never links them by email, because another provider's email is not proof of who signed in.
  Built by the realm sync: every `oidc-` provider uses the realm's own first-login flow `axis first login`, whose only
  step creates a new user, so the realm never shows an "account already exists, link it?" page. A brokered user's
  username is the provider's alias, a dot and the person's subject, such as `oidc-companyissuer.<sub>`, so two providers
  never clash on a username. The realm allows two users to share an email. Linking that the person proves on both sides
  comes later.
- **Roles:** at sign-in the session gets, for each application, the roles that the provider's `roles` mapping gives
  and the roles the [users page](user-administration.md#roles) gives. Platform roles come the same way from the
  platform sign-in.

## Keeping the realm in step

_(planned for M17)_

- **Created by `Axis.Admin`:** `tenant create` creates the tenant's realm, its platform client `axis`, and two clients
  that Axis's hosts use to change the realm: `axis-activation`, with rights over the realm's providers and clients, for
  activation, and `axis-users`, with rights over its users only, for the users page. It stores their secrets and the
  platform client's secret encrypted in the tenant directory and points the tenant's `Oidc` block at the realm. A
  second run updates the realm, brings it up to the account rules and sets its login theme to `axis` again. It also creates the first-login flow
  `axis first login` for brokered users and allows two users to share an email, which turns sign-in by email off: an
  account's username is its email, see [accounts](user-administration.md#accounts). Only `Axis.Admin` holds the identity provider's admin rights over every realm, from the
  `IdentityProvider` settings of its own, see [administration](administration.md#settings).
- **The breached-password list:** the realm's password policy names the file `axis-breached-passwords.txt`, which every
  identity provider that `tenant create` targets must hold in its `data/password-blacklists` folder. The repository
  keeps it in `deploy/keycloak/password-blacklists`, see `deploy/keycloak/README.md`. Without the file, `tenant create`
  fails at its realm step, also for a realm that exists.
- _(planned for M17)_ **Kept in step later:** `tenant host add`, `remove` and `move` do not change the realm yet, so the
  platform client has no redirect addresses until a later part of M17 adds them.
- **Realm sync:** the realm sync is built. It signs in to the realm as `axis-activation` and makes the realm's
  `site-<path>` clients and `oidc-` providers match a desired state. Each site client puts the alias of the provider that
  signed the person in into its ID token as the `identity_provider` claim, and each provider gets claim mappers for the
  display name, the email, the locale and each roles claim, and a `username` mapper that names a new user
  `<provider alias>.<subject>`. Each provider names the realm's first-login flow. It touches no other client or
  provider, so `axis`, `axis-activation`, `axis-users` and anything an operator added stay. A site client that the sync
  creates or updates copies the client authentication and the PAR, PKCE, DPoP and key settings of `axis`, so activation
  never undoes what `tenant oidc set` wrote. Each site client also carries two attributes that every sync rewrites:
  `axis.providers`, the aliases of the providers the site allows separated by spaces, and `axis.accounts`, `true` or
  `false`. The sync refuses an alias that is empty or contains a space. It also copies the redirect addresses, web origins and post-logout redirect
  addresses of `axis`. When the tenant has a platform client secret, the site client's secret is base64url of
  HMAC-SHA256(platform secret, client id), so the server can compute it. Without a platform secret, Keycloak makes the
  secret as before. Without an `axis` client it is a plain client with a secret. Both tenant catalogs carry
  the realm's name, the identity provider's address, the activation secret, the platform client's secret and the secret of
  the `axis-users` client, see
  [tenant realm](host-settings.md#tenant-realm). The server's startup activation calls it, after it activates each
  application folder, for a tenant that has realm details. So does `Axis.Admin` activation, as described below. When the
  identity provider answers 409 Conflict, as when another host syncs the same realm at the same moment, the sync reads
  the realm again and repeats, up to three attempts, and then fails as before.
- **Brought in step by activation:** activating a release adds, changes and removes the realm's brokered providers,
  their claim mappers and the site clients to match the active releases of the tenant and its platform sign-in. A
  provider that nothing names any more is removed. Its users stay, but cannot sign in through it.
  Built for `Axis.Admin`: `activate`, and the publisher's `publish`, `publish` with target `preview` and `rollback`
  update the realm inside their `activate` step, after the activation has committed. A tenant without an activation
  secret, such as a preview tenant or one that signs in through an outside identity provider, skips the update. A
  failure of the identity provider fails the step with a line that names the identity provider step and its status
  code, and the new release stays active, see [activate](administration.md#activate). A `validate` and a `test`
  never update the realm. The server's startup activation updates it too, see above.
- **Brought in step by restore:** `tenant restore` of a tenant that gets a new realm creates the realm as `tenant create`
  does and brings it in step with the active releases of the restored database. The archive holds no secret value, so a
  provider whose bindings have no value yet is left out with a warning. The next activation adds it once the operator has
  set the bindings, see [tenant restore](administration.md#tenant-restore).
- **Bindings:** a missing binding, or a binding of the wrong kind, fails activation, as for a connector (`AXC0100`).
  Built for `oidc` providers: `issuer` is a `url` binding, and `clientId`, `clientSecret` and `clientKey` are `secret`
  bindings. On a preview tenant activation leaves the provider out with a warning, see
  [preview tenants](#preview-tenants).
- **Profile:** built for `tenant oidc set`, which takes `--profile`, `--client-key` and `--retire-client-key`. When the
  tenant has realm details, the command updates the realm's `axis` client and every `site-` client before it saves the
  change, see [sign-in settings](tenant-setting-commands.md#sign-in-settings). Each client lists every key the tenant
  will hold, under its [RFC 7638](https://www.rfc-editor.org/rfc/rfc7638) key id. Under `fapi2` the clients use
  `client-jwt` with ES256, require PAR, PKCE `S256` and DPoP-bound tokens, see
  [FAPI 2.0 profile](security.md#fapi-20-profile). Under `standard` they use `client-secret` and require neither PAR nor
  DPoP. A failure of the identity provider fails the command and changes nothing in the tenant directory. A tenant that
  signs in through an outside identity provider is unchanged.

## Preview tenants

_(planned for M17)_

- A preview tenant signs people in through its main tenant's realm and platform sign-in. Its bindings are empty by
  default, so its sites' brokered providers are left out and an author tries the application through the platform
  sign-in.
- On a preview tenant each site accepts the platform sign-in, and a person holds the application roles the main
  tenant's users page gives them.

## Theme

_(planned for M17)_

- Every page of Axis's identity provider has one frame: the Axis mark above one card in the middle of the page on
  `canvas`, and below it the language links and "© <year> Axis". It follows light and dark mode and the Axis tokens and
  fonts, as the SPA does. The mark is 32 px with the word "Axis" beside it. The card is at most 400 px wide, on
  `surface` with a `borderSubtle` border, `radius-base` corners and `spacing-xl` padding. Its title, such as "Sign
  in", has the organization's name under it in `textMuted`.
- The sign-in page lists the site's brokered providers first, then "or" and the account form when the site allows
  accounts: email or username, password with "Forgot password?", "Keep me signed in" and a full-width Sign in button.
  The brokered providers' "Continue with" buttons are secondary buttons. A field's label follows the form widget's
  [field look](form-widget.md), and "Forgot password?" sits on the right of the password's label.
  "Keep me signed in" keeps the realm's session when the browser closes, never past the 8-hour lifetime of
  [sessions](security.md#session-lifetimes-and-re-authentication).
- A wrong password keeps the username, marks the password and reads "Invalid email, username or password." above the
  form, never saying which one was wrong.
- A site that allows one brokered provider and no accounts shows its button alone, under one line that names it.
- Two-step verification asks for the 6-digit code in six boxes, one digit each, with "Try another way" and "Back to sign in".
- The theme is built from the SPA's own React components and tokens into a Keycloak theme, so the pages look like the
  rest of Axis. Its texts are message bundles in every locale the platform offers. The "Continue with" labels come
  from the applications' texts.
- _(built)_ The sign-in page is drawn by shared React components in `web/src/identitySignIn/` from a sign-in context:
  title, providers, accounts, site, username, error, login address, remember-me, forgot-password address, locales and
  texts. When the sign-in goes through a site client, which carries `axis.accounts`, `site` holds that site's
  providers and accounts flag. The page then lists only those providers, and shows the account form only when the realm
  and the site both allow accounts. A client without `axis.accounts`, such as `axis`, has no `site`, so the page shows
  every provider and the form as before. The account form is a real `<form method="post">` to the login address. The components draw the frame (the 32 px mark
  with the word "Axis", the 400 px card and the organization's name under the title), the "Continue with" secondary
  buttons, the form widget's field labels and "Forgot password?" on the password's row. A shared two-step page with six
  code boxes exists too, but only the preview draws it. The design system's
  `identitySignIn` preview feeds the components fixtures, see
  [shell design system previews](shell.md#design-system-previews). The Keycloak theme `axis` feeds them a real
  context: its `login.ftl` writes Keycloak's sign-in data as JSON and a React bundle draws the page. `scripts/build.sh`
  builds the theme from `web/keycloak-theme/` into `artifacts/keycloak-theme/axis/login/` and checks its files and
  fonts. Keycloak's other pages, two-step verification included, still come from its `keycloak` parent theme. The development realm `axis` uses the theme as
  its login theme: Compose, `scripts/dev.sh`, `scripts/e2e.sh` and the Keycloak of the integration tests load it from
  `artifacts/keycloak-theme`, and each builds it first. The realms of `tenant create` name the theme as their login
  theme. The on-premises Compose example loads it from `AXIS_IDENTITY_THEME_DIR`, see
  [on premises](installation.md#on-premises). The Helm chart loads it from the volume `identity.theme.volume`, see
  [in the cloud](installation.md#in-the-cloud).

## Installation

- _(built)_ The on-premises Compose example runs Axis's identity provider with its own database on the PostgreSQL
  server, behind the same TLS proxy as the server, on one host of its own for the installation. It pins the Keycloak
  version of development and the tests, and the operator upgrades it with Axis. See
  [on premises](installation.md#on-premises).
- _(built)_ The Helm chart runs it the same way: one Deployment and a Service, with the theme and the password list
  from volumes that the operator names. See [in the cloud](installation.md#in-the-cloud).
- The development realm `axis` stays for `scripts/dev.sh` and the tests, as the `default` tenant's realm, see
  [security](security.md#development-users). Under `scripts/dev.sh` and in the E2E run, the server's startup activation
  keeps it in step through its `axis-activation` client, so the realm holds a `site-` client for each sample site, see
  [development realm and users](../development.md#development-realm-and-users). Under Docker Compose it does not,
  because the server's container cannot reach Keycloak at `localhost:8080`.
- **Backup and restore:** the identity provider's database holds the accounts and their credentials. The operator
  backs it up with the installation. `tenant backup` does not carry them, so a tenant restored where its realm does
  not exist gets a new realm, and its accounts need new invitations. This is built: `tenant restore` creates the
  realm in its `realm` step, see [tenant restore](administration.md#tenant-restore). A tenant restored where its realm
  exists keeps the realm and its users.
