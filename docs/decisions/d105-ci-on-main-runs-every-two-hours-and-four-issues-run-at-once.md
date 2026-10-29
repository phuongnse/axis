# D105. CI on main runs every two hours, and four issues run at once

Status: **Agreed**

The owner agreed this on 2026-10-29. It replaces the point of
[D104](d104-codeql-and-the-performance-tests-run-every-night.md) that CI runs
on `main` after every merge, and the limit of three issues at once that
[D68](d68-issues-of-several-milestones-run-at-once.md) records.

- **CI runs on `main` every two hours.** It also runs on a person's push and on
  demand. NexKit does not start it after its merges: it is in NexKit's
  `autopilot.watch_workflows` with CodeQL and the performance workflow, and
  `merge.after_workflows` is empty. A failure that repeats still opens a `bug`
  issue, and pull requests that fail the same way wait, as before.
  - [D112](d112-ci-codeql-and-the-performance-tests-run-on-main-every-few-merges.md)
    replaces this point: NexKit starts CI on `main` once every 5 of its
    merges, because GitHub dropped the schedule.
- **Four issues run at once,** with autopilot's `max_parallel` at 4.
  - The owner raises it to five only when a day at four shows more merges an
    hour, and check jobs that do not wait longer for a runner.
  - [D113](d113-branches-need-not-be-up-to-date-and-three-issues-run-at-once.md)
    replaces this point: three issues run at once, and branches need not be
    up to date.

_Why:_ `main` wants pull requests up to date, and NexKit squash-merges them, so
the tree it merges is the tree its checks just passed. CI after every merge
took six of the twenty jobs that GitHub Free runs at once, at about three merges
an hour. On 29 October, with three issues at once, the check jobs of one round
waited up to 4.6 minutes for a runner. A round of checks takes eight jobs, so
the freed jobs make room for a fourth issue. More than that waits at the merge:
pull requests that must be up to date merge about one per check round of 12 to
15 minutes, and with six issues at once on 27 and 28 October, about 1.9 pull
requests merged an hour against 2.5 to 3 with three. The cost: a flaky test or
a check that skipped work it should have done shows up to two hours after the
merge that caused it, and fewer runs on `main` find fewer flaky tests.
