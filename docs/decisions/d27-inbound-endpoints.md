# D27. Inbound endpoints

Status: **Agreed**

## Context

Other systems need to hand work to Axis, such as a purchasing system that submits a purchase request,
as [what applications use most](../domain/knowledge.md#what-applications-use-most) says. Such a call
must follow the same rules as a user's work, and a machine caller must not use the browser session of
[D23](d23-sign-in-through-oidc-and-the-bff-session.md).

## Decision

- **An `endpoint` resource** declares typed input, the entity of the record it creates, `values` that
  map the input to its fields, and the process it starts on the record.
  [D115](d115-an-inbound-endpoint-call-raises-an-event.md) lets it raise an event as well as, or
  instead of, starting a process.
- **One call, one transaction.** The call creates the record through the record create command, so
  every record rule applies, and starts the process pinned to the active release.
- **Callers send bearer tokens,** OAuth 2.0 access tokens that they get from the tenant's identity
  provider through client credentials. The token's issuer must be the tenant's authority and its
  audience a tenant setting of its own, so a tenant without that setting accepts no caller. Endpoints
  ignore the session cookie, so the CSRF rule does not apply to them.
- **An `invoke` grant on the endpoint is enough.** The record create and the process start are the
  endpoint's contract and need no entity or process grant, and `values` limits what a caller can
  write.
- **The call may wait** a short time for the instance and return chosen fields of the record. When
  the instance has not completed, it answers with a status location that only the same caller can
  read.
- **An idempotency key** per endpoint and caller makes a repeat return the same record and instance.
  A repeat never waits and follows the instance's state.
- **The caller is the actor:** the token's subject is the audit actor and the record's creator.

The detail is in [inbound endpoints](../reference/integration-resources.md#inbound-endpoints),
[inbound endpoint calls](../reference/inbound-endpoints.md) and
[machine callers](../reference/security.md#machine-callers).

## Consequences

- An endpoint goes through the same record command and process start as a user, so no rule is
  skipped.
- Each tenant checks tokens against its own authority and audience, as
  [D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) asks, and a missing audience,
  grant or instance gives no access, as [D12](d12-authorization-is-server-side-and-default-deny.md)
  requires.
- Endpoint tokens stay apart from the browser sign-in and from the service accounts of
  [D86](d86-service-accounts-for-the-record-and-process-apis.md).

## Alternatives

- **An endpoint as a kind of trigger.** An endpoint has an input schema, a grant and a response,
  which a trigger has not.
- **Store the response for a repeat.** Storing it after the wait would need a second write, which a
  crash can lose.
