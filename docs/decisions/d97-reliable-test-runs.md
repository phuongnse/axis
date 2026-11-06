# D97. Reliable test runs

Status: **Agreed**

## Context

CI on `main` should fail for a fault in Axis, not for the machine it ran on. Flaky tests were fixed
one test at a time, and the same cause came back in the next test: clicks during an opening
animation or a fading overlay, outside downloads, timed tests on a loaded runner. Timed tests also
take a large part of the integration job and mark the functional suites red when they miss.

## Decision

- **A flaky test is fixed by its cause, for every test with that cause.** The plan of a `bug` issue
  about a test that fails at times names the cause's class. When the class has failed a test before,
  the change fixes or guards every test of that class, through a shared helper, a fixture or a lint
  rule. A flaky test still fails its job: there is no retry and no quarantine, because NexKit already
  makes a pull request wait while `main` fails the same way.
- **The performance tests run in a workflow of their own,** outside CI's integration job, and a
  failure that repeats opens a `bug` issue. When NexKit starts it is
  [D139](d139-ci-runs-on-main-alone-and-nexkit-checks-every-branch.md)'s choice.
- **End-to-end tests run without motion.** The SPA turns off its animations when the browser asks
  for reduced motion, and Playwright asks for it.
- **CI runs the end-to-end tests on several machines at once,** as shards, and NexKit's checks run
  the same shards as jobs of their own.

The classes, their guards and the shards are in
[test runs and reports](../development.md#test-runs-and-reports).

## Consequences

- A cause is fixed once, not once for each test it breaks.
- A timing miss does not mark the functional suites red, and a CI run takes about as long as its
  integration job.
- There is a second workflow to watch.
- The SPA must look right without motion, for people who ask their browser for reduced motion too.

## Alternatives

- **Retry or quarantine flaky tests.** It hides the cause, which comes back in the next test.
