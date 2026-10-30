# D100. Identity providers per application and sign-in per site

Status: **Agreed**

The owner agreed these points on 2026-10-28. They replace the points of
[D99](d99-axis-ships-its-own-identity-provider.md) that made brokering and accounts settings of the tenant, and its
direct sign-in point. D99's identity provider, its realm per tenant, its MFA rule and its theme hold.
[Axis's identity provider](../reference/identity-provider.md) and [user administration](../reference/user-administration.md)
hold the detail. It is built in M17, except the users page's disable, enable and reset buttons and keeping the realm's redirect addresses in step with tenant hosts, which are _(planned for M28)_.

- **An `identityProvider` resource:** an application declares how its people sign in, as a resource of the release.
  - Its `type` is `accounts`, for accounts that Axis's identity provider holds, `oidc` or `saml`, for a provider that
    the realm brokers to.
  - It has the label of its "Continue with" button and, for `oidc` and `saml`, the claims that give the user id, the
    display name, the email and the locale, the claim mapping to the application's roles, and an optional lowest
    assurance level (`acr`).
  - The values that differ by tenant and environment are deployment bindings, as for a connector: the issuer or
    metadata address is a `url` binding, the client id and the client credential are `secret` bindings.
  - A preview tenant leaves them empty, so a preview never signs anyone in through a real provider by accident.
- **A site picks its sign-in:** a site lists the identity providers it allows in `signIn`. Each site is a client of
  the tenant's realm, and its sign-in page shows only those providers. The server checks at the callback that the
  provider and the assurance level are ones the site allows, because a hidden button is not a control.
- **One provider in the realm per binding:** identity providers of two applications that name the same bindings are one
  provider in the realm, so a person is one user across the tenant's applications. Two that share an issuer binding but
  not their client bindings fail activation. Each application still maps the claims to its own roles.
- **Sign-in once:** the realm's session signs a person in to every site that allows the provider they used, without
  asking again. The user id is the realm's `sub`.
- **No automatic linking:** a person who signs in through two providers is two users. The realm never links them by
  email, because an email from another provider is not proof of who signed in. Linking that the person proves on both
  sides comes later.
- **Sites with hosts of their own:** a tenant host can be given to one site. That host serves only the site, and its
  session cookie reaches nothing else. A site for people outside the organization should get one, because sites that
  share a host share an origin.
- **Platform sign-in:** the home page, Studio, the operator console and the account and users pages belong to no site.
  The operator sets, for each tenant, which providers they accept: accounts, or brokered providers through bindings.
  The author, publisher, operator and administrator roles come from that sign-in. On the tenant's host the home page
  also offers the providers of the sites that share the host.
- **Who writes the realm:** `Axis.Admin` creates a tenant's realm and its clients, because only it holds the identity
  provider's admin rights, as it alone holds the database server's in
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md). Activating a release brings the realm's
  providers and site clients in step with it, with rights over that tenant's realm only. A missing or wrong binding
  fails activation, as for a connector.
- **A users page:** a person who holds the installation-level administrator role, `Users:AdministratorRole`, invites
  accounts, disables and enables people, resets an account's authenticator, and gives and removes application and
  platform roles at `/users`. Given roles add to the roles a provider's claims map. Each change writes an audit
  record.
- **No direct sign-in:** a tenant's `Oidc` block always points at its realm. Axis keeps one format until the first
  production release, as [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) says, so no
  tenant has to move from a provider outside Axis.
- **Signing in without an account:** a public site that nobody signs in to comes later, with its own decision.

_Why:_ who signs in differs by application and by site, such as staff through their organization's provider on an
internal site and citizens through a national provider on a portal, so the sign-in belongs to the application that
the author configures and Studio tests. Keeping the values in bindings keeps one release for every environment, as
connectors do. One provider in the realm per binding keeps one user per person, and refusing to link by email closes
the usual way to take over another person's account. Platform pages need a sign-in before any application exists,
which only a tenant setting can give. A users page in Axis keeps account and role changes themed, tested and audited,
without giving anyone Keycloak's admin console.
