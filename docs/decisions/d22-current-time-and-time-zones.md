# D22. Current time and time zones

Status: **Agreed**

The expression function `now()` gives the current time as a `date-time`. It is
an instant, so it never depends on a time zone. See
[expressions](../reference/expressions.md#functions).

- **One value per transaction:** every `now()` in one database transaction
  gives the same value, the transaction's start time.
  - A process step uses the step's transaction. That covers its decision
    branches, its `updateRecord` values and a task's `assignee.user`. The
    value is the same instant as the step history's `started_at`.
  - A start condition uses the start transaction.
  - A validation uses the transaction that writes the record.
  - A data source filter uses the query. It is translated to SQL as
    PostgreSQL's `now()`.
  - A rule uses the transaction of whatever calls it.
- **Both back ends agree:** the interpreter and the SQL translation give the
  same value for `now()` in one transaction.
- **Not in computed fields:** a computed field's value is stored, so it would
  go stale. `now()` in a computed field, including inside an aggregate's item
  expression, is `AXC0086` at the call.

**Waits for a time zone rule.** Applications have no time zone yet, and the
rule has no milestone. These wait for it:

- `today()`, and taking a date from a `date-time`.
- Calendar durations in a task's `dueIn`, such as `P1M` and `P1Y`. They are
  `AXC0084` for now.
- The local year in a sequence's `{yyyy}`, which is UTC today.

[D75](d75-the-application-time-zone.md) settles this rule for M14: each
application has a time zone. `today()`, taking a date from a `date-time` and
the local year follow it. Calendar durations stay `AXC0084`.

_Why:_ processes need the time of a step, for example to set `submittedAt`.
One value per transaction keeps every expression in a step consistent with
the step's own record. An instant needs no time zone, so `now()` can come
first, and everything that does need one waits for a single rule.
