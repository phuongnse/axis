# D156. Working time: calendars, durations in working hours, and SLAs

Status: **Agreed**

## Context

A task's due date, a deadline, an escalation and a timer counted fixed time from their start, and an
application had one time zone. A request raised on a Friday afternoon with an eight-hour target fell
due in the night, and a public holiday counted as a working day. A service desk promises answers in
working hours, a branch in another city keeps its own hours, and a Vietnamese business works some
Saturdays to make up for a holiday. ServiceNow schedules, Jira Service Management and Zendesk business
hours, Dynamics business closures and Odoo working schedules all count time this way.
[D154](d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
puts working time among the foundations. This decision replaces D71 and D75, and keeps every part of
them that still holds.

## Decision

- **One time zone for each application.** The `application` manifest takes an optional IANA
  `timeZone`, and without it the time zone is `UTC`. It is part of the release.
- **It sets the local date.** `today()`, `toDate(date-time)`, the year of a sequence and its yearly
  restart, the dates in notifications and the date buckets of data sources follow it.
- **Instants and the browser stay as they are.** `now()` stays an instant, a schedule trigger keeps
  its own `timeZone`, and the SPA shows a `date-time` in the browser's time zone.
- **A calendar says when people work.** A `calendar` resource holds a working week, the days and the
  hours of each, in one or more spans such as 08:00 to 12:00 and 13:00 to 17:00, and a time zone,
  the application's unless it names its own. An application may hold several, such as one for each
  branch or team.
- **Days off and make-up days are data.** A calendar's days off, whole or half, and its working days
  outside the week, such as a Saturday worked to make up for a holiday, are kept in the running
  application, where an administrator changes them without a publish. Axis ships the public holidays
  of Vietnam, lunar new year included, which a calendar may take.
- **A duration counts fixed time or working time.** Every duration of a process, a task's `dueIn`, a
  deadline, an escalation, a timer, a wait's timeout and a repeat's longest time, counts fixed time
  from its start, or working time on a calendar. The calendar is named on the step or the process, or
  given by an expression, such as the calendar of the branch on the record. Expressions add working
  time to an instant, count the working time between two instants, and tell a working day.
- **A calendar's change moves what has not fallen due.** A day off or a make-up day added or removed later moves every due time, deadline, escalation, timer and SLA counted on that calendar that has not passed yet, as ServiceNow and Jira count SLAs. One that has passed stays as it fell.
- **Calendar durations stay refused.** `P1M` and `P1Y` are not durations of a process.
- **An SLA is a promise with its own clock.** An SLA names a target in fixed or working time, such as
  a first answer within four working hours, a time at which it is at risk, and the conditions that
  pause its clock, such as waiting for the customer. It is on track, at risk or breached, and being at
  risk or breached escalates or notifies as an escalation does. Each SLA's result is kept for reports.
- **Escalations** reassign the task, notify people as [D73](d73-in-app-and-email-notifications.md)'s
  `notify` does, or both, and do not end it.
- **A deadline** expires the task and moves the instance on. Every escalation comes before it.
- **One outcome.** Each action on an open task locks its row first.
- **Claim.** A role holder may claim a role task, and only the claimer then completes it.
- **Reassign.** The `reassign` action on the `process` grant of [D24](d24-roles-and-policies.md) gives
  an open task, with a reason, to a declared role or to a user who has signed in.
- **A fiscal year may start in any month,** and data sources bucket dates by quarter and by fiscal
  period as well as by day, week, month and year.
- **A due time reads in its calendar's time zone,** with the working time left, and a task that is at
  risk or past due is marked with a status tag.

The detail is in [working time and SLAs](../reference/working-time.md),
[task deadlines and claims](../reference/task-deadlines.md),
[application time zone](../reference/entity-logic-and-sequences.md#application-time-zone) and the
[expression functions](../reference/expressions.md#functions).

## Consequences

- Every user of an application gets the same answer for "today", wherever their browser is, and
  applications that name no time zone and no calendar keep their behaviour.
- A due date, a timer or an SLA skips nights, weekends and holidays when its process asks for working
  time, and a branch counts its own hours.
- A claim on a role is how a team works a queue, so there is no queue resource.
- A task never goes to an id nobody has signed in with, but a new colleague gets tasks only after a
  first sign-in.
- A process still cannot wait for "one month".

## Alternatives

- **The time zone of each user.** Two users of one application would get different dates for the same
  instant, and a sequence's year would depend on who saved the record.
- **Holidays in the release.** A new year's holidays would need a publish of every application.
- **Working time without SLAs.** A service desk would build its promise, its pauses and its breaches
  again in each process.
