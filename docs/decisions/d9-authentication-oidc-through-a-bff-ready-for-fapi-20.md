# D9. Authentication: OIDC through a BFF, ready for FAPI 2.0

Status: **Agreed**

## Context

Axis must sign people in with their organizations' identities, keep no passwords and let no token
reach the browser. Some customers will ask for FAPI 2.0, but requiring it early would block every
milestone.

## Decision

- **OIDC with PKCE through a BFF.** The .NET server is the confidential client, and the browser holds
  only a secure HttpOnly session cookie.
- **Accounts live at the identity provider,** which brokers to enterprise identities. It is Axis's own
  ([D99](d99-axis-ships-its-own-identity-provider.md)).
- **Shaped for FAPI 2.0.** The parts follow its roles, so a tenant can turn the profile on
  ([D83](d83-the-fapi-20-profile-per-tenant.md)).

The detail is in [sign-in and the session](../reference/security.md#sign-in-and-the-session).

## Consequences

The BFF gives most of the protection. The real risk of a configuration platform, business
authorization, lies outside FAPI, and [D12](d12-authorization-is-server-side-and-default-deny.md)
covers it.
