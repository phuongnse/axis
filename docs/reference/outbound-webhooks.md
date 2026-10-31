# Outbound webhooks

Detailed reference for the `webhook` resource and the delivery of an event to it, moved out of
[integration-resources.md](integration-resources.md), which holds the connectors, events, triggers and
inbound endpoints. How a process raises an event is in [process-events.md](process-events.md#raise-event-step).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Outbound webhooks

A webhook tells another system that an event happened, as
[D92](../decisions/d92-outbound-webhooks.md) agrees. The author configures it
in the application, so it is part of the release. There is no API through
which another system subscribes itself. The webhook resource, its compile
checks, the activation check of its bindings, the delivery, the rotation by two
secrets and the dead letter of a delivery that keeps failing are built. The
console view is _(planned for M23)_.

### Webhook resource

A webhook that tells a chat system when a note is closed, in
`webhooks/note-closed-chat.json`:

```json
{
  "id": "5b0f3c1e-8a52-4d0e-9a57-0c1f7e2d6b44",
  "kind": "webhook",
  "name": "NoteClosedChat",
  "formatVersion": 1,
  "event": "NoteClosed",
  "url": { "binding": "ChatWebhookUrl" },
  "secret": { "binding": "ChatWebhookSecret" },
  "timeout": "PT10S"
}
```

- `event` names an event of the application, or `<module id>/<name>` of a
  module the application declares. Only events raise webhooks. A record change
  raises one when a process raises an event for it.
- `url` is a `url` binding and `secret` is a `secret` binding, checked at
  activation as a connector's are: a missing binding or one of the wrong kind
  is `AXC0100` at `/url/binding` or `/secret/binding`, and an `http` URL
  outside Development is `AXC0101`. On a
  [preview tenant](host-settings.md#preview-tenants) a missing binding is a
  warning, and the delivery fails at run time.
- `timeout` is an ISO 8601 duration. It defaults to `PT10S` and is at most
  `PT30S`.
- An unknown event is `AXC0313` at `/event`. A missing `url` or `secret` is
  `AXC0314` at `/url` or `/secret`. A `timeout` that is not a positive ISO 8601
  duration of at most `PT30S` is `AXC0315` at `/timeout`. The schema leaves
  `url` and `secret` optional, so a missing one has this code and not the
  schema error `AXC0004`.
- An event that has a webhook may not carry a value read from a field marked
  `sensitive`. A [raise event step](process-events.md#raise-event-step) that
  maps one into its payload, directly or through a reference path, is `AXC0316`
  at `/steps/{i}/fields/{key}`. A step that maps it into an event that no
  webhook names compiles. An
  [endpoint's event](integration-resources.md#endpoint-resource) that reads an
  input that `values` maps to a sensitive field is `AXC0316` at
  `/event/fields/{key}`.
- A module holds no webhook. A file of kind `webhook` in a module is
  `AXC0202` at `/kind`.

### Webhook delivery

The delivery is built, apart from the part marked _(planned for M23)_.

- **Staged with the event.** Raising the event stages one delivery for each
  webhook of the event, in the same transaction, as a row in
  `axis.webhook_deliveries` and a `webhook.deliver`
  [work item](process-events.md#webhook-delivery). The worker sends it later,
  outside any transaction. The webhooks are those of the release the raising
  instance is pinned to, which compiled the step. A step that rolls back stages
  nothing.
- **Body.** A `POST` with a JSON body:

  ```json
  {
    "type": "NoteClosed",
    "id": "0d6f…",
    "timestamp": "2026-11-02T09:15:00Z",
    "data": {
      "entity": "Note",
      "subjectId": "8c1e…",
      "fields": { "priority": 2, "amount": 120.5 }
    }
  }
  ```

  `id` is the event occurrence id. `timestamp` is the time the occurrence was
  raised, in UTC, and is the same on every attempt. `fields` holds the event's
  payload fields. The delivery stores no body, so each attempt builds the same
  body again from the stored occurrence.

- **Headers.** The Standard Webhooks headers:
  - `webhook-id`, `<occurrence id>_<webhook id>`, both as hyphenated UUIDs, the
    same on every attempt, so a receiver can drop duplicates;
  - `webhook-timestamp`, the attempt's time in Unix seconds;
  - `webhook-signature`, `v1,` and the base64 HMAC-SHA256 of
    `<webhook-id>.<webhook-timestamp>.<body>` with the secret. A secret that
    starts with `whsec_` is the key after base64-decoding the rest, as the
    official Standard Webhooks libraries expect. Any other secret is used as its
    UTF-8 bytes.
- **Two secrets.** The `secret` binding may hold two secrets, separated by a
  space. Each request then carries both signatures, separated by a space, so a
  receiver can move to the new secret before the old one is removed.
- **Outcomes.** A `2xx` delivers. Anything else, a `3xx`, a timeout or a
  network error is retried with the
  [event delivery backoff](process-events.md#webhook-delivery). At
  `Worker:DeliveryAttempts` failures the delivery stops and becomes a
  [dead letter](process-events.md#dead-letters) of the kind `webhook.deliver`
  that an operator redelivers or discards. A redeliver keeps the same
  `webhook-id`. The other webhooks of the occurrence still deliver.
- **Rules of a connector call.** `https` only outside Development, and no
  redirects: a `3xx` fails the attempt. The request is cancelled after the
  webhook's `timeout`.
- **At least once.** A delivery can arrive more than once, and deliveries
  arrive in no fixed order.
- **Audit.** Each delivered request writes an audit record
  `webhook.delivered`, with actor `system`, the webhook, the event occurrence
  and the status code. No body is stored after delivery, and none appears in
  history, audit records or logs. The record names the application, the event's
  entity, the subject record and the raising instance, so it shows in the
  record's history.
- **Test scenarios.** A [scenario](test-scenarios.md) tenant sends no webhook.
  Its deliveries stay queued.
- **Seen in the console.** _(planned for M23)_ The
  [operator console](operator-console.md#views) lists recent deliveries and
  dead letters.
