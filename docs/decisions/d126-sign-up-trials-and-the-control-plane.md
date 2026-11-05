# D126. Sign-up, trials and the control plane

Status: **Agreed**

## Context

[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) puts sign-up, trials and billing
apart from the [cells](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md),
keeps customer data in its region, and ends a trial that is not paid. Bots must not fill the cells,
and a business that stops paying should still be able to take its data.

## Decision

- **The [control plane](../reference/control-plane.md) is a .NET service of its own,** never in a
  cell, and reaches a cell only through the provisioning API, over a private network with mutual TLS.
- **Only routing is global.** A global [directory](../reference/region-directory.md) holds which cell
  holds each tenant and host, with no personal data, and DNS routes each tenant's host to its cell. A
  control plane in each region holds that region's accounts, subscriptions and billing.
- **No tenant exists until the person verifies their email,** and [sign-up](../reference/sign-up.md)
  serves its own proof of work.
- **One clock, in the control plane.** A [subscription](../reference/subscriptions.md)'s state machine
  owns every date and calls the cell, which knows no commerce.
- **An ended trial is read-only, not suspended:** people sign in, read and export, and nothing is
  written. A full suspension stays for the operator.

## Consequences

- A failing cell never stops sign-up or billing, and nothing global sits in a request's path.
- The control plane uses the platform's stack, so the same people and tests cover it.
- Personal data stays in its region without guessing what a lawyer will say, and a proof of work
  served by Axis keeps sign-up data there, which a third-party captcha would not.
- A read-only tenant costs Axis nothing, because nothing grows.
- The sign-up page, the calls to the cells and the directory's DNS writes are new trust boundaries for
  the [threat model](../reference/threat-model.md).
- There is a second service to run, a DNS provider to integrate and a new tenant state in the cell.
- Opening sign-up needs a lawyer's check of where Vietnamese customer data, billing data included,
  must be kept.
