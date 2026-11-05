# D92. Outbound webhooks

Status: **Agreed**

## Context

Telling another system that an event of [D26](d26-events-and-triggers.md) happened is common, and an
author should not have to write a process with a connector call for it.

## Decision

- **A `webhook` resource** names an event, a `url` binding and a `secret` binding. It is part of the
  release: no API lets another system subscribe itself, and a module holds none.
- **Only events raise webhooks.**
- **Delivery is staged with the event** in its transaction and sent later by the worker.
- **Standard Webhooks.** A signed JSON `POST` with the same id on every attempt, delivered at least
  once, in no fixed order, under the rules of a connector call.
- **Failures become dead letters** of [D72](d72-a-delivery-limit-and-dead-letters.md).
- **Two secrets** may be in force during a rotation.
- **An event that has a webhook carries no `sensitive` value.**

The detail is in [outbound webhooks](../reference/outbound-webhooks.md#outbound-webhooks).

## Consequences

- Receivers check signatures with the Standard Webhooks tools they already have, and must accept
  repeats.
- Every failed delivery is seen and fixed in one way.
- A new subscriber needs an author and a release.
