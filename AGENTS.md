# Axis agent instructions

## Before you start

- Read [docs/product-brief.md](docs/product-brief.md) and [docs/decisions.md](docs/decisions.md)
  before any product or architecture work.
- [docs/architecture.md](docs/architecture.md) is the target structure.
  [docs/reference/](docs/reference/) holds the detailed behaviour: configuration,
  storage, record API, frontend, expressions, data sources, processes and
  extension packages.
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
  packages, which hold custom C# code, live under `samples/packages/`.
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

| Script | What it does | Needs |
| --- | --- | --- |
| `scripts/setup.sh` | Restores .NET packages, installs npm packages and the Playwright browser | Network |
| `scripts/build.sh` | Builds the SPA into the server web root, then the .NET solution, which installs the sample extension package in `artifacts/extensions` | |
| `scripts/lint.sh` | `dotnet format` check, oxlint, TypeScript and Prettier checks, and the Markdown link and anchor check | |
| `scripts/test.sh` | .NET unit tests and Vitest | |
| `scripts/integration.sh` | .NET tests against real PostgreSQL and Keycloak through Testcontainers, and a smoke test of `scripts/dev.sh`, skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list | Docker |
| `scripts/e2e.sh` | Starts PostgreSQL, Keycloak, the built purchasing system stub, server and worker, runs Playwright in Chromium, then runs the Compose smoke test, skipped when `NEXKIT_BASE_SHA` is set and no changed file is on its path list | Docker |

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

To run Axis, run `docker compose up --build`. It builds Axis from source and starts
PostgreSQL, Keycloak with the `axis` development realm at http://localhost:8080, the server
with the purchase request sample at http://localhost:5206, the worker, and the purchasing
system stub at http://localhost:5207. The stub, in `tests/Axis.PurchasingStub`, stands in for
the purchasing system that the sample calls when a request is approved. It needs only
Docker. `docker compose logs server` shows the activation diagnostics,
and `docker compose logs worker` shows the worker output. The Keycloak admin console login is
`admin` / `admin`. Set `AXIS_SERVER_PORT`, `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT` and
`AXIS_PURCHASING_PORT` to change the host ports (5206, 5432, 8080 and 5207). All four ports
are published on `127.0.0.1` only.

Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` start Keycloak with the development
realm imported from `samples/keycloak/axis-realm.json`. The realm users are `anna`, `lan`,
`binh`, `chau` and `dung`, and the development password is `axis` for each. Under `scripts/dev.sh`,
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
- It runs the server and the worker under `dotnet watch` and the SPA on Vite in
  one terminal. It starts the worker once the server has migrated the tenant
  databases. Each line starts with `[server]`, `[worker]` or `[web]`, and the
  script's own messages start with `[dev]`. The SPA proxies `/api` and `/health`
  to the server.
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
- `src/Axis.Server/appsettings.Development.json` lists the purchase request
  sample, `../../samples/apps/purchase-requests`, in `ActivateOnStartup`. So
  `scripts/dev.sh` migrates every tenant database, then compiles and activates the
  sample before it listens, and stops on any diagnostic.
- To activate another application folder, add it to that list, or set
  `ActivateOnStartup__0=<folder>` to activate it in place of the sample.
  Relative paths are resolved from `src/Axis.Server`.

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
