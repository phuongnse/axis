# D113. Branches need not be up to date, and three issues run at once

Status: **Agreed**

The owner agreed this on 2026-10-29. It replaces the rule on `main` that a
branch is up to date before it merges, which
[delivery](../delivery.md#decisions) records, and the limit of four issues at
once that
[D105](d105-ci-on-main-runs-every-two-hours-and-four-issues-run-at-once.md)
agrees.

- **The rules on `main` do not want a branch to be up to date.** The owner
  turns off *Require branches to be up to date before merging* in the rules
  for `main`. They still require NexKit's checks. A pull request merges once
  its checks and review pass on its own branch, and a merge no longer sends
  every waiting pull request through another check round.
  - NexKit still resolves conflicts with `main` by itself, with
    `conflicts.auto_resolve` on.
- **Three issues run at once,** with autopilot's `max_parallel` at 3.

_Why:_ with branches that must be up to date, pull requests merged one at a
time, about one per check round, and each merge checked every waiting pull
request again. Those rounds merged nothing and spent runner jobs, usage limits
and `conflicts.max_rounds`. Without the rule, each pull request takes one
round of checks for its own change. Three issues at once keep a round's eight
jobs, of the twenty that GitHub Free runs at once, from waiting for a runner.
The runs were steady before the change: on 29 October, after NexKit 5.8.0,
NexKit merged seven pull requests in two hours, and every CI run on `main`
passed. The cost: the tree that NexKit squash-merges is no longer the tree its
checks passed, so two pull requests that pass alone can break `main` together.
CI on `main`, once every 5 merges as
[D112](d112-ci-codeql-and-the-performance-tests-run-on-main-every-few-merges.md)
agrees, finds such a break, and a failure that repeats opens a `bug` issue.
