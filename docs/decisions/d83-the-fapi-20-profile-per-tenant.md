# D83. The FAPI 2.0 profile per tenant

Status: **Agreed**

M16 adds the FAPI 2.0 Security Profile as a sign-in profile that a tenant can
turn on. It is the step that
[D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) left for
later. It is built, except the conformance run of the verification point, which is _(planned for M32)_. See
[FAPI 2.0 profile](../reference/security.md#fapi-20-profile) for the detail.
The owner agreed these points on 2026-10-25.

- **Which profile:** the FAPI 2.0 Security Profile. The Message Signing
  profile is not in scope.
- **Per tenant, off by default:**
  - A new tenant setting, `Oidc:Profile`, is `standard` or `fapi2`. It is
    `standard` when unset.
  - Many identity providers lack pushed authorization requests or DPoP, so the
    profile cannot be the only one.
  - A preview tenant takes its main tenant's profile, as it takes the rest of
    the sign-in settings ([D48](d48-preview-tenants.md)).
- **The sign-in client under `fapi2`:**
  - Pushed authorization requests are required.
  - PKCE with `S256`, as today.
  - The client authenticates with `private_key_jwt` instead of a client
    secret.
  - The server checks the `iss` parameter of the authorization response
    (RFC 9207).
  - The token request uses DPoP.
- **Machine callers under `fapi2`:**
  - Inbound endpoints and [service accounts](d86-service-accounts-for-the-record-and-process-apis.md)
    accept only DPoP-bound access tokens.
  - DPoP, not mutual TLS, because the TLS proxy of
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md) ends the client's
    TLS connection.
  - The DPoP proof's `htu` is checked against the forwarded scheme and host.
- **The client key:**
  - Each tenant under `fapi2` has a private key for `private_key_jwt`. Its
    public key is registered at the identity provider.
  - Under `Tenancy:Source=directory` it is stored encrypted, like the client
    secret. Under `configuration` it comes from the environment.
  - The commands that set and rotate it are in
    [D87](d87-tenant-setting-commands-and-key-rotation.md).
- **Startup checks:** a tenant under `fapi2` without a client key stops the
  server's start, naming the tenant, as a missing client secret does today.
- **Verification:**
  - The OpenID Foundation's conformance suite runs by hand against the
    development realm set to `fapi2`. Its result is recorded in the
    [ASVS tracking](../reference/asvs.md).
  - Axis seeks no certification.
- **Development realm:** the Keycloak realm gets a second client set up for
  FAPI 2.0, so tests cover both profiles.

_Why:_ D9 shaped the BFF so that FAPI 2.0 could be added without
restructuring. Binding tokens to their sender stops a leaked token from being
used by anyone else, which matters most for machine callers. A setting per
tenant lets an installation turn it on where the identity provider supports
it, without blocking tenants whose provider does not. A self-run conformance
suite finds the gaps at no cost, and certification can come when a customer
needs it.
