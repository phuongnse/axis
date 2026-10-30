# Axis's identity provider

Axis ships an identity provider of its own, so people sign in on one standard Axis page, whatever identity provider
their organization uses. [D99](../decisions/d99-axis-ships-its-own-identity-provider.md) agrees it, and
[D100](../decisions/d100-identity-providers-per-application-and-sign-in-per-site.md) lets each application declare how
its people sign in and each site pick from that. M17 builds it, and each section says whether it is built. The
`identityProvider` resource, a site's `signIn` and their compile checks are built, and so is the tenant's realm that
`tenant create` makes. Activation checks the bindings of `oidc` and `saml` providers and refuses providers that would split a person. Sign-in reads the
`roles` mapping of each provider, activation updates the realm, and a new realm has no accounts, no redirect addresses and no role mappings, so until they do, a tenant signs in through an identity provider that runs outside
Axis when its `Oidc` block points there, as [security](security.md#sign-in-and-the-session) describes. No command points the block at another provider. Accounts, roles given by an administrator and the
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
- **`label`:** the text of the "Continue with" button, as a text key of the application. Activation writes it to the
  realm in every platform locale, see [keeping the realm in step](#keeping-the-realm-in-step).
- **`claims`:** the claims that give the display name, the email and the locale, by default `name`, `email` and
  `locale`. The realm links a person to the provider by the provider's own subject, and the user id that Axis keeps is
  always the realm's `sub`, see [users](#users).
- **`roles`:** how the provider's claims give the application's roles. `claim` names the claim, by default `roles`.
  `values` maps a claim value to a role. Without `values`, a claim value gives the role of the same name, as the roles
  claim does, see [claim values](roles-and-policies.md). A value that gives no declared role is ignored.
- **`acr`:** optional, the assurance levels the provider must sign the person in at. A sign-in that starts on a site asks
  for the levels of all the site's providers as `acr_values`. At the callback, the server compares the upstream `acr`,
  which the token carries as `identity_provider_acr`, with the list of the provider that signed the person in, and
  refuses a token whose `acr` is missing or not in it. Account and platform-page sign-ins do not check it.
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
- **Read only by activation:** activation checks the bindings of `oidc` and `saml` providers against the tenant's, and refuses
  providers that would split a person into two users, see [users](#users). It plans the realm it wants.
  Activation updates the realm, through `Axis.Admin` and the server's startup activation, see
  [keeping the realm in step](#keeping-the-realm-in-step). The realm's sign-in page shows each `oidc` and `saml` provider's `label`. A sign-in that starts on a site page reads the providers a site allows, see [sites and sign-in](#sites-and-sign-in). The callback refuses a provider that the site does not allow, and a token whose `acr` the provider's list does not accept. Studio's blank application creates an `accounts` provider, and New resource creates
  more from an `accounts` starter. Studio edits the kind with the identity provider editor, which does not show a
  binding's value, and a site's `signIn` with a picker of the application's providers.

## Sites and sign-in

- **`signIn`:** a site lists the providers it allows, by name, as `"signIn": ["company"]`. A site with no `signIn`
  allows its application's providers, all of them. Built, with the compile checks below. A sign-in that starts on the site reads it, see
  **A client for each site** below.
  - Names are compared ignoring letter case. The list has at least one entry and no repeated entry, or it is
    `AXC0004`. To allow every provider, leave `signIn` out.
  - An entry that names no provider of the application is `AXC0289`, at `/signIn/{i}` of the site file.
  - An application with sites and no provider is `AXC0290`, at the root of each site file. The samples and the
    application that Studio creates each declare an `accounts` provider for this.
  - A name that matches a provider file that failed the schema is not reported again, and such a file also prevents
    `AXC0290`.
  - The compiled site carries the providers it allows.
- **A client for each site:** activation gives each site a client of the tenant's realm. A sign-in starts for the site
  that `returnUrl` opens, through that site's client `site-<path>`, and the realm's page shows only that site's providers.
  The server reads the first segment of `returnUrl` and looks for an active site with that path. With one
  provider and no accounts, the server adds `kc_idp_hint=<alias>`, so the realm sends the person straight to it. The
  server redeems the code as the site client, with the secret that derives from the platform client's secret, and refuses
  an ID token whose `aud`, or `azp` when present, is not the client that started the sign-in. A preview tenant, a tenant
  without realm details and a `fapi2` tenant keep the platform client `axis`, and so does a `returnUrl` that opens no
  site. The page's filter is built, see the theme below.
- **The callback checks again:** for a sign-in that started on a site, the callback rebuilds the site's allowed providers
  from the active release, with the site path saved in the sign-in state. It reads the `identity_provider` claim of the
  ID token, where no claim means the `accounts` provider. It refuses the token as `providerNotAllowed` when the site
  does not allow that provider, or when the site is no longer active, see
  [security](security.md#sign-in-and-the-session). The realm's page is not the control, and a `kc_idp_hint` does not get
  around the check. The check runs after the token checks and before the platform sign-in check.
- **The callback checks the `acr`:** right after the provider check, when the site's application gives the provider that
  signed the person in an `acr` list, the callback reads the token's `identity_provider_acr`. It refuses the token as
  `acrNotAccepted` when the claim is missing or not in the list, see [security](security.md#sign-in-and-the-session).
  A site's providers that share an alias add their lists together, and a provider without a list adds no check. The
  request asks for the levels of all the site's providers as `acr_values`, because the server does not know which
  provider the person picks. The realm does not forward `acr_values` to the upstream provider, so the callback check is
  the control. A realm that was synced before this check has no `acr` mappers until its next activation, and a sign-in
  through a provider with a list is refused until then.
- **Sign-in once:** a person signed in through a provider at the realm opens another site that allows it without
  being asked again. A site that does not allow it asks the person to sign in with one it does.
  - The session row holds the alias of the provider that signed the person in, from the ID token's `identity_provider`
    claim. A sign-in through accounts holds none, and so does a row from before the column, which counts as accounts.
  - `GET /api/sites/{path}` and the texts and pages under it answer `401` for a session whose provider the site does not
    allow. The answer comes after the `401` for no session and before the `404` for an unknown path. The SPA then shows its
    sign-in card. `GET /api/sites`, the list, holds only the sites that allow the session's provider, so the home
    page shows no site the person cannot open. A tenant without site clients is not filtered.
  - A sign-in for a site adds `prompt=login` when the browser holds a session that the site refuses, so the realm asks for
    credentials and does not sign the person in again through the refused provider.
  - A tenant without site clients checks nothing, as its tokens carry no `identity_provider` claim.
  - The `/api/apps/{app}` data endpoints are not checked yet. They belong to an application, which can have several sites
    with different sign-in.
- **A host of its own:** `tenant host add <id> <host> --site <path>` gives a tenant host to one site, see
  [hosts](tenant-setting-commands.md#hosts). Both the command and the serving are built. The `configuration` tenant
  source gives a host to a site with `Tenants:{id}:SiteHosts`, see [host settings](host-settings.md#tenancy-settings).
  - That host serves only the site: the SPA at `/` and at the site's pages, except under the paths the platform keeps
    for itself (`/health`, `/assets`, `/studio`, `/account` and `/users`), the web root's static files, sign-in
    (`/api/auth/*`), `GET /api/me`, the SPA shell's `/api/site`, `/api/texts/{locale}` and `/api/about`,
    `/api/sites/{path}` and below for that site, and `/api/apps/{app}` for the site's application. Everything else is
    a `404` problem there, such as `/api/sites`, other sites and applications, Studio, the users page, the account
    page and `/api/me/sessions`. The refusal comes before authentication, so it renews no session.
  - `GET /api/site` names the host's site in `hostSite`, and `null` on a host that belongs to no site. The SPA shows
    that site at `/` and its pages at `/{page}`, so a page opens at `shop.example.test/orders`, and every link it
    builds has no path prefix. The SPA then has no Studio, account or users routes, so those addresses show the
    not-found page.
  - A session stays on the host that made it. A person signs in separately on the site's host and on the tenant's other
    hosts, see [the session](security.md#sign-in-and-the-session).
  - The tenant's other hosts redirect the site's path and anything below it with `302` to the same path and query on
    the site's host, without the site's path (`/shop/orders` becomes `/orders`), so the site has one origin and the
    old address of a page reaches it. The site's API under `/api` still answers on the other hosts.
  - A site for people outside the organization, such as citizens or customers, should get a host of its own. Sites
    that share a host share an origin, so a script that runs in one could act in the other for a person signed in to
    both.

## Platform sign-in

`tenant sign-in set` stores the platform sign-in in the tenant directory, and `tenant create` and `tenant restore`
set accounts on. Both are built. The server's callback for the platform pages reads the stored platform sign-in, see
below. The realm sync is built: `tenant sign-in set` brings the tenant's realm in step, and every activation keeps the
platform sign-in's providers in it, see [keeping the realm in step](#keeping-the-realm-in-step). The server's other
checks of the stored platform sign-in are _(planned for M17)_.

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
  matches the `identity_provider` claim of the ID token by its alias: `oidc-` and the lower-case issuer binding, or
  `saml-` and the lower-case metadata binding. A token with no `identity_provider` claim is an
  account sign-in.
- On the tenant's host, the callback also accepts a provider, or accounts, that an active site on that host allows. A
  site shares the tenant's host when it has no host of its own. A provider that only a site with its own host allows is
  still refused there. A sign-in accepted only through a site maps no platform roles, so it opens no Studio, operator
  console or users page. It keeps the roles that the tenant's roles claim gives.
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
- On the tenant's host the home page's sign-in also accepts the providers of the sites that share the host. This is
  built. The realm's sign-in page for the `axis` client is not narrowed, as that client has no `axis.accounts`
  attribute, so it still shows every provider of the realm and the callback is the control. After sign-in the home page
  lists only the sites that allow the provider the person used.

## Users

_(planned for M17)_

- **The user id is the realm's `sub`.** The issuer is always the tenant's realm, so a user is one realm user.
- **One provider per binding:** the realm holds one brokered provider for each set of bindings. Two applications
  whose providers name the same bindings use the same provider in the realm, and the platform sign-in shares it too,
  so a person is one user across the tenant. Two providers that name the same issuer or metadata binding with other
  client bindings fail activation, because the same person would become two users. Two with the same bindings and
  other `claims` fail too. Both are `AXC0291`, reported before provisioning at `/issuer/binding` or `/claims` of
  the provider that is activated. The client bindings rule is built for `oidc` providers. The `claims` rule is built for
  `oidc` and `saml` providers, and `saml` providers that name the same metadata binding share one provider. Activation
  compares binding names ignoring letter case, reads the providers of the tenant's other active releases, and gives
  each `oidc` provider the alias `oidc-` and its lower-case issuer binding, and each `saml` provider the alias `saml-`
  and its lower-case metadata binding. The sharing with the platform sign-in is built: its `oidc` and `saml` providers
  join the plan as one more member, so a platform provider that names the same bindings as an application's provider
  uses its `oidc-` or `saml-` provider. Activation also refuses an application provider that clashes with a stored platform provider, and the
  message names the platform sign-in. `tenant sign-in set` refuses a platform provider that clashes with an active
  release's provider, or with another platform provider, before it stores anything, see
  [platform sign-in](tenant-setting-commands.md#platform-sign-in).
- **No automatic linking:** a person who signs in through two providers is two users, even when both give the same
  email. The realm never links them by email, because another provider's email is not proof of who signed in.
  Built by the realm sync: every `oidc-` and `saml-` provider uses the realm's own first-login flow `axis first login`, whose only
  step creates a new user, so the realm never shows an "account already exists, link it?" page. A brokered user's
  username is the provider's alias, a dot and the person's subject, such as `oidc-companyissuer.<sub>`, or for a `saml`
  provider its alias, a dot and the NameID, such as `saml-agencymetadata.<NameID>`, so two providers never clash on a
  username. The realm allows two users to share an email. Linking that the person proves on both sides
  comes later.
- **Roles:** at sign-in the session gets, for each active application, the roles that the provider's `roles` mapping
  gives, kept by application id. This is built. A provider matches when its alias equals the sign-in's `identity_provider`
  claim, and an application with several matching providers gets the union of their roles. A role mapped in one
  application grants nothing in another, and an account sign-in gets no mapped roles. The roles claim of the tenant
  still gives roles to every application. Studio, the console and the users page check the roles claim and platform roles only. The roles the [users page](user-administration.md#roles)
  gives join them. Platform roles come from the platform sign-in as before.

## Keeping the realm in step

_(planned for M17)_

- **Created by `Axis.Admin`:** `tenant create` creates the tenant's realm, its platform client `axis`, and two clients
  that Axis's hosts use to change the realm: `axis-activation`, with rights over the realm's providers and clients, and
  `manage-realm` for the providers' label texts, which Keycloak requires for realm texts and which also covers every
  realm setting, for activation, and `axis-users`, with rights over its users only, for the users page. It stores their secrets and the
  platform client's secret encrypted in the tenant directory and points the tenant's `Oidc` block at the realm. A
  second run updates the realm, brings it up to the account rules and sets its login theme to `axis` again. It turns on language support in the realm, with `en` and `vi` and `en` as the default, so
  `kc_locale` and the theme's language links work. It also creates the first-login flow
  `axis first login` for brokered users. It turns off Keycloak's Verify Profile required action, so no sign-in stops on a
  page that asks for a missing profile field, such as the last name that a `saml` provider may not send. It allows two users to share an email, which turns sign-in by email off: an
  account's username is its email, see [accounts](user-administration.md#accounts). Only `Axis.Admin` holds the identity provider's admin rights over every realm, from the
  `IdentityProvider` settings of its own, see [administration](administration.md#settings).
- **The breached-password list:** the realm's password policy names the file `axis-breached-passwords.txt`, which every
  identity provider that `tenant create` targets must hold in its `data/password-blacklists` folder. The repository
  keeps it in `deploy/keycloak/password-blacklists`, see `deploy/keycloak/README.md`. Without the file, `tenant create`
  fails at its realm step, also for a realm that exists.
- _(planned for M17)_ **Kept in step later:** `tenant host add`, `remove` and `move` do not change the realm yet, so the
  platform client has no redirect addresses until a later part of M17 adds them.
- **Realm sync:** the realm sync is built. It signs in to the realm as `axis-activation` and makes the realm's
  `site-<path>` clients and `oidc-` and `saml-` providers match a desired state. Each site client puts the alias of the provider that
  signed the person in into its ID token as the `identity_provider` claim, and each provider gets claim mappers for the
  display name, the email, the locale and each roles claim, and a `username` mapper that names a new user
  `<provider alias>.<subject>`. Each provider also gets an `acr` mapper (`oidc-user-session-note-idp-mapper`) that copies the
  upstream `acr` claim into the person's realm session, and each site client an `identity provider acr` mapper that puts
  that session value into its ID token as the `identity_provider_acr` claim. Each site client also gets one mapper for each roles claim of
  the providers its site allows, named `axis_roles_<claim>`, which puts that user attribute into the ID token. The sync removes such a
  mapper that the site no longer needs. Each provider names the realm's first-login flow. It touches no other client or
  provider, so `axis`, `axis-activation`, `axis-users` and anything an operator added stay. A `saml-` provider is
  imported from its metadata: the sync reads the metadata from the `metadata` binding's address before it changes the
  realm, and uploads it to Keycloak's import-config endpoint, so an unreachable address changes nothing. The provider
  asks for a persistent NameID, takes the person's subject from it, and signs its requests with the realm's own key
  (RSA-SHA256). It gets mappers for the display name, the email, the locale and each roles claim, which read SAML
  attributes of those names, and a `username` mapper that names a new user `<provider alias>.<NameID>`. It gets no `acr`
  mapper, so a site sign-in through a `saml` provider whose `acr` list is not empty is refused. The realm publishes
  the provider's own metadata at `/realms/<realm>/broker/<alias>/endpoint/descriptor`, and the organization
  registers that address with its provider. A site client that the sync
  creates or updates copies the client authentication and the PAR, PKCE, DPoP and key settings of `axis`, so activation
  never undoes what `tenant oidc set` wrote. It also gives that site client every protocol mapper of `axis`, matched by
  name with the same type and config, so a sign-in through it carries the same claims. The `identity provider` and
  `identity provider acr` mappers stay as they are, and a mapper that is removed from `axis` is not removed from the site clients. Each site client also carries two attributes that every sync rewrites:
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
- **Provider labels:** a brokered provider's `displayName` in the realm is the message key `${axisProvider.<alias>}`.
  The sync writes the key's text for `en` and `vi` through the realm's localization API, changing only texts that
  differ, and removes the `axisProvider.` texts of providers that the realm no longer holds. The theme's `login.ftl`
  resolves the key with `advancedMsg`. Activation takes a label from the texts of the application: the text of the
  locale, then the text of the application's fallback locale, then the first locale in ordinal order that has the key,
  then the provider's name. An application has no fallback locale of its own, so it takes the one of its first site in
  path order, or `en` when it has no site. When several applications share a provider, the label comes from the
  application with the lowest application id. A tenant created before `manage-realm` was granted needs `tenant create` again, or activation
  fails at the label step with a 403.
- **Brought in step by activation:** activating a release adds, changes and removes the realm's brokered providers,
  their claim mappers and the site clients to match the active releases of the tenant and its platform sign-in. The
  stored platform providers are always part of the plan, so a provider that only the platform sign-in names stays in
  the realm. A provider that nothing names any more is removed. Its users stay, but cannot sign in through it.
  Built for `Axis.Admin`: `activate`, and the publisher's `publish`, `publish` with target `preview` and `rollback`
  update the realm inside their `activate` step, after the activation has committed. A tenant without an activation
  secret, such as a preview tenant or one that signs in through an outside identity provider, skips the update. A
  failure of the identity provider fails the step with a line that names the identity provider step and its status
  code, and the new release stays active, see [activate](administration.md#activate). A `validate` and a `test`
  never update the realm. The server's startup activation updates it too, see above. `tenant sign-in set` brings the
  realm in step after it stores the platform sign-in, see [platform sign-in](tenant-setting-commands.md#platform-sign-in).
- **Brought in step by restore:** `tenant restore` of a tenant that gets a new realm creates the realm as `tenant create`
  does and brings it in step with the active releases of the restored database. The archive holds no secret value, so a
  provider whose bindings have no value yet is left out with a warning. The next activation adds it once the operator has
  set the bindings, see [tenant restore](administration.md#tenant-restore).
- **Bindings:** a missing binding, or a binding of the wrong kind, fails activation, as for a connector (`AXC0100`).
  Built for `oidc` providers: `issuer` is a `url` binding, and `clientId`, `clientSecret` and `clientKey` are `secret`
  bindings. Built for `saml` providers: `metadata` is a `url` binding, reported at `/metadata/binding`. On a preview tenant activation leaves the provider out with a warning, see
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
- Two-step verification asks for the 6-digit code in six boxes, one digit each, with "Back to sign in" and, when Keycloak offers another way to sign in, "Try another way".
- "Forgot password?" opens a page that asks for the email or username, with a "Send link" button and "Back to sign in".
  Keycloak sends the link, and the page after "Send link" is still Keycloak's own.
- The page that sets a password, which an invitation and a reset link both open, asks for "New password" and "Confirm
  password" and has no way back, because it is a required step. The browser checks only that both fields are filled
  and match. Keycloak checks the password rules and its error shows above the form. Setting a password signs the
  person out on other devices, as Keycloak does by default.
- Add an authenticator, where a person who must add one sees the QR code, the key to type by hand, the 6-digit code in the same six boxes and the device name. The device name is required only when the person already has an authenticator.
- The theme is built from the SPA's own React components and tokens into a Keycloak theme, so the pages look like the
  rest of Axis. Its texts are message bundles in every locale the platform offers. The "Continue with" labels come
  from the applications' texts, which activation writes to the realm (_built_).
- _(built)_ The sign-in page is drawn by shared React components in `web/src/identitySignIn/` from a sign-in context:
  title, providers, accounts, site, username, error, login address, remember-me, forgot-password address, locales and
  texts. When the sign-in goes through a site client, which carries `axis.accounts`, `site` holds that site's
  providers and accounts flag. The page then lists only those providers, and shows the account form only when the realm
  and the site both allow accounts. A client without `axis.accounts`, such as `axis`, has no `site`, so the page shows
  every provider and the form as before. The account form is a real `<form method="post">` to the login address. The components draw the frame (the 32 px mark
  with the word "Axis", the 400 px card and the organization's name under the title), the "Continue with" secondary
  buttons, the form widget's field labels and "Forgot password?" on the password's row. The two-step page is drawn too,
  from its own context with `page` set to `twoStep`: six code boxes, one digit each, and a real
  `<form method="post">` to the login address that posts `otp` and `login`. "Try another way" posts `tryAnotherWay=on`
  and shows only when Keycloak offers another way to sign in. "Back to sign in" uses Keycloak's restart address. The "Forgot password?" page (`page` of `resetPassword`) is a
  real `<form method="post">` to the login address that posts `username`, and its "Back to sign in" uses `url.loginUrl`.
  The page that sets a password (`page` of `updatePassword`) posts `password-new`, `password-confirm` and a hidden
  `logout-sessions=on`. The page after "Send link" is still Keycloak's. The page that adds an authenticator is
  drawn too, from its own context with `page` set to `addAuthenticator`: the QR code (a base64 PNG), the manual key, the secret, the
  same six code boxes and the device name, in a real `<form method="post">` to the login address that posts `totp`, `totpSecret`,
  `userLabel` and, when Keycloak gave one, `mode`. It leaves out Keycloak's "Sign out from other devices" box and the cancel button
  of an app-initiated action. The design system's
  `identitySignIn` preview feeds the components fixtures, see
  [shell design system previews](shell.md#design-system-previews). The Keycloak theme `axis` feeds them a real
  context: its `login.ftl`, `login-otp.ftl`, `login-config-totp.ftl`, `login-reset-password.ftl` and `login-update-password.ftl` write Keycloak's data as JSON, with a `page` field of `signIn`, `twoStep`, `addAuthenticator`, `resetPassword` or `updatePassword`, and a React bundle draws the page. `scripts/build.sh`
  builds the theme from `web/keycloak-theme/` into `artifacts/keycloak-theme/axis/login/` and checks its files and
  fonts. The pages the theme has no template for, such as the page after "Send link", still come from Keycloak's `keycloak` parent theme. The development realm `axis` uses the theme as
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
