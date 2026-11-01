# Subscriptions

Detailed reference for the subscription of a tenant of Axis SaaS: its trial, its paid periods, the
read-only state, the reminders and the deletion. It follows
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md),
[D126](../decisions/d126-sign-up-trials-and-the-control-plane.md) and
[D127](../decisions/d127-plans-payments-and-invoices.md). The trial and the read-only state are
_(planned for M33)_. Paid periods, reminders and dunning are _(planned for M34)_. The conventions of
[architecture.md](../architecture.md) apply.

## One clock

- **Each tenant has one subscription** in its regional [control plane](control-plane.md). A state
  machine there owns every date: the end of the trial, each period, each reminder, each grace and the
  deletion.
- **The cell knows no commerce.** The control plane changes a tenant through the
  [provisioning API](tenant-lifecycle.md#provisioning-api): its quotas, its read-only state, a
  resume and a deletion. A cell never counts days for billing.
- **Runs as jobs.** A job checks each subscription's next date every hour, so a restart or an outage
  delays a step and never skips one. Every change of state writes an audit record.

## States

| State | The tenant | Next |
| --- | --- | --- |
| `trialing` | Works, with the trial's quotas | `active` on the first payment, `readOnly` at the trial's end |
| `active` | Works, with the plan's quotas | `pastDue` when an invoice is unpaid on its due date |
| `pastDue` | Works, with a banner to pay | `active` on payment, `readOnly` after 14 days of grace |
| `readOnly` | Read-only, see [the read-only tenant](#the-read-only-tenant) | `active` on payment, `deleting` after its wait |
| `cancelled` | Works until the paid period ends | `readOnly` at the period's end |
| `deleting` | Suspended, in the cell's grace window | `deleted` after the window, `readOnly` if the operator cancels the deletion |
| `deleted` | Gone | — |

- **Paying ends any wait.** A payment in `pastDue` or `readOnly` makes the subscription `active`
  again at once: the control plane resumes the tenant and writes the plan's quotas.
- **Cancelling.** The owner cancels in the [billing portal](billing-portal.md). The tenant keeps
  working to the end of the paid period, then follows the same path as an unpaid one.

## The timeline

D118's days count from the trial's end or from a missed payment. The cell's 7-day deletion grace
comes after them.

| Day | A trial that is not paid | A paid tenant that stops paying |
| --- | --- | --- |
| Trial day 23, or 7 days before the due date | Email: 7 days left | Email: invoice due in 7 days |
| Trial day 29, or 1 day before | Email: 1 day left | Email: invoice due tomorrow |
| Trial day 30, or the due date | `readOnly`, email | Email: due today; `pastDue` the next day |
| Due date + 14 | — | `readOnly`, email |
| 7 days before the deletion | Email: deletion in 7 days, export now | Same |
| 30 days in `readOnly` for a trial, 90 for a paid tenant | `deleting`: the control plane calls `DELETE /api/cell/tenants/{id}` | Same |
| 7 days later | The cell purges the tenant, see [deleting a tenant](tenant-lifecycle.md#deleting-a-tenant) | Same |

- **Export until the deletion.** While `readOnly`, a tenant administrator exports the data
  from the application, see [exports by an administrator](tenant-lifecycle.md#exports-by-a-tenant-administrator).
  During the cell's 7-day grace the tenant is suspended, and only the operator can export it or
  cancel the deletion.
- **Read-only, not suspended.** D118 suspends a tenant at the trial's end or after the grace. Read-only
  gives the same protection to Axis, no new work and no growth, while the business can still see and
  take its data. A full suspension stays for the operator, for abuse or a security incident.

## The read-only tenant

The cell's tenant status `readOnly` is set by `POST /api/cell/tenants/{id}/read-only` and cleared by
`resume`, see [tenant read-only](tenant-status-commands.md#tenant-read-only).

- **Signing in works.** People sign in, read records, lists, data sources, history, tasks and
  dashboards, and export what their grants allow.
- **No write.** Every request that writes answers `403` problem details whose `reason` is
  `tenant-read-only`, titled "This workspace is read-only.": record writes, imports, task
  completions, process starts, inbound endpoint calls, Studio saves and publish requests, and the
  users page's changes.
- **No work.** The worker claims none of the tenant's new work, as for a suspended tenant. Work
  already claimed finishes. Schedules do not fire, and a missed occurrence is not caught up after a
  resume. No email is sent for the tenant.
- **A banner.** `GET /api/me` answers `tenantStatus: "readOnly"`, and the shell shows a banner on
  every page: why the workspace is read-only, and, for an administrator, a link to the
  [billing portal](billing-portal.md).
- **Quotas.** Unchanged. Nothing grows, so none is reached.

## Emails

- **Who gets them.** Every administrator of the tenant, read through
  `GET /api/cell/tenants/{id}/administrators`, and every [billing contact](billing-portal.md#people)
  of the subscription, each once.
- **From where.** The control plane's own mail domain, in the subscription's language, see
  [its own work](control-plane.md#its-own-work).
- **Which.** Those of [the timeline](#the-timeline), plus: the trial has started, a payment is
  received, an e-invoice is issued, a plan or add-on change, a cancellation, and the deletion is done.
  Each states the date of the next step and what stops it.
- **Never a secret.** An email holds no password, token or payment detail. Links go to the billing
  portal, which asks the person to sign in.
