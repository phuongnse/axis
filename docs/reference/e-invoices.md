# E-invoices

_(planned for M34)_

Axis issues a Vietnamese electronic invoice for each payment it takes in Vietnam. This page covers
when an e-invoice is issued, the provider adapter, what an e-invoice holds and what happens after it
is issued. A payment abroad gets Paddle's invoice instead, see
[paying abroad](payments.md#paying-abroad).

## When

An e-invoice is issued once a payment is confirmed, never before.

- **On payment.** The [control plane](control-plane.md) issues the e-invoice once the payment of a
  billing invoice is confirmed, by its webhook or by the reconciliation, never before. An invoice
  paid in two transfers gets one e-invoice when it is fully paid.
- **As a job.** Issuing runs as a work item that retries, so a provider outage delays an e-invoice
  and never loses one. One that still fails after its retries alerts the operator.

## Through an adapter

The control plane reaches the e-invoice provider through an adapter, so the provider can change.

- **Any provider.** The control plane calls the e-invoice provider through an adapter: issue, read
  the status, and adjust or replace an issued invoice. The provider is the one Axis's accountant
  uses, such as VNPT, Viettel or MISA, and is chosen before the first payment is taken. A new
  provider is a new adapter.
- **Settings.** The provider's address, Axis's tax code, the invoice template and series, and the
  VAT rate are settings of the control plane, which the accountant confirms. The credentials are
  Kubernetes Secrets.

## What it holds

An e-invoice holds the buyer, the lines of the billing invoice and the amounts in `VND`.

- **The buyer.** The business's legal name, its tax code when it has one, its address and the email
  that receives invoices, from the [billing details](billing-portal.md#pages). The portal asks for
  them before the first payment. A buyer without a tax code gets an invoice without one, as the law
  allows for a person.
- **The lines.** Those of the billing invoice: the plan and each add-on with its period, and any
  proration, with VAT.
- **The amounts** in `VND`, from the [ledger](payments.md#the-ledger).

## After it is issued

The buyer gets the e-invoice by email, and the provider keeps it as the law asks.

- **Sent.** The provider's lookup code and the links to the PDF and XML go by email to the invoice
  email and the billing contacts, and stay in the portal's invoice list.
- **Kept.** The control plane keeps each e-invoice's number, series, lookup code and status. The
  provider keeps the invoice as the law asks.
- **Corrections.** A wrong invoice is adjusted or replaced through the provider, as the law asks,
  by the operator with `Axis.ControlPlane.Admin`, see [operators](control-plane.md#operators). Each
  correction writes an audit record.
