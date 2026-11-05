# D23. Sign-in through OIDC and the BFF session

Status: **Agreed**

## Context

Policies and processes need to know who acts, through each tenant's own identities.
[D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) makes the server the confidential
client. A cookie carried to another tenant's host must not act there.

## Decision

- **A sign-in client per tenant.** Each tenant's `Oidc` block names its realm in Axis's identity
  provider ([D99](d99-axis-ships-its-own-identity-provider.md)), its client and its claims. The user
  id is `sub` by default, because it never changes. The client secret never sits in a committed file,
  and the server does not start with a block missing or incomplete.
- **One HttpOnly session cookie,** Secure and SameSite=Lax, holding no token. The session lives on
  the server ([D84](d84-asvs-level-2-and-a-server-side-session.md)) and ends 8 hours after sign-in or
  after 1 hour idle.
- **A session belongs to one tenant.** On another tenant's host it is no session.
- **Roles are read at sign-in,** from the roles claim.
- **Application content needs a session.** Only what the shell needs before sign-in stays open.
- **CSRF by a header.** Every write under `/api` carries `X-Axis-Csrf: 1`, or gets `403`, besides the
  JSON content type and the SameSite cookie. There is no antiforgery token.
- **A development realm** with users of fixed ids, committed and never valid outside development and
  the tests.
- **The actor is the session's user id.** Audit records keep no display name
  ([D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)).

The detail is in [sign-in and the session](../reference/security.md#sign-in-and-the-session).

## Consequences

- A stolen or reused cookie cannot act in another tenant.
- A role given at the identity provider waits for the next sign-in.
- The SPA sends the CSRF header on every write.
