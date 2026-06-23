# Local dev — Axis CLI

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

The full dev stack runs through `python scripts/axis.py local-dev up`: **Postgres**, **Redis**, **MailDev**, **LocalStack**, **Kafka** (KRaft), **Schema Registry**, **RabbitMQ**, **Vault** (dev mode), the **.NET API**, and the **Vite SPA**. Browser-facing local services run on HTTPS by default: Web at `https://localhost:3000` and API at `https://localhost:5281`. The API runs in Docker by restoring, building, then executing `dotnet Axis.Api.dll`; restart the `api` service after backend changes. Frontend hot-reloads via Vite. Source is bind-mounted — edit on the host, containers pick up changes.

**Canonical service graph and port list:** [`docker-compose.yml`](../../docker-compose.yml) is the source of truth; `scripts/axis.py local-dev ...` is the repo-owned command surface for running it. Published ports bind to `127.0.0.1` only. CI runs `python scripts/axis.py check local-dev-docs` to catch drift.

**Environment contract:** Project docs own repo-root commands, compose services,
ports, and verification commands. Environment adapters below only make that
baseline reachable from the current execution context; do not add personal
paths, agent sandbox flags, or environment-specific workarounds to feature specs
or pattern docs.

---

## Prerequisites

- **Python 3 + PyYAML** - repo maintenance commands run through
  `python scripts/axis.py ...`; install PyYAML into that Python with
  `python -m pip install PyYAML`.
- **.NET SDK 8.x** - local verification, EF migrations, and API contract generation; version must match [`global.json`](../../global.json).
- **Node.js + npm** - frontend verification, OpenAPI TypeScript generation, and legacy wireframe export; major must match [`frontend/.nvmrc`](../../frontend/.nvmrc).
- **Buf CLI + Lychee** - protobuf and markdown-link verification; use versions in [scripts.md § Tool Versions](./scripts.md#tool-versions).
- **Docker** - Docker Engine + Compose v2. Run Axis CLI commands from the repo root; if Docker is not visible, use [Environment adapters](#environment-adapters).
- **OpenSSL** - local development CA and localhost certificate generation; see [Local HTTPS](./local-https.md).
- Host ports free (default compose bindings):

  `3000`, `5281`, `5432`, `6379`, `1025`, `1080`, `4566`, `29092`, `8081`, `5672`, `15672`, `8200`

  Optional observability profile also uses `3001`, `4317`, `4318` — see [Observability (optional)](#observability-optional).
- The API container writes .NET build artifacts to `/tmp/axis-artifacts`, so repo-level Axis .NET builds no longer share `bin/obj` with the container.

---

## Environment doctor

Run the doctor before debugging local stack issues:

```bash
python scripts/axis.py doctor
```

It checks the repo root, Python launcher, PyYAML, Git, documented .NET SDK
major, documented Node major, npm, Buf, Lychee, Docker CLI, Docker Compose, the
active Docker endpoint, and known adapter probes.

Use strict mode when you want the command to fail on blocking local-dev issues:

```bash
python scripts/axis.py doctor --strict
```

## Local HTTPS

Compose requires `.dev-certs/rootCA.pem`, `.dev-certs/rootCA.cer`,
`.dev-certs/localhost.pem`, and `.dev-certs/localhost-key.pem`. Generate and
trust them with [local-https.md](./local-https.md) before running the stack.
Services fail fast when the files are missing; browser and Playwright E2E trust
the local CA instead of disabling HTTPS validation.

## Environment adapters

Use these only when the canonical repo-root commands cannot see the required
tooling. Keep the canonical command in plans and verification reports; mention
the adapter only as execution context.

### Docker endpoint adapter

Preferred path: run Docker, .NET, frontend, and Axis CLI commands from the same
execution environment where `python scripts/axis.py check docker` succeeds.

If Docker is reachable only through an exported endpoint, set `DOCKER_HOST` for
the current process/session using that environment's standard env-var mechanism:

```text
DOCKER_HOST=<docker-endpoint>
```

Only expose local Docker TCP endpoints to loopback/private interfaces; do not
bind them publicly.

Use the endpoint's `/_ping` URL as the quick daemon probe when the Docker CLI is
not visible in the current environment.

If Docker is available only from another execution environment, run the same
canonical Axis CLI repo-root command there.

### Package-manager adapter

If a shell wrapper blocks package-manager execution, fix that environment or use
the runtime's native binary/shim only as the implementation path behind the
matching Axis frontend wrapper. Keep reported commands as Axis commands, e.g.
`python scripts/axis.py frontend ci` and `python scripts/axis.py frontend test`.

---

## Start everything

From the repo root:

```bash
python scripts/axis.py local-dev up
```

If the cert files above are missing, `api`, `web`, and `e2e` fail fast instead
of silently falling back to HTTP.

First boot:

- Infrastructure images pull (Postgres, Redis, Kafka, RabbitMQ, … — several hundred MB total).
- **API** waits for Postgres, Redis, LocalStack, Kafka, Schema Registry, and RabbitMQ to be healthy, then restores, builds, and executes the API DLL with artifacts under `/tmp/axis-artifacts` — first restore ~1–2 min; NuGet cache lives in the `nuget_packages` volume.
- **Web** syncs package-lock dependencies when `package-lock.json` changes, then starts Vite — first install ~1 min; `node_modules` cache lives in `web_node_modules`.

Add `--build` only after changing [`frontend/Dockerfile.dev`](../../frontend/Dockerfile.dev) or the root [`Dockerfile`](../../Dockerfile) (production image). Browser smoke builds only the E2E image because it bakes in the current frontend source and dependencies.

Optional browser smoke uses `python scripts/axis.py local-dev e2e`; see
[testing.md § Browser E2E](./testing.md#browser-e2e).

---

## URLs and ports

| Service | Host access | Notes |
|---|---|---|
| Web (SPA) | <https://localhost:3000> | Vite dev server with the local HTTPS certificate. Browser API calls use `https://localhost:5281/api`. |
| API | <https://localhost:5281> | Container listens on HTTPS `8443`; mapped to host `5281`. |
| API health | <https://localhost:5281/health> | Anonymous; compose healthcheck target using `.dev-certs/rootCA.pem`. |
| API ready | <https://localhost:5281/health/ready> | Includes Postgres + Redis probes. |
| Scalar / Swagger | <https://localhost:5281/scalar/v1> | OpenAPI explorer (Development / Staging only). |
| MailDev UI | <http://localhost:1080> | Outbound email (SMTP `:1025`) lands here. |
| Postgres | `localhost:5432` | User `axis` / password `axis_dev_pass`. API startup creates and migrates active module DBs in Development: `axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_formbuilder`, `axis_workflowengine`. |
| Redis | `localhost:6379` | No auth. |
| LocalStack | <http://localhost:4566> | S3 only (avatars). |
| Kafka (KRaft) | `localhost:29092` | Host listener for local API runs through `python scripts/axis.py dotnet run-api`. Containers use `kafka:9092` on the compose network (not host-published). Carries `*Event` / `*Snapshot` ([ADR-025](../TECH_STACK.md#adr-025-transport-selection-rule-by-message-name-suffix)). |
| Schema Registry | <http://localhost:8081> | `BACKWARD`-only compatibility ([ADR-019](../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). |
| RabbitMQ | `localhost:5672` (AMQP) | Credentials `axis` / `axis_dev_pass`. |
| RabbitMQ UI | <http://localhost:15672> | Management UI. Carries `*Command` / `*Job` / `*SagaStep` ([ADR-024](../TECH_STACK.md#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration)). |
| Vault (dev) | <http://localhost:8200> | Root token `axis-dev-root-token`. **In-memory only** — secrets lost on restart ([ADR-022](../TECH_STACK.md#adr-022-secrets-management-via-hashicorp-vault-in-production)). |

Wolverine **AutoProvision** creates Kafka topics and RabbitMQ exchanges/queues in Development ([`Program.cs`](../../src/Axis.Api/Program.cs)).

---

## Observability (optional)

Grafana LGTM stack ([ADR-018](../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability)):

```bash
python scripts/axis.py local-dev observability up
```

| Service | Host access | Notes |
|---|---|---|
| Grafana | <http://localhost:3001> | Traces / metrics / logs UI. |
| OTLP gRPC | `localhost:4317` | Point an API run outside compose at this, or set `OpenTelemetry__Otlp__Endpoint=http://otel-lgtm:4317` when API runs in compose with the profile. |
| OTLP HTTP | `localhost:4318` | Alternative OTLP ingress. |

Default compose API has OTLP export off (`OpenTelemetry__Otlp__Endpoint=""`) so the stack starts without LGTM.

---

## Daily operations

Run from the repo root. Use [Environment adapters](#environment-adapters) only
when the current execution context cannot reach Docker.

| Goal | Command |
|---|---|
| Pause everything (keep containers + data) | `python scripts/axis.py local-dev stop` |
| Stop and remove containers after smoke/E2E (keep data) | `python scripts/axis.py local-dev down` |
| Start again | `python scripts/axis.py local-dev start` or `python scripts/axis.py local-dev up` |
| Tail logs (all services) | `python scripts/axis.py local-dev logs -f` |
| Tail one service | `python scripts/axis.py local-dev logs -f api` |
| Service status | `python scripts/axis.py local-dev status` |
| Restart one service | `python scripts/axis.py local-dev restart api` |
| Force recreate (env change) | `python scripts/axis.py local-dev recreate api` |
| Run browser smoke | `python scripts/axis.py local-dev e2e` |
| Shell in API container | `python scripts/axis.py local-dev shell api` |
| `psql` | `python scripts/axis.py local-dev psql` |

---

## Hot reload

| Stack | Triggers reload | Needs container restart |
|---|---|---|
| Backend (`api`) | Restart `api` after saving `.cs` under `src/`. | Any backend code, `Directory.Packages.props`, or new `<PackageReference>` → `python scripts/axis.py local-dev restart api`. |
| Frontend (`web`) | Saving under `frontend/src/` — Vite HMR. | `vite.config.ts`, `package.json`, `package-lock.json`, or new npm dep → `python scripts/axis.py local-dev restart web`; the container syncs package-lock dependencies automatically when the lockfile hash changes. |

Vite polling is enabled (`VITE_USE_POLLING=1`) because file-watch events through
bind mounts are not consistent across container runtimes. Expect ~200–500 ms
between frontend save and reload.

---

## Run tests without the full stack

Backend integration tests use **Testcontainers** — no compose stack required for CI-style verification:

```bash
python scripts/axis.py dotnet build
python scripts/axis.py dotnet test
python scripts/axis.py frontend ci
python scripts/axis.py frontend test
```

When `python scripts/axis.py check docker` works for the process running .NET,
Testcontainers uses that Docker endpoint and no extra configuration is needed. If it does not, use the
[Docker endpoint adapter](#docker-endpoint-adapter) instead of
hardcoding Docker endpoints in test code:

```text
DOCKER_HOST=<docker-endpoint>
python scripts/axis.py dotnet test
```

---

## EF Core migrations

Pre-production: each module keeps migrations generated by EF. Do **not** hand-write migration `.cs` files or patch `.Designer.cs` without re-running the tool.

Generate migrations through `python scripts/axis.py dotnet ef ...`; the wrapper
checks the documented .NET SDK before invoking the EF tool. If the EF tool is
missing, install it for the active SDK or fix `PATH` before generating a
migration. Repo scripts must use `PATH`, not hardcoded SDK locations.

Connection string env vars (dummy host is fine for `migrations add`; tests use Testcontainers):

| Module | Environment variable |
|--------|----------------------|
| Identity | `IDENTITY_CONNECTION_STRING` or `ConnectionStrings__Identity` |
| DataModeling | `DATAMODELING_CONNECTION_STRING` or `ConnectionStrings__DataModeling` |
| WorkflowBuilder | `WORKFLOWBUILDER_CONNECTION_STRING` or `ConnectionStrings__WorkflowBuilder` |
| FormBuilder | `FORMBUILDER_CONNECTION_STRING` or `ConnectionStrings__FormBuilder` |
| WorkflowEngine | `WORKFLOWENGINE_CONNECTION_STRING` or `ConnectionStrings__WorkflowEngine` |

Regenerate from repo root (Identity example):

```bash
python scripts/axis.py dotnet ef migrations add InitialCreate \
  --project src/Modules/Identity/Axis.Identity.Infrastructure/Axis.Identity.Infrastructure.csproj \
  --startup-project src/Modules/Identity/Axis.Identity.Infrastructure/Axis.Identity.Infrastructure.csproj
```

Each migration ships **both** `{timestamp}_{Name}.cs` and `{timestamp}_{Name}.Designer.cs`. Workspace modules use `*DbContextFactory` + `DesignTimePublicSchemaWorkspaceContext` (`public` schema at design time).

---

## Reset the database

When migrations or seed data are broken:

```bash
python scripts/axis.py local-dev reset-all
```

On the next boot, **Development only**, `IdentityDbContext.Database.MigrateAsync()` runs at startup ([`Program.cs`](../../src/Axis.Api/Program.cs)) before OpenIddict seeding.

Per-workspace module schemas (`workspace_{workspace-id}`) are provisioned on demand by each module's `WorkspaceVerifiedHandler` when Identity publishes `WorkspaceVerifiedEvent` over Kafka (e.g. [`WorkspaceVerifiedHandler`](../../src/Modules/DataModeling/Axis.DataModeling.Infrastructure/Messaging/WorkspaceVerifiedHandler.cs)). Only Identity's public schema migrates at API startup.

Wipe Postgres only (keep npm/NuGet caches):

```bash
python scripts/axis.py local-dev reset-db
```

---

## Gotchas

- **`launchSettings.json` is ignored** in the container because compose starts the compiled DLL directly, not the SDK launch-profile path; launch profile `applicationUrl` does not apply there.
- **Host browser trust is separate from container trust.** Docker services mount the required files from `.dev-certs/`; Windows, macOS, or Linux browsers need the root CA trusted in that host OS before `https://localhost:3000` is clean.
- **Container build artifacts stay outside the bind mount.** The API compose command passes an artifacts path for restore/build, so container-generated `bin/obj` outputs do not overwrite repo-level IDE or developer-tool artifacts.
- **MailDev health** — compose disables MailDev's baked-in healthcheck (`healthcheck: disable: true`); nothing waits on it.
- **API startup order** — Kafka + Schema Registry + RabbitMQ must be healthy before the API starts; first boot can take ~60s on the API healthcheck `start_period`.
- **Module vs workspace migrations** — API startup migrates each module's public schema in Development; workspace schemas migrate on first workspace provisioning.

---

## Files involved

| File | Role |
|---|---|
| [`docker-compose.yml`](../../docker-compose.yml) | Service graph, env vars, volumes, healthchecks — **port source of truth** |
| `.dev-certs/` | Ignored local CA and HTTPS leaf certificate used by compose |
| `python scripts/axis.py doctor` | Local environment diagnostics for PATH, Docker, package-manager, and adapter visibility |
| `python scripts/axis.py local-dev ...` | Repo-owned command surface for local stack operations |
| `python scripts/axis.py check local-dev-docs` | CI/doc drift: verifies this file matches compose |
| [`Dockerfile`](../../Dockerfile) | Production API image (not used by default compose dev) |
| [`frontend/Dockerfile.dev`](../../frontend/Dockerfile.dev) | Node + Vite dev image |
| [`frontend/Dockerfile.e2e`](../../frontend/Dockerfile.e2e) | Playwright E2E image with NSS certificate tooling |
| [`frontend/vite.config.ts`](../../frontend/vite.config.ts) | `VITE_API_PROXY_TARGET`, `VITE_DEV_HTTPS_CERT`, `VITE_DEV_HTTPS_KEY`, `VITE_USE_POLLING` |
| [`src/Axis.Api/Program.cs`](../../src/Axis.Api/Program.cs) | Dev Identity `MigrateAsync`, Wolverine AutoProvision, Scalar |
