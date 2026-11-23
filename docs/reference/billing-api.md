# Billing API

The operator's billing system drives a tenant's commercial life through this API, as
[D168](../decisions/d168-billing-is-the-operators-own-system-and-axis-keeps-plans-as-quotas-the-clock-and-a-billing-api.md)
agrees. It sets the tenant's plan, its add-ons and the date the tenant is paid through. It ends a
subscription and reads the tenant's state and usage. Each region's control plane serves the API for
the tenants of its region. What the states and dates mean is on [subscriptions](subscriptions.md), and
what a plan is on [plans](plans.md).

## Signing in

The billing system signs in as a client of an identity provider that the operator names, and the
control plane checks each token it sends.
[D169](../decisions/d169-the-billing-client-signs-in-with-oauth-client-credentials-from-the-operators-identity-provider.md)
agrees this rule.

- **The client.** The billing system holds one client of that provider, and gets a token with the
  client credentials grant.
- **The checks.** The control plane accepts a bearer token only when its issuer and signature match the
  provider, it has not expired and its audience is the one the operator set. The client id must be the
  configured client, and the `scope` claim must hold `axis-billing`.
- **The settings.** `ControlPlane:Billing:Authority` is the provider's issuer URL, which must be `https`.
  `ControlPlane:Billing:Audience` is the audience that the token carries, and `ControlPlane:Billing:ClientId`
  is the client's id. Without these settings every billing route answers `401`.
- **No token, or a token that does not pass.** A missing, expired or wrongly signed token answers `401`
  with `WWW-Authenticate: Bearer`. So does a wrong issuer or audience, and a token of another client. The
  answer never says which check failed.
- **A token without the scope.** A valid token of the billing client that lacks `axis-billing` answers `403`.

A refused call changes nothing and writes no audit record.

## The version

Every route starts with `/api/billing/v1/`, and the version is part of the public contract. A change
that could break a caller does not change `v1`.

- **Within a version a change only adds.** A new optional field in a request body, or a new field in an
  answer, is the only change `v1` takes.
- **A caller ignores a field it does not know.** So a new field breaks no caller.
- **A breaking change is `v2`.** It is served beside `v1`, and `v1` keeps answering as it did.
- **An unknown version answers `404`.**

## Idempotency

Each call that changes a subscription can be retried safely, so a lost answer never makes it act twice.
The rule follows the one the [cell's provisioning API](tenant-lifecycle.md#provisioning-api) uses, and
the [control plane's calls to a cell](control-plane.md#reaching-the-cells).

- **The key.** Each `PUT` and `POST` needs an `Idempotency-Key` header of 1 to 255 characters. Without
  one, the call answers `400` and changes nothing. A `GET` takes no key.
- **The same request again.** The same key with the same request answers the first answer again, for 24
  hours. It changes nothing and writes no second audit record.
- **Another request with the same key.** It answers `422` and changes nothing.
- **What the key covers.** The route, the tenant and the body are hashed with SHA-256, so a retry must send
  the body byte for byte as before.
- **Parallel retries.** A lock on the key keeps two parallel retries from both acting.
- **Where the key comes from.** The billing system derives it from its own record, such as
  `paid-through:<its invoice id>`.

## Answers every call shares

Every call checks the same things in the same order: the sign-in, then the key, then the tenant, then the
body. The first check that fails gives the answer.

- **`401` and `403`** as [signing in](#signing-in) says.
- **`400`** for a missing `Idempotency-Key`, and for a body that does not parse or holds a malformed value.
  The answer is problem details keyed by the field.
- **`404`** for a tenant id that this region's control plane holds no subscription for. This also covers a
  tenant of another region, so the answer never shows which tenants exist elsewhere.
- **`422`** for a reused key with another request.
- **`200`** for a call that changes a subscription: the answer is the subscription as
  [reading a tenant](#reading-a-tenant) shows it, without `usage`. A caller ignores a field it does not
  know, so `usage` can be added to these answers later. A replay answers the same text. A refused call keeps
  nothing under its key, so a retry is checked again.

Each call names its own `409` and `422` cases below.

## Setting the plan and add-ons

This call sets the tenant's plan and its user packs at once. The packs are add-ons, and the plan and
packs decide the tenant's quotas, see [from a plan to the quotas](plans.md#from-a-plan-to-the-quotas).

```http
PUT /api/billing/v1/tenants/{tenantId}/subscription/plan
Authorization: Bearer <token>
Idempotency-Key: <key>
Content-Type: application/json

{ "code": "team", "version": 2, "addOns": [{ "code": "users-10", "quantity": 2 }] }
```

- **The whole list.** `addOns` is the whole list of packs the tenant keeps. An empty list drops every pack.
- **The answer.** `200` with the tenant, as [the shared answers](#answers-every-call-shares) say.
- **The move.** The call pins the plan version and the packs. It queues `writeQuotas` in the same
  transaction, through the one helper that every move uses.
- **At once, in any state.** The plan applies at once in every state except `deleting` and `deleted`, a
  `trialing` subscription included. The call leaves the state and the dates alone.
- **The same values again.** A call with the plan and packs the tenant already has is a new instruction. It
  writes the audit record and queues `writeQuotas` again.
- **`422`** for a plan code and version, or an add-on code, that `ControlPlane:Plans` does not list. It
  also answers `422` for a trial plan, for a quantity below 1 and for a repeated add-on code. The check
  comes after the replay check, so a retry still gets the first answer.
- **`409`** for a subscription that is `deleting` or `deleted`.
- **A plan the use does not fit.** The call answers `409` with the title "The tenant's use is over the plan's
  quotas." for a smaller plan, or fewer packs, that the tenant's use does not fit, and changes nothing. The
  check comes after the `422` checks and the `409` for a subscription that is `deleting` or `deleted`. It reads
  the tenant's usage from its cell as [reading a tenant](#reading-a-tenant) does, and compares `storage`,
  `users` and `applications`. `emailsPerDay` is a daily count that resets, so it is not compared. The new limits
  are the ones `writeQuotas` writes: the plan's quotas, `users` with the packs, and each override that has not
  ended. A quota is over when its new limit is below the use and also below the limit the cell holds now, so a
  tenant that is already over a quota the new plan raises or keeps is not refused. A `users` use of `null` is
  never over. The problem details hold a `quotas` list, and each item holds `quota`, the new `limit`, `used` and
  `over`, the amount over. The refused call keeps nothing under its key, so after the use goes down the same key
  is checked again, see [changing a plan](plans.md#changing-a-plan).
- **`502`** with the title "The tenant's cell could not be read." when the cell gives no usage, for the same
  reasons as [reading a tenant](#reading-a-tenant). Without the usage the call cannot tell whether the tenant
  fits, so it changes nothing.
- **The audit record.** `subscription.planSet`, see [audit records](#audit-records).

## Setting the paid-through date

This call sets the date that the tenant is paid through. The tenant is paid through the end of that day
in UTC.

- **The clock follows the date.** Past the date, the clock moves the subscription on, as
  [the clock](subscriptions.md#one-clock) says.

```http
PUT /api/billing/v1/tenants/{tenantId}/subscription/paid-through
Authorization: Bearer <token>
Idempotency-Key: <key>
Content-Type: application/json

{ "paidThrough": "2027-01-31" }
```

- **The answer.** `200` with the tenant, as [the shared answers](#answers-every-call-shares) say.
- **A subscription that becomes active.** A `trialing`, `pastDue`, `readOnly` or `cancelled` subscription
  becomes `active`. The move queues `writeQuotas`, and from `pastDue` or `readOnly` it also queues
  `resumeTenant`, as [states](subscriptions.md#states) says. An `active` subscription keeps its state and
  takes the new date.
- **The move.** The call sets the next date to the start of the day after the paid-through date in UTC.
  That is when the clock moves an `active` subscription to `pastDue`.
- **`422`** for a date before today in UTC. The check comes after the replay check, so a retry on the next day
  still gets the first answer.
- **`409`** for a subscription that pins no plan. The plan is set first, see [the plan call](#setting-the-plan-and-add-ons).
- **`409`** for a subscription that is `deleting` or `deleted`.
- **The audit record.** `subscription.active` when the state moves, otherwise `subscription.paidThroughSet`.

## Ending a subscription

This call ends a subscription at its paid-through date. The tenant keeps working until that date, then
follows the path of an unpaid tenant, as [states](subscriptions.md#states) says.

```http
POST /api/billing/v1/tenants/{tenantId}/subscription/end
Authorization: Bearer <token>
Idempotency-Key: <key>
```

The call has no body.

- **The move.** An `active` subscription becomes `cancelled`, with a new step id. It keeps its paid-through
  date and its next date, so the tenant works until the day after its paid-through date. Then the clock
  moves it to `readOnly`, as [the clock](subscriptions.md#one-clock) says.
- **The answer.** `200` with the tenant, as [the shared answers](#answers-every-call-shares) say.
- **Already cancelled.** A `cancelled` subscription answers `200` with the tenant and writes nothing. It
  keeps nothing under the key either, and a retry gets the same answer because the state does not change.
- **`409`** for any other state: `trialing`, `pastDue`, `readOnly`, `deleting` or `deleted`.
- **The audit record.** `subscription.cancelled`, once, when the move is made.

## Reading a tenant

This call reads the tenant's subscription and its usage. It changes nothing and writes no audit record.

```http
GET /api/billing/v1/tenants/{tenantId}/subscription
Authorization: Bearer <token>
```

The answer is `200` with the subscription, shaped like this example:

```json
{
  "tenantId": "3f2a6c1e-7b4d-4e0a-9d2f-5a1b8c9e0f12",
  "state": "active",
  "nextAt": "2027-02-01T00:00:00Z",
  "trialEndsAt": "2026-12-22T00:00:00Z",
  "paidThrough": "2027-01-31",
  "plan": { "code": "team", "version": 2 },
  "addOns": [{ "code": "users-10", "quantity": 2 }],
  "usage": {
    "storage": { "limit": 21474836480, "used": 1048576 },
    "users": { "limit": 27, "used": 12 },
    "applications": { "limit": 10, "used": 3 },
    "emailsPerDay": { "limit": 2000, "used": 40 }
  }
}
```

- **Empty fields.** A field that the subscription does not have is `null`. So `plan` is `null` for a
  subscription that pins no plan, and `nextAt` is `null` once it is `deleted`.
- **Usage.** `usage` is the `usage` of the cell's tenant read, which [the tenant routes](tenant-lifecycle.md#routes)
  describe and [usage](tenant-quotas.md#usage) explains. It holds `storage`, `users`, `applications` and
  `emailsPerDay`, each with its `limit` and its `used`. It is `null` for a `deleted` subscription.
- **Where the usage comes from.** The control plane finds the tenant's cell in the
  [region directory](region-directory.md#routing-a-host) and reads `GET /api/cell/tenants/{id}` on each call. It
  caches nothing. A `deleted` subscription makes no cell call.
- **`502`** with the title "The tenant's cell could not be read." when the cell gives no usage: the tenant has
  no row in the directory, the cell is not one of the region's, the cell does not know the tenant or sends no
  usage, or the call fails. A tenant that is not yet provisioned answers `502` until it is.

## Audit records

Each call that changes a subscription writes one audit record, as [the control plane's own work](control-plane.md#its-own-work)
says. The record is written in the same transaction as the change.

- **The record.** The actor kind is `billing`, the actor is the client id from the token, and the subject
  is the subscription id.
- **The actions.** Each call names its action in its own section.
- **No record for a replay, a refusal or a read.** A replay answers the first answer again, a refused call
  changes nothing, and a read writes none.

## Rate limits

_(planned for M34)_

Each billing client has a limit of its own, so a billing system in a loop cannot flood the control plane.

- **The partition.** A `billing` partition counts the calls, by the token's client id. Each replica counts in
  memory, with the token bucket of [how a limit counts](headers-and-rate-limits.md#how-a-limit-counts).
- **The limit.** 600 requests a minute for one replica. It is a code default and not a `ControlPlane:` setting,
  as the control plane's sign-up limits are.
- **Where it runs.** After the token check. A call with a bad token, or without the scope, still gets its `401`
  or `403`, never `429`, as [which partition counts a request](headers-and-rate-limits.md#which-partition-counts-a-request)
  says.
- **Over the limit.** The call answers `429` problem details titled "Too many requests.", with `Retry-After`, as
  [a refused request](headers-and-rate-limits.md#a-refused-request) says. It changes nothing, keeps nothing under
  its `Idempotency-Key` and writes no audit record. A retry after the wait is checked as a new call.
- **The metric.** `axis.rate_limits.refused` counts the refused calls, with `axis.partition` set to `billing`.

## Events

The control plane tells the operator's billing system what happens to each tenant, so the billing system can
start a customer, ask for a payment or charge for usage without polling, as
[D168](../decisions/d168-billing-is-the-operators-own-system-and-axis-keeps-plans-as-quotas-the-clock-and-a-billing-api.md)
agrees. There are three events: a tenant is created, a subscription changes state, and a tenant's usage of a day
is final.

- **The address.** The setting `ControlPlane:BillingEvents:Url` is an absolute `https` URL. Without it, no event
  is written and the control plane starts normally.
- **The secret.** The setting `ControlPlane:BillingEvents:Secret` comes only from the file
  `/run/secrets/ControlPlane__BillingEvents__Secret`. It may hold two secrets, separated by a space, so the
  operator can rotate it.
- **The body.** A `POST` with a JSON body `{ type, id, timestamp, apiVersion, data }`, shaped as in
  [webhook delivery](outbound-webhooks.md#webhook-delivery). `id` is a UUID made when the event is written.
  `timestamp` is the time of the change, in UTC, and is the same on every attempt. `apiVersion` is `"v1"`.
- **The signature.** The Standard Webhooks headers `webhook-id`, `webhook-timestamp` and `webhook-signature`,
  made as [webhook delivery](outbound-webhooks.md#webhook-delivery) says. Here `webhook-id` is the event's `id`
  alone.
- **Written with the change.** Each event is a job on the control plane's queue. It is queued in the
  transaction of the change that causes it, so no change is left without its event and no event without its
  change.
- **Delivery.** `https` only, no redirect is followed, and an attempt times out after 10 seconds. A `2xx`
  delivers. Anything else is retried with the queue's backoff and attempt limit, see
  [its own work](control-plane.md#its-own-work). The last failure ends the job `failed`, and
  `axis.controlplane.jobs.failed` counts it. The billing system then catches up by
  [reading a tenant](#reading-a-tenant).
- **The same id on every attempt.** Every attempt sends the same `id` and body, so a receiver drops a copy by
  `webhook-id`. Events arrive in no fixed order, and `timestamp` lets a receiver keep the newest.
- **Within `v1`.** A new event type or a new field is not breaking, and a receiver ignores what it does not
  know, as [the version](#the-version) says.
- **Nothing secret.** An event holds no password, token or secret, and no body is logged.

### Tenant created

`tenant.created` is written when a sign-up is confirmed and its trialing subscription is made. `tenantId` is the
id that the billing routes take, which is the tenant's subdomain, and `host` is that subdomain on the region's
tenant domain. The business name and the administrator's name, email and language are read from the sign-up when
the event is sent, and every attempt sends the same ones.

```json
{
  "type": "tenant.created",
  "id": "5b0e8d52-1c7a-4f3e-9a64-2d8f7e61b0c3",
  "timestamp": "2026-12-01T09:15:00Z",
  "apiVersion": "v1",
  "data": {
    "tenantId": "3f2a6c1e-7b4d-4e0a-9d2f-5a1b8c9e0f12",
    "region": "vn",
    "host": "acme.vn.example.com",
    "businessName": "Acme Trading",
    "administrator": { "name": "An Nguyen", "email": "an@acme.example", "language": "vi" },
    "trialEndsAt": "2026-12-31T09:15:00Z"
  }
}
```

### Subscription state changed

`subscription.stateChanged` is written with every move that writes a `subscription.<state>` audit record,
see [audit records](#audit-records). A plan call, or a paid-through call that leaves the state as it was,
writes none. `paidThrough` and `nextAt` are as the move leaves them, and `null` when the subscription has none,
as a trial has no paid-through date and a deleted subscription no next date.

```json
{
  "type": "subscription.stateChanged",
  "id": "c47a19e0-83d2-4b6f-8e15-0a9d3f2b7c64",
  "timestamp": "2027-02-01T00:00:00Z",
  "apiVersion": "v1",
  "data": {
    "tenantId": "3f2a6c1e-7b4d-4e0a-9d2f-5a1b8c9e0f12",
    "state": "pastDue",
    "previousState": "active",
    "paidThrough": "2027-01-31",
    "nextAt": "2027-02-15T00:00:00Z"
  }
}
```

### Daily usage

`usage.daily` is written once for each tenant that is not `deleted` and each finished UTC day. It reads the
row from the cell's [usage days](tenant-lifecycle.md#routes) route, and [usage](tenant-quotas.md#usage)
explains its columns.

- **When.** The control plane looks every hour. From 02:00 UTC it writes yesterday's event, because a day's row is
  final only after the cell's first hourly sample of the next day. A day that was missed is not written later.
- **The timestamp.** `timestamp` is the end of the day, which is midnight UTC of the next day.
- **Once.** The control plane records each subscription's day in the transaction that writes the event, so a second
  run, a retry or another replica writes none. A record is kept for 30 days.
- **No row.** A tenant that has no row in the region directory, a cell that does not know it and a cell that has
  no row for the day give no event that hour. The next hour asks again.
- **No count.** `users` and `applications` are `null` when the cell's row holds none, never `0`.
- **A cell that cannot be read.** A failed call, a bad answer or a cell that is not one of the region's skips that
  tenant. The other tenants still get their events, and the job then fails, so the queue retries it and writes
  only the events that are still missing.

```json
{
  "type": "usage.daily",
  "id": "e91c04b7-5a3d-4d28-b6f0-7c2a8d19e345",
  "timestamp": "2027-01-16T00:00:00Z",
  "apiVersion": "v1",
  "data": {
    "tenantId": "3f2a6c1e-7b4d-4e0a-9d2f-5a1b8c9e0f12",
    "day": "2027-01-15",
    "databaseBytes": 52428800,
    "fileBytes": 1048576,
    "users": 12,
    "applications": 3,
    "emailsSent": 40,
    "connectorCalls": 120,
    "workItemsRun": 310
  }
}
```

## Connectors

_(planned for M34)_

Anyone may write a connector between the billing API and a billing system, as a service of its own and under
any licence, as
[D168](../decisions/d168-billing-is-the-operators-own-system-and-axis-keeps-plans-as-quotas-the-clock-and-a-billing-api.md)
agrees. A connector uses only the calls and events on this page.
