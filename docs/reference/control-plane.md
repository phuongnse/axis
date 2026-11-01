# Control plane

Detailed reference for the control plane: the service that runs Axis's own business around the
[cells](cells.md), which is sign-up, trials, plans, payments and invoices. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md),
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md) and
[D127](../decisions/d127-plans-payments-and-invoices.md). Everything in this file is _(planned for
M33)_, except the parts that billing adds, _(planned for M34)_. The conventions of
[architecture.md](../architecture.md) apply.

## What it is

- **A service of its own, not an Axis application.** The control plane is a .NET 10 service in the
  repository under `controlplane/`, with the same libraries, analyzers and test setup as the platform:
  real PostgreSQL through Testcontainers for its storage, and Playwright for its pages.
- **Apart from the cells.** It runs on a small Kubernetes cluster of its own, with its own managed
  PostgreSQL, in the region it serves. It is never deployed in a cell, and a cell never calls it. So a
  failing cell never stops sign-up or billing, and billing code never runs beside a customer's data.
- **No engine.** It holds no application, process or record of a customer. It reaches a tenant only
  through the cell's [provisioning API](tenant-lifecycle.md#provisioning-api).
- **Its own Helm chart and image.** `deploy/helm/axis-controlplane` and the image
  `axis-controlplane`, built, scanned and versioned with the platform's images.

## Two parts

| Part | Where it runs | What it holds |
| --- | --- | --- |
| The [region directory](region-directory.md) | One instance for all regions | Which cell holds each tenant and host. No personal data. |
| The regional control plane | One instance in each region, with its data in that region | Sign-up, [billing portal](billing-portal.md) accounts, [subscriptions](subscriptions.md), [plans](plans.md), [payments](payments.md), [e-invoices](e-invoices.md), terms acceptances, and the emails it sends |

- **Data stays in its region.** A person who signs up in `vn` gives their name, email and billing
  details to the `vn` control plane, and nothing of it leaves the region, as D118 asks. The directory
  holds tenant ids, hosts and cell ids only.
- **Where the directory runs.** Beside the first region's control plane, in its own database. It
  holds no personal data, so its place does not change where customer data is kept.

## Reaching the cells

- **One client for each cell.** The cell's [operator realm](cell-console.md#sign-in) has a client for
  the control plane, with the client credentials grant and the role `Cell:OperatorRole`. Its secret is
  a Kubernetes Secret of the control plane, never a setting.
- **Private network.** The control plane calls `Cell:InternalUrl`, never the public console host. The
  cluster of the control plane and the cells of its region share a private network, and the calls use
  mutual TLS: the cell accepts on its internal listener only a client certificate that the region's
  internal certificate authority issued to the control plane.
- **Idempotent.** Every `POST` to a cell carries an `Idempotency-Key` that the control plane derives
  from its own record, such as the sign-up id, so a retried call never makes a second tenant.
- **Jobs.** The control plane polls a cell job until it ends, see [jobs](tenant-lifecycle.md#provisioning-api).

## Its own work

- **Durable jobs.** Provisioning a tenant, the subscription clock, emails, invoice issuing and the
  daily reconciliation run as work items in the control plane's database, claimed with a lease as the
  [publisher](administration.md#publisher) claims its requests. A restart loses nothing, and a failed
  item is retried with a back-off, up to 10 attempts, then alerts the operator.
- **Emails.** The control plane sends its own emails, from a mail domain of its own,
  `ControlPlane:MailFrom`, with SPF, DKIM and DMARC. It never uses a cell's mail domain, so one
  tenant's sending never touches a sign-up or a payment reminder. Every email is in `en` and `vi`,
  in the language the person chose at sign-up.
- **Audit.** Every change to a subscription, a payment, an invoice or an account writes an
  append-only audit record with its actor: a person of the [billing portal](billing-portal.md), a
  provider's webhook, a job, or an operator.

## Operators

- **Who.** The platform operator, who runs the cells, runs the control plane too.
- **Commands.** `Axis.ControlPlane.Admin` holds the operator's commands: extend a trial, record a
  bank transfer that arrived without a payment link, issue a refund, resend an email, adjust an
  e-invoice, and list a region's sign-ups and subscriptions. Each writes an audit record. There is no
  operator web page in M33 or M34.
- **Settings.** `ControlPlane:` settings hold the region, its domain, its cells' internal addresses,
  the mail settings, the [plans](plans.md) and the providers' non-secret values. Secrets are
  Kubernetes Secrets.

## Health and telemetry

- **Health.** `/health/live` and `/health/ready`, as the platform's hosts answer them. Ready checks
  its database, the directory and the mail relay.
- **Telemetry.** Logs, metrics and traces as the platform's hosts write them. A log never holds a
  payment card, a bank account, a tax code or a webhook body.
