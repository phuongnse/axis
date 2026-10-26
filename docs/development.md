# Development

How to run Axis on one machine, what the scripts do, and the development data they set up: the
database roles, the Keycloak realm and its users, and the preview tenant. Production installs follow
[installation](reference/installation.md). The short list of commands is in
[AGENTS.md](../AGENTS.md#commands).

## Scripts

Run from the repository root. Each script is also a NexKit check and a CI step.

| Script                   | What it does                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Needs   |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| `scripts/setup.sh`       | Restores .NET packages, installs npm packages and the Playwright browser, and installs pinned Helm, kubeconform and the Kubernetes schemas into `~/.cache/axis-tools`. Each step and each tool download gets up to 4 tries, waiting 15, 30 and 60 seconds between them, so a short refusal such as a 403 is retried. On CI the Playwright system packages install as a step of their own                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Network |
| `scripts/build.sh`       | Builds the SPA into the server web root and checks its size against the budget in `web/bundle-budget.json`, which prints the gzipped size of the main bundle and of the chart chunk and fails when the main bundle grew more than 50 KB over its size before M12 or when the chart code is not in its own chunk, then the .NET solution, which installs the sample extension package in `artifacts/extensions`, then installs the sample modules in `artifacts/modules` and the sample templates in `artifacts/templates`                                                                                                                                                                                                                                                                                                                                                                                                                                              |         |
| `scripts/lint.sh`        | `dotnet format` check, oxlint, TypeScript and Prettier checks, the check that no test selects an `ant-` class, the check that every Playwright locator matches a string name or text exactly, the check that no test outside a `*PerformanceTests` class measures elapsed time, the Markdown link and anchor check, the docs structure check, and the Helm chart lint and schema check                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |         |
| `scripts/test.sh`        | .NET unit tests, Vitest and the tests of the scripts themselves                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |         |
| `scripts/integration.sh` | .NET tests against real PostgreSQL and Keycloak through Testcontainers, which also run the test scenarios of every application under `samples/apps/` and of every sample template under `samples/templates/` on that PostgreSQL. Both are skipped when `NEXKIT_BASE_SHA` is set and every changed file is on the safe list in `scripts/lib/affected.mjs`. The performance tests, whose class names end in `PerformanceTests`, run one class at a time, after the other integration tests. They do not run when `NEXKIT_BASE_SHA` is set, and CI on `main` runs them. The smoke test of `scripts/dev.sh` is skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list. With arguments, which go to `dotnet test`, such as `--filter-class 'Axis.Integration.Tests.Studio*'`, it runs only the matching tests, without the scenario suites and the smoke test                                                                                        | Docker  |
| `scripts/e2e.sh`         | Starts PostgreSQL, Keycloak, the built purchasing system stub, server, worker and publisher, creates the `journey` tenant for Studio journeys with its own database `axis_journey`, roles `axis_journey_schema` and `axis_journey_runtime`, and server on `127.0.0.2`, which the worker and the publisher serve too, builds and checks the design-system bundle in `artifacts/design-system` for its spec, runs Playwright in Chromium, then runs the Compose smoke test, the production images smoke test and the on-premises smoke test, which installs `deploy/compose/` from the production images for two tenants, each skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list. Playwright and the services it starts are skipped when `NEXKIT_BASE_SHA` is set and every changed file is under `docs/` or is a Markdown file. With arguments, which go to Playwright, such as `tests/e2e/specs/studio.spec.ts` or `--grep 'publishes'`, it runs only the matching specs, after the sign-in setup, without the three smoke tests | Docker  |
| `scripts/scan.sh`        | NuGet and npm vulnerability scan that fails on high or critical findings. CI is to run it on its triggers and once a week _(planned for M16)_                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Network |
| `scripts/load.sh`        | _(Planned for M16.)_ The load test: k6 in a container against the Compose stack with two server replicas, on a generated application of typical size. It prints each performance objective with its measured value and fails when one is missed. CI runs it once a week and on demand. It is not a NexKit check and gates no merge. See [performance](reference/performance.md)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | Docker  |

The CodeQL workflow, `.github/workflows/codeql.yml`, scans C# and TypeScript on pull requests to
`main` and on `main`. Its alerts go to GitHub code scanning, and its check fails on a new high or
critical alert. It is not a NexKit check and does not run on NexKit's branches. NexKit starts it on
`main` after each merge.

The bundle budget, `web/bundle-budget.json`, keeps the SPA's first load small. The main bundle is the
entry script of `index.html` and the scripts it preloads, gzipped. Its baseline is 537,019 bytes,
built from commit `ad13bd4`, the last commit before any M12 code. It may grow by 50 KB over that.
The chart library loads in its own lazy chunk, the file `assets/ChartDrawing-*.js`. The page
designer's canvas and palette load in another, `assets/StudioPageDesign-*.js`, when the Design tab
opens. The form designer's Design tab loads as `assets/StudioFormDesign-*.js`, when it first opens.
Both designers share the drag and drop library, `@dnd-kit`, in `assets/DragAndDrop-*.js`. It measures
15.4 KB gzipped. `scripts/build.sh` runs [`scripts/check-bundle.mjs`](../scripts/check-bundle.mjs)
after the SPA build. It prints the four sizes and fails when the main bundle is over budget, when
there is no chart chunk, page design chunk or drag and drop chunk or when any of them is among the
main bundle files. Do not raise the budget to make the check pass: find what grew.

## Test runs and reports

The test, integration and E2E scripts run every suite even when one fails. The exceptions are
the suites that NexKit's `NEXKIT_BASE_SHA` shows no change can affect: the integration tests and
their scenarios under the safe list, Playwright under its smaller list, and the smoke tests under
their path lists. The lists are in [`scripts/lib/affected.mjs`](../scripts/lib/affected.mjs), and any
doubt runs the suite: a missing base, a git error or an empty change list. A performance test
class name ends in `PerformanceTests`, so it stays out of NexKit's checks, and the class joins its
project's performance collection, which runs alone after the other tests. A guard test fails when
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

Each application's test scenarios are a suite of their own, named `scenarios-<application name>`.
The integration tests run them and write `artifacts/test-results/scenarios-<application name>.xml`.
A failure names the scenario and the step. To run them by hand, run
`dotnet run --project src/Axis.Scenarios -- <folder> --server <cs> --extensions artifacts/extensions --modules artifacts/modules`.

## Production images and Axis.Admin

The `Dockerfile` also has three production targets, `axis-server`, `axis-worker` and `axis-admin`,
which hold no samples or development settings. Build one with
`docker build --target axis-server --build-arg AXIS_VERSION=<version> .`. `scripts/e2e.sh` builds
and runs all three in the `images` suite, from `scripts/images.test.mjs`. The default target is still
the development server.

`Axis.Admin` is the console tool that the platform operator runs. It creates, suspends,
resumes, lists, backs up and restores tenants, and it migrates and activates them. Run it with
`dotnet run --project src/Axis.Admin -- <command>`, or with the `axis-admin` image. See
[administration](reference/administration.md). The production install is in `deploy/compose/`
and `deploy/helm/axis/`, see [installation](reference/installation.md).

## Docker Compose

To run Axis, run `docker compose up --build`. It builds Axis from source and starts
PostgreSQL, Keycloak with the `axis` development realm at http://localhost:8080, the server
with the purchase request, leave request and service desk samples at http://localhost:5206, the worker, the publisher, and the purchasing
system stub at http://localhost:5207. The stub, in `tests/Axis.PurchasingStub`, stands in for
the purchasing system that the sample calls when a request is approved. It needs only
Docker. The images hold the `departments` sample module in `/modules`.
Compose also starts one publisher, `axis-admin publisher`, which runs the
publish requests of Studio. See [D47](decisions/d47-publishing-from-studio.md).
It serves the tenants `default` and `default-preview`, so it runs preview requests too. It has no port and no health check, and writes `default ready` and `default-preview ready` once the server has migrated each tenant database.
`docker compose logs server` shows the activation diagnostics,
`docker compose logs worker` shows the worker output and `docker compose logs publisher` shows the publisher output. `docker compose ps` shows the worker
healthy once its `/health/ready` passes, and `docker compose up --wait` waits for that. The worker's
health address `http://127.0.0.1:5208` stays inside its container. The Keycloak admin console login is
`admin` / `admin`. Set `AXIS_SERVER_PORT`, `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT` and
`AXIS_PURCHASING_PORT` to change the host ports (5206, 5432, 8080 and 5207). All four ports
are published on `127.0.0.1` only. The server creates Studio's new applications in
`artifacts/studio-apps`, which the checkout mounts at `/studio-apps`. Set `AXIS_STUDIO_APPS_DIR`
to move that folder. On Linux the server runs as UID 1654, so create the folder first and make it
writable by that UID. Docker creates a missing folder as root.

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
adds the `agent` and `service-desk-lead` users and gives an employee the `requester` role, in a site
at `/service-desk`. The request-approval template journey, `tests/e2e/specs/request-approval-journey.spec.ts`,
adds the `approver` user, `minh`. The `author` role lets the authoring API list and read the resources of every sample under
`samples/apps/` under `scripts/dev.sh`, and opens the Studio page at http://localhost:5173/studio.
Studio saves into those committed folders, so `git checkout -- samples/apps` reverts an edit.
The `publisher` role lets the authoring API publish, shows **Publish** on the Studio page and allows
rollback from the release history. Under `scripts/dev.sh`,
the server signs people in through Keycloak at `/api/auth/sign-in`, and the session then
answers `GET /api/me`. The SPA header offers sign-in and sign-out there. Sign-in through
Compose is not wired yet: the server in its container cannot reach Keycloak at the address the
browser uses. The site endpoints, the record API and the data source endpoint need a session, so
under Compose they answer `401`, and an application page starts a sign-in that cannot complete yet.
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
  activates the purchase request, leave request and service desk samples on it at start. The worker runs it too,
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
`../../samples/apps/service-desk`, in `ActivateOnStartup`. So
`scripts/dev.sh` migrates every tenant database, then compiles and activates all three
samples before it listens, and stops on any diagnostic.

To activate another application folder, add it to that list, or set
`ActivateOnStartup__0=<folder>` to activate it in place of the purchase request
sample. The leave request and service desk samples stay active.
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
- The bundle sets `window.Axis`. `window.Axis.<name>(element)` mounts one preview into an element and
  returns a function that unmounts it. The names are `table`, `form`, `taskInbox`, `processInspector`,
  `detail`, `chart-<kind>` for `bar`, `line` and `pie`, `kpi`, `columns`, `tabs`, `section` and
  `field-<type>` for each field type. A `field-<type>` preview draws the input the form widget uses for that field type,
  with its label and the fixture value, laid out as on a form. It has no Save, Cancel or action buttons and no history,
  which only the `form` preview shows. Its input works as on a form, and nothing is saved. A mounted widget follows
  `data-theme` on the root element of the page.
  `dark` is dark mode, and any other value or none is light mode. A mounted widget also follows the token custom
  properties of the root element, such as `--primary`, see [frontend](reference/frontend.md).
- `web/design-system.html` is the harness. After `scripts/design-system.sh`, open it from disk. With no
  query it mounts every preview. `?preview=<name>&mode=light|dark` mounts one preview in that mode. The
  Vite dev server does not serve the previews.

`tests/e2e/specs/design-system.spec.ts` mounts each preview from the bundle in the harness, in both modes,
and fails on a console error or on a request other than the harness and the bundle. It also checks that
changing `data-theme` redraws a table, that a mounted form follows `--primary` on the root element and redraws when
it changes, and that no fixture reaches the server's web root. It checks that a field preview has no Save button and
no history, and it opens the date-time picker and the reference lookup and adds a child collection row.

## Formatting

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
