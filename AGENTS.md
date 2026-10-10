# Axis agent instructions

## Before you start

- Read [docs/product-brief.md](docs/product-brief.md) and [docs/decisions.md](docs/decisions.md)
  before any product or architecture work.
- [docs/architecture.md](docs/architecture.md) is the target structure.
  [docs/reference/](docs/reference/) holds the detailed behaviour: configuration,
  storage, record API, frontend, expressions and data sources.
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
- Each issue is one behaviour. It has 2–5 acceptance criteria that a test or
  command can verify, and it fits in a single agent session. Split anything
  larger before starting. Its text names the concepts it touches, because
  NexKit chooses the issue's [profile](docs/delivery.md#profiles) from the
  issue text alone.
- Build in vertical slices. Every milestone ends with something that runs
  end to end and is covered by an automated test.
- Every behaviour change needs a test. Use real PostgreSQL through
  Testcontainers for storage and engine behaviour. Use Playwright for UI
  journeys. Test denied access as well as allowed access.
- No application-specific code in the platform. Purchase requests and other
  sample applications live as configuration under `samples/`.
- Never commit secrets, tokens, local logs or machine snapshots.

## Working through NexKit

- All work goes through NexKit, as described in
  [docs/delivery.md](docs/delivery.md). The exception is changes in paths
  NexKit may not change: those are hand-made pull requests.
- NexKit merges its pull request once the checks pass and the AI review
  approves, while its `auto_merge` setting is on. With it off, only the owner
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
| `scripts/build.sh` | Builds the SPA into the server web root, then the .NET solution | |
| `scripts/lint.sh` | `dotnet format` check, oxlint, TypeScript and Prettier checks, and the Markdown link and anchor check | |
| `scripts/test.sh` | .NET unit tests and Vitest | |
| `scripts/integration.sh` | .NET tests against real PostgreSQL through Testcontainers, and a smoke test of `scripts/dev.sh` | Docker |
| `scripts/e2e.sh` | Starts PostgreSQL, the built server and the worker, runs Playwright in Chromium, then runs the Compose smoke test | Docker |

The test, integration and E2E scripts run every suite even when one fails. Each
suite writes a JUnit file to `artifacts/test-results/`. The E2E server output
goes to `artifacts/logs/e2e-server.log`, and the E2E worker output to
`artifacts/logs/e2e-worker.log`. `scripts/test-report.mjs` then prints a
summary per suite and the failed tests with their `file:line`. On GitHub Actions
the summary goes to the job summary and each failed test becomes an error
annotation.

To run Axis, run `docker compose up --build`. It builds Axis from source and starts
PostgreSQL, the server with the purchase request sample at http://localhost:5206, and the
worker. It needs only Docker. `docker compose logs server` shows the activation diagnostics,
and `docker compose logs worker` shows the worker output. Set
`AXIS_SERVER_PORT` and `AXIS_POSTGRES_PORT` to change the host ports (5206 and 5432). Both
ports are published on `127.0.0.1` only.

For local development with hot reload, run `scripts/dev.sh`. It needs Docker, .NET and Node, and
works on a fresh clone:

- It installs the SPA packages when `web/node_modules` is missing or
  `web/package-lock.json` changed since the last install. It installs no
  Playwright browser.
- It starts PostgreSQL with Docker Compose and waits until it accepts
  connections.
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
- `AXIS_POSTGRES_PORT`, `AXIS_SERVER_PORT` and `AXIS_WEB_PORT` move the three
  ports. The defaults are 5432, 5206 and 5173. When `AXIS_POSTGRES_PORT` is set,
  the script points the server's connection strings and the worker's
  `Tenants__default__ConnectionString` at that port.
- Ctrl+C stops the server, the worker and the SPA. PostgreSQL keeps running with
  its data. Run `docker compose down` to stop it. If any of the three exits on
  its own, the others stop too and the script exits non-zero.
- To run the worker on its own, run `dotnet run --project src/Axis.Worker`. It
  reads its tenants from its `Tenants` section, as the server does, and runs
  their due work items. It waits until the server has migrated each tenant
  database, so either can start first. If you moved PostgreSQL with
  `AXIS_POSTGRES_PORT`, override `Tenants__default__ConnectionString` for the
  worker.
- `src/Axis.Server/appsettings.Development.json` lists the purchase request
  sample, `../../samples/apps/purchase-requests`, in `ActivateOnStartup`. So
  `scripts/dev.sh` migrates every tenant database, then compiles and activates the
  sample before it listens, and stops on any diagnostic.
- To activate another application folder, add it to that list, or set
  `ActivateOnStartup__0=<folder>` to activate it in place of the sample.
  Relative paths are resolved from `src/Axis.Server`.

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
