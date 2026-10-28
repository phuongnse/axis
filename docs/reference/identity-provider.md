# Axis's identity provider

Axis ships an identity provider of its own, so every tenant's people sign in on one standard Axis page, whatever
identity provider their organization uses. [D99](../decisions/d99-axis-ships-its-own-identity-provider.md) agrees
it. None of it is built yet: M17 builds it, so every section is _(planned for M17)_. Until it is built, each
tenant's `Oidc` block points at an identity provider that runs outside Axis, as
[security](security.md#sign-in-and-the-session) describes.

## What it is

_(planned for M17)_

- Axis's identity provider is Keycloak with the Axis theme. Every installation runs it beside the server and the
  worker.
- The server's sign-in does not change: it stays the confidential OIDC client of
  [D23](../decisions/d23-sign-in-through-oidc-and-the-bff-session.md), and the session stays on the server. Each
  tenant's `Oidc` block points at that tenant's realm in Axis's identity provider.
- It brokers to the identity providers of the tenant's organization. A person of an organization that uses Microsoft
  Entra ID or Okta clicks "Continue with Microsoft" or "Continue with Okta" on the Axis page, signs in there, and comes
  back through Axis's identity provider to Axis.

## Realms

_(planned for M17)_

- Each tenant has one realm of its own, with its people, its sign-in settings, its brokered identity providers and
  the Axis theme. Tenants share nothing in it, as they share no database.
- The realm's display name is the organization's name, which every sign-in page shows under its title.

## Accounts and MFA

_(planned for M17)_

- A realm may hold accounts of its own, for an organization with no identity provider. Each tenant turns them on or
  off. With them off, the sign-in page shows only the organization's own identity providers.
- An account of Axis's identity provider must use a second step, a code from an authenticator app, because Axis then
  holds the password and answers for it. A person who signs in through their organization's identity provider follows
  that organization's MFA policy, and Axis asks for no second step on top.

## Direct sign-in

_(planned for M17)_

- Every tenant signs in through Axis's identity provider. An `Oidc` block that points straight at an organization's
  identity provider stays supported only while that tenant moves to Axis's identity provider, so the platform keeps one
  sign-in path to build, test and secure.

## Theme

_(planned for M17)_

- Every page of Axis's identity provider has one frame: the Axis mark above one card in the middle of the page on
  `canvas`, and below it the language links and "© <year> Axis". It follows light and dark mode and the Axis tokens and
  fonts, as the SPA does.
- The sign-in page lists the organization's identity providers first, then "or" and the account form: email or
  username, password with "Forgot password?", "Keep me signed in" and a full-width Sign in button.
- A wrong password keeps the username, marks the password and reads "Invalid email, username or password." above the
  form, never saying which one was wrong.
- An organization that signs in only through its own identity provider shows its button alone, under one line that
  names it.
- Two-step verification asks for the 6-digit code, with "Try another way" and "Back to sign in".
- The theme is built from the SPA's own React components and tokens into a Keycloak theme, so the pages look like the
  rest of Axis. Its texts are message bundles in every locale the platform offers.
- _(built)_ The design system's `identitySignIn` preview draws these pages from fixtures, see
  [shell design system previews](shell.md#design-system-previews).

## Installation

_(planned for M17)_

- Docker Compose, the Helm chart and the on-premises install run Axis's identity provider with its own database on the
  PostgreSQL server, behind the same TLS proxy as the server. The operator upgrades it with Axis.
- The development realm `axis` stays for `scripts/dev.sh` and the tests, see
  [security](security.md#development-users).
