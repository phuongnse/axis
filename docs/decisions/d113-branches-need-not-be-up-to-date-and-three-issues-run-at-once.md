# D113. Branches need not be up to date, and three issues run at once

Status: **Agreed**

## Context

NexKit squash-merges each pull request once its checks and review pass. When the rules on `main`
want a branch to be up to date, pull requests merge one at a time, about one per round of checks,
and each merge sends every waiting pull request through another round that merges nothing. Those
rounds spend runner jobs, usage limits and conflict rounds. How many issues run at once sets how
many rounds of checks compete for runners: a round takes eight jobs, of the twenty that GitHub Free
runs at once.

## Decision

- **The rules on `main` do not want a branch to be up to date.** They still require NexKit's checks.
  A pull request merges once its checks and review pass on its own branch, and NexKit resolves
  conflicts with `main` by itself.
- **Three issues run at once,** with autopilot's `max_parallel` at 3.

The settings are in [delivery](../delivery.md#decisions).

## Consequences

- Each pull request takes one round of checks for its own change, and three rounds at once do not
  wait for runners.
- The tree that NexKit merges is not the tree its checks passed, so two pull requests that pass alone
  can break `main` together. CI on `main`, as
  [D136](d136-ci-codeql-and-the-performance-tests-run-on-main-every-few-merges-started-by-nexkit-alone.md) runs it, finds such
  a break, and a failure that repeats opens a `bug` issue.

## Alternatives

- **Keep branches up to date.** Every merge tests the exact tree it merges, but pull requests merge
  one per round, and the rounds spent bringing others up to date merge nothing.
- **Four or more issues at once.** More rounds then wait for runners, and more issues do not merge
  faster.
