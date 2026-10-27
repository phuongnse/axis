# Performance

Detailed reference for the service level objectives of the server and the worker, and the load
test that measures them. The conventions of [architecture.md](../architecture.md) apply: sections
marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D82](../decisions/d82-performance-slos-and-load-tests.md). All of this page is
_(planned for M16)_. The per-request budgets of the `*PerformanceTests` classes are built. They
move to a workflow of their own, as [D97](../decisions/d97-reliable-test-runs.md) says. See
[testing](../architecture.md#testing).

## Objectives

Each objective is a 95th percentile over one load test run, at 50 concurrent users per tenant, in
the [reference environment](#reference-environment).

| Group  | Routes                                                                                                                                                       | Objective                             | Metric                                         |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------- | ---------------------------------------------- |
| Reads  | Record lists and single records, data source rows, site and page metadata, the task list                                                                     | p95 under 300 ms                      | `http.server.request.duration`, by route       |
| Writes | Record create, update and delete, process start, task complete, an inbound endpoint call without `wait`                                                      | p95 under 500 ms                      | `http.server.request.duration`, by route       |
| Worker | Due work items: process steps, connector calls, event delivery                                                                                               | p95 claimed within 5 s of its due time | The [worker metrics](process-execution.md#worker-metrics) |
| Errors | Every request of the run                                                                                                                                     | Under 0.1% answer `5xx`               | `http.server.request.duration`, by status code |

- **Same metrics in production.** Each objective uses a metric that the hosts already export, so an
  operator can watch the same numbers on a running installation. Axis builds no alerting.
- **New routes.** A milestone that adds a route in one of the groups adds it to the load test, and
  the route takes that group's objective.

## Reference environment

- Two server replicas behind the TLS proxy, as in
  [several server replicas](installation.md#several-server-replicas).
- One worker.
- One PostgreSQL server that holds the platform database and the tenant databases.
- Each replica, the worker and PostgreSQL run on a machine of 4 vCPU and 8 GB of memory.

The objectives hold only for this environment. An installation on other machines runs the load test
to find its own numbers.

## Load test

- **Script.** `scripts/load.sh` runs k6 in a container against the Docker Compose stack with two
  server replicas. It needs Docker. See [development](../development.md#scripts).
- **Data.** It creates a tenant with a generated application of
  [typical size](../domain/knowledge.md#typical-application-size), and records in every entity.
- **Users.** Each virtual user signs in once through the development realm's sign-in page and keeps
  its session cookie for the whole run. The test authentication scheme is not used, because only
  test hosts register it.
- **Output.** It prints each objective with its measured value and exits non-zero when one is
  missed. The k6 summary is kept as a CI artifact.
- **When it runs.** CI runs it once a week and on demand. It never gates a merge, and it is not a
  NexKit check, because a run on a shared CI machine varies too much to block a pull request.
