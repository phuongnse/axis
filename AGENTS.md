# Axis agent instructions

## Before you start

- Read [docs/product-brief.md](docs/product-brief.md) and [docs/decisions.md](docs/decisions.md)
  before any product or architecture work.
- [docs/architecture.md](docs/architecture.md) is the target structure.
  [docs/reference/](docs/reference/) holds the detailed behaviour: configuration,
  administration, installation, storage, record API, frontend, expressions, data sources, processes and
  extension packages. The Studio authoring API is in
  [docs/reference/authoring.md](docs/reference/authoring.md).
- [docs/domain/concepts.md](docs/domain/concepts.md) defines the vocabulary. Use those
  terms in code, APIs and UI.
- [docs/domain/knowledge.md](docs/domain/knowledge.md) explains why the design rules exist.
  Check it before changing configuration, execution, storage or security behaviour.

## Language

Use English for source code, comments, configuration, commit messages,
issues and repository documentation. The owner may discuss things in
Vietnamese.

## Working rules

- Docs are the source of truth. An issue links to the relevant section; it
  does not copy it. If a change alters agreed behaviour, update the doc in the
  same change. When a change makes any statement in the docs false, including
  summaries such as the README status, the roadmap or the concepts table, fix
  it in the same change. See
  [docs/delivery.md](docs/delivery.md#keeping-the-docs-current).
- Keep the [decisions](docs/decisions.md) status words exact: **Agreed**
  means agreed, and **Proposed** means still open. Never present a proposal as
  agreed.
- Each issue is one behaviour and fits in a single agent session. Aim for 2–5
  acceptance criteria that a test or command can verify. More than five is a
  signal to check whether the issue holds more than one behaviour. If it does,
  split it. If every criterion checks the same behaviour, more are fine. A plan
  is not revised only to cut the number of criteria. Split anything larger than
  one session before starting. Its text names the concepts it touches, because
  NexKit chooses the issue's [profile](docs/delivery.md#profiles) from the
  issue text alone.
- Build in vertical slices. Every milestone ends with something that runs
  end to end and is covered by an automated test.
- Every behaviour change needs a test. Use real PostgreSQL through
  Testcontainers for storage and engine behaviour. Use Playwright for UI
  journeys. Test denied access as well as allowed access.
- No application-specific code in the platform. Purchase requests and other
  sample applications live as configuration under `samples/`. Sample extension
  packages, which hold custom C# code, live under `samples/packages/`. Sample modules
  live under `samples/modules/`.
- Never commit secrets, tokens, local logs or machine snapshots.

## Working through NexKit

- All work goes through NexKit, as described in
  [docs/delivery.md](docs/delivery.md). The exception is changes in paths
  NexKit may not change: those are hand-made pull requests.
- NexKit merges its pull request once the checks pass and the AI review
  approves, while its `merge.auto` setting is on. With it off, only the owner
  merges.

Writing on GitHub, for issues, plans, pull request descriptions and reviews:

- Lead with the point. Use short sentences and one idea per bullet.
- Say what changes in behaviour, and why, before naming classes or files.
- Put decisions and risks the owner must judge in their own short section.
- Keep implementation detail out of the main text, or in a `<details>` block.

## Commands

Run from the repository root. Each script is also a NexKit check and a CI step.

| Script                   | What it does                                                                                                                                                                                                                                                                                                                                                                      | Needs   |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| `scripts/setup.sh`       | Restores .NET packages, installs npm packages and the Playwright browser, and installs pinned Helm, kubeconform and the Kubernetes schemas into `~/.cache/axis-tools`                                                                                                                                                                                                             | Network |
| `scripts/build.sh`       | Builds the SPA into the server web root, then the .NET solution, which installs the sample extension package in `artifacts/extensions`, then installs the sample modules in `artifacts/modules`                                                                                                                                                                                   |         |
| `scripts/lint.sh`        | `dotnet format` check, oxlint, TypeScript and Prettier checks, the Markdown link and anchor check, and the Helm chart lint and schema check                                                                                                                                                                                                                                       |         |
| `scripts/test.sh`        | .NET unit tests and Vitest                                                                                                                                                                                                                                                                                                                                                        |         |
| `scripts/integration.sh` | .NET tests against real PostgreSQL and Keycloak through Testcontainers, which also run the test scenarios of every application under `samples/apps/` on that PostgreSQL, and a smoke test of `scripts/dev.sh`, skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list                                                                                      | Docker  |
| `scripts/e2e.sh`         | Starts PostgreSQL, Keycloak, the built purchasing system stub, server and worker, runs Playwright in Chromium, then runs the Compose smoke test, the production images smoke test and the on-premises smoke test, which installs `deploy/compose/` from the production images for two tenants, each skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list | Docker  |
| `scripts/scan.sh`        | NuGet and npm vulnerability scan that fails on high or critical findings. CI runs it and once a week _(planned for M10)_                                                                                                                                                                                                                                                          | Network |

A CodeQL workflow _(planned for M10)_ scans C# and TypeScript. A new high or critical alert
fails its check.

The test, integration and E2E scripts run every suite even when one fails. The one
exception is a smoke test that NexKit's `NEXKIT_BASE_SHA` shows no change can affect. It is
skipped and the summary lists it as skipped. Without `NEXKIT_BASE_SHA`, as in CI and local
runs, every suite runs. Each suite writes a JUnit file to `artifacts/test-results/`. The E2E server output
goes to `artifacts/logs/e2e-server.log`, the E2E worker output to
`artifacts/logs/e2e-worker.log`, and the purchasing system stub output to
`artifacts/logs/e2e-purchasing.log`. `scripts/test-report.mjs` then prints a
summary per suite and the failed tests with their `file:line`. On GitHub Actions
the summary goes to the job summary and each failed test becomes an error
annotation.

Each application's test scenarios are a suite of their own, named `scenarios-<application name>`.
The integration tests run them and write `artifacts/test-results/scenarios-<application name>.xml`.
A failure names the scenario and the step. To run them by hand, run
`dotnet run --project src/Axis.Scenarios -- <folder> --server <cs> --extensions artifacts/extensions --modules artifacts/modules`.

The `Dockerfile` also has three production targets, `axis-server`, `axis-worker` and `axis-admin`,
which hold no samples or development settings. Build one with
`docker build --target axis-server --build-arg AXIS_VERSION=<version> .`. `scripts/e2e.sh` builds
and runs all three in the `images` suite, from `scripts/images.test.mjs`. The default target is still
the development server.

`Axis.Admin` is the console tool that the platform operator runs. It creates, suspends,
resumes, lists, backs up and restores tenants, and it migrates and activates them. Run it with
`dotnet run --project src/Axis.Admin -- <command>`, or with the `axis-admin` image. See
[administration](docs/reference/administration.md). The production install is in `deploy/compose/`
and `deploy/helm/axis/`, see [installation](docs/reference/installation.md).

To run Axis, run `docker compose up --build`. It builds Axis from source and starts
PostgreSQL, Keycloak with the `axis` development realm at http://localhost:8080, the server
with the purchase request and leave request samples at http://localhost:5206, the worker, and the purchasing
system stub at http://localhost:5207. The stub, in `tests/Axis.PurchasingStub`, stands in for
the purchasing system that the sample calls when a request is approved. It needs only
Docker. The images hold the `departments` sample module in `/modules`.
*(Planned for M10:)* Compose also starts one publisher, `axis-admin publisher`, which runs the
publish requests of Studio. See [D47](docs/decisions.md#d47-publishing-from-studio--agreed).
`docker compose logs server` shows the activation diagnostics,
and `docker compose logs worker` shows the worker output. `docker compose ps` shows the worker
healthy once its `/health/ready` passes, and `docker compose up --wait` waits for that. The worker's
health address `http://127.0.0.1:5208` stays inside its container. The Keycloak admin console login is
`admin` / `admin`. Set `AXIS_SERVER_PORT`, `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT` and
`AXIS_PURCHASING_PORT` to change the host ports (5206, 5432, 8080 and 5207). All four ports
are published on `127.0.0.1` only. The integration test fixtures create two database
roles, `axis_schema` and `axis_runtime`, with the development password `axis`. They own each test
database with `axis_schema`, run servers and workers as `axis_runtime`, and run startup activation as
`axis_schema`. Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the same. PostgreSQL loads
`samples/postgres/init-roles.sql` on its first start, which creates the two roles and makes
`axis_schema` own the tenant database. The server uses `axis_schema` for `SchemaConnectionString` and
`axis_runtime` for every other connection string. The worker uses only `axis_runtime` and gets no
`SchemaConnectionString`. `axis` stays the bootstrap superuser, for setup and health checks only.
PostgreSQL runs init scripts only on an empty volume, so an existing local Compose volume needs
`docker compose down -v` once. That deletes its data. See
[database roles](docs/reference/storage.md#database-roles).

Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` start Keycloak with the development
realm imported from `samples/keycloak/axis-realm.json`. The realm users are `anna`, `lan`,
`binh`, `chau`, `dung`, `giang` and `hoa`, and the development password is `axis` for each. `giang` holds the `operator` role, which opens the purchase request sample's Approvals inspector page. The leave request sample, `samples/apps/leave-requests`, reuses `anna`, `lan`, `binh` and `chau` with the same `employee` and `department-manager` roles, in a site at `/leave`. `hoa` holds the `author` role, which lets the authoring API list and read the purchase request sample's resources under `scripts/dev.sh`. *(Planned for M10:)* `hoa` also holds the `publisher` role, which lets Studio publish and roll back. The Studio page at http://localhost:5173/studio opens for `hoa`. Under `scripts/dev.sh`,
the server signs people in through Keycloak at `/api/auth/sign-in`, and the session then
answers `GET /api/me`. The SPA header offers sign-in and sign-out there. Sign-in through
Compose is not wired yet: the server in its container cannot reach Keycloak at the address the
browser uses. The site endpoints, the record API and the data source endpoint need a session, so
under Compose they answer `401`, and an application page starts a sign-in that cannot complete yet.
See [sign-in and the session](docs/architecture.md#sign-in-and-the-session).

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
- It runs the server and the worker under `dotnet watch` and the SPA on Vite in
  one terminal. It starts the worker once the server has migrated the tenant
  databases. Each line starts with `[server]`, `[worker]` or `[web]`, and the
  script's own messages start with `[dev]`. The SPA proxies `/api` and `/health`
  to the server.
- *(Planned for M10:)* It also runs one publisher, `axis-admin publisher`, with
  its lines starting with `[publisher]`. It starts once the server has
  migrated the tenant databases. See
  [publisher](docs/reference/administration.md#publisher).
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
  export traces, metrics and logs. See [telemetry export](docs/reference/configuration.md#telemetry-export).
- Ctrl+C stops the server, the worker and the SPA. PostgreSQL keeps running with
  its data, and Keycloak and the purchasing system stub keep running. Run
  `docker compose down` to stop them.
  If any of the three exits on its own, the others stop too and the script
  exits non-zero.
- To run the worker on its own, run `dotnet run --project src/Axis.Worker`. It
  reads its tenants from its `Tenants` section, as the server does, and runs
  their due work items. It waits until the server has migrated each tenant
  database, so either can start first. If you moved PostgreSQL with
  `AXIS_POSTGRES_PORT`, override `Tenants__default__ConnectionString` for the
  worker.
- `dotnet run` of the server or the worker needs the sample extension package built
  first, with `scripts/build.sh` or
  `dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj`.
  Until then `Extensions:Path` names a folder that does not exist, and they stop.
  It also needs the sample modules installed, with `scripts/build.sh` or
  `bash scripts/lib/install-sample-modules.sh`. Until then `Modules:Path` names a
  folder that does not exist, and they stop too.
- `src/Axis.Server/appsettings.Development.json` lists the purchase request
  sample, `../../samples/apps/purchase-requests`, and the leave request sample,
  `../../samples/apps/leave-requests`, in `ActivateOnStartup`. So
  `scripts/dev.sh` migrates every tenant database, then compiles and activates both
  samples before it listens, and stops on any diagnostic.
- To activate another application folder, add it to that list, or set
  `ActivateOnStartup__0=<folder>` to activate it in place of the purchase request
  sample. The leave request sample stays active.
  Relative paths are resolved from `src/Axis.Server`.

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
