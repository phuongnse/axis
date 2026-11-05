# D84. ASVS Level 2 and a server-side session

Status: **Agreed**

## Context

"Secure" means little until it is a list a review can check. Axis holds the business and personal
data of many tenants, and OWASP aims ASVS Level 2 at such applications. The level shows a gap in
[D23](d23-sign-in-through-oidc-and-the-bff-session.md)'s session: a cookie that holds the claims
cannot be revoked.

## Decision

- **ASVS 5.0, Level 2.** The identity provider meets the account controls, because Axis has no
  passwords, and the operator meets malware scanning of uploads
  ([D76](d76-file-money-and-enum-label-field-types.md)).
- **One tracking page** lists each requirement once with its status and evidence, and
  `scripts/check-docs.mjs` checks it.
- **The session lives on the server.** A tenant database row holds it, keyed by a hash of a random
  id, which is all the cookie holds. Sign-out deletes the row, and every sign-in makes a new id.
- **A `__Host-` cookie** outside plain-http Development and Testing.
- **A failed sign-in shows no reason.** The audit record of
  [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md) keeps it.

The detail is in [ASVS](../reference/asvs.md) and
[sign-in and the session](../reference/security.md#sign-in-and-the-session).
[D85](d85-rate-limits-and-security-headers.md), [D87](d87-tenant-setting-commands-and-key-rotation.md)
and [D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md) close the other gaps.

## Consequences

- Sign-out means what it says: a copied cookie stops at once.
- Each request reads the session table once.

## Alternatives

- **Level 3.** It assumes controls an on-premises install cannot promise, such as a hardware key
  module.
- **Claims in the cookie.** It cannot be revoked.
