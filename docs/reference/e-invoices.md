# E-invoices

Detailed reference for the Vietnamese electronic invoices that Axis issues for the payments it takes
in Vietnam. It follows [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md)
and [D127](../decisions/d127-plans-payments-and-invoices.md). Everything in this file is _(planned
for M34)_. A payment abroad gets Paddle's invoice instead, see
[paying abroad](payments.md#paying-abroad). The conventions of [architecture.md](../architecture.md)
apply.

## When

- **On payment.** The [control plane](control-plane.md) issues the e-invoice once the payment of a
  billing invoice is confirmed, by its webhook or by the reconciliation, never before. An invoice
  paid in two transfers gets one e-invoice when it is fully paid.
- **As a job.** Issuing runs as a work item that retries, so a provider outage delays an e-invoice
  and never loses one. One that still fails after its retries alerts the operator.

## Through an adapter

- **Any provider.** The control plane calls the e-invoice provider through an adapter: issue, read
  the status, and adjust or replace an issued invoice. The provider is the one Axis's accountant
  uses, such as VNPT, Viettel or MISA, and is chosen before M34 takes a payment, as D118 says. A
  new provider is a new adapter.
- **Settings.** The provider's address, Axis's tax code, the invoice template and series, and the
  VAT rate are settings of the control plane, which the accountant confirms. The credentials are
  Kubernetes Secrets.

## What it holds

- **The buyer.** The business's legal name, its tax code when it has one, its address and the email
  that receives invoices, from the [billing details](billing-portal.md#pages). The portal asks for
  them before the first payment. A buyer without a tax code gets an invoice without one, as the law
  allows for a person.
- **The lines.** Those of the billing invoice: the plan and each add-on with its period, and any
  proration, with VAT.
- **The amounts** in `VND`, from the [ledger](payments.md#the-ledger).

## After it is issued

- **Sent.** The provider's lookup code and the links to the PDF and XML go by email to the invoice
  email and the billing contacts, and stay in the portal's invoice list.
- **Kept.** The control plane keeps each e-invoice's number, series, lookup code and status. The
  provider keeps the invoice as the law asks.
- **Corrections.** A wrong invoice is adjusted or replaced through the provider, as the law asks,
  by the operator with `Axis.ControlPlane.Admin`, see [operators](control-plane.md#operators). Each
  correction writes an audit record.
