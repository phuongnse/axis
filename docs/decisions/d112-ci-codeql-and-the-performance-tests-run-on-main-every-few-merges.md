# D112. CI, CodeQL and the performance tests run on main every few merges

Status: **Agreed**

The owner agreed this on 2026-10-29. NexKit 5.8.0 can start a workflow once
every few of its merges. Axis uses that for CI, CodeQL and the performance
workflow, in place of their schedules. It replaces the point of
[D104](d104-codeql-and-the-performance-tests-run-every-night.md) that CodeQL
and the performance workflow run every night and that NexKit watches them
without starting them, and the point of
[D105](d105-ci-on-main-runs-every-two-hours-and-four-issues-run-at-once.md)
that CI runs on `main` every two hours.

- **NexKit starts CI on `main` once every 5 of its merges,** CodeQL once every
  32 and the performance workflow once every 62. They are in NexKit's
  `merge.after_workflows` with `every`, and `autopilot.watch_workflows` is
  empty. NexKit counts the commits on `main` since a workflow's latest run
  there, so a person's push that ran it starts the count again.
  - 32 and 62 are not multiples of 5, so CodeQL and the performance workflow
    seldom start at the same merge as CI, even after a push that ran them
    all.
- **No workflow has a schedule.** Each also runs on demand. CI also runs on a
  person's push, and CodeQL on a person's push to `main` and on hand-made pull
  requests, as [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md)
  says.
- **Each of the three ends with a job that starts a NexKit sweep.** A run that
  NexKit starts with the Actions token sends no `workflow_run` event, so
  without it a failure would wait for the next sweep. A failure that repeats
  still opens a `bug` issue, and pull requests that fail the same way wait.

_Why:_ GitHub dropped the schedules in this repository. CI's two-hourly
schedule did not run once on 29 October, and NexKit's own hourly schedule
fired every five to seven hours, so `main` got no CI after NexKit's merges at
all. A count of merges ties the runs to the work that lands and needs no
schedule. NexKit merged about 60 pull requests a day on 28 and 29 October, so
CI runs about every two hours, CodeQL about twice a day, and the performance
tests, whose results change little from one merge to the next, about once a
day. The cost: a flaky test, or a check that skipped work it should have done,
shows up to five merges after the merge that caused it, a CodeQL alert up to
32 and a performance regression up to 62, and the last merges before a quiet
spell wait for the next ones.
