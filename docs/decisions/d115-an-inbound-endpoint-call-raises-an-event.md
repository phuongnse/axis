# D115. An inbound endpoint call raises an event

Status: **Agreed**

The owner agreed this on 2026-10-30. [D101](d101-the-extensibility-milestone-is-split-into-six.md) put
"raising an event from an inbound endpoint call" in M19 without a design. This is that design. It adds to the
`endpoint` resource of [D27](d27-inbound-endpoints.md). It is built. See
[endpoint resource](../reference/integration-resources.md#endpoint-resource) and
[the call](../reference/inbound-endpoints.md).

- **An optional `event`:**
  - An endpoint may name an `event`, with `fields` that map each payload field to an expression over the input.
    The expressions follow the rules of `values`: they see only the input fields, and call no rule or aggregate
    and follow no path.
  - The event is about the endpoint's `entity`. The checks are those of a raise event step, with the same codes,
    reported under `/event`.
- **`process` becomes optional:**
  - An endpoint names a `process`, an `event`, or both. One with neither is refused.
  - An endpoint without a `process` creates the record and raises the event, and the event's triggers start
    whatever work follows. It answers `201` as soon as it commits, and it may not declare `output` or `wait`,
    because there is no instance to wait for.
- **Raised in the call's transaction:**
  - After the call inserts the instance, or after it creates the record when there is no `process`, it raises the
    event for the new record, as a raise event step does. It stores the occurrence, runs the `sync` event
    triggers, queues the `event.deliver` work item and stages the event's [webhook](d92-outbound-webhooks.md)
    deliveries.
  - The occurrence names the instance the call started as the one that raised it, or no instance when the
    endpoint has no `process`, and the active release.
  - A webhook dead letter of an occurrence that no instance raised has no process. No process's dead letter routes
    list it, and the [operator console](d93-an-operator-web-console.md) shows it.
  - Any failure writes nothing, as for the rest of the call. A run-time error in an expression is a `400` keyed
    `/event/fields/<key>`.
  - A repeated call with the same `Idempotency-Key` answers the stored result and raises nothing.
- **Sensitive values:** an expression that reads an input that `values` maps to a field marked `sensitive` counts
  as reading that field. So the webhook rule of D92 applies: an event that has a webhook may not carry it.
- **Studio:** the endpoint's generic form shows the event's `fields` as typed rows, as it shows `values`, so the
  endpoint needs no JSON view.

_Why:_ another system's call often means "this happened" as much as "start this work". Other processes, and the
webhooks of D92, should hear about it without a raise event step at the top of every started process. Raising the
event in the call's transaction keeps one rule: an occurrence exists only if its record does. An endpoint that only
announces is common, and without it an author would write an empty process just to satisfy the resource.
