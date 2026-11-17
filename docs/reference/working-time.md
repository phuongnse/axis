# Working time and SLAs

A request raised on a Friday afternoon should not fall due in the night, and a public holiday should not
count as a working day. This page covers the `calendar` resource, the days off and make-up days that an
administrator keeps, durations counted in working time, what happens to a due time when a calendar
changes, and SLAs, as [D156](../decisions/d156-working-time-calendars-durations-in-working-hours-and-slas.md)
agrees. The durations themselves are on the pages of the steps that use them, see
[task deadlines](task-deadlines.md#escalations-and-deadline), [process flow](process-flow.md) and
[processes](processes.md#compile-checks).

## Calendar resource

_(planned for M39)_

A `calendar` resource says when people work: a week of days, each with its hours, and a time zone. An
application may hold several, such as one for each branch or team.

```json
{
  "id": "3b8f2c1e-6d4a-4e7b-9a15-c2d7e8f90a41",
  "kind": "calendar",
  "name": "HanoiOffice",
  "formatVersion": 1,
  "label": { "textKey": "calendars.hanoiOffice" },
  "timeZone": "Asia/Ho_Chi_Minh",
  "week": {
    "monday": [{ "from": "08:00", "to": "12:00" }, { "from": "13:00", "to": "17:00" }],
    "tuesday": [{ "from": "08:00", "to": "12:00" }, { "from": "13:00", "to": "17:00" }],
    "wednesday": [{ "from": "08:00", "to": "12:00" }, { "from": "13:00", "to": "17:00" }],
    "thursday": [{ "from": "08:00", "to": "12:00" }, { "from": "13:00", "to": "17:00" }],
    "friday": [{ "from": "08:00", "to": "12:00" }, { "from": "13:00", "to": "17:00" }],
    "saturday": [],
    "sunday": []
  },
  "publicHolidays": "VN"
}
```

- **`week`.** It names each day of the week and gives its spans. A day with no spans is not worked. A
  span is a `from` and a `to`, written `HH:mm`. The spans of a day ascend and do not overlap. The
  compiler refuses a span that is badly written, is empty or backwards, or overlaps another.
- **`timeZone`.** It is optional. Without it the calendar uses the
  [application time zone](resource-files.md#application-time-zone), and the compiler checks an id as it
  checks that one. The spans are local times in this zone, so a branch in another city keeps its own hours.
- **`publicHolidays`.** It is optional. `"VN"` takes the public holidays of Vietnam, the solar ones and
  the lunar ones, with lunar new year and the Hùng Kings' day included. They are days off of the
  calendar and are kept as [days off](#days-off-and-make-up-days) say.
- **`label`.** It has the `{ "textKey" }` shape of any label.
- **Shipped holidays.** Axis ships the fixed-date and the lunar holidays of Vietnam. The extra days off
  and the make-up days that the government sets each year cannot ship ahead of time, so the calendar
  keeps them as its own data.

## Days off and make-up days

_(planned for M39)_

A calendar's days off and its working days outside the week are data of the running application. An
administrator changes them without a publish, because a new year's holidays should not need a release of
every application. They are not part of any release.

- **A day off.** It has a `date`, an optional `from` and `to` when only a part of the day is off, such as
  a half day, and an optional `note`. A day off with no `from` and `to` is a whole day.
- **A make-up day.** It has a `date` and `worksAs`, a weekday of the calendar whose spans the date works.
  A Saturday that makes up for a holiday has `"worksAs": "monday"`, so it needs no second set of hours.
- **Which wins.** A make-up day on a date overrides a day off on the same date.
- **Shipped holidays.** The days of `publicHolidays` read as `publicHoliday` days off. They cannot be
  deleted, and a make-up day on one of those dates overrides it.

| Route | What it does |
| --- | --- |
| `GET /api/apps/{app}/calendars/{calendar}/days?year=` _(planned for M39)_ | Lists the days off, make-up days and public holidays of a year |
| `PUT /api/apps/{app}/calendars/{calendar}/days/{date}` _(planned for M39)_ | Sets the day off or the make-up day of one date |
| `DELETE /api/apps/{app}/calendars/{calendar}/days/{date}` _(planned for M39)_ | Removes the day off or the make-up day of one date |

- **Grant.** A user needs a [`calendar` grant](roles-and-policies.md#policies) in the active release:
  `read` for the list, and `update` for the set and the delete.
- **Errors.** The checks run in the task API's order, as [task API errors](task-api.md#task-api-errors) say: `401`, `404`
  for a calendar the active release does not have, `403` for the grant, then `400` for the body or the
  date. A public holiday cannot be deleted, and the answer is a `400`.
- **Audit.** `calendar.dayChanged`, with the actor and the details `{ "calendar", "date", "from", "to" }`.
  A delete writes it too, with no `from` and `to`.
- **Why a route.** The worker reads the days when it counts time, and it must not depend on the schema
  of a module's entity. A grant fits [roles and policies](roles-and-policies.md) as any other.

## Durations in working time

_(planned for M39)_

Every duration of a process counts fixed time or working time. That covers a task's `dueIn`, the `after`
of a deadline and of an escalation, a timer's `wait`, a wait's `timeout.after`, the longest time of a
repeat and every other duration of a process, by kind.

```json
{
  "name": "resolve",
  "type": "task",
  "calendar": { "expression": "branch.calendar" },
  "assignee": { "role": "service-desk-agent" },
  "form": "TicketResolve",
  "dueIn": { "working": "PT8H" },
  "escalations": [{ "name": "escalation1", "after": { "working": "PT6H" }, "reassign": { "role": "service-desk-lead" } }],
  "deadline": { "after": "P5D", "next": "markExpired" },
  "outcomes": [{ "name": "resolved", "label": { "textKey": "task.resolved" }, "next": "confirm" }]
}
```

- **Fixed or working.** A plain ISO string, such as `"P5D"`, counts fixed time as before. An object
  `{ "working": "PT8H" }` counts working time. One step may mix both, such as a fixed timer and a
  due date in working time. A `working` duration has hours, minutes and seconds only, because the
  length of a working day differs from one calendar to another.
- **The calendar.** A step names it with `calendar`, and so may the process. A step's calendar wins over
  the process's. `calendar` is a calendar name, or `{ "expression": "…" }`, which gives the name as text
  with the scope of a `when`, such as the calendar of the branch on the record.
- **Checks.** The compiler refuses a `working` duration when neither the step nor the process names a
  calendar, and a name that no calendar has. An expression that gives `null` or an unknown name fails the
  step when it runs.
- **Counting.** Time runs only inside the spans of the calendar's day, in the calendar's time zone. It
  skips days off and adds make-up days. A start outside the spans counts from the next span.
- **An example.** With spans 08:00 to 12:00 and 13:00 to 17:00, a duration of `PT8H` that starts on
  Friday at 15:00 falls due on Monday at 15:00. When Monday is a day off, it falls due on Tuesday at
  15:00.
- **Kept with the time.** A due time stores the name of its calendar, so the worker can count it again
  when the calendar changes. This holds for a task's `due_at` and for each work item that counts working time.
- **Calendar durations.** `P1M` and `P1Y` are still refused as durations of a process, see
  [compile checks](processes.md#compile-checks).
- **Expressions.** [`addWorkingTime`, `workingTimeBetween` and `isWorkingDay`](expressions.md#functions)
  add working time to an instant, count it between two instants and tell a working day.

## When a calendar changes

_(planned for M39)_

A day off or a make-up day that an administrator adds or removes moves every due time counted on that
calendar that has not passed yet.

- **What moves.** A task's `due_at`, its deadline and escalation items, timer and timeout items, and the
  at-risk and target times of SLAs. The worker counts each of them again from its start.
- **What stays.** A due time that has passed stays as it fell. A fixed-time duration never moves, because
  no calendar counted it.
- **Why.** An SLA keeps its promise in working time, so a new holiday moves it, as ServiceNow and Jira
  count SLAs.
- **A moved item.** An item that is now due runs at once. One that moves later waits, and an open task
  shows its new `dueAt`.

## SLAs

_(planned for M39)_

An SLA is a promise with its own clock, such as a first answer within four working hours. A process
declares its SLAs in `slas`:

```json
{
  "slas": [
    {
      "name": "firstAnswer",
      "label": { "textKey": "ticket.sla.firstAnswer" },
      "start": "triage",
      "stop": ["answer"],
      "pauseIn": ["waitForCustomer"],
      "target": { "working": "PT4H" },
      "atRisk": { "working": "PT3H" },
      "onAtRisk": {
        "notify": {
          "to": [{ "role": "service-desk-lead" }],
          "channels": ["inApp"],
          "message": { "subject": { "textKey": "ticket.sla.subject" }, "body": { "textKey": "ticket.sla.body" } }
        }
      },
      "onBreach": { "reassign": { "role": "service-desk-lead" } }
    }
  ]
}
```

- **Clock.** `start` is a step name and is optional, so without it the clock starts with the instance.
  The clock stops when the instance reaches a step of `stop`. It pauses while the instance waits in a
  step of `pauseIn`, such as waiting for the customer. The engine decides the pause, because a condition
  on the record would make every record write check the SLAs.
- **Times.** `target` and `atRisk` are durations, fixed or working, counted from the start without the
  paused time. `atRisk` is shorter than `target`. A working duration counts on the process's `calendar`.
- **States.** An SLA is `onTrack`, `atRisk` or `breached`.
- **Actions.** `onAtRisk` runs when the SLA becomes at risk, and `onBreach` when it is breached. Each
  takes the `notify` and `reassign` of an
  [escalation](task-deadlines.md#escalations-and-deadline). A `reassign` applies to each open task of the
  instance. An SLA does not end a task or an instance.
- **Names.** An SLA's name is unique in the process, ignoring letter case. Each step name in `start`,
  `stop` and `pauseIn` names a step of the process.
- **Result.** Axis keeps one result for each instance and SLA: when it started, when it falls due, when it
  stopped, the time it was paused, and whether it was met or breached. The reports of M54 read it.
- **What people see.** A task of an instance whose SLA is at risk or breached shows an "At risk" tag, see
  [task states](task-deadlines.md#task-states).

## Tables

_(planned for M39)_

Working time adds these tables and columns to the tenant database. [Process tables](process-tables.md)
and [storage](storage.md) list them once the code holds them.

- **`axis.calendar_days`.** One row for each calendar and date: the `calendar`, the `date`, the `kind`
  (`dayOff` or `makeUp`), `from` and `to` for a part of a day, `works_as` for a make-up day and `note`.
- **`axis.process_slas`.** One row for each instance and SLA: `instance_id`, `sla`, `state`, `started_at`,
  `at_risk_at`, `due_at`, `paused`, `stopped_at` and `result`.
- **`calendar`.** A column of `axis.process_tasks` and of `axis.process_work_items`, which holds the name
  of the calendar a due time was counted on, or `null` for fixed time.
