# Payments

Detailed reference for how a tenant of Axis SaaS pays: invoices, bank transfers through VietQR,
payments abroad through a merchant of record, provider webhooks, the daily reconciliation and the
billing ledger. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D127](../decisions/d127-plans-payments-and-invoices.md). Everything in this file is _(planned for
M34)_. No payment is taken before the company, the providers' contracts and the accountant are in
place, as D118 says. The conventions of [architecture.md](../architecture.md) apply.

## Invoices

- **A billing invoice for each period.** The [control plane](control-plane.md) creates the invoice
  of a period 7 days before it starts, with its lines: the plan, each add-on, and any prorated charge
  or credit. Its due date is the period's first day. A plan change or an add-on makes an invoice at
  once, due in 7 days.
- **Not the e-invoice.** This is Axis's own record of what is owed. The legal invoice of a payment in
  Vietnam is the [e-invoice](e-invoices.md), issued once it is paid.
- **One currency.** A subscription's currency is set by its first payment: `VND` when it pays in
  Vietnam, `USD` when it pays abroad. It does not change.

## Paying in Vietnam

- **Provider.** PayOS, with VietQR bank transfers, as D118 agrees. Card payments come later.
- **A payment link for each invoice.** The control plane creates a PayOS payment request with the
  invoice's amount in `VND` and a unique integer `orderCode`, and shows its VietQR code and transfer
  details in the [billing portal](billing-portal.md) and in the reminder emails' link.
- **No automatic charge.** A bank transfer cannot be pulled, so each period the owner or a billing
  contact pays its invoice. The reminders of [the timeline](subscriptions.md#the-timeline) ask them
  to.
- **Paid.** An invoice is paid when the payments recorded against it reach its amount. A smaller
  payment is recorded and the rest stays due. A larger one leaves a credit that the next invoice
  uses.
- **A transfer without a link.** A transfer that reaches Axis's account outside PayOS is recorded by
  the operator against its invoice, with `Axis.ControlPlane.Admin`, see
  [operators](control-plane.md#operators).

## Paying abroad

- **Merchant of record.** Paddle Billing sells the subscription, takes the payment, handles tax and
  issues the invoice, as D118 agrees. Axis is the seller to Paddle, not to the customer.
- **Subscriptions in Paddle.** The control plane creates a Paddle subscription for the plan's price
  in `USD`, and Paddle charges the card each period and retries a failed charge.
- **Mapped to Axis's states.** Paddle's `past_due` makes the Axis subscription `pastDue`, and Axis's
  14 days of grace start then. Paddle's `canceled` after its retries makes it `readOnly`.
- **No e-invoice from Axis.** Paddle's invoice is the customer's invoice.

## Webhooks

Both providers tell Axis about payments by webhooks, at `POST /api/payments/payos/webhook` and
`POST /api/payments/paddle/webhook` on the regional control plane.

1. **Signature first.** The control plane checks the provider's signature over the raw body before it
   parses anything: PayOS's HMAC-SHA256 checksum with its checksum key, and Paddle's
   `Paddle-Signature` with its secret and a timestamp no older than 5 minutes. A bad signature answers
   `401` and is logged without the body.
2. **Inbox.** It stores the event in `billing.webhook_inbox`, unique on the provider and the
   provider's event id, and answers `200`. A repeated event finds its row and changes nothing.
3. **In order.** A job applies the inbox's events for each invoice in the order the provider sent
   them, through the subscription's state machine. An event that does not fit the current state, such
   as a payment for a cancelled invoice, is kept and alerts the operator.

## Reconciliation

Webhooks can be lost, so each night the control plane asks each provider for the day's transactions
and compares them with its ledger.

- A payment that the provider has and Axis lacks is recorded, as if its webhook had come.
- A payment that Axis has and the provider lacks alerts the operator, and nothing is changed.
- An amount that differs alerts the operator.
- Each run writes its counts as a metric and an audit record.

## The ledger

- **Append-only.** `billing.ledger_entries` holds every charge, credit, payment, refund and
  proration: the subscription, the invoice, the kind, the signed amount in minor units, the currency,
  the provider's reference and the time. No entry is updated or deleted: the control plane's database
  role has `INSERT` and `SELECT` on the table and nothing else. A correction is a new entry.
- **Balances are sums.** What a subscription owes is the sum of its entries, so the ledger is the
  only source of money, and the invoices and the portal read it.
- **Refunds.** The operator issues a refund with `Axis.ControlPlane.Admin`. A refund through Paddle
  goes through its API. A refund in Vietnam is a bank transfer the operator makes, then records.
- **Idempotent calls.** Each call to a provider carries an idempotency key from the ledger's record,
  so a retry never charges twice.
