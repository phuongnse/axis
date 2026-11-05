# D127. Plans, payments and invoices

Status: **Agreed**

## Context

[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) gives each tenant a plan with one
price for the whole tenant, and payments and invoices run by the
[control plane](d126-sign-up-trials-and-the-control-plane.md). A price change must never surprise a
customer, a bank transfer cannot be pulled, webhooks get lost and repeat, and the person who pays is
often not a user of the application.

## Decision

- **A subscription pins its [plan](../reference/plans.md)'s version.** An upgrade applies at once,
  prorated, and a downgrade at the period's end. A tenant that needs a few more users buys a user pack
  instead of the next plan.
- **A [billing portal](../reference/billing-portal.md) of its own,** with billing accounts of its own,
  runs on the control plane.
- **Payments never double or vanish.** Signed webhooks go through an inbox that drops repeats and are
  applied in order, a daily reconciliation catches a lost one, and money is an append-only ledger in
  integer minor units. In Vietnam each invoice is paid by bank transfer through PayOS, and abroad Paddle
  is the merchant of record. See [payments](../reference/payments.md).
- **An [e-invoice](../reference/e-invoices.md) is issued only once a payment is confirmed.**
- **A tenant past its payment grace is read-only, not suspended,** as an ended trial is, see
  [subscriptions](../reference/subscriptions.md#the-timeline).

## Consequences

- These are how Stripe Billing and Paddle work, and the standard way to keep money right, so a
  customer meets no surprise.
- User packs keep one price for the tenant without making a business of twelve jump to a much larger
  plan.
- Payment data stays out of the cells, and a business whose tenant is read-only can still pay.
- An e-invoice never needs to be cancelled for an unpaid bill.
- The providers' webhooks, the e-invoice provider and the portal are new trust boundaries for the
  [threat model](../reference/threat-model.md).
- There are two payment integrations, an e-invoice adapter, a portal and a nightly reconciliation to
  watch.
- Taking a payment needs a company that signs with the providers, and another merchant of record if
  Paddle does not accept a seller based in Vietnam.
