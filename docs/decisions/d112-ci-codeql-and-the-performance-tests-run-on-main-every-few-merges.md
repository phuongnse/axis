# D112. CI, CodeQL and the performance tests run on main every few merges

Status: **Agreed**

## Context

NexKit merges many pull requests a day, and each pull request has passed its own checks. Running CI,
CodeQL and the performance tests on `main` after every merge repeats that work and takes runner jobs
from the pull requests that are waiting, of the twenty that GitHub Free runs at once. Running them on
a schedule did not work: GitHub dropped the schedules in this repository, so `main` went without CI.
Still, `main` needs these runs: they find flaky tests, suites that a check skipped, and two pull
requests that pass alone but break `main` together, see
[D113](d113-branches-need-not-be-up-to-date-and-three-issues-run-at-once.md).

## Decision

- **NexKit starts the workflows on `main` by a count of its merges:** CI once every 5 merges, CodeQL
  once every 32 and the performance workflow once every 62. NexKit counts the commits on `main` since
  a workflow's latest run there, so a person's push that ran it starts the count again. 32 and 62 are
  not multiples of 5, so the three seldom start at the same merge.
- **No workflow has a schedule.** Each also runs on demand. CI also runs on a person's push, and
  CodeQL on a person's push to `main` and on hand-made pull requests, as
  [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md) says.
- **A failure is noticed when the run ends.** Each of the three ends with a job that starts a NexKit
  sweep, because a run that NexKit starts with the Actions token sends no `workflow_run` event. A
  failure that repeats opens a `bug` issue, and pull requests that fail the same way wait.

The settings are in [delivery](../delivery.md#decisions).

## Consequences

- At about 60 merges a day, CI runs about every two hours, CodeQL about twice a day and the
  performance tests about once a day. The performance tests change little from one merge to the next
  and depend on the runner's load, so a run that covers many merges is a fair measure.
- A flaky test, or a check that skipped work it should have done, shows up to five merges after the
  merge that caused it, a CodeQL alert up to 32 and a performance regression up to 62.
- The last merges before a quiet spell wait for the next merges before `main` checks them.

## Alternatives

- **Run after every merge.** It repeats checks that passed on the branch and makes the waiting pull
  requests wait for runners.
- **Run on a schedule.** GitHub did not keep the schedules, and a schedule does not follow how much
  work lands.
