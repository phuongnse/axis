# D25. Connectors, deployment bindings and secrets

Status: **Agreed**

## Context

Processes must call external systems, such as the purchasing system that creates a purchase order.
A call can succeed while its response is lost, so a blind retry can create a second order, and
[D11](d11-durable-process-engine.md) keeps every external call out of a transaction. The URLs and
secrets of a system also differ per tenant and environment, while one release must run in all of
them.

## Decision

- **A release names its URLs and secrets as deployment bindings** and never holds their values. The
  values live per tenant in the host configuration or the
  [tenant directory](d42-tenant-directory-in-the-platform-database.md), and a host reads a binding
  when it makes a call. Activation fails when a binding the release uses is missing or of the wrong
  kind.
- **Secret values come from the environment,** except in Development, and never appear in a
  release, history, audit record, log or problem details.
- **A `connector` resource describes a system:** its base URL, its authentication, its timeout and
  typed operations. An operation may name the system's idempotency header and a lookup that finds an
  earlier call by its key. [D91](d91-connector-oauth2-and-structured-payloads.md) adds OAuth2 and
  structured payloads.
- **Calls go only to `https`** outside Development, and never follow a redirect, which could send a
  secret to another host.
- **A call is staged.** The step's transaction stores the call with a stable operation identity, and
  the instance waits. The worker sends the call later, with the identity as the idempotency key of
  every attempt and lookup, and sends the input stored with the call.
- **Each outcome is failed, retryable or unknown.** A step's retry policy sets the attempts and a
  doubling backoff, and the worker ignores `Retry-After`, so backoff is decided in one place.
- **An unknown outcome is never retried blindly.** It is sent again only under the system's
  idempotency key, else reconciled through the lookup, else it stays visible until an operator
  resolves it ([D35](d35-operator-retry-cancel-and-resolve.md)).
- **A settled call writes one audit record,** and no request or response body is stored.

The detail is in
[connectors and deployment bindings](../reference/integration-resources.md#connectors-and-deployment-bindings)
and [connector calls](../reference/process-execution.md#connector-calls).

## Consequences

- The approved purchase request creates exactly one purchase order, even after a lost response or a
  crash.
- One release runs in every tenant and environment, and changing a secret needs no new release.
- A connector without an idempotency header depends on its lookup being correct, and one without
  either needs an operator for every unknown outcome.
- The server and the worker must both be able to read each tenant's binding values.

## Alternatives

- **URLs and secrets in the release.** A release would then differ per tenant and environment, and
  carry secrets.
