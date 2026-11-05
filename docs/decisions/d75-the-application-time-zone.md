# D75. The application time zone

Status: **Agreed**

## Context

A date such as "today", or the year in a sequence number, depends on where the business is.
[D22](d22-current-time-and-time-zones.md) gives `now()` as an instant, which needs no time zone, and
leaves every rule that does need one to a single time zone rule.

## Decision

- **One time zone for each application.** The `application` manifest takes an optional IANA
  `timeZone`, and without it the time zone is `UTC`. It is part of the release.
- **It sets the local date.** `today()`, `toDate(date-time)`, the year of a sequence and its yearly
  restart, the dates in notifications and the date buckets of data sources follow it.
- **Instants and the browser stay as they are.** `now()` stays an instant, a schedule trigger keeps
  its own `timeZone`, and the SPA shows a `date-time` in the browser's time zone.
- **Durations stay fixed.** Calendar durations such as `P1M` and `P1Y` stay refused in `dueIn`,
  timers, deadlines and escalations.

The detail is in [application time zone](../reference/resource-files.md#application-time-zone) and
the [expression functions](../reference/expressions.md#functions).

## Consequences

- Every user of an application gets the same answer for "today", wherever their browser is.
- Applications that name no time zone keep their behaviour.
- A business in several time zones has to pick one for each application.
- A process cannot wait for "one month". Calendar durations are only useful with business calendars,
  which are a choice of their own.

## Alternatives

- **The time zone of each user.** Two users of one application would get different dates for the same
  instant, and a sequence's year would depend on who saved the record.
