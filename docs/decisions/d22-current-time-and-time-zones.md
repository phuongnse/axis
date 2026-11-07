# D22. Current time and time zones

Status: **Agreed**

## Context

Processes need the current time, for example to set `submittedAt` when a request is submitted.
Every expression in one step must see the same time as the step's own record, and the SQL
translation of a filter must agree with the interpreter. A current time that is an instant needs no
time zone, while a date or a local year does.

## Decision

- **`now()` is an instant.** The expression function `now()` gives the current time as a
  `date-time`, so it never depends on a time zone.
- **One value per transaction.** Every `now()` in one database transaction gives the same value,
  the transaction's start time. The interpreter and the SQL translation give the same value.
- **Not in computed fields.** A computed field's value is stored and would go stale, so `now()` in
  a computed field is a compile error.
- **Local dates come from the application.** Today's date and the local year of a sequence follow
  the application's time zone of [D75](d75-the-application-time-zone.md). A schedule trigger that
  names its own time zone runs in it, as [D26](d26-events-and-triggers.md) agrees.

Which transaction each kind of expression uses is in
[functions](../reference/expressions.md#functions).

## Consequences

- A process step's decisions, record writes and history share one time.
- A value that depends on the current time is set by a process step or compared in a filter or a
  condition, and never stored in a computed field.
