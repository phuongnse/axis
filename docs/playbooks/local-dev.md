# Local Dev

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/scripts.md](./scripts.md) · [AGENTS.md](../../AGENTS.md)

Use `python scripts/axis.py local-dev ...` for local stack work. Do not document raw Docker/npm/dotnet workflows as project commands.

## Prerequisites

- Python and Git from [scripts.md § Tool Versions](./scripts.md#tool-versions).
- Docker Engine with Compose reachable from the shell running tests. Native Docker Engine inside WSL is supported; Docker Desktop is not required.
- OpenSSL on PATH, or from Git for Windows, for local HTTPS certificates.
- .NET SDK from [global.json](../../global.json) and Node from [frontend/.nvmrc](../../frontend/.nvmrc), either already available or installed user-locally by Axis.

First-time preparation is `python scripts/axis.py setup --profile local-dev --install-user-tools`. This validates the cumulative doctor profile, restores locked dependencies, generates local certificates, and installs the pre-push hook. Add `--trust-local-ca` to opt into current-user host trust; without it, setup reports the trust state and exact browser-readiness follow-up. `--yes` skips the Axis prompt, but Windows may still show its security warning. Use `--plan-only` before execution. Run `python scripts/axis.py doctor --profile local-dev --strict` for diagnosis without setup mutations.

## HTTPS

Cert material stays local under ignored `.dev-certs/`; private keys never leave that directory.

- `.dev-certs/rootCA.pem` is for containers, E2E Node trust, and Playwright browser trust stores.
- `.dev-certs/rootCA.cer` is for host OS trust.
- `.dev-certs/localhost.pem` and `.dev-certs/localhost-key.pem` cover `localhost`, loopback, `api`, and `web`.

`local-dev certs` reuses valid material. To rotate a CA trusted through Axis, run `local-dev untrust-certs`, `local-dev certs --renew`, then `local-dev trust-certs`; Axis blocks replacement while its current trust marker remains. The trust commands update the current-user Windows store (including WSL browsers) or macOS login keychain after confirming the displayed SHA-256 fingerprint. Native Linux remains manual and never invokes `sudo`: import or remove `.dev-certs/rootCA.cer` in the browser or user trust store.

## Environment Adapters

If Docker Engine is native to WSL or reachable through another execution context, correct the active shell environment instead of changing tests. Doctor distinguishes an unreachable daemon from Docker group membership that requires a new login shell or WSL restart.

The package-manager adapter resolves the documented Node/npm binary/shim path from Axis's user-local tool directory, PATH, nvm, nvm-windows, or Volta. OpenSSL for certs resolves PATH or Git for Windows. Setup diagnoses Docker and OpenSSL but never invokes an OS package manager, `sudo`, or service configuration.

## Stack

Use `python scripts/axis.py local-dev up`; it waits for Compose healthchecks and prints the ready host URLs plus host-browser trust status. Stop with `python scripts/axis.py local-dev down` unless the user asks to keep services running.

An independently deployed first-party product may supply one or more explicit Compose YAML files with `python scripts/axis.py local-dev --compose-overlay <path> <command>`. Put the option before the command and repeat it in deployment order when multiple overlays are required. Axis resolves every path before invoking Docker and rejects missing, non-YAML, or duplicate files; it never discovers sibling products or imports their client identity. After the first topology-changing command successfully applies an overlay, Axis records the normalized ordered paths in ignored `.local/local-dev-topology.json`; an existing Compose API container is adopted from its Compose metadata before mutation. The marker contains no deployment values or secrets and survives ordinary `down`. Later lifecycle, diagnostic-container, database, and browser commands fail before Docker mutation when overlays are omitted, reordered, added, or removed; use the owning product wrapper or the exact recorded `--compose-overlay` arguments. Read-only `status` and `logs` remain available without matching arguments. If an external restart recreates containers without the recorded overlay, use the owning product's explicit data-preserving topology recovery command when it can prove the same ordered overlay and preserved deployment state; do not delete the marker or reset the database. Successful `down --volumes --yes` clears the marker after destroying persistent data; cancellation or failure preserves it. A successful explicit `reset-all --yes` shuts down the recorded topology, destroys all local volumes, and replaces the marker with its requested topology; a failed reset leaves the prior marker intact.

A trusted overlay may own a finite browser verification service selected with `local-dev ... e2e --service <name> -- <test-args>`; Axis validates the service name and retains the build/run boundary without exposing an arbitrary command. The product repository owns the finite command that supplies these arguments and its deployment values; use only trusted overlay files because Compose configuration can execute containers. Host-native `python scripts/axis.py dotnet run-api` is blocked while local data is bound to overlays because that path cannot prove the overlay-owned deployment values.

Mandatory services in [docker-compose.yml](../../docker-compose.yml): `postgres`, `redis`, `maildev`, `api`, and `web`; optional services: `otel-lgtm` for observability debugging and `e2e`.

Host ports published by compose: `1025`, `1080`, `3000`, `4318`, `5281`, `5432`, `6379`.

`Axis.Mcp` runs on the host through the MCP client's `python scripts/axis.py mcp serve` command; it is intentionally not a Compose service. The bridge uses the API's loopback HTTPS endpoint and the local CA from `.dev-certs/`. The MCP entrypoint reuses a healthy stack or starts `local-dev up` before handing stdin/stdout to the stdio bridge. Do not daemonize the bridge or run it as a detached background process: its stdout is the MCP protocol stream.

[docker-compose.yml](../../docker-compose.yml) and this playbook are the source of truth for Axis local Docker services. If compose changes, update this file in the same PR.

After frontend manifest or toolchain changes, reconcile running local-dev services with the current manifests before trusting browser smoke or E2E results. Use the Axis local-dev wrapper to recreate affected services when dependency volumes or runtime caches may be stale.

The `web` service uses Vite hot module replacement and a CA-verified HTTPS healthcheck. Browser OAuth requests stay on the current web origin and reach the API through the `/connect` proxy, which preserves the browser-facing Host so OpenIddict redirects stay same-origin; the API's canonical `OpenIddict:Issuer` remains `https://localhost:5281` regardless of transport. The Compose E2E runner shares the `web` network namespace, so Chromium reaches the configured `https://localhost:3000` origin and a Node-owned loopback callback without rewriting redirects or weakening TLS. Exact local redirect URIs and browser origins come from the required `OpenIddict:PublicClientCatalog` in [src/Axis.Api/appsettings.json](../../src/Axis.Api/appsettings.json); CORS is derived from that catalog and Compose does not own a separate origin list. The `api` service rebuilds and restarts when mounted source, project, or migration files change so startup composition and development migrations run against the new code. Watcher builds and launches share a container-owned artifacts layout outside the source bind mount; `python scripts/axis.py check local-dev-docs` enforces those invariants. A brief unhealthy period while the API restarts is expected. Compose command or environment changes require `local-dev recreate <service>` because they change the container definition rather than mounted source.

Local overrides live in ignored root `.env.local`. See [.env.example](../../.env.example) for optional Compose variables; stack defaults stay in [docker-compose.yml](../../docker-compose.yml).

## Environment

| Layer | Owner | When you set it |
|---|---|---|
| Docker Compose stack | [docker-compose.yml](../../docker-compose.yml) | Default `local-dev up` — no `.env` file required. |
| Compose overrides | `.env.local` (copy from [.env.example](../../.env.example)) | Optional; only when a compose default needs changing (e.g. `VITE_USE_POLLING`). |
| Product deployment overlay | Explicit external YAML passed with `local-dev --compose-overlay <path>` | Independently owned first-party client registration, exact browser origin, and product service composition; never product identity in Axis source. |
| API on host | [src/Axis.Api/appsettings.json](../../src/Axis.Api/appsettings.json) | Host-native dev without the API container (`python scripts/axis.py dotnet run-api`). Override with ASP.NET env vars (`Section__Key`) or ignored `appsettings.Development.json`. |
| EF migration scaffolding | Finite module mapping in [scripts/axis.py](../../scripts/axis.py) | `python scripts/axis.py migration add <module> <Name>`; the wrapper supplies a non-routable design-time connection string. |
| Shell adapters | `python scripts/axis.py doctor --profile local-dev` | `DOCKER_HOST`, `NVM_DIR`, `PATH` when tools resolve from another context. |
| Browser verification | [docker-compose.yml](../../docker-compose.yml), [frontend/Dockerfile.e2e](../../frontend/Dockerfile.e2e), and [frontend/playwright.config.ts](../../frontend/playwright.config.ts) | `local-dev smoke` and `local-dev e2e` both start or reconcile the local stack, build the pinned browser image, and run in the web network namespace with the localhost browser origin, API, Maildev, loopback callback, and browser trust configured. Pass Playwright args after `--` to scope a file or title. |

[docker-compose.yml](../../docker-compose.yml) owns service-facing database, cache, mail, TLS, and E2E values. [src/Axis.Api/appsettings.json](../../src/Axis.Api/appsettings.json) owns API defaults, including the canonical `OpenIddict:Issuer` and public-client catalog; deployments override the issuer and complete catalog with their externally authoritative API and first-party client origins. [frontend/vite.config.ts](../../frontend/vite.config.ts) owns host Vite proxy defaults. Keep `App:BaseUrl` as the browser-facing origin used in verification email links; the local default is `https://localhost:3000`.

## Daily Operations

Prefer scoped CLI commands: `status`, `up`, `down`, `smoke`, `e2e`, and focused checks. Browser verification has one execution environment: both browser commands reconcile the mandatory stack, build the Compose E2E image, import the local CA into its container-local Chromium trust store, and keep HTTPS verification enabled. When changed source requires an image-backed runtime service rebuild, name only that service with `python scripts/axis.py local-dev e2e --build-service <compose-service> -- <playwright-args>`; repeat the option for each changed service. Runtime rebuilds are explicit and scoped, never broad or automatic. `python scripts/axis.py local-dev smoke` always runs the fixed `e2e/local-dev-smoke.pw.ts` journey. Use `python scripts/axis.py local-dev e2e -- e2e/sign-in-user.pw.ts` for acceptance evidence; add filters such as `-g "AT-001"` when one row is in scope. Running `python scripts/axis.py local-dev e2e` with no args runs the full browser suite; reserve it for CI or a cross-cutting diff that invalidates every browser surface, not routine review. The reconciled stack remains running after either command. Package Playwright scripts stay behind repo wrappers. `local-dev shell [service]` is an unrestricted container diagnostic escape hatch; it is never a finite workflow or evidence command.

Use runtime-specific dev servers only through the documented Axis wrapper or owning package script.

Run unit or focused frontend tests while iterating. Integration/API tests need Docker/Testcontainers.

## Database

Create migrations through `python scripts/axis.py migration add <identity|business-objects|rules|audit|authorization|solutions> <PascalCaseName>`. Axis builds only the selected Infrastructure project serially, then scaffolds with `--no-build` while fixing the startup project, `DbContext`, output directory, and isolated design-time connection string for that module.

Identity and Audit dev database startup use `MigrateAsync`. Audit uses the `Audit`/`ConnectionStrings__Audit` connection targeting `axis_audit`. Compose mounts [infra/postgres/init.d](../../infra/postgres/init.d) read-only at PostgreSQL's first-initialization directory; [01-create-databases.sql](../../infra/postgres/init.d/01-create-databases.sql) creates `axis_audit` only when the Postgres data volume is first initialized. Use reset paths only for disposable local data; do not use schema initialization shortcuts.

Volume deletion is never implicit. `local-dev down --volumes`, `local-dev reset-db`, and `local-dev reset-all` refuse to run without `--yes`.

## Guardrails

Do not commit local secrets, ports, certs, or personal URLs. Keep compose/docs drift checked through `python scripts/axis.py check local-dev-docs`.
