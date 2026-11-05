# D99. Axis ships its own identity provider

Status: **Agreed**

## Context

A person's first sight of Axis is its sign-in page. Without an identity provider of its own, Axis
shows each organization's page, or Keycloak's default one, and cannot keep one look or one sign-in
path to test and secure. Keycloak already serves development and the tests, and brokers to OIDC and
SAML providers.

## Decision

- **Keycloak with the Axis theme in every installation,** run by Docker Compose, the Helm chart and
  the on-premises install. The server stays the OIDC client of
  [D23](d23-sign-in-through-oidc-and-the-bff-session.md).
- **One realm per tenant,** as each tenant has a database of its own, and the tenant's `Oidc` block
  points at it.
- **The realm brokers** to the organization's providers, offered as "Continue with" buttons, and can
  hold accounts of its own. [D100](d100-identity-providers-per-application-and-sign-in-per-site.md)
  says which ones an application and a site offer.
- **Accounts need a second step.** A brokered sign-in follows the organization's MFA policy.
- **The pages are drawn from the SPA's React components and tokens.**

The detail is in [Axis's identity provider](../reference/identity-provider.md).

## Consequences

- People sign in on one Axis page, and users, passwords and MFA stay out of Axis's code.
- Every installation has one more component to install, upgrade and secure.
