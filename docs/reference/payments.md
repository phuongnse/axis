# Payments

A tenant of Axis SaaS pays for its subscription by bank transfer in Vietnam and through a merchant of
record abroad. This page covers the billing invoices, the two ways to pay, the providers' webhooks,
the daily reconciliation and the ledger that holds every amount. No payment is taken before the
company, the providers' contracts and the accountant are in place.

## Invoices

_(planned for M34)_

A billing invoice is Axis's own record of what a subscription owes for a period or a change.

- **A billing invoice for each period.** The [control plane](control-plane.md) creates the invoice
  of a period 7 days before it starts, with its lines: the plan, each add-on, and any prorated charge
  or credit. Its due date is the period's first day. A plan change or an add-on makes an invoice at
  once, due in 7 days.
- **Not the e-invoice.** This is Axis's own record of what is owed. The legal invoice of a payment in
  Vietnam is the [e-invoice](e-invoices.md), issued once it is paid.
- **One currency.** A subscription's currency is set by its first payment: `VND` when it pays in
  Vietnam, `USD` when it pays abroad. It does not change.

## Paying in Vietnam

_(planned for M34)_

In Vietnam a tenant pays each invoice by bank transfer, through a VietQR code.

- **Provider.** PayOS, with VietQR bank transfers.
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

_(planned for M34)_

Abroad a merchant of record sells the subscription and takes the payment.

- **Merchant of record.** Paddle Billing sells the subscription, takes the payment, handles tax and
  issues the invoice. Axis is the seller to Paddle, not to the customer.
- **Subscriptions in Paddle.** The control plane creates a Paddle subscription for the plan's price
  in `USD`, and Paddle charges the card each period and retries a failed charge.
- **Mapped to Axis's states.** Paddle's `past_due` makes the Axis subscription `pastDue`, and Axis's
  14 days of grace start then. Paddle's `canceled` after its retries makes it `readOnly`.
- **No e-invoice from Axis.** Paddle's invoice is the customer's invoice.

## Webhooks

_(planned for M34)_

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

_(planned for M34)_

Webhooks can be lost, so each night the control plane asks each provider for the day's transactions
and compares them with its ledger.

- A payment that the provider has and Axis lacks is recorded, as if its webhook had come.
- A payment that Axis has and the provider lacks alerts the operator, and nothing is changed.
- An amount that differs alerts the operator.
- Each run writes its counts as a metric and an audit record.

## The ledger

The ledger is the only source of money: every amount is an entry in it, and nothing else holds one.

- **Append-only.** `billing.ledger_entries` holds every charge, credit, payment, refund and
  proration. Its columns are the `id`, the `subscription_id`, the optional `invoice_id`, the `kind`
  (`charge`, `credit`, `payment`, `refund` or `proration`), the signed `amount_minor`, the `currency`,
  the optional `provider_reference`, the `idempotency_key` and `occurred_at`. The control plane's
  database role has `INSERT` and `SELECT` on the table and nothing else, and triggers reject every
  update, delete and truncate with `restrict_violation`, even from a superuser. A correction is a new
  entry.
- **Whole numbers.** `amount_minor` is a `bigint` of minor units, such as dong for `VND` and cents
  for `USD`. A positive amount raises what the subscription owes and a negative one lowers it, so
  charges and refunds are positive, payments and credits are negative, and a proration is never zero.
  The `currency` is a three-letter upper-case ISO 4217 code. The database refuses an entry with
  another kind, sign or currency.
- **One entry for each key.** The `idempotency_key` is unique. An append with a key that exists adds
  nothing and reports so, even if the amount differs, so a retried webhook or job never counts money
  twice.
- **Balances are sums.** What a subscription owes is the sum of its entries for each currency, as a
  whole number, so the ledger is the only source of money, and the invoices and the portal read it.
- **Refunds** _(planned for M34)_. The operator issues a refund with `Axis.ControlPlane.Admin`. A
  refund through Paddle goes through its API. A refund in Vietnam is a bank transfer the operator
  makes, then records.
- **Idempotent calls** _(planned for M34)_. Each call to a provider carries an idempotency key from
  the ledger's record, so a retry never charges twice.
