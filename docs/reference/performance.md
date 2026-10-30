# Performance

Detailed reference for the service level objectives of the server and the worker, and the load
test that measures them. The conventions of [architecture.md](../architecture.md) apply: sections
marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D82](../decisions/d82-performance-slos-and-load-tests.md). All of this page is
_(planned for M18)_, except [resource-demanding routes](#resource-demanding-routes), the routes of the
[objectives](#objectives) and the [script](#load-test), [data](#load-test) and [users](#load-test) of the load test, which are built. The per-request budgets of the `*PerformanceTests` classes are built. They
run in the performance workflow, as [D97](../decisions/d97-reliable-test-runs.md) says. See
[testing](../architecture.md#testing).

## Resource-demanding routes

_(Built.)_ These routes take much time, memory or database work. Each is limited so that
one caller cannot use up the server, and none builds a response that takes longer than a
caller's or a proxy's timeout, because long work is asynchronous: the route answers `202` and the
caller polls.

| Route                                                                                           | Why it is heavy                                                         | What limits it                                                                                                                                                                                                     |
| ----------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Exports of [records and data source rows](import-export.md#export)                              | One query reads up to every matching row                                | `Data:ExportQueryTimeout` (a `503` past it), `Data:ExportMaxRows` (the file stops there), rows streamed to a temporary file, so memory stays low, and `RateLimits:Session`                                         |
| [Imports](import-export.md#import)                                                              | A large file is parsed, checked and written                             | `Data:ImportMaxBytes` (a `413`), `202` and then a status poll, the worker's dry run and commit, `Data:ImportMaxRows`, a cap of 1,000 entries and 100 MiB when an `xlsx` workbook unpacks, and `RateLimits:Session` |
| [File uploads](file-fields.md#upload)                                                           | Large request bodies                                                    | `Files:MaxSize` and the field's `maxSize` (a `413`) and `RateLimits:Session`                                                                                                                                       |
| Record lists, data source rows, search and lookups                                              | Filters and sorts over large tables                                     | `Data:QueryTimeout` (a `503`), a `pageSize` of at most 100 and `RateLimits:Session`                                                                                                                                |
| [Inbound endpoint](inbound-endpoints.md#the-call) calls with `wait`                             | The call holds a connection to the caller while it waits                | `wait` of at most `PT30S`, no database connection held while waiting, a `202` when the instance is not done, and `RateLimits:Bearer`                                                                               |
| Studio validate, publish, preview, test and rollback, see [publishing](authoring.md#publishing) | A request checks and builds a whole application, and may run migrations | A `202` and a status poll, one request at a time for a tenant and application, see [the publisher](administration.md#publisher), a time limit of 10 minutes for `test`, and `RateLimits:Publishing`                |

- **Settings.** The `Data:*` and `Files:MaxSize` settings are in
  [data settings](host-settings.md#data-settings). The rate limits are per replica, see
  [rate limits](headers-and-rate-limits.md#rate-limits).
- **Limits per caller.** A rate limit counts a tenant and a user, or a tenant and a caller id for a
  bearer token, so one caller's burst never uses another's allowance.
- **Work queues.** Imports and publish requests wait in a table until the worker or the publisher
  takes them, so a burst of requests adds to the queue and not to the load of the server.

## Objectives

Each objective is a 95th percentile over one load test run, at 50 concurrent users per tenant, in
the [reference environment](#reference-environment).

| Group  | Routes                                                                                                  | Objective                              | Metric                                                                                       |
| ------ | ------------------------------------------------------------------------------------------------------- | -------------------------------------- | -------------------------------------------------------------------------------------------- |
| Reads  | Record lists and single records, data source rows, site and page metadata, the task list                | p95 under 300 ms                       | `http.server.request.duration`, by route                                                     |
| Writes | Record create, update and delete, process start, task complete, an inbound endpoint call without `wait` | p95 under 500 ms                       | `http.server.request.duration`, by route                                                     |
| Worker | Due work items: process steps, connector calls, event delivery                                          | p95 claimed within 5 s of its due time | `axis.work_items.claim_delay`, see the [worker metrics](process-execution.md#worker-metrics) |
| Errors | Every request of the run                                                                                | Under 0.1% answer `5xx`                | `http.server.request.duration`, by status code                                               |

The load test sends these routes, and a test fails when this list and `tests/load/objectives.js` differ:

| Group  | Route                                                   |
| ------ | ------------------------------------------------------- |
| Reads  | `GET /api/apps/{app}/entities/{entity}/records`         |
| Reads  | `GET /api/apps/{app}/entities/{entity}/records/{id}`    |
| Reads  | `GET /api/apps/{app}/data-sources/{dataSource}/rows`    |
| Reads  | `GET /api/sites/{path}`                                 |
| Reads  | `GET /api/sites/{path}/pages/{page}`                    |
| Reads  | `GET /api/apps/{app}/tasks`                             |
| Writes | `POST /api/apps/{app}/entities/{entity}/records`        |
| Writes | `PATCH /api/apps/{app}/entities/{entity}/records/{id}`  |
| Writes | `DELETE /api/apps/{app}/entities/{entity}/records/{id}` |
| Writes | `POST /api/apps/{app}/processes/{process}/instances`    |
| Writes | `POST /api/apps/{app}/tasks/{id}/complete`              |
| Writes | `POST /api/apps/{app}/endpoints/{name}`                 |

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

- **Script.** _(Built.)_ `scripts/load.sh` builds the production images and starts the Docker Compose
  stack with two server replicas, one worker and PostgreSQL over TLS, as in
  [several server replicas](installation.md#several-server-replicas). It starts `server` and
  then `server-2`, runs `admin migrate`, creates the tenant `load` and runs the k6
  scenario `tests/load/objectives.js` in a container through the proxy: 50 users, each in a loop over the routes of the
  [objectives](#objectives), with a pause of about 1 s an iteration. Every request goes through one helper that tags it with
  its objective (`reads`, `writes` or `signIn`) and its route. The run fails, with a non-zero exit code, when reads miss p95
  under 300 ms, writes miss p95 under 500 ms, the rate of `5xx` answers, the `server_errors` metric, is 0.1% or more, or
  fewer than 99% of its checks pass. The sign-in pages and the token request are tagged `signIn`, so they count toward no objective.
  The stack also runs the https stub `load-stub`, a Caddy service under the `load` profile whose certificate comes from the
  run's own authority and which answers every call with a fixed JSON body. The worker trusts that authority. It removes the
  stack at the end, unless `--keep` is given. It needs Docker. See [development](../development.md#scripts).
- **Data.** _(Built.)_ It creates a tenant with a generated application of
  [typical size](../domain/knowledge.md#typical-application-size), and records in every entity.
  `tests/Axis.LoadData` writes that application from a seed, so the same seed gives the same files:
  `dotnet run --project tests/Axis.LoadData -- <folder> [--seed <n>]`. It writes 100 seed records
  for each of its 40 entities and refuses a folder that already holds files. `scripts/load.sh`
  writes the application with seed 1 into its run folder and activates it in the tenant `load`,
  which stores the release and writes the seed records. The application also declares the inbound
  endpoint `CreateEntity00`, which creates an `Entity00` record from the inputs `name` and
  `quantity` and starts `Entity00Process01` on it without `wait`, and the role `load-caller`, whose
  policy grants `invoke` on that endpoint. It declares the connector `LoadService`, with one `POST`
  operation `notify` and the `url` binding `LoadServiceUrl`, and the event `Entity00Changed`. The data
  change trigger `Entity00QuantityChanged` starts the process `Entity00Announce` when an update changes the `quantity` of
  an `Entity00` record. That process raises the event. The worker delivers the event to the async event trigger
  `Entity00ChangedDelivered`, which starts the process `Entity00Notify`, and its step calls the connector. So the run
  makes `process.step`, `connector.call` and `event.deliver` work items. `scripts/load.sh` sets the binding
  to `https://load-stub:8443` for the tenant `load` before it activates the application.
- **Users.** _(Built.)_ `scripts/load.sh` adds the users `load-user-01` to `load-user-50` to the
  realm of the tenant `load`, each with a password, an authenticator whose secret comes from the
  seed and the application's roles, except `load-caller`. It writes their names, passwords and
  secrets to a file that k6 reads. It also adds the machine caller, a confidential client
  `load-caller` whose service account holds the role `load-caller` and whose tokens carry the
  audience `axis-load-endpoints`. It writes the client id and secret to `caller.json`, next to the
  users file, and runs `admin tenant oidc set load --endpoint-audience axis-load-endpoints` so that
  the tenant accepts those tokens. _(Built.)_ Each virtual user signs in once through the tenant's realm sign-in
  page, with its password and then its authenticator code, and keeps its session cookie for the whole run. It works on the
  entity `Entity{i}`, with `i` from its number modulo 40. An iteration starts the process `Entity{i}Process00` on a record
  and completes the review task of the record it started in the iteration before. The machine caller calls the
  endpoint `CreateEntity00` in every fifth iteration of each user, so the calls stay under the default of
  `RateLimits:Bearer`. The test authentication scheme is not used,
  because only test hosts register it.
- **Output.** _(Built.)_ k6 writes its summary to `artifacts/load-results/summary.json`, and the
  cleanup keeps that folder. `scripts/lib/load-objectives.mjs` prints each objective with its
  measured value and exits non-zero when one is missed or a metric is missing from the summary. The
  evaluator also reads `worker-metrics.json` and checks the claim delay p95 of the tenant `load` against
  5 s. It interpolates the percentile inside the histogram buckets, and it fails when the metric is
  missing. The k6 summary is kept as a CI artifact _(planned with the weekly run)_.
- **Worker metrics.** _(Built.)_ `scripts/load.sh` adds an OpenTelemetry Collector to the stack. The worker
  exports its metrics to it every 5 s over OTLP with https, because the worker refuses plain http
  outside Development. The collector's certificate comes from the run's own authority, which the worker
  trusts. The collector writes the metrics, as OTLP JSON with one document per line, to
  `artifacts/load-results/worker-metrics.json`. After k6 the script stops the worker and then the
  collector, so the last values reach the file, and the cleanup keeps it.
- **When it runs.** CI runs it once a week and on demand. It never gates a merge, and it is not a
  NexKit check, because a run on a shared CI machine varies too much to block a pull request.
