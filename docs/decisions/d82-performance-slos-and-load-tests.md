# D82. Performance SLOs and load tests

Status: **Agreed**

M16 sets service level objectives for the server and the worker, and a load
test that measures them. It builds on the per-request budgets of the
`*PerformanceTests` classes, the
[typical application size](../domain/knowledge.md#typical-application-size)
and the two server replicas of
[D49](d49-several-server-replicas-behind-a-tls-proxy.md). The objectives and the
load test are _(planned for M18)_. See [performance](../reference/performance.md) for the
detail. The owner agreed these points on 2026-10-25.

- **Objectives:**
  - Each objective is a 95th percentile response time for one group of
    routes, measured at 50 concurrent users per tenant.
  - Reads answer within 300 ms at the 95th percentile: record lists and single
    records, data source rows, site and page metadata, and the task list.
  - Writes answer within 500 ms at the 95th percentile: record create, update
    and delete, process start, task complete and an inbound endpoint call
    without `wait`.
  - The worker claims due work within 5 seconds of its due time at the 95th
    percentile.
  - Fewer than 0.1% of requests in a run fail with a `5xx`.
- **Reference environment:**
  - Two server replicas behind the TLS proxy of D49, one worker and one
    PostgreSQL server.
  - Each replica, the worker and PostgreSQL run on a machine of 4 vCPU and
    8 GB of memory.
  - The numbers hold only for this environment. An installation measures its
    own.
- **Metrics:** each objective maps to a metric that a host already exports,
  such as `http.server.request.duration` and the worker metrics. An operator
  can watch the same numbers in production. Axis builds no alerting.
- **Load test:**
  - A new script, `scripts/load.sh`, runs k6 in a container against the
    Compose stack with two server replicas.
  - Its data is a generated application of typical size, with records in
    every entity.
  - Each virtual user signs in once through the identity provider's sign-in
    page and keeps its session. The test authentication scheme is not used,
    because only test hosts register it.
  - It prints each objective with its measured value, and fails when one is
    missed.
- **When it runs:**
  - CI runs it once a week and on demand. The results are kept as a run
    artifact.
  - It never gates a merge, and it is not a NexKit check. A run on a shared
    CI machine varies too much to block a pull request.
  - The `*PerformanceTests` classes stay as they are. They guard single
    requests on every run of CI on `main`.
    - [D97](d97-reliable-test-runs.md) replaces this line: they run in a
      workflow of their own on every push to `main`.
- **Scope:** the scenarios cover the routes that exist when M16 starts. A
  milestone that adds a route in a group above adds it to the load test.

_Why:_ see [typical application size](../domain/knowledge.md#typical-application-size).
A budget per request shows that one call is fast alone. It does not show what
happens when many users share a tenant, a replica and a database. Fixing the
environment makes two runs comparable, and keeping the test out of merges
keeps a noisy machine from blocking work. The cost is that a slowdown shows up
in the weekly run, after the merge that caused it.
