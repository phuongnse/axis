# FAPI 2.0 profile

A tenant can turn on the FAPI 2.0 Security Profile for sign-in and for machine callers, for
organizations whose rules ask for it. This page says what the profile changes: the tenant setting,
sign-in, the tokens that machine callers and service accounts send, and the client key. The rest of
authentication and authorization is in [authentication and authorization](security.md), and the
reasons for a profile per tenant are in [D83](../decisions/d83-the-fapi-20-profile-per-tenant.md).

## Tenant setting

`Oidc:Profile` is `standard` or `fapi2`, and `standard` when unset. Any other value stops the server's
start, naming the tenant. A preview tenant takes its main tenant's profile.

A `standard` tenant uses no PAR, no check of the `iss` response parameter and no DPoP, and
authenticates with a client secret.

## Sign-in under `fapi2`

Under `fapi2` the server sends the authorization request through a back channel, authenticates with a
signed key rather than a secret, and checks who answered.

- **Pushed authorization requests.** The server sends the authorization request through pushed
  authorization requests (PAR), PKCE `S256` included. The browser goes to the identity provider with
  only the `request_uri` that comes back. A provider whose metadata has no PAR endpoint fails the
  tenant's sign-in: the server logs a warning that names the tenant, and the browser goes to
  `/?signIn=failed`. No audit record is written, as no callback happened.
- **`private_key_jwt`.** The client authenticates at the PAR endpoint and at the token endpoint with
  `private_key_jwt`: a JWT of 60 seconds, signed with the tenant's newest client key, with the `kid` of
  that key. Its `iss` and `sub` are the client id, its `aud` is the tenant's `Authority`, and each
  request has its own `jti`. The server sends no client secret, even when the tenant has one. Client
  keys are EC P-256 keys and the assertion is signed with ES256. RSA keys are not supported.
- **The `iss` response parameter.** The server checks the `iss` parameter of the authorization
  response (RFC 9207). It must equal the tenant's `Authority` character for character, after one
  trailing `/` is removed from the `Authority`. A callback with no `iss`, or a different one, creates
  no session and writes `session.signInRefused` with the reason `invalidIssuer`, and the code is never
  sent to the token endpoint. The check runs after the state checks, so a callback with a bad state is
  still refused as `invalidState` or `otherTenant`. An error response from the provider is refused as
  `providerError`, whatever its `iss`.
- **DPoP at the token endpoint.** The token request carries a DPoP proof (RFC 9449), signed with a new
  P-256 key that is made for each sign-in and thrown away after it. The proof has `typ` `dpop+jwt`, the
  `htm` `POST` and the token endpoint as `htu`, and holds only the public key. If the provider answers
  `use_dpop_nonce`, the server sends the request once more, with the nonce in a new proof signed with
  the same key and with a new client assertion. A second refusal fails the sign-in as `providerError`.
  The authorization code is not bound to the key, and the PAR request carries no proof. The server
  keeps no token after sign-in, so the DPoP-bound access token is discarded.

## Machine callers under `fapi2`

Under `fapi2`, inbound endpoints and service accounts accept only access tokens bound to a DPoP key
(RFC 9449). A DPoP proof is a short signed JWT that the caller makes for each request, so a stolen
token is useless without the caller's private key. Mutual TLS is not offered to these callers, because
the TLS proxy ends the client's TLS connection. The rule sits in the token check itself, so it holds on every route
that takes a bearer token.

- **The header.** The token arrives as `Authorization: DPoP <token>`, with one `DPoP` header that holds
  the proof. The same token under `Authorization: Bearer` is refused, and so is a plain bearer token.
- **The key.** The proof's `typ` is `dpop+jwt` and its `alg` is `ES256` or `PS256`. Its `jwk` header
  holds the public key, and a `jwk` with a private key is refused. A `PS256` key must be an RSA key of
  at least 2048 bits, the floor of FAPI 2.0. The signature is checked with that key.
- **The request.** The proof's `htm` is the request's method. Its `htu` is the request's scheme, host
  and path, without the query and fragment, after the
  [forwarded headers](host-settings.md#forwarded-headers) rules. So a proof made for the address the
  caller used passes behind a listed proxy, and one made for the proxy's internal address does not.
- **The token.** The proof's `ath` is the hash of the access token, so a proof made for one token never
  works with another. The token's `cnf.jkt` must equal the thumbprint of the proof's key.
- **Freshness.** The proof's `iat` must be within 60 seconds of the server's clock, on either side, and
  its `jti` must not be blank.
- **No replay store.** The server keeps no store of seen `jti` values and asks for no `nonce`, because
  a store on one replica would not stop a replay on another. So the same proof can be replayed for up
  to 60 seconds, but only to the same method and address and with the same token.
- **The answer.** Any failure is a `401` with `WWW-Authenticate: DPoP algs="ES256 PS256"`, which never
  says which check failed. A tenant under `standard` answers `WWW-Authenticate: Bearer`.

## The client key

A tenant under `fapi2` has a private key for `private_key_jwt`, an EC P-256 key as a PEM (PKCS#8),
which takes the place of the client secret.

- **Where it is.** Under `configuration` it comes from the environment, such as
  `Tenants__default__Oidc__ClientKey`, as a PEM, with its key id in `Oidc:ClientKeyId`. Under
  `directory` it is stored encrypted in the tenant directory, with the field name `oidc_client_key` and
  its key id, see [platform database](storage.md#platform-database).
- **How many.** A tenant can hold two keys under `directory`, the current one and the previous one, and
  one under `configuration`. A tenant under `fapi2` without a key, or with a key without a key id,
  stops the server's start.
- **Rotation.** The tenant can hold a second key while the identity provider changes over. The server
  signs with the newest key after its next refresh. `tenant oidc set --client-key` and
  `--retire-client-key` change the keys, see
  [tenant setting commands](tenant-setting-commands.md#sign-in-settings). For a tenant that signs in
  through its own realm, the command registers each key in the realm's clients and removes a retired
  one, so the operator only adds, waits for the refresh and retires. For a tenant that signs in through
  an identity provider outside Axis, such as a restored one, the operator registers the public keys
  there.

## Verification

_(planned for M32)_

The OpenID Foundation's conformance suite runs by hand against the development realm under `fapi2`.
Its result is recorded in [ASVS](asvs.md). Axis seeks no certification.

## Development realm

The development realm has a second client, `axis-fapi`, so tests cover both profiles. Only an
integration test host uses it.

- **The client.** It has no secret and authenticates with `private_key_jwt`, and it requires PAR, PKCE
  `S256` and DPoP-bound tokens.
- **The client policy.** A client policy applies to a client that has a `fapi2` role, and only
  `axis-fapi` has one, so it never touches the other clients. Its profile `axis-fapi2` has four
  executors: the PKCE enforcer, the secure client authenticator that allows only `client-jwt`, the
  signature algorithm check for signed JWT client assertions and the DPoP enforcer. The DPoP enforcer
  refuses every update of a client that has DPoP-bound tokens off, so the client has them on, and the
  [legal sync](identity-provider.md#keeping-the-realm-in-step) can write its links.
- **The keys.** The client's JWKS holds the public keys of two EC P-256 keys, `key-1` and `key-2`. It
  lists each key under that name and under its key id from `tenant oidc set --client-key`, the JWK
  thumbprint, so Keycloak accepts both. Their private keys are committed as
  `samples/keycloak/axis-fapi-key-1.pem` and `axis-fapi-key-2.pem`, for development and tests only.
  They are registered on this client of the development realm and nowhere else.
