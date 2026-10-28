# D99. Axis ships its own identity provider

Status: **Agreed**

The owner wants every tenant's people to sign in on one standard Axis page, not on a default Keycloak page or on each
organization's own, and agreed these points on 2026-10-28. [Axis's identity provider](../reference/identity-provider.md)
holds the detail. None of it is built yet: M17 builds it, and the reference docs mark it _(planned for M17)_.

- **One identity provider in every installation:** Axis ships Keycloak with the Axis theme, and Docker Compose, the
  Helm chart and the on-premises install run it. The server stays the confidential OIDC client of
  [D23](d23-sign-in-through-oidc-and-the-bff-session.md), and each tenant's `Oidc` block points at that tenant's realm.
  This replaces "an OIDC provider that runs elsewhere" in the installation docs.
- **One realm per tenant:** each tenant's people, sign-in settings and brokered identity providers live in a realm of
  their own, as each tenant has a database of its own.
- **Brokering:** the realm brokers to the identity providers of the tenant's organization, which the sign-in page offers
  as "Continue with" buttons.
- **Accounts per tenant:** a realm may hold accounts of its own, for an organization with no identity provider. Each
  tenant turns them on or off.
- **MFA for accounts:** an account of Axis's identity provider must use a second step. A person who signs in through
  their organization follows that organization's MFA policy.
- **Direct sign-in only while moving:** an `Oidc` block that points straight at an organization's identity provider stays
  supported only while that tenant moves to Axis's identity provider.
- **The Axis theme:** the identity provider's pages are drawn from the SPA's React components and tokens, as the
  design system's `identitySignIn` preview shows.

_Why:_ a person's first sight of Axis is its sign-in page. Without an identity provider of its own, Axis shows
whatever page each organization's provider draws, or Keycloak's default one, and cannot keep one standard look or one
sign-in path to test and secure. Keycloak is already the identity provider of development and the tests, brokers to
OIDC and SAML providers, and keeps users, passwords and MFA out of Axis's own code. The cost is one more component to
install, upgrade and secure in every installation.
