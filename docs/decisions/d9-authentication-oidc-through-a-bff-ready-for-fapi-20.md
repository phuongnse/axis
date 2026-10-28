# D9. Authentication: OIDC through a BFF, ready for FAPI 2.0

Status: **Agreed**

- **Protocol:** OIDC authorization code flow with PKCE.
- **Client:** a .NET backend-for-frontend (BFF) acts as the confidential
  client.
- **Session:** the browser holds only a secure HttpOnly session cookie.
  Tokens never reach the browser.
- **Identity provider:** an external OIDC provider. Development uses Keycloak.
  Enterprise SAML or directory identities are handled by brokering through the
  identity provider, not inside Axis.
  - [D99](d99-axis-ships-its-own-identity-provider.md) changes this line: Axis ships its own identity provider, which
    brokers to each organization's.
- **FAPI 2.0:** the structure follows FAPI 2.0 roles (authorization server,
  confidential client, resource server). PAR, `private_key_jwt` and DPoP can be
  enabled later without restructuring. FAPI conformance is a later milestone.
  - [D83](d83-the-fapi-20-profile-per-tenant.md) adds the FAPI 2.0 profile as
    a setting per tenant.

_Why:_ the BFF pattern gives most of the protection. For a configuration
platform the real risk is business authorization, which FAPI does not cover.
Requiring full FAPI early would block every milestone.
