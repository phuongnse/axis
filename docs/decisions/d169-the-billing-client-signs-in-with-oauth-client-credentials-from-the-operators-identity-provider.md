# D169. The billing client signs in with OAuth client credentials from the operator's identity provider

Status: **Proposed**

## Context

[D168](d168-billing-is-the-operators-own-system-and-axis-keeps-plans-as-quotas-the-clock-and-a-billing-api.md)
gives the billing API a client and a scope of its own. It does not say how the client signs in. The
control plane is a .NET service with no identity provider of its own, so it cannot issue the tokens. A
cell already takes tokens for its machine callers, such as the control plane, from its operator realm,
with the client credentials grant, see [reaching the cells](../reference/control-plane.md#reaching-the-cells).

## Decision

- **The operator registers one client for its billing system** in an OAuth 2.0 and OpenID Connect
  provider that it chooses, such as a Keycloak realm. The client holds the scope `axis-billing`.
- **The billing system gets a token with the client credentials grant** and sends it as a bearer token.
- **The control plane checks each token** against the provider's metadata. It checks the issuer and the
  signature, the expiry, the audience that the operator names, that the client id (`client_id`, or `azp`)
  is the configured client, and that the `scope` claim holds `axis-billing`.
- **Only that client may call the billing API.** Its client id is the actor of each audit record, see
  [audit records](../reference/billing-api.md#audit-records).
- **The operator names the provider in settings,** as [signing in](../reference/billing-api.md#signing-in)
  says.
- **The API is reached over HTTPS at the control plane's address.** The token, not the network, is the guard.

## Consequences

- The operator runs or reuses an identity provider for its billing system. Axis runs none for it.
- The control plane needs only public metadata and standard claims, so any provider that follows the
  standards works, and the control plane holds no secret for the billing client.
- The operator must make its token carry the audience and the scope. In Keycloak, for example, that is
  an audience mapper and a client scope on the billing client.

## Alternatives

- **A static API key.** It carries no scope, and it is a long-lived shared secret that the operator must
  rotate by hand.
- **Mutual TLS alone.** It proves who calls but gives no scope, and every operator would need a
  certificate issued for its billing system.
- **A realm that ships with the control plane.** It would be a second identity provider to run beside
  the realms of the cells.
