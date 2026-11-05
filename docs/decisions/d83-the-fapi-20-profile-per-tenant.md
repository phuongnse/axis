# D83. The FAPI 2.0 profile per tenant

Status: **Agreed**

## Context

[D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) left FAPI 2.0 for later. A token not
bound to its sender works for whoever steals it, which matters most for machine callers. Many
identity providers lack pushed authorization requests or DPoP.

## Decision

- **The FAPI 2.0 Security Profile,** not Message Signing.
- **Per tenant, off by default.** `Oidc:Profile` is `standard` or `fapi2`. A preview tenant takes its
  main tenant's.
- **Sign-in under `fapi2`** uses pushed authorization requests, PKCE `S256`, `private_key_jwt`, the
  `iss` response check and DPoP. The tenant's client key is set and rotated by
  [D87](d87-tenant-setting-commands-and-key-rotation.md)'s commands, and the server does not start
  without it.
- **Machine callers under `fapi2`** need DPoP-bound tokens.
- **Verified, not certified.** The OpenID Foundation's conformance suite runs by hand against the
  development realm, which has a client for each profile.

The detail is in [FAPI 2.0 profile](../reference/fapi2-profile.md).

## Consequences

- Under `fapi2` a stolen token is useless without the caller's key.
- A tenant whose provider lacks the profile signs in under `standard`, and Axis tests both.
- Axis holds no certificate to show a customer.

## Alternatives

- **`fapi2` for every tenant.** It would shut out providers without it.
- **Mutual TLS.** The TLS proxy of [D49](d49-several-server-replicas-behind-a-tls-proxy.md) ends the
  client's connection, so the server never sees its certificate.
