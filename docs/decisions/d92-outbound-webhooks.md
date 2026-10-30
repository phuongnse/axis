# D92. Outbound webhooks

Status: **Agreed**

The owner agreed this on 2026-10-25. M19 lets an application tell another
system that an event happened, without a process step that calls a connector.
It builds on the events of [D26](d26-events-and-triggers.md), the bindings of
[D25](d25-connectors-deployment-bindings-and-secrets.md), and the event
delivery limit and dead letters that M14 adds. The resource, its compile
checks and the activation check of its bindings are built. Delivery, rotation
of the secret and the console view are _(planned for M19)_. See
[outbound webhooks](../reference/integration-resources.md#outbound-webhooks).

- **A `webhook` resource:**
  - It names an `event` of the application, or of a module the application
    declares, a `url` binding and a `secret` binding for signing.
  - The author configures it in the application, so it is part of the
    release. There is no API through which another system subscribes itself.
  - Only events raise webhooks. A record change raises one when a process
    raises an event for it.
  - A module holds no webhook.
  - An event that has a webhook may not carry a value read from a field
    marked `sensitive`. A raise event step that maps one into its payload is a
    new diagnostic.
- **Delivery:**
  - Raising the event stages one delivery for each webhook of that event, in
    the same transaction. The worker sends it later, outside any transaction.
  - The request is a `POST` with a JSON body: the event name, the occurrence
    id, the time, the subject's entity and id, and the payload fields.
  - It carries the Standard Webhooks headers `webhook-id`,
    `webhook-timestamp` and `webhook-signature`. `webhook-id` is the same on
    every attempt, so a receiver can drop duplicates. The signature is an
    HMAC-SHA256 with the `secret` binding.
  - Delivery is at least once. Order is not guaranteed.
  - `https` only outside Development, no redirects, and a timeout of 10
    seconds by default and at most 30.
  - A `2xx` delivers. Anything else is retried with backoff, up to the event
    delivery limit, and then becomes a dead letter. An operator redelivers or
    discards it, as for any dead letter.
- **Rotating the secret:** the binding may hold two secrets, separated by a
  space. Each request then carries both signatures, so a receiver can switch
  to the new secret before the old one is removed.
- **Audit:** each delivered request writes an audit record
  `webhook.delivered`, with the webhook, the event occurrence and the status
  code. It never holds the body.
- **Activation:** a missing or wrong binding fails activation, as for a
  connector. On a preview tenant a missing binding is a warning, and the
  delivery fails at run time, so a preview never calls a real system by
  accident.

_Why:_ telling another system about an event is common enough that an author
should not have to write a process with a connector call for it. The Standard
Webhooks headers are what receivers already know how to check. Sharing M14's
delivery limit and dead letters gives one way to see and fix every delivery
that failed.
