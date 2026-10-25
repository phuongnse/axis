# D84. ASVS Level 2 and a server-side session

Status: **Agreed**

M16 verifies Axis against OWASP ASVS 5.0 Level 2, the level that
[D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md) left to M16.
It also closes the gaps in the session that the level shows. All of it is
_(planned for M16)_. See [ASVS](../reference/asvs.md) and
[sign-in and the session](../reference/security.md#sign-in-and-the-session)
for the detail. The owner agreed these points on 2026-10-25.

- **The level:** ASVS 5.0, Level 2.
  - Axis holds business and personal data of many tenants. OWASP aims Level
    2 at most such applications.
  - Level 3 assumes controls that an on-premises install cannot promise,
    such as keys held in a hardware module.
- **Delegated to the identity provider:** multi-factor authentication,
  password rules, account lockout and account recovery. Axis has no
  passwords of its own ([D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md)).
  The tracking names each such requirement as delegated.
- **Tracking:**
  - A new reference page, [ASVS](../reference/asvs.md), lists each Level 2
    requirement once, with its status: met, gap, delegated or not
    applicable.
  - Each met requirement names its evidence: a test class, a doc section or
    a check.
  - Each gap names the issue that closes it.
  - `scripts/check-docs.mjs` checks that every Level 2 requirement id appears
    exactly once.
- **Server-side session:**
  - Session state moves to a table in the tenant database. The cookie holds
    only a random session id.
  - This replaces the point of
    [D23](d23-sign-in-through-oidc-and-the-bff-session.md) that the cookie
    holds the user id, the display name, the roles and the tenant id.
  - Sign-out deletes the session row, so a copied cookie stops working at
    once.
  - The lifetimes stay: 8 hours after sign-in, or 1 hour without requests.
  - The table stores a hash of the session id, never the id itself.
  - Sign-in creates a new session id every time, so a session id fixed before
    sign-in is never used.
- **The cookie:** the cookie is named `__Host-Axis.Session`. The prefix makes
  the browser refuse it without Secure, with a domain or with a path other
  than `/`. In Development and Testing over plain http it keeps the old name.
- **A refused sign-in:** a callback that fails redirects to the shell's
  sign-in page, which says that sign-in did not complete. It no longer
  answers a `500`. The audit record of
  [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md) keeps its
  reason code. The page shows no reason.
- **Other Level 2 gaps** are closed by the decisions of the same milestone:
  security headers and rate limits
  ([D85](d85-rate-limits-and-security-headers.md)) and key rotation
  ([D87](d87-tenant-setting-commands-and-key-rotation.md)).
- **Sign-off:** the tracking page can start at once. The final pass comes
  last in M16, after M14 and M15 have added their routes, so it covers what
  they built.

_Why:_ an agreed level turns "secure" into a list that a review can check.
Level 2 matches the data Axis holds, and delegating the account controls
keeps one place for them, the identity provider. A cookie that holds the
claims cannot be revoked: whoever copies it stays signed in until it expires.
Holding the state on the server makes sign-out mean what it says. The cost is
one read of the session table on each request.
