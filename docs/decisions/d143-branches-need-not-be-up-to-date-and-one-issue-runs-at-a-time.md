# D143. Branches need not be up to date, and one issue runs at a time

Status: **Agreed**

## Context

With branches that must be up to date, pull requests merged one at a time, about one per round of
checks, and each merge sent every waiting pull request through another round. Those rounds merged
nothing and spent runner jobs, usage limits and `conflicts.max_rounds`.

Several issues at once also spend the Claude usage limits several times as fast. Each issue runs an
agent session, a review and its fixes, so a few issues at once reach the limit of both
subscriptions well before it resets, and NexKit then pauses with work half done.

## Decision

- **The rules on `main` do not want a branch to be up to date.** They still require NexKit's checks.
  A pull request merges once its checks and review pass on its own branch, and a merge does not send
  the waiting pull requests through another round. NexKit resolves conflicts with `main` by itself,
  with `conflicts.auto_resolve` on.
- **One issue runs at a time,** with autopilot's `max_parallel` at 1.

## Consequences

Each pull request takes one round of checks for its own change, and the usage limits last for one
issue's work at a time, so NexKit pauses less often. A milestone takes longer, because its issues
no longer overlap.

The tree that NexKit squash-merges is not the tree its checks passed, so two pull requests that pass
alone can still break `main` together. CI on `main` finds such a break, as
[D139](d139-ci-runs-on-main-alone-and-nexkit-checks-every-branch.md) agrees.

## Alternatives

- **Three issues at once.** A round's checks fit in the runner jobs that GitHub Free runs at once,
  but the usage limits ran out before they reset.
