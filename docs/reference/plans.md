# Plans

A plan is what a tenant of Axis SaaS pays its operator for: a price for the whole tenant and the quotas
it sets. This page covers the catalogue of plans, how a plan becomes the tenant's quotas, add-ons, and
changing a plan. The operator sets the prices from what the [cell load test](performance.md#cell-load-test)
and a running cell measure.

## The catalogue

Plans are settings of the regional [control plane](control-plane.md), `ControlPlane:Plans`, so a
price change needs no decision and no release.

```json
{
  "code": "team",
  "version": 2,
  "name": { "en": "Team", "vi": "Nhóm" },
  "includedUsers": 25,
  "quotas": {
    "storage": 21474836480,
    "applications": 10,
    "emailsPerDay": 2000,
    "packageCpuSecondsPerDay": 3600,
    "concurrentImports": 2,
    "concurrentConnectorCalls": 10,
    "concurrentQueries": 10,
    "workerWeight": 2,
    "authoringRequests": 200
  },
  "prices": [{ "currency": "VND", "month": 990000 }, { "currency": "USD", "month": 3900 }],
  "addOns": [
    { "code": "users-10", "users": 10, "prices": [{ "currency": "VND", "month": 290000 }, { "currency": "USD", "month": 1200 }] }
  ]
}
```

The numbers above show the shape only. They are not prices.

- **Shape.** A plan has a `code`, a `version`, a `name` in `en` and `vi`, `includedUsers`, the
  `quotas` of [tenant quotas](tenant-quotas.md#quotas) except `users`, `prices` and `addOns`.
- **One price for the tenant.** A price is for the whole tenant, with `includedUsers` in it, not a
  price for each user.
- **What a price covers.** An operator's lowest price for a plan should cover its share of a cell's fixed
  cost, counted with the cell 60% full of paying tenants, the cost of its quotas, the payment and invoice
  fees, time for support, and the operator's margin. Axis does not set the price.
- **Money in minor units.** Every amount is an integer in the currency's smallest unit: dong for
  `VND`, cents for `USD`. No amount is a decimal or a float.
- **A year costs ten months.** A plan gives only monthly prices. A yearly period costs 10 times the
  monthly price.
- **A price for each currency.** `VND` in `vn`, and `USD` for a customer that pays abroad. Axis
  never converts a price at an exchange rate.
- **Versions.** Changing a plan's quotas or prices adds a new version. A subscription pins the
  version it bought and keeps it until its plan changes, so a price rise never reaches a customer
  in the middle of what they agreed. A plan version that a subscription pins is never removed.
- **The start check.** The control plane checks `ControlPlane:Plans` at start and stops on a bad
  catalogue. It refuses a number that is not plain digits, such as a decimal price, a missing
  currency or quota key, a repeated code and version, a trial plan with a price, a trial plan with more than 10 included
  users, and a second trial plan code. The message
  names the plan code, version and setting, and never holds a value. The quota keys it requires are
  the platform's own list of quotas except `users`, so a new quota makes every catalogue invalid
  until its plans set it. An empty catalogue is valid.
- **The pin.** A subscription holds a plan code and version, a currency and a period (`month` or
  `year`). They are empty until a plan is bought, and a trial works without them. After the
  migrations the start reads every pinned version, and it stops when `ControlPlane:Plans` no longer
  lists one, naming each missing plan code and version.
- **The trial's plan.** A plan marked `"trial": true` has no price and no add-ons.
- **No AI requests** _(planned for M35)_. A plan sets no `authoringRequests`, because each author's own key
  pays for the author's requests, see [a person's own key](model-bindings.md#a-persons-own-key).
- **Package run time**. Each plan sets `packageCpuSecondsPerDay`, see
  [package run time a day](tenant-quotas.md#package-run-time-a-day). The start check refuses a trial
  plan whose value is not 0, so only a paid plan runs an application's packages.
- **The trial's quotas.** The trial plan sets a trial's quotas: those of a mid plan, with at most
  10 users. Every plan marked trial has one code, and the newest version of it, the highest version
  number, applies to a new trial. A trial pins no plan, so a new version reaches the next trial.
  [Provisioning](sign-up.md#provisioning) sends the quotas of that version, with `users` set to its
  included users. A region with no trial plan answers `503` to `POST /api/sign-up`.

## From a plan to the quotas

The control plane turns a subscription's plan and add-ons into the tenant's quotas on the cell.

- **The `writeQuotas` job.** One `PUT /api/cell/tenants/{id}/quotas` for a subscription sets every
  quota of its pinned plan version, with `users` and any override on top, see
  [quotas](tenant-quotas.md#quotas). The call names every quota, so repeating it writes the same
  rows, and it carries no idempotency key. A `deleting` or `deleted` subscription, and one that pins
  no plan, gets no call. A job that cannot reach the cell, or whose tenant has no directory row, fails
  and the job queue retries it.
- **A payment.** A payment that makes a subscription `active` queues `writeQuotas` in the same
  transaction, see [states](subscriptions.md#states).
- **The other events** _(planned for M34)_. When a subscription starts, changes its plan or adds or
  drops an add-on, the code that makes the move queues `writeQuotas` in the same transaction,
  through the one helper that every move uses.
- **Users.** `users` is `includedUsers` plus the users of every add-on pack the subscription holds. The
  packs are the rows of `control_plane.subscription_add_ons`: `subscription_id`, `add_on_code`,
  `quantity` and `created_at`, one row for each code. A code that the pinned plan version does not list
  fails the job, so that the tenant never loses users quietly.
- **Overrides.** An operator who sets a quota by hand writes an override with
  [`quota override`](control-plane.md#operators), which holds a reason and an end date. The rows of
  `control_plane.quota_overrides` are `subscription_id`, `quota`, `value`, `reason`, `until`,
  `operator` and `created_at`, one row for each subscription and quota, so a new override replaces the
  old one. The job writes the override instead of the plan's value until its end date, and an override
  that has ended drops out of the next write. A subscription that pins no plan takes no override.
- **One source.** The plan is the only source of a SaaS tenant's quotas, and the override is the only
  other. A quota set on the cell directly is put back at the next sync.
- **Sync.** Each night the recurring `syncQuotas` job queues one `writeQuotas` job for every
  subscription that pins a plan and is not `deleting` or `deleted`, so a lost call is corrected within
  a day.

## Add-ons

_(planned for M34)_

An add-on lets a business have more users without moving to the next plan.

- **User packs.** An add-on adds a fixed number of users, such as 10. A business with a few more
  people than its plan takes a pack instead of the next plan.
- **Adding and dropping.** The operator's billing system adds or drops a pack of a tenant through the
  [billing API](billing-api.md#setting-the-plan-and-add-ons). Dropping one
  that would leave more enabled users than the new number is refused, naming how many users to disable
  first.

## Changing a plan

_(planned for M34)_

The operator's billing system changes a tenant's plan through the
[billing API](billing-api.md#setting-the-plan-and-add-ons), and Axis refuses a smaller plan that the
tenant's use does not fit.

- **A larger plan** applies at once, and the control plane writes its quotas to the cell.
- **A smaller plan** is refused when the tenant's use is over one of the smaller plan's quotas, such as
  more users or storage. The refusal names each quota and the amount over.
