# D72. A delivery limit and dead letters

Status: **Agreed**

M14 puts a limit on how often the worker tries a delivery again, and keeps
what still fails as a dead letter that an operator can see and act on. The
owner agreed this on 2026-10-25. It is built in M14, except the dead letter of
one instance in a wait for event step, which is _(planned for M18)_. See
[dead letters](../reference/process-events.md#dead-letters). It ends the "no
limit on these retries yet" of event delivery.

- **What it covers:** three kinds of delivery.
  - `event.deliver`: one occurrence to one `async` event trigger, or to one
    instance in a wait for event step
    ([D69](d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md)).
  - `trigger.fanOut`: one occurrence of a schedule trigger.
  - `notification.email`: one email ([D73](d73-in-app-and-email-notifications.md)).
- **Counted per target:** a failure is counted for the occurrence and its
  trigger or waiting instance, or for the fan-out item, or for the email. A
  separate transaction counts it after the failed one rolls back, as a failed
  step is recorded today.
- **Backoff and limit:** each attempt waits longer: 30 seconds, doubled each
  time, at most one hour. After `Worker:DeliveryAttempts` failures, default 5,
  the worker writes a dead letter and carries on with the rest. The other
  triggers of the occurrence still get it.
- **Dead letter:** one row in `axis.dead_letters`, generic by `kind`, so M19's
  outbound webhooks can reuse it. It names what failed, the attempts, the last
  error as a fixed text with no value, and a state: `open`, `redelivered` or
  `discarded`.
- **Operator commands:** `redeliver` and `discard`, each with a required reason.
  - Redeliver queues the delivery again with a fresh count. Discard closes it.
  - Each writes an audit record, `deadLetter.redelivered` or
    `deadLetter.discarded`.
  - Listing dead letters needs the `inspect` grant on the process the delivery
    is for. Redeliver and discard need its `retry` grant.
- **Seen by operators:** the process inspector widget lists the open dead
  letters of its process. The worker logs a warning for each dead letter and
  counts them in a metric.

_Why:_ see [execution](../domain/knowledge.md#execution). A delivery that fails
for ever must not block the next ones or use the worker without end. A fixed
limit with backoff covers short outages, and a dead letter keeps what is left
for a person, so nothing is lost and nothing is retried blindly. One table for
every kind gives operators, and later the operator console, one place to look.
