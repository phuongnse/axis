# D144. Branches need not be up to date

Status: **Agreed**

## Context

With branches that must be up to date, pull requests merged one at a time, about one per round of
checks, and each merge sent every waiting pull request through another round. Those rounds merged
nothing and spent runner jobs, usage limits and `conflicts.max_rounds`.

How many issues run at once trades the Claude usage limits and the runner jobs against how fast a
milestone ends. The right number moves with the subscriptions and the work, so the owner tunes it
often. A decision for each value made every change of a setting a change of decisions and docs.

## Decision

- **The rules on `main` do not want a branch to be up to date.** They still require NexKit's checks.
  A pull request merges once its checks and review pass on its own branch, and a merge does not send
  the waiting pull requests through another round. NexKit resolves conflicts with `main` by itself,
  with `conflicts.auto_resolve` on.
- **How many issues run at once is a setting, not a decision.** Autopilot's `max_parallel` holds it,
  and the owner changes it with a hand-made pull request to `.nexkit/config.json` alone. The docs name
  the setting and why it exists, never its value.

## Consequences

Each pull request takes one round of checks for its own change.

The tree that NexKit squash-merges is not the tree its checks passed, so two pull requests that pass
alone can still break `main` together. CI on `main` finds such a break, as
[D139](d139-ci-runs-on-main-alone-and-nexkit-checks-every-branch.md) agrees. More issues at once
make such a break more likely.

## Alternatives

- **Keep branches up to date.** Every merge tests the exact tree it merges, but pull requests merge
  one per round, and the rounds spent bringing others up to date merge nothing.
- **A fixed number of issues at once in a decision.** The docs then state the value in several
  places, and each change of the setting needs a new decision.
