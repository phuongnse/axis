# D86. Service accounts for the record and process APIs

Status: **Agreed**

## Context

Other systems need to read and write records and start processes without a person. An
[inbound endpoint](d27-inbound-endpoints.md) gives them only its narrow contract, and Axis keeps no
user directory ([D23](d23-sign-in-through-oidc-and-the-bff-session.md)) to hold API keys.

## Decision

- **A service account is a client at the identity provider,** with client credentials and roles in
  the roles claim. Axis issues no API keys.
- **An audience of its own,** `Oidc:ServiceAudience`, apart from the endpoint audience.
- **It reaches** the record API, data source rows, process start, process inspection and the
  operator commands, besides the endpoints of [D27](d27-inbound-endpoints.md).
- **Never tasks.** A task asks a person to decide.
- **The header picks the scheme.** A request with an `Authorization` header never falls back to the
  cookie, and needs no CSRF header.
- **Same checks as a person:** the endpoint token checks, DPoP under `fapi2`
  ([D83](d83-the-fapi-20-profile-per-tenant.md)), the same policies
  ([D12](d12-authorization-is-server-side-and-default-deny.md)) and the bearer rate limit
  ([D85](d85-rate-limits-and-security-headers.md)). The user id claim, `sub` by default, is the
  actor.

The detail is in [service accounts](../reference/security.md#service-accounts).

## Consequences

- Identities and roles stay at the identity provider, and no policy is skipped for a system.
- A system never completes, claims or reassigns a task.

## Alternatives

- **API keys issued by Axis.** Axis would need an identity store.
- **One audience with endpoints.** An endpoint token would reach the record API.
