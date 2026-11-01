# Billing portal

Detailed reference for the billing portal, where the owner of a tenant of Axis SaaS and their billing
contacts choose a plan, pay, and get their invoices. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md),
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md) and
[D127](../decisions/d127-plans-payments-and-invoices.md). Accounts, owners and the trial's page are
_(planned for M33)_. Plans, payments and invoices are _(planned for M34)_. The conventions of
[architecture.md](../architecture.md) apply.

## Where

- **On the control plane.** `https://account.<region domain>`, served by the regional
  [control plane](control-plane.md). It is not a page of the cell, so it works while the tenant is
  read-only or suspended, and no payment data enters a cell.
- **From the application.** The shell shows "Billing" to a person who holds the
  [administrator role](user-administration.md#the-administrator-role) in a tenant of a cell, and it
  opens `https://account.<region domain>/tenants/<tenant id>`. The read-only banner links there too,
  see [the read-only tenant](subscriptions.md#the-read-only-tenant).

## Accounts

- **Its own accounts.** The portal signs people in through a realm of its own, `axis-billing`, in an
  identity provider that the control plane runs, with the Axis theme. A billing account is not an
  account of any tenant realm, because the person who pays may not use the application, and the
  person who uses it may not pay.
- **Verified.** An account's email is verified before it signs in. The owner's account is created and
  verified by the [sign-up](sign-up.md#verifying-the-email-first).
- **Second factor.** An authenticator app or a passkey, offered at the first sign-in. An owner who
  has none is asked again at each sign-in, and needs one to cancel the subscription or to transfer
  ownership.
- **Session.** As the platform's [session](security.md#sign-in-and-the-session): a server-side
  session, 30 minutes idle, 12 hours at most.

## People

| Role | What they do |
| --- | --- |
| `owner` | Everything: plan, add-ons, payment, billing details, contacts, cancelling, and handing ownership to another account |
| `billing` | Pay, see invoices, and change the billing details |

- **The first owner** is the person who signed up. A tenant has at least one owner.
- **Inviting.** An owner invites a person by email, as an owner or a billing contact. The person
  creates or uses a billing account, and the link takes effect once they accept.
- **A tenant administrator** who is not an owner or a billing contact sees only that billing is
  handled by the owner, and the owner's name.

## Pages

- **Overview.** The subscription's state and its next date, the plan, the add-ons, the open invoice,
  and the tenant's usage against its quotas, read through the provisioning API.
- **Plans.** The plans of the region with their quotas and prices, for a change, see
  [changing a plan](plans.md#changing-a-plan). A refused downgrade lists each quota that is over.
- **Add-ons.** User packs to add or drop, see [add-ons](plans.md#add-ons).
- **Pay.** The open invoice with its VietQR code and transfer details, or Paddle's checkout abroad,
  see [payments](payments.md).
- **Invoices.** Each billing invoice with its lines and status, and each e-invoice with its lookup
  code and its PDF and XML.
- **Billing details.** The legal name, the tax code, the address and the invoice email of the
  [e-invoices](e-invoices.md#what-it-holds).
- **Contacts.** The owners and billing contacts, with invitations.
- **Cancel.** Cancels at the period's end, see [states](subscriptions.md#states), after the owner types
  the tenant's id.
- **Terms.** The versions the owner accepted, and the current ones to accept, see
  [terms acceptance](sign-up.md#terms-acceptance).

## Rules

- **Grants are checked on the server.** Every route checks the account's role on that tenant. A
  person of another tenant gets `404`.
- **Audit.** Every change writes an audit record with the account as its actor.
- **Rate limits.** The portal's routes have limits by account and by client address, as the
  platform's [rate limits](headers-and-rate-limits.md#rate-limits) work.
- **Security headers** as the platform's, see [security headers](headers-and-rate-limits.md#security-headers).
  The pay page allows Paddle's checkout script and frame only, and only on that page.
