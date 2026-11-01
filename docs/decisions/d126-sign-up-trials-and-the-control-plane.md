# D126. Sign-up, trials and the control plane

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets the mechanics of M33 Sign-up and trials that
[D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) left open, on the cells of
[D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md), so that M33 can
be broken into issues. It adds to D118 and changes one point: an ended trial is read-only, not
suspended. Everything here is _(planned for M33)_. [Control plane](../reference/control-plane.md),
[region directory](../reference/region-directory.md), [sign-up](../reference/sign-up.md) and
[subscriptions](../reference/subscriptions.md) hold the detail.

- **The control plane is a .NET service of its own:**
  - It lives under `controlplane/` with its own PostgreSQL, on a small cluster of its own, never in a
    cell.
  - It reaches a cell only through the provisioning API, over a private network with mutual TLS and a
    client of the cell's operator realm.
- **A global directory, regional data:**
  - The global directory holds only tenant to cell and host to cell, with no personal data.
  - A control plane in each region holds that region's accounts, sign-ups, subscriptions and billing.
  - Each tenant's subdomain gets a `CNAME` to its cell, so no global router sits in a request's path.
- **Verify the email first:**
  - No tenant exists until the person confirms a single-use link sent to their email.
  - The page serves its own ALTCHA proof of work, has a `sign_up` rate-limit partition, gives one
    trial to each verified email, and refuses disposable mailbox domains.
  - The person chooses the subdomain, from an editable suggestion, against reserved words and the hold
    on deleted hosts.
  - Progress shows by polling, and an email says when the tenant is ready.
- **One clock in the control plane:**
  - A subscription state machine owns the trial and every later date, and calls the cell.
  - The cell knows no commerce.
  - The control plane sends trial and billing emails from its own mail domain, in `en` and `vi`, to
    every tenant administrator. For that, the provisioning API gains a read of a tenant's
    administrators.
- **Read-only instead of suspended:**
  - An ended trial, and a tenant past its payment grace, become `readOnly`. People sign in, read and
    export, with a banner. Nothing is written and no work runs.
  - A full suspension stays for the operator.
  - A trial is read-only for 30 days, then its deletion is scheduled. The cell's 7-day grace follows,
    so the data is gone 37 days after the trial ends.
- **The administrator exports:** a tenant administrator runs the cell's export from the application,
  while active or read-only. It is audited, with the warning that the file holds sensitive values in
  clear.
- **Terms are recorded:** each acceptance of the terms of service and the data processing agreement is
  an append-only row with the version, the time, the client address and the account.
- **New trust boundaries:** the public sign-up page, the control plane's calls to the cells and the
  directory's DNS writes. M33's breakdown has an issue that adds them to the
  [threat model](../reference/threat-model.md), as
  [D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md) says.
- **Outside the repository, before sign-up opens:**
  - the domain and the DNS provider of each region;
  - a lawyer's check of where Vietnamese customer data must be kept, and of whether billing data
    counts as customer data;
  - the texts of the terms of service and the data processing agreement, as D118 says.

_Why:_ a control plane inside a cell would put Axis's business beside customer data and stop sign-up
whenever that cell fails, so D118's separation becomes a service of its own. It uses the platform's
stack so that the same people and tests cover it. Keeping personal data in each region and only
routing facts in the global directory answers D118's residency rule without guessing what a lawyer
will say. DNS routing keeps one failure inside one cell. Verifying the email before anything exists
means a bot fills no cell. A proof of work served by Axis keeps sign-up data in the region, which a
third-party captcha would not. One clock in the control plane keeps commerce out of the engines. A
read-only tenant lets a business that stopped paying still see and take its data, which D118 already
promised through export, at no cost to Axis, because nothing grows. The cost is a second service to
build and run, a DNS provider to integrate, and a new tenant state in the cell.
