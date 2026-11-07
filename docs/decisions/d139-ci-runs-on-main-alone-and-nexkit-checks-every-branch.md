# D139. CI runs on main alone, and NexKit checks every branch

Status: **Agreed**

## Context

NexKit merges many pull requests a day, and each pull request has passed its own checks. Running CI,
CodeQL and the performance tests on `main` after every merge repeats that work and takes runner jobs
from the pull requests that are waiting, of the twenty that GitHub Free runs at once. Running them on
a schedule did not work: GitHub dropped the schedules in this repository, so `main` went without CI.
Still, `main` needs these runs: they find flaky tests, suites that a check skipped, and two pull
requests that pass alone but break `main` together, see
[D113](d113-branches-need-not-be-up-to-date-and-three-issues-run-at-once.md).

NexKit acts as a GitHub App, so its merges are pushes that start `push` workflows, and the runs it
starts send `workflow_run` events, as a person's do. NexKit also checks and reviews a person's pull
request on request, with the same scripts that CI runs, and sets the statuses that the rules on
`main` require. A CI run on the same branch would run every script a second time.

## Decision

- **NexKit starts the workflows on `main` by a count of its merges:** CI once every 5 merges, CodeQL
  once every 32 and the performance workflow once every 62. NexKit counts the commits on `main` since
  a workflow's latest run there. 32 and 62 are not multiples of 5, so the three seldom start at the
  same merge.
- **No push to `main` starts them, and CI's only schedule is its weekly scan.** A push trigger for
  `main` would run them after every merge as well. Each runs on demand. The weekly run covers the
  dependency and image scans alone, as
  [D107](d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) agrees, because a
  new advisory can appear while nothing merges.
- **NexKit checks every branch, and CI runs on `main` alone.** NexKit's checks run on its own pull
  requests in each round and on a person's pull request at `/nexkit review`, so no push to a branch
  starts CI. A pull request that changes CI's own workflow file runs CI, so the change is tried before
  it merges. CodeQL scans hand-made pull requests, as
  [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md) says, because no NexKit check runs
  it.
- **A failure is noticed when the run ends.** NexKit's workflow listens for the end of each of these
  runs on `main` and starts a sweep. A failure that repeats opens a `bug` issue, and pull requests that
  fail the same way wait.

The settings are in [delivery](../delivery.md#decisions).

## Consequences

- At about 60 merges a day, CI runs about every two hours, CodeQL about twice a day and the
  performance tests about once a day. The performance tests change little from one merge to the next
  and depend on the runner's load, so a run that covers many merges is a fair measure.
- A flaky test, or a check that skipped work it should have done, shows up to five merges after the
  merge that caused it, a CodeQL alert up to 32 and a performance regression up to 62.
- The last merges before a quiet spell wait for the next merges before `main` checks them.
- The owner's merge of a hand-made pull request does not start CI on `main`. NexKit's checks ran on
  the pull request, and the next count of NexKit's merges runs CI on `main`.
- A push to a hand-made branch starts no check by itself. Whoever pushes comments `/nexkit review`,
  and the pull request cannot merge until its statuses are green.

## Alternatives

- **Run after every merge.** It repeats checks that passed on the branch and makes the waiting pull
  requests wait for runners.
- **Run on a schedule.** GitHub did not keep the schedules, and a schedule does not follow how much
  work lands.
- **Keep the push trigger for `main` and skip NexKit's merges in the job.** It runs CI after the owner's
  merges too, at the cost of a filter on the App's name in every workflow, and of a run that only
  skips after each merge.
- **Run CI on every push to a hand-made branch as well.** It runs the scripts that `/nexkit review`
  runs a second time, about ten more jobs for each push, out of the twenty that GitHub Free runs at
  once.
