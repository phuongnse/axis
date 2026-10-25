# D86. Service accounts for the record and process APIs

Status: **Agreed**

M16 lets other systems call the record API, the data source endpoint and the
process API as service accounts, with a bearer token. The record API is built.
The data source rows, the process API, rate limits and the `fapi2` DPoP binding
are _(planned for M16)_. See
[service accounts](../reference/security.md#service-accounts) for the detail.
The owner agreed these points on 2026-10-25.

- **Replaces a point of D27:**
  [D27](d27-inbound-endpoints.md) accepts bearer tokens only on endpoint
  routes. This decision also accepts them on the routes listed below.
  Endpoints keep their own rules.
- **A service account is a client at the identity provider:**
  - It is a client of the tenant's identity provider that gets tokens through
    the client credentials grant.
  - Axis issues no API keys of its own, because it keeps no user directory
    ([D23](d23-sign-in-through-oidc-and-the-bff-session.md)).
- **Audience:**
  - A new tenant setting, `Oidc:ServiceAudience`, separate from
    `Oidc:EndpointAudience`. An endpoint caller gets no access to the record
    API by accident.
  - A tenant without it accepts no service account token.
- **Routes:**
  - the record API, record history included;
  - data source rows;
  - process start;
  - process inspection: the instance list and one instance;
  - the operator commands retry, cancel and resolve.
- **Never tasks:** the task API, the task inbox and the form state endpoint
  stay for people. A task asks a person to decide, so a service account
  cannot complete, claim or reassign one.
- **Not reached:** site and page metadata, `GET /api/me`, Studio and the
  authoring API. A bearer token there is ignored, as today.
- **Choosing the scheme:**
  - On the routes above, a request with an `Authorization: Bearer` header is
    a service account request. It never falls back to the session cookie, and
    the cookie is not renewed.
  - Such a request needs no CSRF header, because a browser cannot add the
    header to a cross-site request on its own.
  - A request without the header uses the session, as today.
- **Token checks:** the same as for endpoints: signature, issuer, expiry,
  and an audience that holds `Oidc:ServiceAudience`. Under the `fapi2`
  profile of [D83](d83-the-fapi-20-profile-per-tenant.md) the token must be
  DPoP-bound.
- **Authorization:**
  - The roles come from the roles claim, as for users.
  - Policies apply unchanged, record filters and fields included
    ([D12](d12-authorization-is-server-side-and-default-deny.md)).
  - A service account with no grant gets a `403`, as a user does.
- **Actor:** the caller id comes from the user id claim, `sub` by default. It
  is the audit actor and the creator of a new record. The display name falls
  back to the client id, `azp`, as for endpoints.
- **Rate limits:** service account requests use the bearer partition of
  [D85](d85-rate-limits-and-security-headers.md).
- **Development realm:** the Keycloak realm gets a service account client with
  the `axis-services` audience, so tests cover allowed and denied calls.

_Why:_ other systems need to read and write records and start processes
without a person signing in, and today they can only use the narrow contract
of an endpoint. A client at the identity provider keeps one place for
identities and their roles. Running every call through the same policies as a
user means no rule is skipped. A separate audience keeps an endpoint token
out of the record API. Keeping tasks for people keeps the meaning of a task:
a decision a person makes.
