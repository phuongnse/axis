# Development

How to run Axis on one machine, what the scripts do, and the development data they set up: the
database roles, the Keycloak realm and its users, and the preview tenant. Production installs follow
[installation](reference/installation.md). The short list of commands is in
[AGENTS.md](../AGENTS.md#commands).

## Scripts

Run from the repository root. Each script is also a NexKit check and a CI step.

| Script                   | What it does                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Needs   |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| `scripts/setup.sh`       | Restores .NET packages, installs npm packages and the Playwright browser, and installs pinned Helm, kubeconform and the Kubernetes schemas into `~/.cache/axis-tools`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Network |
| `scripts/build.sh`       | Builds the SPA into the server web root, then the .NET solution, which installs the sample extension package in `artifacts/extensions`, then installs the sample modules in `artifacts/modules` and the sample templates in `artifacts/templates`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |         |
| `scripts/lint.sh`        | `dotnet format` check, oxlint, TypeScript and Prettier checks, the check that no test selects an `ant-` class, the Markdown link and anchor check, the docs structure check, and the Helm chart lint and schema check                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |         |
| `scripts/test.sh`        | .NET unit tests, Vitest and the tests of the scripts themselves                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |         |
| `scripts/integration.sh` | .NET tests against real PostgreSQL and Keycloak through Testcontainers, which also run the test scenarios of every application under `samples/apps/` and of every sample template under `samples/templates/` on that PostgreSQL. Both are skipped when `NEXKIT_BASE_SHA` is set and every changed file is on the safe list in `scripts/lib/affected.mjs`. The performance tests, whose class names end in `PerformanceTests`, run one class at a time, after the other integration tests. They do not run when `NEXKIT_BASE_SHA` is set, and CI on `main` runs them. The smoke test of `scripts/dev.sh` is skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list. With arguments, which go to `dotnet test`, such as `--filter-class 'Axis.Integration.Tests.Studio*'`, it runs only the matching tests, without the scenario suites and the smoke test                                                                                                                                                         | Docker  |
| `scripts/e2e.sh`         | Starts PostgreSQL, Keycloak, the built purchasing system stub, server, worker and publisher, creates the `journey` tenant for Studio journeys with its own database `axis_journey`, roles `axis_journey_schema` and `axis_journey_runtime`, and server on `127.0.0.2`, which the worker and the publisher serve too, runs Playwright in Chromium, then runs the Compose smoke test, the production images smoke test and the on-premises smoke test, which installs `deploy/compose/` from the production images for two tenants, each skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list. Playwright and the services it starts are skipped when `NEXKIT_BASE_SHA` is set and every changed file is under `docs/` or is a Markdown file. With arguments, which go to Playwright, such as `tests/e2e/specs/studio.spec.ts` or `--grep 'publishes'`, it runs only the matching specs, after the sign-in setup, without the three smoke tests | Docker  |
| `scripts/scan.sh`        | NuGet and npm vulnerability scan that fails on high or critical findings. CI is to run it on its triggers and once a week _(planned for M16)_                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Network |

The CodeQL workflow, `.github/workflows/codeql.yml`, scans C# and TypeScript on pull requests to
`main` and on `main`. Its alerts go to GitHub code scanning, and its check fails on a new high or
critical alert. It is not a NexKit check and does not run on NexKit's branches. NexKit starts it on
`main` after each merge.

## Test runs and reports

The test, integration and E2E scripts run every suite even when one fails. The exceptions are
the suites that NexKit's `NEXKIT_BASE_SHA` shows no change can affect: the integration tests and
their scenarios under the safe list, Playwright under its smaller list, and the smoke tests under
their path lists. A skipped suite is listed as skipped. Without `NEXKIT_BASE_SHA`, as in CI and local
runs, every suite runs. Each suite writes a JUnit file to `artifacts/test-results/`. The E2E server output
goes to `artifacts/logs/e2e-server.log`, the E2E worker output to
`artifacts/logs/e2e-worker.log`, the E2E journey server output to `artifacts/logs/e2e-journey-server.log`, the E2E publisher output to `artifacts/logs/e2e-publisher.log`, and the
purchasing system stub output to `artifacts/logs/e2e-purchasing.log`. `scripts/test-report.mjs` then prints a
summary per suite and the failed tests with their `file:line`. On GitHub Actions
the summary goes to the job summary and each failed test becomes an error
annotation.

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
are published on `127.0.0.1` only.

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
at `/service-desk`. The `author` role lets the authoring API list and read the purchase request
sample's resources under `scripts/dev.sh`, and opens the Studio page at http://localhost:5173/studio.
The `publisher` role lets the authoring API publish, shows **Publish** on the Studio page and allows
rollback from the release history. Under `scripts/dev.sh`,
the server signs people in through Keycloak at `/api/auth/sign-in`, and the session then
answers `GET /api/me`. The SPA header offers sign-in and sign-out there. Sign-in through
Compose is not wired yet: the server in its container cannot reach Keycloak at the address the
browser uses. The site endpoints, the record API and the data source endpoint need a session, so
under Compose they answer `401`, and an application page starts a sign-in that cannot complete yet.
See [sign-in and the session](reference/security.md#sign-in-and-the-session).

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
- E2E's journey server sets `Studio:ApplicationsPath` to a temporary folder. Development and
  Compose set it to `artifacts/studio-apps` _(planned for M13)_, which `artifacts/` already keeps
  out of git. See
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

## Formatting

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
