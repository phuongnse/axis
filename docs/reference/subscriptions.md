# Subscriptions

The subscription of a tenant of Axis SaaS holds every date of its commercial life: its trial, its
paid periods, the read-only state, the reminders and the deletion. This page covers the clock that
owns those dates, the states, the timeline, what a read-only tenant can still do, and the emails that
tell people what comes next.

## One clock

_(planned for M33)_

One state machine in the control plane owns every date of a subscription, and the cell knows no
commerce.

- **Each tenant has one subscription** in its regional [control plane](control-plane.md). A state
  machine there owns every date: the end of the trial, each period, each reminder, each grace and the
  deletion.
- **The cell knows no commerce.** The control plane changes a tenant through the
  [provisioning API](tenant-lifecycle.md#provisioning-api): its quotas, its read-only state, a resume
  and a deletion. A cell never counts days for billing.
- **Runs as jobs.** A job checks each subscription's next date every hour, so a restart or an outage
  delays a step and never skips one. Every change of state writes an audit record.

## States

_(planned for M33)_

A subscription is in one of these states, and each state says how the tenant works.

| State | The tenant | Next |
| --- | --- | --- |
| `trialing` | Works, with the trial's quotas | `active` on the first payment, `readOnly` at the trial's end |
| `active` | Works, with the plan's quotas | `pastDue` when an invoice is unpaid on its due date |
| `pastDue` | Works, with a banner to pay | `active` on payment, `readOnly` after 14 days of grace |
| `readOnly` | Read-only, see [the read-only tenant](#the-read-only-tenant) | `active` on payment, `deleting` after its wait |
| `cancelled` | Works until the paid period ends | `readOnly` at the period's end |
| `deleting` | Suspended, in the cell's grace window | `deleted` after the window, `readOnly` if the operator cancels the deletion |
| `deleted` | Gone | — |

- **Paid states** _(planned for M34)_. `active`, `pastDue` and `cancelled`, and every move on a
  payment, come with paid plans.
- **Paying ends any wait** _(planned for M34)_. A payment in `pastDue` or `readOnly` makes the
  subscription `active` again at once: the control plane resumes the tenant and writes the plan's
  quotas.
- **Cancelling** _(planned for M34)_. The owner cancels in the [billing portal](billing-portal.md).
  The tenant keeps working to the end of the paid period, then follows the same path as an unpaid
  one.

## The timeline

_(planned for M33)_

The days count from the trial's end or from a missed payment, and the cell's 7-day deletion grace
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

- **A paid tenant's timeline** _(planned for M34)_. The column of a paid tenant that stops paying
  comes with paid plans.
- **Export until the deletion.** While `readOnly`, a tenant administrator exports the data from the
  application, see
  [exports by an administrator](tenant-lifecycle.md#exports-by-a-tenant-administrator). During the
  cell's 7-day grace the tenant is suspended, and only the operator can export it or cancel the
  deletion.
- **Read-only, not suspended.** A tenant whose trial ended or whose grace passed becomes read-only,
  not suspended. Read-only protects Axis as well, with no new work and no growth, while the business
  can still see and take its data. A full suspension stays for the operator, for abuse or a security
  incident.

## The read-only tenant

A read-only tenant can still be signed in to and read, and nothing in it changes or runs. It is the
cell's tenant status `readOnly`, which [`tenant read-only`](tenant-status-commands.md#tenant-read-only)
and `POST /api/cell/tenants/{id}/read-only` set and `resume` clears.

- **Signing in works** _(planned for M33)_. People sign in, read records, lists, data sources,
  history, tasks and dashboards, and export what their grants allow.
- **No write** _(planned for M33)_. Every request that writes answers `403` problem details whose
  `reason` is `tenant-read-only`, titled "This workspace is read-only.": record writes, imports, task
  completions, process starts, inbound endpoint calls, Studio saves and publish requests, and the
  users page's changes.
- **No work**. The worker claims none of the tenant's new work, as for a suspended
  tenant. Work already claimed finishes. Schedules do not fire, and a missed occurrence is not caught
  up after a resume: the worker skips them when it first sees the tenant active again. No email is sent for the tenant.
- **A banner.** `GET /api/me` answers `tenantStatus: "readOnly"`, and the shell shows a banner on
  every page: why the workspace is read-only, and, for an administrator in a cell, a link to the
  [billing portal](billing-portal.md).
- **Quotas.** Unchanged. Nothing grows, so none is reached.

## Emails

_(planned for M33)_

The control plane emails the people of a subscription before each step, so nobody is surprised by
one.

- **Who gets them.** Every administrator of the tenant, read through
  `GET /api/cell/tenants/{id}/administrators`, and every
  [billing contact](billing-portal.md#people) of the subscription, each once.
- **From where.** The control plane's own mail domain, in the subscription's language, see
  [its own work](control-plane.md#its-own-work).
- **Which.** Those of [the timeline](#the-timeline), plus: the trial has started, and the deletion is
  done. Each states the date of the next step and what stops it.
- **Billing emails** _(planned for M34)_. A payment is received, an e-invoice is issued, a plan or
  add-on changes, and a subscription is cancelled. Each states the date of the next step and what
  stops it.
- **Never a secret.** An email holds no password, token or payment detail. Links go to the billing
  portal, which asks the person to sign in.
