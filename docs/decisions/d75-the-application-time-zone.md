# D75. The application time zone

Status: **Agreed**

M14 gives each application a time zone. The owner agreed this on 2026-10-25.
The manifest key, `today()`, `toDate` and the local year of sequences are
built. Notification dates are still _(planned for M14)_. See
[application time zone](../reference/resource-files.md#application-time-zone).
It settles the time zone rule that
[D22](d22-current-time-and-time-zones.md) waits for.

- **Manifest key:** the `application` manifest gets an optional `timeZone`, an
  IANA time zone id such as `Asia/Ho_Chi_Minh`. Without it, the time zone is
  `UTC`, so applications that exist today behave as before. An unknown id is a
  new diagnostic, `AXC0249`.
- **What uses it:**
  - `today()` gives the date in the application's time zone at the
    transaction's time. `toDate(date-time)` takes the calendar date of an
    instant in that time zone. Both are new expression functions. Neither is
    allowed in a computed field, as `now()` is not.
  - The `{yyyy}` of a sequence is the local year, and its yearly restart
    follows the local year.
  - Notifications show dates in it
    ([D73](d73-in-app-and-email-notifications.md)).
  - M15's date buckets of data sources use it.
- **Still fixed durations:** calendar durations such as `P1M` and `P1Y` stay
  `AXC0084` in `dueIn`, timers, deadlines and escalations. The owner chose
  fixed durations for M14.
- **Release:** the time zone is part of the release. A change applies to work
  that runs on the new release.
- **Unchanged:** `now()` stays an instant. A schedule trigger keeps its own
  `timeZone`. The SPA still shows a `date-time` in the browser's time zone.
- **Effect on earlier decisions:** D22 links here where its waiting points are
  settled.

_Why:_ a date such as "today" or a year in a number depends on where the
business is, and one time zone per application gives every user of it the same
answer. `UTC` as the default keeps existing applications unchanged. Calendar
durations need business calendars to be useful, which the owner left out of
M14.
