# Local Dev

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/scripts.md](./scripts.md) · [AGENTS.md](../../AGENTS.md)

Use `python scripts/axis.py local-dev ...` for local stack work. Do not document raw Docker/npm/dotnet workflows as project commands.

## Prerequisites

- .NET SDK from [global.json](../../global.json).
- Node from [frontend/.nvmrc](../../frontend/.nvmrc).
- Docker reachable from the shell running tests.
- Optional: Playwright Chromium from `python scripts/axis.py frontend install-browsers` for `local-dev smoke` and host browser E2E.
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

The package-manager adapter resolves the documented Node/npm binary/shim path from PATH, nvm, nvm-windows, or Volta. OpenSSL for certs resolves PATH or Git for Windows.

## Stack

Use `python scripts/axis.py local-dev up`. Stop with `python scripts/axis.py local-dev down` unless the user asks to keep services running.

Mandatory services in [docker-compose.yml](../../docker-compose.yml): `postgres`, `redis`, `maildev`, `api`, and `web`; optional services: `otel-lgtm` for observability debugging and `e2e`.

Host ports published by compose: `1025`, `1080`, `3000`, `4318`, `5281`, `5432`, `6379`.

[docker-compose.yml](../../docker-compose.yml) and this playbook are the source of truth for Axis local Docker services. If compose changes, update this file in the same PR.

After frontend manifest or toolchain changes, reconcile running local-dev services with the current manifests before trusting browser smoke or E2E results. Use the Axis local-dev wrapper to recreate affected services when dependency volumes or runtime caches may be stale.

Local overrides live in ignored root `.env.local`. See [.env.example](../../.env.example) for optional Compose variables; stack defaults stay in [docker-compose.yml](../../docker-compose.yml).

## Environment

| Layer | Owner | When you set it |
|---|---|---|
| Docker Compose stack | [docker-compose.yml](../../docker-compose.yml) | Default `local-dev up` — no `.env` file required. |
| Compose overrides | `.env.local` (copy from [.env.example](../../.env.example)) | Optional; only when a compose default needs changing (e.g. `VITE_USE_POLLING`). |
| API on host | [src/Axis.Api/appsettings.json](../../src/Axis.Api/appsettings.json) | Host-native dev without the API container (`python scripts/axis.py dotnet run-api`). Override with ASP.NET env vars (`Section__Key`) or ignored `appsettings.Development.json`. |
| EF migrations | `ConnectionStrings__Identity`, `ConnectionStrings__Objects`, `IDENTITY_CONNECTION_STRING`, or `OBJECTS_CONNECTION_STRING` | `python scripts/axis.py dotnet ef ...` only. |
| Shell adapters | `python scripts/axis.py doctor` | `DOCKER_HOST`, `NVM_DIR`, `PATH` when tools resolve from another context (WSL, Docker Desktop). |
| Host browser smoke | [frontend/playwright.config.ts](../../frontend/playwright.config.ts) and a running local stack | `python scripts/axis.py local-dev smoke -- <playwright-args>` reuses host Chromium against `https://localhost:3000` and API health at `https://localhost:5281` without rebuilding the E2E profile. |
| E2E | [docker-compose.yml](../../docker-compose.yml) and [frontend/playwright.config.ts](../../frontend/playwright.config.ts) | `python scripts/axis.py local-dev e2e` builds and runs the compose E2E profile with API, web, Maildev, service URLs, and browser trust configured. Pass Playwright args after `--` to scope a file or title. |

Common API settings (compose uses service hostnames; host run uses `localhost`):

| Setting | Compose default | Host `appsettings.json` | Purpose |
|---|---|---|---|
| Identity DB | `ConnectionStrings__Identity` → `postgres` | `ConnectionStrings:Identity` → `localhost:5432` | PostgreSQL for Identity/OpenIddict. |
| Objects DB | `ConnectionStrings__Objects` → `postgres` | `ConnectionStrings:Objects` → `localhost:5432` | PostgreSQL for Objects module definitions and published versions. |
| Redis | `Redis__ConnectionString` → `redis:6379` | `Redis:ConnectionString` → `localhost:6379` | Sessions, idempotency, caches. |
| App base URL | `App__BaseUrl` → `https://localhost:3000` | `App:BaseUrl` → `https://localhost:3000` | Browser-facing origin used in verification email links; use the URL the email recipient's browser can open, not an internal Compose service name. |
| SMTP | `Email__Host` / `Email__Port` → `maildev:1025` | `Email:Host` / `Email:Port` → `localhost:1025` | Outbound mail (Maildev locally). |
| Email sender | `Email__FromAddress` / `Email__FromName` → `noreply@axis.localhost` / `Axis Platform` | `Email:FromAddress` / `Email:FromName` → same | Message sender identity; use a verified owned domain outside local dev. |
| CORS | `Cors__AllowedOrigins__0` → SPA origin | `Cors:AllowedOrigins` | Browser origins allowed to call the API. |
| Auth rate limit | `RateLimiting__Auth__PermitLimit` → high local/E2E limit | `RateLimiting:Auth:PermitLimit` → API default | Keeps repeated local browser journeys from exhausting the production-like auth throttle while preserving configurable API behavior. |
| HTTPS certs | `/https/*.pem` mounts | Kestrel / Vite dev cert env in compose `web` | Local TLS for SPA and API. |

Frontend dev (compose `web` service sets these; host Vite uses [frontend/vite.config.ts](../../frontend/vite.config.ts) defaults):

| Variable | Default behavior | Purpose |
|---|---|---|
| `VITE_API_PROXY_TARGET` | `https://api:8443` in compose; `https://localhost:7275` on host | Vite proxy target for `/api` and `/connect`. |
| `VITE_API_URL` | unset → `/api` (same-origin proxy) | Direct API base URL; only set when bypassing the proxy. |
| `VITE_CONNECT_URL` | unset → current page origin in dev | OAuth `/connect` base URL. |
| `VITE_DEV_HTTPS_CERT` / `VITE_DEV_HTTPS_KEY` | compose cert mounts | HTTPS for Vite dev server. |
| `VITE_USE_POLLING` | auto on WSL; see `.env.example` | Chokidar polling for bind-mount hot reload. |

## Daily Operations

Prefer scoped CLI commands: `status`, `up`, `down`, `smoke`, `e2e`, and focused checks. Use `python scripts/axis.py local-dev smoke -- <playwright-args>` for fast host-browser layout or UI smoke against an already-running local stack; the wrapper sets the browser-facing base URL, skips the Playwright dev server, and uses the local root CA when present. Use `python scripts/axis.py local-dev e2e -- e2e/sign-in-user.pw.ts` for Compose-backed evidence; add Playwright filters such as `-g "AT-001"` when one acceptance row is in scope. Running `python scripts/axis.py local-dev e2e` with no args remains the full Axis browser E2E workflow. Package Playwright scripts stay behind repo wrappers. `local-dev shell [service]` runs inside the container; host shell (PowerShell, bash, WSL) does not matter.

Use runtime-specific dev servers only through the documented Axis wrapper or owning package script.

Run unit or focused frontend tests while iterating. Integration/API tests need Docker/Testcontainers.

## Database

Create migrations through `python scripts/axis.py dotnet ef migrations add ...`. Use the owning module Infrastructure project as both project and startup project when a `*DbContextFactory` exists.

Identity dev database startup uses `MigrateAsync`. Use reset paths only for disposable local data; do not use schema initialization shortcuts.

## Guardrails

Do not commit local secrets, ports, certs, or personal URLs. Keep compose/docs drift checked through `python scripts/axis.py check local-dev-docs`.
