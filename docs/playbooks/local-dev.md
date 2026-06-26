# Local Dev

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/scripts.md](./scripts.md) · [AGENTS.md](../../AGENTS.md)

Use `python scripts/axis.py local-dev ...` for local stack work. Do not document raw Docker/npm/dotnet workflows as project commands.

## Prerequisites

- .NET SDK from [global.json](../../global.json).
- Node from [frontend/.nvmrc](../../frontend/.nvmrc).
- Docker reachable from the shell running tests.
- Local certs from `python scripts/axis.py local-dev certs` when HTTPS is needed.

Run `python scripts/axis.py doctor` when toolchain resolution feels wrong.

## HTTPS

Cert material stays local under `.dev-certs/`, which is ignored.

- `.dev-certs/rootCA.pem` is for containers and E2E Node trust.
- `.dev-certs/rootCA.cer` is for host OS trust.
- `.dev-certs/localhost.pem` and `.dev-certs/localhost-key.pem` cover `localhost`, loopback, `api`, and `web`.

Trust the root CA in the OS that runs the browser. On WSL with a Windows browser, import `.dev-certs/rootCA.cer` into Windows Current User trusted roots.

## Environment Adapters

If Docker is reachable only through Docker Desktop/WSL indirection, correct the shell environment instead of changing tests.

The package-manager adapter resolves the documented Node/npm binary/shim path. Do not bypass wrappers in docs or skills.

## Stack

Use `python scripts/axis.py local-dev up`. Stop with `python scripts/axis.py local-dev down` unless the user asks to keep services running.

Mandatory services in [docker-compose.yml](../../docker-compose.yml): `postgres`, `redis`, `maildev`, `api`, and `web`; optional services: `otel-lgtm` and `e2e`.

Host ports published by compose: `1025`, `1080`, `3000`, `4318`, `5281`, `5432`, `6379`, `7456`.

[docker-compose.yml](../../docker-compose.yml), [docker-compose.open-design.yml](../../docker-compose.open-design.yml), and this playbook are the source of truth for Axis local Docker services. If compose changes, update this file in the same PR.

Use the observability stack only when debugging telemetry.

Local overrides live in ignored root `.env.local`. Open Design runs from [docker-compose.open-design.yml](../../docker-compose.open-design.yml); use `python scripts/axis.py local-dev open-design up` to clone, build, configure, and start it at `127.0.0.1:7456`; stop it with `python scripts/axis.py local-dev open-design stop`. Local-dev builds a local image from `~/open-design`, mounts the local Codex CLI binary, and syncs local Codex ChatGPT auth into a Linux-native Docker mount without writing or passing API keys. Run `codex login` in WSL first when Codex is not authenticated. Open Design API auth is disabled for the loopback-only local browser flow.

## Daily Operations

Prefer scoped CLI commands: `status`, `up`, `down`, `e2e`, `open-design`, and focused checks.

Use runtime-specific dev servers only through the documented Axis wrapper or owning package script.

Run unit or focused frontend tests while iterating. Integration/API tests need Docker/Testcontainers.

## Database

Create migrations through `python scripts/axis.py dotnet ef migrations add ...`. Use the owning module Infrastructure project as both project and startup project when a `*DbContextFactory` exists.

Identity dev database startup uses `MigrateAsync`.

Use reset paths only for disposable local data. Do not use schema initialization shortcuts.

## Guardrails

Do not commit local secrets, ports, certs, Open Design env files, or personal URLs. Keep compose/docs drift checked through `python scripts/axis.py check local-dev-docs`.
