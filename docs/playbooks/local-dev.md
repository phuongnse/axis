# Local Dev

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/scripts.md](./scripts.md) · [AGENTS.md](../../AGENTS.md)

Use `python scripts/axis.py local-dev ...` for local stack work. Do not document raw Docker/npm/dotnet workflows as project commands.

## Prerequisites

- Python 3 and Git; use `python3` on WSL/Linux or `py -3` on Windows when `python` is unavailable.
- Docker Engine with Compose reachable from the shell running tests. Native Docker Engine inside WSL is supported; Docker Desktop is not required.
- OpenSSL on PATH, or from Git for Windows, for local HTTPS certificates.
- .NET SDK from [global.json](../../global.json) and Node from [frontend/.nvmrc](../../frontend/.nvmrc), either already available or installed user-locally by Axis.

First-time preparation is `python scripts/axis.py setup --profile local-dev --install-user-tools`. This validates the cumulative doctor profile, restores locked dependencies, installs Playwright Chromium, generates local certificates, and installs the pre-push hook. Add `--trust-local-ca` to opt into current-user host trust; `--yes` skips the Axis prompt, but Windows may still show its security warning. Use `--plan-only` before execution. Run `python scripts/axis.py doctor --profile local-dev --strict` for diagnosis without setup mutations.

## HTTPS

Cert material stays local under ignored `.dev-certs/`; private keys never leave that directory.

- `.dev-certs/rootCA.pem` is for containers, E2E Node trust, and Playwright browser trust stores.
- `.dev-certs/rootCA.cer` is for host OS trust.
- `.dev-certs/localhost.pem` and `.dev-certs/localhost-key.pem` cover `localhost`, loopback, `api`, and `web`.

`local-dev certs` reuses valid material; use `local-dev certs --renew` only to rotate it. Run `local-dev trust-certs` or `untrust-certs` to update the current-user Windows store (including WSL browsers) or macOS login keychain after confirming the displayed SHA-256 fingerprint. Native Linux remains manual and never invokes `sudo`: import or remove `.dev-certs/rootCA.cer` in the browser or user trust store.

## Environment Adapters

If Docker Engine is native to WSL or reachable through another execution context, correct the active shell environment instead of changing tests.

The package-manager adapter resolves the documented Node/npm binary/shim path from Axis's user-local tool directory, PATH, nvm, nvm-windows, or Volta. OpenSSL for certs resolves PATH or Git for Windows. Setup diagnoses Docker and OpenSSL but never invokes an OS package manager, `sudo`, or service configuration.

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
| EF migrations | `ConnectionStrings__Identity`, `ConnectionStrings__BusinessObjects`, `IDENTITY_CONNECTION_STRING`, or `BUSINESS_OBJECTS_CONNECTION_STRING` | `python scripts/axis.py dotnet ef ...` only. |
| Shell adapters | `python scripts/axis.py doctor --profile local-dev` | `DOCKER_HOST`, `NVM_DIR`, `PATH` when tools resolve from another context. |
| Host browser smoke | [frontend/playwright.config.ts](../../frontend/playwright.config.ts) and a running local stack | `python scripts/axis.py local-dev smoke -- <playwright-args>` reuses host Chromium against `https://localhost:3000`, skips the dev server, and imports the local CA into the ignored repo-local `.dev-browser/` NSS store. |
| E2E | [docker-compose.yml](../../docker-compose.yml) and [frontend/playwright.config.ts](../../frontend/playwright.config.ts) | `python scripts/axis.py local-dev e2e` builds and runs the compose E2E profile with API, web, Maildev, service URLs, and browser trust configured. Pass Playwright args after `--` to scope a file or title. |

Common API settings (compose uses service hostnames; host run uses `localhost`):

| Setting | Compose default | Host `appsettings.json` | Purpose |
|---|---|---|---|
| Identity DB | `ConnectionStrings__Identity` → `postgres` | `ConnectionStrings:Identity` → `localhost:5432` | PostgreSQL for Identity/OpenIddict. |
| Business Objects DB | `ConnectionStrings__BusinessObjects` → `postgres` | `ConnectionStrings:BusinessObjects` → `localhost:5432` | PostgreSQL for business object definitions and published versions. |
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

Prefer scoped CLI commands: `status`, `up`, `down`, `smoke`, `e2e`, and focused checks. Use `python scripts/axis.py local-dev smoke -- <playwright-args>` for fast host-browser layout or UI smoke against an already-running local stack; the wrapper sets the browser-facing base URL, skips the Playwright dev server, and prepares an isolated Chromium NSS trust store from the local CA. The store is rebuilt only when the CA fingerprint changes, and HTTPS verification stays enabled. Compose E2E imports the same CA into its container-local Chromium trust store. Use `python scripts/axis.py local-dev e2e -- e2e/sign-in-user.pw.ts` for Compose-backed evidence; add Playwright filters such as `-g "AT-001"` when one acceptance row is in scope. Running `python scripts/axis.py local-dev e2e` with no args remains the full Axis browser E2E workflow. Package Playwright scripts stay behind repo wrappers. `local-dev shell [service]` runs inside the container; host shell (PowerShell, bash, WSL) does not matter.

Use runtime-specific dev servers only through the documented Axis wrapper or owning package script.

Run unit or focused frontend tests while iterating. Integration/API tests need Docker/Testcontainers.

## Database

Create migrations through `python scripts/axis.py dotnet ef migrations add ...`. Use the owning module Infrastructure project as both project and startup project when a `*DbContextFactory` exists.

Identity dev database startup uses `MigrateAsync`. Use reset paths only for disposable local data; do not use schema initialization shortcuts.

## Guardrails

Do not commit local secrets, ports, certs, or personal URLs. Keep compose/docs drift checked through `python scripts/axis.py check local-dev-docs`.
