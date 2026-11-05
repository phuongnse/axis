# D72. A delivery limit and dead letters

Status: **Agreed**

## Context

The worker delivers events, fans out schedules and sends emails. A delivery that fails for ever must
not block the next ones or use the worker without end, and what still fails must not be lost or
retried blindly, as [execution](../domain/knowledge.md#execution) warns.

## Decision

- **Failures are counted per target,** in a transaction apart from the failed one, and the other
  targets of the same item are still delivered.
- **Backoff, then a limit.** After `Worker:DeliveryAttempts` failures the target becomes a dead letter,
  whose last error is a fixed text with no value. One table holds every kind.
- **An operator redelivers or discards it,** with a reason and an audit record, under the process's
  `inspect` and `retry` grants. The process inspector widget lists them.

The detail is in [dead letters](../reference/process-events.md#dead-letters).

## Consequences

- Short outages heal within the backoff, and a long one leaves dead letters for a person, so nothing
  is lost.
- Webhooks ([D92](d92-outbound-webhooks.md)) and emails ([D73](d73-in-app-and-email-notifications.md))
  share the table and its commands, so the [operator console](d93-an-operator-web-console.md) has one
  place to look.
