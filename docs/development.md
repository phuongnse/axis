# Development

How to run Axis on one machine, what the scripts do, and the development data they set up: the
database roles, the Keycloak realm and its users, and the preview tenant. Production installs follow
[installation](reference/installation.md). The short list of commands is in
[AGENTS.md](../AGENTS.md#commands).

## Scripts

Run from the repository root. NexKit and CI run the scripts from `scripts/setup.sh` to
`scripts/smoke.sh`. The others say which workflow runs them.

| Script                   | What it does                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Needs   |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| `scripts/setup.sh`       | Restores .NET packages, installs npm packages and the Playwright browser, and installs pinned Helm, kubeconform and the Kubernetes schemas into `~/.cache/axis-tools`. Each step and each tool download gets up to 4 tries, waiting 15, 30 and 60 seconds between them, so a short refusal such as a 403 is retried. On CI the Playwright system packages install as a step of their own                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Network |
| `scripts/build.sh`       | Builds the SPA into the server web root and checks its size against the budget in `web/bundle-budget.json`, which prints the gzipped size of the main bundle and of the chart chunk and fails when the main bundle grew more than 50 KB over its size before M12 or when the chart code is not in its own chunk, then the .NET solution, which installs the sample extension package in `artifacts/extensions`, then installs the sample modules in `artifacts/modules` and the sample templates in `artifacts/templates`                                                                                                                                                                                                                                                                                                                                                                                                                                              |         |
| `scripts/lint.sh`        | `dotnet format` check, oxlint, TypeScript and Prettier checks, the check that no test selects an `ant-` class, the check that every Playwright locator matches a string name or text exactly, the check that no file under `web/` writes raw HTML, the check that no test outside a `*PerformanceTests` class measures elapsed time, the Markdown link and anchor check, the docs structure check, and the Helm chart lint and schema check                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |         |
| `scripts/test.sh`        | .NET unit tests, Vitest and the tests of the scripts themselves. It installs the sample modules and templates in `artifacts/` first, for the server tests                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |         |
| `scripts/integration.sh` | .NET tests against real PostgreSQL and Keycloak through Testcontainers, which also run the test scenarios of every application under `samples/apps/` and of every sample template under `samples/templates/` on that PostgreSQL. Both are skipped when `NEXKIT_BASE_SHA` is set and every changed file is on the safe list in `scripts/lib/affected.mjs`. It leaves out the performance tests, whose class names end in `PerformanceTests`, which `scripts/performance.sh` runs. With arguments, which go to `dotnet test`, such as `--filter-class 'Axis.Integration.Tests.Studio*'`, it runs only the matching tests, without the scenario suites                                                                                        | Docker  |
| `scripts/e2e.sh`         | Starts PostgreSQL, Keycloak, the built purchasing system stub, server, worker and publisher, creates the `journey` tenant for Studio journeys with its own database `axis_journey`, roles `axis_journey_schema` and `axis_journey_runtime`, and server on `127.0.0.2`, which the worker and the publisher serve too, builds and checks the design-system bundle in `artifacts/design-system` for its spec, and runs Playwright in Chromium. Playwright and the services it starts are skipped when `NEXKIT_BASE_SHA` is set and every changed file is under `docs/` or is a Markdown file. With arguments, which go to Playwright, such as `tests/e2e/specs/studio.spec.ts` or `--grep 'publishes'`, it runs only the matching specs, after the sign-in setup. With `AXIS_E2E_SHARD`, such as `1/3`, it runs only that shard of Playwright | Docker  |
| `scripts/smoke.sh`       | The smoke tests, each skipped when `NEXKIT_BASE_SHA` is set and every changed file is under `docs/` or is a Markdown file, the safe list in `scripts/lib/affected.mjs`: the `dev-script` test starts `scripts/dev.sh` in a copy of the repository, the `compose` test runs the Compose stack, the `images` test builds and runs the production images, and the `onprem` test installs `deploy/compose/` from the production images for two tenants. It installs the sample modules in `artifacts/modules` first, for the `onprem` test. CI and NexKit each run it as a job of its own | Docker  |
| `scripts/performance.sh` | The performance tests of the integration tests, whose class names end in `PerformanceTests`, alone and one class at a time, against real PostgreSQL and Keycloak. It writes `artifacts/test-results/performance.xml`. The performance workflow runs it on every push to `main`. It is not a NexKit check. With arguments, which go to `dotnet test` in place of its class filter, such as `--filter-class '*SearchPerformanceTests'`, it runs only the matching classes | Docker  |
| `scripts/scan.sh`        | NuGet and npm vulnerability scan that fails on high or critical findings, unless the finding is in `scripts/scan-accepted.json` and its time frame has not ended, see [dependency updates](#dependency-updates). CI is to run it on its triggers and once a week _(planned for M16)_                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Network |
| `scripts/load.sh`        | _(Planned for M16.)_ The load test: k6 in a container against the Compose stack with two server replicas, on a generated application of typical size. It prints each performance objective with its measured value and fails when one is missed. CI runs it once a week and on demand. It is not a NexKit check and gates no merge. See [performance](reference/performance.md)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | Docker  |

The CodeQL workflow, `.github/workflows/codeql.yml`, scans C# and TypeScript on pull requests to
`main` and on `main`. Its alerts go to GitHub code scanning, and its check fails on a new high or
critical alert. It is not a NexKit check and does not run on NexKit's branches. NexKit starts it on
`main` after each merge.

The performance workflow, `.github/workflows/performance.yml`, runs `scripts/performance.sh` on
every push to `main` and on demand, and NexKit starts it after each merge. A failure gates no merge,
but NexKit watches the workflow as it watches CI, so a failure that repeats opens a `bug` issue. Its
results are the `test-results-performance` artifact.

The bundle budget, `web/bundle-budget.json`, keeps the SPA's first load small. The main bundle is the
entry script of `index.html` and the scripts it preloads, gzipped. Its baseline is 537,019 bytes,
built from commit `ad13bd4`, the last commit before any M12 code. It may grow by 50 KB over that.
The chart library loads in its own lazy chunk, the file `assets/ChartDrawing-*.js`. The page
designer's canvas and palette load in another, `assets/StudioPageDesign-*.js`, when the Design tab
opens. The form designer's Design tab loads as `assets/StudioFormDesign-*.js`, when it first opens.
The process designer's Design tab loads as `assets/StudioProcessDesign-*.js`, when it first opens.
All three designers share the drag and drop library, `@dnd-kit`, in `assets/DragAndDrop-*.js`. It
measures 15.4 KB gzipped. `scripts/build.sh` runs [`scripts/check-bundle.mjs`](../scripts/check-bundle.mjs)
after the SPA build. It prints the five sizes and fails when the main bundle is over budget, when
there is no chart chunk, page design chunk, process design chunk or drag and drop chunk or when any of
them is among the main bundle files. Do not raise the budget to make the check pass: find what grew.

`scripts/build.sh` then runs [`scripts/check-fonts.mjs`](../scripts/check-fonts.mjs) on the same
output. It lists the font files in `assets/` and fails when a file is not IBM Plex Sans or IBM Plex
Mono in the Latin, Latin Extended or Vietnamese subset, when one of the six combinations has no
`.woff2` file, or when a license text is missing from `fonts/`.

## Dependency updates

Axis keeps its third-party packages current, and sets how fast a vulnerable one must change.

**Inventory.** These files list every dependency and its version.

- `Directory.Packages.props` holds the central version of each direct NuGet package.
  `dotnet list package --include-transitive` lists the transitive ones, which the scan reads too.
- `web/package-lock.json` and `tests/e2e/package-lock.json` pin every npm package.
- `global.json` pins the .NET SDK.

**Sources.** Packages come from nuget.org and the npm registry, `registry.npmjs.org`, and nowhere
else. The repository has no `nuget.config` and no `.npmrc`, so no other source is set. A change
that adds a source changes this section too.

**Time frames.** The time starts on the day the scan first finds the vulnerability.

| Severity | Time frame to update or replace the package |
| --- | --- |
| Critical | 7 days |
| High | 30 days |
| Moderate | 90 days |
| Low | The next routine update |

- **Routine update.** At least once per milestone, every dependency moves to its latest compatible
  version, whether or not it has a finding.
- **What the scan enforces.** `scripts/scan.sh` enforces the time frames of critical and high
  findings, see [D50](decisions/d50-sign-in-audit-records-and-security-scanning-in-ci.md). It prints
  moderate and low findings and passes. People keep their time frames in the routine update.
- **A fix that does not exist yet.** When no fixed version exists, record the finding in
  `scripts/scan-accepted.json`: `{ "advisory": "GHSA-...", "package": "<name>", "found": "YYYY-MM-DD",
  "reason": "<why it waits>" }`. `found` is the day the scan first found it. The scan then passes
  the finding until `found` plus its time frame, and prints `accepted until <date>`.
- **Past the deadline.** After that date the scan fails again and prints
  `past its time frame since <date>` on the finding's line. Update the package, or remove the
  package, and delete the entry.
- **A bad entry.** An entry with no advisory, no package, no date, an invalid date or a date in the
  future fails the scan and names the entry. An entry that matches no finding does nothing, so
  delete it when its package is updated.

## Test runs and reports

The test, integration and E2E scripts run every suite even when one fails. The exceptions are
the suites that NexKit's `NEXKIT_BASE_SHA` shows no change can affect. The integration tests and
their scenarios use their safe list. Playwright and the smoke tests use the smaller list of `docs/`
and Markdown files. The lists are in [`scripts/lib/affected.mjs`](../scripts/lib/affected.mjs), and
any doubt runs the suite: a missing base, a git error or an empty change list. A performance test
class name ends in `PerformanceTests`. In the integration tests such a class stays out of
`scripts/integration.sh`, so neither NexKit's checks nor CI's integration job runs it, and
`scripts/performance.sh` runs it. The class joins its project's performance collection, which runs
alone, one class at a time. A guard test fails when
a `*PerformanceTests` class is outside that collection. A skipped suite is listed
as skipped. Without `NEXKIT_BASE_SHA`, as in CI and local runs, every suite runs. Each suite writes a JUnit file to `artifacts/test-results/`. The E2E server output
goes to `artifacts/logs/e2e-server.log`, the E2E worker output to
`artifacts/logs/e2e-worker.log`, the E2E journey server output to `artifacts/logs/e2e-journey-server.log`, the E2E publisher output to `artifacts/logs/e2e-publisher.log`, and the
purchasing system stub output to `artifacts/logs/e2e-purchasing.log`. `scripts/test-report.mjs` then prints a
summary per suite and the failed tests with their `file:line`. On GitHub Actions
the summary goes to the job summary, and each failed test becomes an error
annotation. The failed tests are also listed at the end of the job log, with each
message cut to 20 lines.

Playwright does not retry a failed test, on CI or locally, so a flaky test fails its job. The
trace of a failed test is kept. On `main`, NexKit runs a failed job once more. A run that passes
only on that second attempt is recorded on a `bug` issue.

A test that fails at times is fixed by its cause, for every test with that cause, as
[D97](decisions/d97-reliable-test-runs.md) says. The plan of its `bug` issue names the cause's
class. When that class has failed a test before, the change fixes or guards every test of the
class, through a shared helper, a fixture or a lint rule. The classes seen so far and their guards:

- An outside download or service: `scripts/setup.sh`, the CI install steps and the Docker builds of the
  smoke tests try again.
- A click during an animation or a redraw: Playwright asks for reduced motion, so the SPA shows
  dialogs and overlays without animation. `waitForDialog`, `confirmDialog` and the drag helper wait
  until the element is ready. The drag helper, and the binding choice in the journeys, also wait
  until the page canvas has drawn the dropped draft (`waitForCanvas`).
- The real clock: a test fixes the time with `axis.now` and runs schedules in UTC.
- State left by another test: a test counts from its own baseline.
- Planner statistics: a seed that a plan depends on runs `ANALYZE` over every row.

CI splits Playwright into three shards that run as parallel jobs, as
[D97](decisions/d97-reliable-test-runs.md) says, and NexKit runs the same shards as its checks `e2e-1`
to `e2e-3`, each in a job of its own.
To run one shard, set `AXIS_E2E_SHARD`, such as `AXIS_E2E_SHARD=2/3 scripts/e2e.sh`. Its Playwright
results go to `artifacts/test-results/playwright-<n>.xml`. The smoke tests run in a CI job of their own,
from `scripts/smoke.sh`, so the integration job and the shards hold only their own tests.

The SPA turns off its animations when the browser asks for reduced motion, and Playwright asks for
it, so `waitForDialog` and `confirmDialog` return at once. A spec that slows animations on purpose
to prove a wait asks for motion again with `test.use({ reducedMotion: 'no-preference' })`.

Each application's test scenarios are a suite of their own, named `scenarios-<application name>`.
The integration tests run them and write `artifacts/test-results/scenarios-<application name>.xml`.
A failure names the scenario and the step. To run them by hand, run
`dotnet run --project src/Axis.Scenarios -- <folder> --server <cs> --extensions artifacts/extensions --modules artifacts/modules`.

## Production images and Axis.Admin

The `Dockerfile` also has three production targets, `axis-server`, `axis-worker` and `axis-admin`,
which hold no samples, development settings or source control metadata. Build one with
`docker build --target axis-server --build-arg AXIS_VERSION=<version> .`. `scripts/smoke.sh` builds
and runs all three in the `images` suite, from `scripts/images.test.mjs`. The default target is still
the development server. The `images` and on-premises suites build each image up to three times, and wait
15 seconds, then 30 seconds, between attempts, because a download in the build can fail at times.

`Axis.Admin` is the console tool that the platform operator runs. It creates, suspends,
resumes, lists, backs up and restores tenants, and it migrates and activates them. Run it with
`dotnet run --project src/Axis.Admin -- <command>`, or with the `axis-admin` image. See
[administration](reference/administration.md). The production install is in `deploy/compose/`
and `deploy/helm/axis/`, see [installation](reference/installation.md).

## Docker Compose

To run Axis, run `docker compose up --build`. It builds Axis from source and starts
PostgreSQL, Keycloak with the `axis` development realm at http://localhost:8080, the server
with the purchase request, leave request, service desk and asset management samples at http://localhost:5206, the worker, the publisher, and the purchasing
system stub at http://localhost:5207. The stub, in `tests/Axis.PurchasingStub`, stands in for
the purchasing system that the sample calls when a request is approved. It needs only
Docker. The images hold the `departments` sample module in `/modules`.
Compose also starts one publisher, `axis-admin publisher`, which runs the
publish requests of Studio. See [D47](decisions/d47-publishing-from-studio.md).
It serves the tenants `default` and `default-preview`, so it runs preview requests too. It has no port and no health check, and writes `default ready` and `default-preview ready` once the server has migrated each tenant database.
`docker compose logs server` shows the activation diagnostics,
`docker compose logs worker` shows the worker output and `docker compose logs publisher` shows the publisher output. `docker compose ps` shows the worker
healthy once its `/health/ready` passes, and `docker compose up --wait` waits for that. The server has 5 minutes and the worker 6 minutes to become healthy before `docker compose up --wait` fails. The worker's
health address `http://127.0.0.1:5208` stays inside its container. The Keycloak admin console login is
`admin` / `admin`. Set `AXIS_SERVER_PORT`, `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT` and
`AXIS_PURCHASING_PORT` to change the host ports (5206, 5432, 8080 and 5207). All four ports
are published on `127.0.0.1` only. The server creates Studio's new applications in
`artifacts/studio-apps`, which the checkout mounts at `/studio-apps`. Set `AXIS_STUDIO_APPS_DIR`
to move that folder. On Linux the server runs as UID 1654, so create the folder first and make it
writable by that UID. Docker creates a missing folder as root.

Compose runs no mail server, so the worker logs one warning per tenant and sends no email, and the
emails of `notify` steps stay queued. Set `Email__Smtp__Host`, `Email__Smtp__Port`,
`Email__Smtp__UseTls` and `Email__From` on the worker to turn sending on. Only the Development
environment, which Compose runs, sends email with `Email__Smtp__UseTls` off. See
[mail settings](reference/notifications.md#mail-settings).

## Database roles

The integration test fixtures create two database
roles, `axis_schema` and `axis_runtime`, with the development password `axis`. They own each test
database with `axis_schema`, run servers and workers as `axis_runtime`, and run startup activation as
`axis_schema`. Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the same. PostgreSQL loads
`samples/postgres/init-roles.sql` on its first start, which creates the two roles and makes
`axis_schema` own the tenant database. It also creates the preview tenant's database `axis_preview` and
its two roles, `axis_preview_schema` and `axis_preview_runtime`, with the development password `axis`,
which play the same parts for `default-preview`. `init-roles.sql` also creates `axis_scenario`, a role with `CREATEDB` and the development password `axis`. It owns no database. Compose, `scripts/dev.sh` and `scripts/e2e.sh` give it to the publisher as `Scenarios__ServerConnectionString`, so Studio can run test scenarios, see [D55](decisions/d55-running-test-scenarios-from-studio.md). The server turns on `Studio:Tests`: E2E sets `Studio__Tests=true`, and Compose and `scripts/dev.sh` use `appsettings.Development.json`. An existing local Compose volume has no `axis_scenario`, so it needs `docker compose down -v` once, which deletes its data. The server uses `axis_schema` for `SchemaConnectionString` and
`ConnectionStrings:PlatformSchema`, and `axis_runtime` for every other connection string. The worker uses only `axis_runtime` and gets no
`SchemaConnectionString`. `axis` stays the bootstrap superuser, for setup and health checks only.
PostgreSQL runs init scripts only on an empty volume, so an existing local Compose volume has no
`axis_preview` database. It needs `docker compose down -v` once. That deletes its data. Until then
the server stops at start, because it cannot reach `axis_preview`. See
[database roles](reference/storage.md#database-roles).

## Development realm and users

Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` start Keycloak with the development
realm imported from `samples/keycloak/axis-realm.json`. The development password is `axis`
for every user. The [development users table](reference/security.md#development-users) lists each
user and the roles they hold. The `operator` role opens the purchase request sample's Approvals
inspector page. The leave request sample, `samples/apps/leave-requests`, reuses the `employee` and
`department-manager` users, in a site at `/leave`. The service desk sample, `samples/apps/service-desk`,
adds the `agent` user and two `service-desk-lead` users and gives an employee the `requester` role, in a site
at `/service-desk`. The asset management sample, `samples/apps/asset-management`, adds the `asset-manager` user `quan`
and the `auditor` user `thao`, and uses `lan`, an employee, as the holder of assets, in a site at `/asset-management`.
The request-approval template journey, `tests/e2e/specs/request-approval-journey.spec.ts`,
adds the `approver` user, `minh`. The `author` role lets the authoring API list and read the resources of every sample under
`samples/apps/` under `scripts/dev.sh`, and opens the Studio page at http://localhost:5173/studio.
Studio saves into those committed folders, so `git checkout -- samples/apps` reverts an edit.
The `publisher` role lets the authoring API publish, shows **Publish** on the Studio page and allows
rollback from the release history. Under `scripts/dev.sh`,
the server signs people in through Keycloak at `/api/auth/sign-in`, and the session then
answers `GET /api/me`. The SPA header offers sign-in and sign-out there. Sign-in through
Compose is not wired yet: the server in its container cannot reach Keycloak at the address the
browser uses. The site endpoints, the record API and the data source endpoint need a session, so
under Compose they answer `401`, and an application page shows the sign-in prompt, whose button starts a sign-in that cannot complete yet.
See [sign-in and the session](reference/security.md#sign-in-and-the-session).
The realm also has the client `axis-fapi` for the `fapi2` sign-in profile. Only an integration
test host uses it, and it signs in with the development keys in `samples/keycloak`, see
[FAPI 2.0 profile](reference/security.md#fapi-20-profile).

## Hot reload with `scripts/dev.sh`

For local development with hot reload, run `scripts/dev.sh`. It needs Docker, .NET and Node, and
works on a fresh clone:

- It installs the SPA packages when `web/node_modules` is missing or
  `web/package-lock.json` changed since the last install. It installs no
  Playwright browser.
- It starts PostgreSQL, Keycloak and the purchasing system stub with Docker
  Compose. It waits until PostgreSQL accepts connections, Keycloak's `axis`
  realm answers and the stub answers. The first run builds the stub's image.
- It builds the sample extension package, `samples/packages/PaymentReferences`, into
  `artifacts/extensions`, where the Development settings of the server and the worker
  point `Extensions:Path`. The purchase request sample declares the package, so it
  does not activate without it. A change to the package's C# code needs a restart of
  the script.
- It installs the sample modules, `samples/modules/*`, into
  `artifacts/modules/<id>/<version>/`, where the Development settings of the server and
  the worker point `Modules:Path`. The server reads the module catalog only when it starts,
  so a module edit needs a restart of the script.
- It installs the sample templates, `samples/templates/*`, into
  `artifacts/templates/<id>/<version>/`, and creates that folder even when there is no
  sample. The Development settings of the server point `Templates:Path` there, and so do
  the E2E settings. The Compose server sets it to `/templates`, which holds the
  `request-approval` sample template. The server reads the template catalog only when it starts, so a template
  edit needs a restart of the script. The production images and `deploy/` set no
  `Templates:Path`, so they list no template.
- E2E's journey server sets `Studio:ApplicationsPath` to a temporary folder. `scripts/dev.sh` and
  the Compose server set it to `artifacts/studio-apps`, which `artifacts/` keeps out of git. An
  exported `Studio__ApplicationsPath` replaces that folder in `scripts/dev.sh`. See
  [D51](decisions/d51-creating-applications-and-resources-from-studio.md).
- It runs the server and the worker under `dotnet watch` and the SPA on Vite in
  one terminal. It starts the worker once the server has migrated the tenant
  databases. Each line starts with `[server]`, `[worker]` or `[web]`, and the
  script's own messages start with `[dev]`. The SPA proxies `/api` and `/health`
  to the server.
- It starts no mail server, so the worker logs one warning per tenant and sends no email, and the
  emails of `notify` steps stay queued. Export `Email__Smtp__Host`, `Email__Smtp__Port`,
  `Email__Smtp__UseTls` and `Email__From` to turn sending on. The script runs Development, the only
  environment that sends email with `Email__Smtp__UseTls` off. See
  [mail settings](reference/notifications.md#mail-settings).
- It also runs one publisher, `axis-admin publisher`, with
  its lines starting with `[publisher]`. It starts once the server has
  migrated the tenant databases. It runs from the built `Axis.Admin.dll`, not under
  `dotnet watch`, so a change to its code needs a restart of the script. The script
  sets its tenant settings as environment variables of that process only, because
  `Axis.Admin` reads no `appsettings` file. They cover `default` and `default-preview`,
  so it runs preview requests too. See
  [publisher](reference/administration.md#publisher).
- Development, Compose and E2E configure one preview tenant, `default-preview`, with
  `PreviewOf: default`, where an author tries an application before publishing it. See
  [D48](decisions/d48-preview-tenants.md). It has its own database `axis_preview`
  and its own roles, `axis_preview_schema` and `axis_preview_runtime`. It has no deployment
  bindings, so the purchase request sample logs `AXC0100` warnings on it, and a connector call
  there fails. It signs people in through the same Keycloak realm as `default`. The server
  activates the purchase request, leave request, service desk and asset management samples on it at start. The worker runs it too,
  with only its runtime connection string. Its host is `preview.localhost`: Compose and
  `scripts/dev.sh` serve it at `http://preview.localhost:5206`, and `scripts/dev.sh` also serves it
  through the SPA at `http://preview.localhost:5173`. The E2E server and worker serve it on
  `localhost`, while `default` stays on `127.0.0.1`. The realm accepts sign-in and sign-out
  redirects to those addresses. An existing local Compose volume needs `docker compose down -v`
  once, because it has no `axis_preview` database.
- `dotnet watch` applies C# changes, or restarts the server or the worker without
  a prompt when it cannot. Vite applies SPA changes.
- A change to any `*.json` file under an activated application folder restarts the
  server once, and the server activates the edited configuration again. The
  script watches the `ActivateOnStartup` folders itself, and prints a `[dev]` line
  when it restarts the server. The worker and the SPA keep running. If the edited
  configuration does not activate, the server exits on its own and the script stops
  with a non-zero code.
- `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT`, `AXIS_PURCHASING_PORT`,
  `AXIS_SERVER_PORT` and `AXIS_WEB_PORT` move the five ports. The defaults are
  5432, 8080, 5207, 5206 and 5173. When `AXIS_POSTGRES_PORT` is set, the script points the server's
  connection strings and the worker's `Tenants__default__ConnectionString` at
  that port. When `AXIS_KEYCLOAK_PORT` is set, it points the server's
  `Tenants__default__Oidc__Authority` at Keycloak on that port. When
  `AXIS_PURCHASING_PORT` is set, it points the server's and the worker's
  `Tenants__default__Bindings__PurchasingSystemUrl__Value` at the purchasing
  system stub on that port.
- The worker serves `/health/live` and `/health/ready` at `http://127.0.0.1:5208` and
  answers `404` elsewhere. `Health__Urls` moves that address. Two workers on one machine
  need different addresses.
- Neither Docker Compose nor `scripts/dev.sh` starts a telemetry collector, so logs go
  to the console only. Set `OTEL_EXPORTER_OTLP_ENDPOINT` for the server and the worker to
  export traces, metrics and logs. See [telemetry export](reference/host-settings.md#telemetry-export).
- Ctrl+C stops the server, the worker, the publisher and the SPA. PostgreSQL keeps running with
  its data, and Keycloak and the purchasing system stub keep running. Run
  `docker compose down` to stop them.
  If any of the four exits on its own, the others stop too and the script
  exits non-zero.

## Running a host on its own

To run the worker on its own, run `dotnet run --project src/Axis.Worker`. It
reads its tenants from its `Tenants` section, as the server does, and runs
their due work items. It waits until the server has migrated each tenant
database, so either can start first. If you moved PostgreSQL with
`AXIS_POSTGRES_PORT`, override `Tenants__default__ConnectionString` for the
worker.

`dotnet run` of the server or the worker needs the sample extension package built
first, with `scripts/build.sh` or
`dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj`.
Until then `Extensions:Path` names a folder that does not exist, and they stop.
It also needs the sample modules installed, with `scripts/build.sh` or
`bash scripts/lib/install-sample-modules.sh`. Until then `Modules:Path` names a
folder that does not exist, and they stop too. The server also needs `Templates:Path`'s
folder, which `bash scripts/lib/install-sample-templates.sh` creates.

## Activating other application folders

`src/Axis.Server/appsettings.Development.json` lists the purchase request
sample, `../../samples/apps/purchase-requests`, the leave request sample,
`../../samples/apps/leave-requests`, and the service desk sample,
`../../samples/apps/service-desk`, and the asset management sample,
`../../samples/apps/asset-management`, in `ActivateOnStartup`. So
`scripts/dev.sh` migrates every tenant database, then compiles and activates all four
samples before it listens, and stops on any diagnostic.

To activate another application folder, add it to that list, or set
`ActivateOnStartup__0=<folder>` to activate it in place of the purchase request
sample. The other samples stay active.
Relative paths are resolved from `src/Axis.Server`.

## Widget previews

The design system has a live preview of every widget, drawn by the real widget components in light and dark
mode, see the "Design tokens and previews" bullet of the [frontend reference](reference/frontend.md) and
[D67](decisions/d67-live-widget-previews-in-the-design-system.md). The previews are one bundle, built from
`web/src/designSystem/`. It feeds fixture page metadata to the widgets and answers their API calls from
fixtures, only for the API paths of the fixture application. It needs no server, database or sign-in.

- `scripts/design-system.sh` builds the bundle into `artifacts/design-system/axis-design-system.js` and
  checks it with `scripts/check-design-system.mjs`: one classic script, no `import`, no literal `</script`
  or `<!--`, at most 6 MB, and no HTML file in the folder. It needs no Playwright browser. It is not a
  NexKit check. The local agent session publishes the bundle as the Design System artifact.
- The bundle sets `window.Axis`. `window.Axis.<name>(element)` mounts one preview into an element. It
  draws every state of the preview in one card, each under its caption, and returns a function that
  unmounts them all, see [D95](decisions/d95-widget-previews-show-the-states-a-design-change-touches.md).
  Every preview has at least one state, and a preview with only one shows it as "Typical". A state can
  bring its own fixtures, on its own paths in the fixture application. The names are `table`, `form`,
  `taskInbox`, `processInspector`, `detail`, `chart-<kind>` for `bar`, `line` and `pie`, `kpi`, `columns`,
  `tabs`, `section`, `states` and `field-<type>` for each field type. The `states` preview draws, once,
  the states every widget shares: a table while it loads and a table whose request fails. The `table`,
  `taskInbox`, `detail`, each `chart-<kind>` and `kpi` previews draw "With data" and "Empty". The table's data
  has row and toolbar actions and a long-text row, and the inbox's has an overdue and a claimed task.
  `processInspector` draws one state with a failed instance and an open dead letter. A
  `field-<type>` preview draws the input the form widget uses for that field type,
  with its label and the fixture value, laid out as on a form, in four states: Typical, Required, Read-only, and
  Validation error, which shows a fixed message under the input. _(planned)_ It draws three states instead, as
  [D96](decisions/d96-rules-from-the-design-system-review.md) agrees: Editable, Read-only, and Required, help text
  and error, which shows the required mark, a help text and a fixed message at once. `field-child-collection` draws line items with a
  reference cell and a computed cell, in four states: Reference and computed cells, Cell error, Read-only, and Empty.
  The `form` preview draws Validation errors, which submits the form once when it mounts to a fixture that answers
  with a validation problem, Read-only by a condition, and Wizard steps. _(planned)_ It adds Fields in columns, with
  errors, where fields of several widths share rows and some show messages, and Many steps, with seven steps or more,
  so the steps bar shows numbers. The `table` preview draws With data, Empty and Many row actions, which folds a
  row's actions into the "…" menu. The `kpi` preview's comparison is last month's total. A field preview has no Save, Cancel or action
  buttons and no history, which only the `form` preview shows. Its input works as on a form, and nothing is saved. A mounted widget follows
  `data-theme` on the root element of the page.
  `dark` is dark mode, and any other value or none is light mode. A mounted widget also follows the token custom
  properties of the root element, such as `--primary`, see [frontend](reference/frontend.md). The Design System
  artifact draws each card in a frame sandboxed without `allow-forms`, where the browser blocks a form's own
  submission before its submit event fires. The bundle therefore fires a form's submit event itself, for a click on
  a submit button, Enter in an input and `requestSubmit()`, and cancels the browser's submission.
- _(planned)_ The artifact lays out every card at 992 px, the [narrow width](reference/frontend.md) breakpoint, with
  `width=992` in the card's marker, and scales it to fit its frame. So every card shares one scale and shows the
  desktop layout.
- _(planned)_ The cards come in six groups, in this order: Field inputs (each `field-<type>`), Data widgets (`table`,
  `form`, `detail`), Charts and KPIs (each `chart-<kind>`, `kpi`), Process widgets (`taskInbox`,
  `processInspector`), Containers (`columns`, `tabs`, `section`) and Shared states (`states`). _(planned for M16)_ A seventh
  group, Shell, comes last, see [shell](reference/shell.md#design-system-previews).
  `scripts/design-system.sh` writes the card order as the bundle's first line, `/* @ds-bundle: {...} */`, so a sync
  keeps it.
- `web/design-system.html` is the harness. After `scripts/design-system.sh`, open it from disk. With no
  query it mounts every preview. `?preview=<name>&mode=light|dark` mounts one preview in that mode. The
  Vite dev server does not serve the previews.

`tests/e2e/specs/design-system.spec.ts` mounts each preview from the bundle in the harness, in both modes,
and fails on a console error or on a request other than the harness and the bundle. It also checks that
changing `data-theme` redraws a table, that a mounted form follows `--primary` on the root element and redraws when
it changes, that a validation message follows `--dangerText`, and that no fixture reaches the server's web root. It
checks that a field preview has no Save button and no history, and that the `states` preview shows a busy widget and
the load error alert. For each field type except the child collection, it checks in both modes that the required,
read-only and error states show. It checks the child collection's reference, computed, error, read-only and empty
states, and the form's validation, read-only and wizard states. It opens the date-time picker and the reference lookup
and adds a child collection row. It mounts the `form` preview in a frame sandboxed like the artifact's and checks its
validation messages and a save. It finds each data preview's empty text under its "Empty" caption, the table's row
and toolbar actions, the inbox's overdue and claimed tags, and the inspector's failed instance and dead letter actions.

## Formatting

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
