# D97. Reliable test runs

Status: **Agreed**

CI on `main` fails for a fault in Axis, not for the machine it ran on, and the
slow suites take less wall time. The owner agreed these points on 2026-10-27.
All of it holds now. See
[test runs and reports](../development.md#test-runs-and-reports) for the
detail.

- **A flaky test is fixed by its cause, for every test with that cause.** The
  plan of a `bug` issue about a test that fails at times names the cause's
  class, such as an outside download, a click during an animation or a
  redraw, the real clock, state left by another test, or planner statistics.
  When the class has failed a test before, the change fixes or guards every
  test of that class, through a shared helper, a fixture or a lint rule, and
  not only the test the issue names. A flaky test still fails its job: there
  is no retry and no quarantine, because NexKit already makes a pull request
  wait while `main` fails the same way.
- **The performance tests run in a workflow of their own.** The
  `*PerformanceTests` classes run in `performance.yml` on every push to `main`
  and on demand, and no longer in CI's integration job. NexKit watches it as it
  watches CI, so a failure that repeats opens a `bug` issue.
  - This replaces the point of
    [D82](d82-performance-slos-and-load-tests.md) that they guard single
    requests on every run of CI on `main`. They still run on every push to
    `main`.
- **End-to-end tests run without motion.** The SPA turns off its animations
  when the browser asks for reduced motion, and Playwright asks for it. People
  who ask their browser for reduced motion get the same.
- **CI runs the end-to-end tests on several machines at once.** The CI
  workflow splits Playwright into shards, so a CI run takes about as long as
  its integration job. NexKit's checks run the whole suite as before.

_Why:_ from 20 to 27 October, 62 `bug` issues reported a failure on `main`.
About 25 came from outside downloads and GitHub's API, which CI and setup now
retry.
Most of the rest were fixed one test at a time, and the same cause came back
in the next test: four end-to-end failures were clicks during an opening
animation or a fading overlay, and the export performance test was fixed three
times. Timed tests depend on the shared runner's load and take about a third of
the integration job, so a timing miss marked the functional suites red. The
cost is a second workflow to watch, and a SPA that must look right without
motion.
