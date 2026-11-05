# D115. An inbound endpoint call raises an event

Status: **Agreed**

## Context

Another system's call to an endpoint of [D27](d27-inbound-endpoints.md) often means "this happened" as
much as "start this work". Other processes and the [webhooks](d92-outbound-webhooks.md) should hear
about it without a raise event step at the top of every started process, and an endpoint that only
announces should need no empty process.

## Decision

- **An endpoint may name an `event`,** mapping its payload from the input under the rules of `values`.
- **`process` is optional:** an endpoint names a `process`, an `event` or both.
- **The event is raised in the call's transaction,** as a raise event step raises it, and a repeated
  idempotency key raises nothing.
- **An input mapped to a `sensitive` field counts as that field,** so a webhook never carries it.

The detail is in the [endpoint resource](../reference/integration-resources.md#endpoint-resource) and
[the call](../reference/inbound-endpoints.md#the-call).

## Consequences

- An occurrence exists only if its record does.
- An endpoint without a `process` answers when it commits, with no `output` or `wait`.
- A webhook dead letter of such a call belongs to no process, so only the
  [operator console](d93-an-operator-web-console.md) lists it.
