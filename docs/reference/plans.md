# Plans

Detailed reference for the plans of Axis SaaS: what a tenant pays for, how a plan sets its quotas,
add-ons, and changing a plan. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D127](../decisions/d127-plans-payments-and-invoices.md). Everything in this file is _(planned for
M34)_, except the trial's plan, _(planned for M33)_. The prices themselves are set when M34 is broken
down, from what the cell load test and the first cell measure, as D118 agrees. The conventions of
[architecture.md](../architecture.md) apply.

## The catalogue

Plans are settings of the regional [control plane](control-plane.md), `ControlPlane:Plans`, so a
price change needs no decision and no release, as D118 agrees.

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
  price for each user, as D118 agrees.
- **Money in minor units.** Every amount is an integer in the currency's smallest unit: dong for
  `VND`, cents for `USD`. No amount is a decimal or a float.
- **A year costs ten months.** A plan gives only monthly prices. A yearly period costs 10 times the
  monthly price, as D118 agrees.
- **A price for each currency.** `VND` in `vn`, and `USD` for a customer that pays abroad. Axis
  never converts a price at an exchange rate.
- **Versions.** Changing a plan's quotas or prices adds a new version. A subscription pins the
  version it bought and keeps it until its plan changes, so a price rise never reaches a customer
  in the middle of what they agreed. A plan version that a subscription pins is never removed.
- **The trial's plan.** A plan marked `"trial": true` sets a trial's quotas: those of a mid plan, with
  at most 10 users, as D118 agrees. It has no price.

## From a plan to the quotas

- **The control plane writes them.** When a subscription starts, changes its plan, adds or drops an
  add-on, or becomes `active` again, the control plane writes the tenant's quota rows through
  `PUT /api/cell/tenants/{id}/quotas`, see [quotas](tenant-quotas.md#quotas).
- **Users.** `users` is `includedUsers` plus the users of every add-on.
- **One source.** The plan is the only source of a SaaS tenant's quotas. An operator who sets a quota
  by hand writes an override in the control plane, with a reason and an end date, and the control
  plane writes it to the cell. A quota set on the cell directly is put back at the next sync.
- **Sync.** Each night the control plane writes every subscription's quotas again, so a lost call is
  corrected within a day.

## Add-ons

- **User packs.** An add-on adds a fixed number of users, such as 10, at the add-on's price. A
  business with a few more people than its plan buys a pack instead of the next plan.
- **Buying.** The owner or a billing contact adds a pack in the [billing portal](billing-portal.md).
  It applies at once, and its price for the rest of the period is charged at once, prorated.
- **Dropping.** A pack is dropped at the period's end. Dropping one that would leave more enabled
  users than the new number is refused, naming how many users to disable first.

## Changing a plan

- **Upgrade at once.** A larger plan applies at once. The unused part of the current period is
  credited, and the new plan's price for the rest of the period is charged, both by the days left.
  The charge is invoiced at once, see [payments](payments.md).
- **Downgrade at the period's end.** A smaller plan applies when the period ends. It is refused when
  the tenant's use is over one of the smaller plan's quotas, such as more users or storage, and the
  refusal names each quota and the amount over. At the period's end the check runs again. If the use
  is over then, the change waits one more period, and the owner gets an email.
- **Switching period.** Monthly to yearly applies at once, as an upgrade. Yearly to monthly applies
  at the end of the year.
- **Proration.** By whole days, rounded down in the customer's favour, in minor units.
