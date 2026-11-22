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

_(planned for M34)_

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
- **`422`** for a plan code and version, or an add-on code, that `ControlPlane:Plans` does not list. It
  also answers `422` for a trial plan, and for a quantity below 1.
- **`409`** for a subscription that is `deleting` or `deleted`.
- **`409`** for a smaller plan, or fewer packs, that the tenant's use does not fit. The answer names each
  quota and the amount over, see [changing a plan](plans.md#changing-a-plan).
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

_(planned for M34)_

This call ends a subscription at its paid-through date. The tenant keeps working until that date, then
follows the path of an unpaid tenant, as [states](subscriptions.md#states) says.

```http
POST /api/billing/v1/tenants/{tenantId}/subscription/end
Authorization: Bearer <token>
Idempotency-Key: <key>
```

The call has no body.

- **The move.** An `active` subscription becomes `cancelled`.
- **The answer.** `200` with the tenant, as [the shared answers](#answers-every-call-shares) say.
- **Already cancelled.** A `cancelled` subscription answers `200` with the tenant and writes nothing.
- **`409`** for any other state: `trialing`, `pastDue`, `readOnly`, `deleting` or `deleted`.
- **The audit record.** `subscription.cancelled`, once, when the move is made.

## Reading a tenant

_(planned for M34)_

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
- **`502`** when the cell cannot be read.

## Audit records

Each call that changes a subscription writes one audit record, as [the control plane's own work](control-plane.md#its-own-work)
says. The record is written in the same transaction as the change.

- **The record.** The actor kind is `billing`, the actor is the client id from the token, and the subject
  is the subscription id.
- **The actions.** Each call names its action in its own section.
- **No record for a replay, a refusal or a read.** A replay answers the first answer again, a refused call
  changes nothing, and a read writes none.
