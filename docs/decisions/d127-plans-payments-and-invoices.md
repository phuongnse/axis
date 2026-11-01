# D127. Plans, payments and invoices

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets the mechanics of M34 Plans and billing that
[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) left open, on the control plane of
[D126](d126-sign-up-trials-and-the-control-plane.md), so that M34 can be broken into issues. It adds
to D118 and changes one point: a tenant past its payment grace is read-only, not suspended.
Everything here is _(planned for M34)_. [Plans](../reference/plans.md),
[payments](../reference/payments.md), [e-invoices](../reference/e-invoices.md),
[billing portal](../reference/billing-portal.md) and [subscriptions](../reference/subscriptions.md)
hold the detail.

- **Versioned plans:**
  - A plan has quotas, a number of included users, and a monthly price in each currency, `VND` and
    `USD`, with no exchange rate. A year costs ten months, as D118 agrees.
  - A subscription pins the plan's version, so a price change never reaches a customer mid-term.
  - The control plane writes the plan's quotas to the cell through the provisioning API, and writes
    them again each night.
- **Changing a plan:**
  - An upgrade applies at once, prorated by days.
  - A downgrade applies at the period's end. It is refused while the tenant's use is over the smaller
    plan's quotas.
- **User packs:** a tenant that needs a few more users buys a pack of a fixed size instead of the next
  plan. It applies at once, prorated, and is dropped at the period's end.
- **A billing portal of its own:**
  - It runs at `account.<region domain>` on the control plane, with billing accounts in a realm of
    its own, verified by email.
  - The owner and the billing contacts are linked to the tenant.
  - It works while the tenant is read-only, and the application links to it.
- **Payments that never double or vanish:**
  - Webhooks are checked by signature before they are parsed. They go into an inbox that drops
    repeats, and are applied in order through the subscription's state machine.
  - A daily reconciliation with each provider records what a lost webhook missed.
  - Money is integer minor units in an append-only ledger.
  - In Vietnam, each invoice gets a PayOS payment link with VietQR. Abroad, Paddle is the merchant of
    record and runs its own card retries.
- **E-invoices on payment:**
  - A payment in Vietnam gets its e-invoice once it is confirmed, through an adapter to the
    provider that Axis's accountant uses.
  - Paddle's invoice serves payments abroad.
- **Dunning:**
  - Reminders go out 7 days before the due date, 1 day before, and on the day.
  - 14 days of grace follow, with the tenant working and a banner.
  - Then the tenant is read-only. After 90 days read-only, its deletion is scheduled, followed by the
    cell's 7-day grace.
- **New trust boundaries:** the payment providers' webhooks, the e-invoice provider and the billing
  portal. M34's breakdown has an issue that adds them to the
  [threat model](../reference/threat-model.md), as
  [D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md) says.
- **Outside the repository, before M34 takes a payment:**
  - the prices, and the plans' names and tiers, set from the cell's measured cost as D118 agrees;
  - the company that signs with the providers;
  - PayOS's contract;
  - whether Paddle accepts a seller based in Vietnam, and another merchant of record if it does not;
  - the e-invoice provider, the VAT rate and the invoice series, from Axis's accountant;
  - the texts of the terms of service and the data processing agreement, from a lawyer.

_Why:_ pinned plan versions, prorated upgrades and downgrades at the period's end are how Stripe
Billing and Paddle work, and they never surprise a customer. User packs keep D118's price for the
whole tenant without making a business of twelve jump to a much larger plan. A portal on the control
plane keeps payment data out of the cells and still serves a business whose tenant is read-only. A
person who pays is often not a user of the application, so the portal has accounts of its own. Bank
transfers cannot be pulled, so in Vietnam each period is an invoice that a person pays. Webhooks get
lost and repeat, so an inbox, an ordered state machine, a reconciliation and an append-only ledger
are the standard way to keep money right. It is the same pattern as Axis's connector idempotency. An
e-invoice issued only after payment never needs to be cancelled for an unpaid bill. The cost is two
payment integrations, an e-invoice adapter, a portal, and a nightly reconciliation to watch.
