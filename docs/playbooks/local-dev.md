# Local dev — Axis CLI

> **Navigation**: [<- docs/README.md](../README.md) . [<- scripts](./scripts.md) . [<- AGENTS.md](../../AGENTS.md)

Use `python scripts/axis.py local-dev ...` for local stack work. Do not document raw Docker/npm/dotnet workflows as project commands.

## Prerequisites

- .NET SDK from `global.json`.
- Node from `frontend/.nvmrc`.
- Docker reachable from the shell running tests.
- Local certs generated through Axis CLI when HTTPS is needed.

## Environment doctor

Run `python scripts/axis.py doctor` when the toolchain or wrappers feel wrong.

## Local HTTPS

Use `python scripts/axis.py local-dev certs`. Cert material stays local.

## Environment adapters

### Docker endpoint adapter

If Docker is reachable only through Docker Desktop/WSL indirection, correct the shell environment instead of changing tests. Report the execution context when Docker/Testcontainers cannot be reached.

### Package-manager adapter

Axis wrappers resolve the documented Node/npm binary/shim path. Do not bypass wrappers in docs or skills.

## Start everything

Use `python scripts/axis.py local-dev up`. Stop with `python scripts/axis.py local-dev down` unless the user asks to keep services running.

## URLs and ports

Mandatory services: `postgres`, `redis`, `maildev`, `api`, and `web`; optional services: `otel-lgtm` and `e2e`.

Host ports published by compose: `1025`, `1080`, `3000`, `4318`, `5281`, `5432`, `6379`.

The source of truth is `docker-compose.yml` plus this playbook. If compose changes, update this file in the same PR.

## Observability (optional)

Use the local Grafana stack only when debugging telemetry.

## Daily operations

Prefer scoped CLI commands: `status`, `up`, `down`, `e2e`, and focused checks.

## Hot reload

Use runtime-specific dev servers only through the documented Axis wrapper or owning package script.

## Run tests without the full stack

Run unit or focused frontend tests while iterating. Integration/API tests need Docker/Testcontainers.

## EF Core migrations

Create migrations through `python scripts/axis.py dotnet ef migrations add ...`. Use the owning module Infrastructure project as both project and startup project when a `*DbContextFactory` exists. Never hand-write migration files.

Identity dev bootstrap uses `MigrateAsync`.

## Reset the database

Use local-dev reset paths only for disposable local data.

## Gotchas

- Do not use schema bootstrap shortcuts.
- Do not commit local secrets, ports, certs, or personal URLs.
- Keep compose/docs drift checked through `python scripts/axis.py check local-dev-docs`.

## Files involved

`docker-compose.yml`, `.env`-style local files, cert output, module connection settings, and this playbook.
