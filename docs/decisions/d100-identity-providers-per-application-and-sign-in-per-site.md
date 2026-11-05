# D100. Identity providers per application and sign-in per site

Status: **Agreed**

## Context

Each tenant has a realm in Axis's identity provider
([D99](d99-axis-ships-its-own-identity-provider.md)). Who signs in differs by application and by
site: staff through their organization's provider on an internal site, citizens through a national
provider on a portal. Platform pages need a sign-in before any application exists.

## Decision

- **An `identityProvider` resource.** An application declares its sign-in: accounts of Axis's
  identity provider, or an `oidc` or `saml` provider the realm brokers to, with its claims, its
  mapping to the application's roles and an optional lowest assurance level.
- **Values in bindings.** The provider's address and client credentials are deployment bindings, as
  for a connector. A preview tenant leaves them empty, so it never signs anyone in through a real
  provider.
- **A site picks its sign-in.** Each site is a client of the realm and lists the providers it allows.
  The callback checks the provider and the assurance level again, because a hidden button is not a
  control.
- **One user per person and provider.** Providers that name the same bindings are one provider in
  the realm, and two that share an issuer binding but not their client bindings fail activation. The
  user id is the realm's `sub`, and the realm's session signs a person in to every site that allows
  their provider. Two providers make two users: an email from another provider is no proof of who
  signed in.
- **A site may get a host of its own,** which serves only that site. An outside site should, because
  sites on one host share an origin.
- **Platform sign-in is a tenant setting.** The operator sets which providers the platform pages
  accept, and the author, publisher, operator and administrator roles come from it.
- **`Axis.Admin` creates the realm,** as it alone holds the identity provider's admin rights
  ([D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md)). Activation keeps the
  realm in step, with rights over that realm only.
- **A users page** lets a holder of the installation's administrator role invite accounts, disable
  and enable people, reset an authenticator, and give roles, each change audited.
- **Always through the realm.** A tenant's `Oidc` block points at its realm. No tenant has to move
  from a provider outside Axis, because Axis keeps one format until its first production release
  ([D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)).

The detail is in [Axis's identity provider](../reference/identity-provider.md) and
[user administration](../reference/user-administration.md).

## Consequences

- One release serves every environment.
- Refusing to link by email closes the usual account takeover, at the cost of two users for a person
  with two providers.
- Account and role changes are themed, tested and audited, without Keycloak's admin console.

## Alternatives

- **Sign-in as tenant settings only.** It would not travel with the application, and Studio could not
  test it.
