# Axis agent instructions

## Before you start

- Read [docs/product-brief.md](docs/product-brief.md) and [docs/decisions.md](docs/decisions.md)
  before any product or architecture work.
- [docs/architecture.md](docs/architecture.md) is the target structure.
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
  same change.
- Keep the [decisions](docs/decisions.md) status words exact: **Agreed**
  means agreed, and **Proposed** means still open. Never present a proposal as
  agreed.
- Each issue is one behaviour. It has 2–5 acceptance criteria that a test or
  command can verify, and it fits in a single agent session. Split anything
  larger before starting.
- Build in vertical slices. Every milestone ends with something that runs
  end to end and is covered by an automated test.
- Every behaviour change needs a test. Use real PostgreSQL through
  Testcontainers for storage and engine behaviour. Use Playwright for UI
  journeys. Test denied access as well as allowed access.
- No application-specific code in the platform. Purchase requests and other
  sample applications live as configuration under `samples/`.
- Never commit secrets, tokens, local logs or machine snapshots.

## Commands

Run from the repository root. Each script is also a NexKit check and a CI step.

| Script | What it does | Needs |
| --- | --- | --- |
| `scripts/setup.sh` | Restores .NET packages, installs npm packages and the Playwright browser | Network |
| `scripts/build.sh` | Builds the SPA into the server web root, then the .NET solution | |
| `scripts/lint.sh` | `dotnet format` check, oxlint, TypeScript and Prettier checks | |
| `scripts/test.sh` | .NET unit tests and Vitest | |
| `scripts/integration.sh` | .NET tests against real PostgreSQL through Testcontainers | Docker |
| `scripts/e2e.sh` | Starts PostgreSQL and the built server, runs Playwright in Chromium | Docker |

The test, integration and E2E scripts run every suite even when one fails. Each
suite writes a JUnit file to `artifacts/test-results/`, and the E2E server output
goes to `artifacts/logs/e2e-server.log`. `scripts/test-report.mjs` then prints a
summary per suite and the failed tests with their `file:line`. On GitHub Actions
the summary goes to the job summary and each failed test becomes an error
annotation.

For local development:

- Run `docker compose up -d` to start PostgreSQL.
- Run `dotnet run --project src/Axis.Server` to start the server on port 5206.
- Run `npm run dev --prefix web` to start the SPA with hot reload. It proxies
  `/api` and `/health` to the server.

Formatting fixes: `dotnet format Axis.slnx` and `npm run format --prefix web`.
