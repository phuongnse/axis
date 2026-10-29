# D104. CodeQL and the performance tests run every night

Status: **Agreed**

The owner agreed this on 2026-10-29. NexKit 5.6.0 can watch a workflow on
`main` without starting it after its merges. Axis uses that for the CodeQL and
performance workflows, so fewer jobs run after each merge. It replaces the point
of [D97](d97-reliable-test-runs.md) that the performance tests run on every
push to `main`.

- **CodeQL and the performance workflow run every night on `main`.** Each also
  runs on demand. CodeQL also scans a person's push to `main` and hand-made
  pull requests, as [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md)
  says.
- **NexKit watches them without starting them.** They are in NexKit's
  `autopilot.watch_workflows`, not in `merge.after_workflows`. A failure that
  repeats still opens a `bug` issue, and pull requests that fail the same way
  wait, as for CI.
- **CI still runs on `main` after every merge.** It stays in
  `merge.after_workflows`. Its runs on `main` find flaky tests and suites that a
  check skipped, and they let NexKit make a pull request wait while `main`
  fails the same way. A schedule for CI is a later choice, once flaky failures
  on `main` are rare.

_Why:_ `main` wants pull requests up to date, and NexKit squash-merges them, so
the tree it merges is the tree its checks just passed. A run after every merge
repeats work. On 28 and 29 October, at about three merges an hour, CI, CodeQL
and the performance workflow started about nine jobs per merge, and check jobs
of open pull requests waited 5 to 7 minutes for a runner in two of five sampled
rounds. The performance tests depend on the runner's load, so a nightly run is
a fairer measure. The cost: a CodeQL alert or a performance regression shows up
to a day after the merge that caused it, and a run then covers every merge of
that day.
