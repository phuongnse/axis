# Local dev — `docker compose up`

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

The full dev stack runs from one `docker compose up -d`: **Postgres**, **Redis**, **MailDev**, **LocalStack**, **Kafka** (KRaft), **Schema Registry**, **RabbitMQ**, **Vault** (dev mode), the **.NET API**, and the **Vite SPA**. The API runs in Docker via `dotnet run`; restart the `api` service after backend changes. Frontend hot-reloads via Vite. Source is bind-mounted — edit on the host, containers pick up changes.

**Canonical port list:** [`docker-compose.yml`](../../docker-compose.yml) is the source of truth; this doc explains how to use it. CI runs `python scripts/axis.py check local-dev-docs` to catch drift.

---

## Prerequisites

- **Python 3** - repo maintenance commands run through `python scripts/axis.py ...`.
  If a shell cannot resolve `python`, fix PATH before using repo scripts.
- **PyYAML** - Python package required by skill-creator `quick_validate.py`
  when editing repo-scoped Codex skills under `.agents/skills/`. Install it
  into the same Python that runs repo scripts:
  `python -m pip install PyYAML`.
- **.NET SDK 8.x** - required for host-side `dotnet build`, `dotnet test`,
  `dotnet format`, EF migrations, and API contract generation. The compose API
  container has its own SDK, but local verification expects `dotnet` on `PATH`.
- **Node.js + npm** - required for host-side frontend checks (`npm run ci`,
  `npm run test`), OpenAPI TypeScript generation, and wireframe export. The
  compose web container has its own Node runtime, but local verification expects
  `node` and `npm` on `PATH`.
- **Docker** — Docker Engine + Compose v2.
  - **Linux / macOS:** run commands from the repo root.
  - **Windows:** use the Docker endpoint available to your shell when `docker info` works. If it does not, but Docker Engine lives inside WSL2, run compose commands from WSL (`wsl -- bash -lc "cd /path/to/axis && …"`) or open a WSL shell and `cd` there.
- **Windows PowerShell:** prefer `npm.cmd` over `npm` when execution policy blocks
  `npm.ps1`.
- Host ports free (default compose bindings):

  `3000`, `5280`, `5432`, `6379`, `1025`, `1080`, `4566`, `29092`, `8081`, `5672`, `15672`, `8200`

  Optional observability profile also uses `3001`, `4317`, `4318` — see [Observability (optional)](#observability-optional).
- The API container writes .NET build artifacts to `/tmp/axis-artifacts`, so host-side `dotnet build` no longer shares `bin/obj` with the container.

---

## One-time environment doctor

Run the doctor before debugging local stack issues:

```bash
python scripts/axis.py doctor
```

It checks the repo root, Python launcher, PyYAML, Git, .NET SDK, Node, npm,
Docker CLI, Docker Compose, the active Docker endpoint, WSL Docker, and the
common WSL2 TCP daemon endpoint at `127.0.0.1:2375`.

Use strict mode when you want the command to fail on blocking local-dev issues:

```bash
python scripts/axis.py doctor --strict
```

Windows with Docker Engine inside WSL2 usually needs one of these paths:

```powershell
# Current shell only
$env:DOCKER_HOST = "tcp://127.0.0.1:2375"

# Persist for future PowerShell sessions
[Environment]::SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:2375", "User")
```

Only expose the Docker TCP daemon on `127.0.0.1`; do not bind it to a public
interface.

If Docker works in WSL but not in PowerShell, run compose through WSL:

```powershell
wsl.exe bash -lc "cd /path/to/axis && docker compose up -d"
```

If PowerShell blocks `npm.ps1`, use `npm.cmd`:

```powershell
cd frontend
npm.cmd run ci
npm.cmd run test
```

---

## Start everything

From the repo root:

```bash
docker compose up -d
```

**Windows (WSL)** — same command inside WSL:

```powershell
wsl -- bash -lc "cd /path/to/axis && docker compose up -d"
```

First boot:

- Infrastructure images pull (Postgres, Redis, Kafka, RabbitMQ, … — several hundred MB total).
- **API** waits for Postgres, Redis, LocalStack, Kafka, Schema Registry, and RabbitMQ to be healthy, then runs `dotnet restore` and `dotnet run` with container artifacts under `/tmp/axis-artifacts` — first restore ~1–2 min; NuGet cache lives in the `nuget_packages` volume.
- **Web** syncs `npm ci` when `package-lock.json` changes, then starts Vite — first install ~1 min; `node_modules` cache lives in `web_node_modules`.

Add `--build` only after changing [`frontend/Dockerfile.dev`](../../frontend/Dockerfile.dev) or the root [`Dockerfile`](../../Dockerfile) (production image).

---

## URLs and ports

| Service | Host access | Notes |
|---|---|---|
| Web (SPA) | <http://localhost:3000> | Vite dev server. `/api/*` and `/connect/*` proxy to the API container. |
| API | <http://localhost:5280> | Container listens on `8080`; mapped to host `5280`. |
| API health | <http://localhost:5280/health> | Anonymous; compose healthcheck target. |
| API ready | <http://localhost:5280/health/ready> | Includes Postgres + Redis probes. |
| Scalar / Swagger | <http://localhost:5280/scalar/v1> | OpenAPI explorer (Development / Staging only). |
| MailDev UI | <http://localhost:1080> | Outbound email (SMTP `:1025`) lands here. |
| Postgres | `localhost:5432` | User `axis` / password `axis_dev_pass`. API startup creates and migrates active module DBs in Development: `axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_formbuilder`, `axis_workflowengine`. |
| Redis | `localhost:6379` | No auth. |
| LocalStack | <http://localhost:4566> | S3 only (avatars). |
| Kafka (KRaft) | `localhost:29092` | Host listener for local `dotnet run`. Containers use `kafka:9092` on the compose network (not host-published). Carries `*Event` / `*Snapshot` ([ADR-025](../TECH_STACK.md#adr-025-transport-selection-rule-by-message-name-suffix)). |
| Schema Registry | <http://localhost:8081> | `BACKWARD`-only compatibility ([ADR-019](../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)). |
| RabbitMQ | `localhost:5672` (AMQP) | Credentials `axis` / `axis_dev_pass`. |
| RabbitMQ UI | <http://localhost:15672> | Management UI. Carries `*Command` / `*Job` / `*SagaStep` ([ADR-024](../TECH_STACK.md#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration)). |
| Vault (dev) | <http://localhost:8200> | Root token `axis-dev-root-token`. **In-memory only** — secrets lost on restart ([ADR-022](../TECH_STACK.md#adr-022-secrets-management-via-hashicorp-vault-in-production)). |

Wolverine **AutoProvision** creates Kafka topics and RabbitMQ exchanges/queues in Development ([`Program.cs`](../../src/Axis.Api/Program.cs)).

---

## Observability (optional)

Grafana LGTM stack ([ADR-018](../TECH_STACK.md#adr-018-opentelemetry-sdk-with-grafana-stack-for-observability)):

```bash
docker compose --profile observability up -d
```

| Service | Host access | Notes |
|---|---|---|
| Grafana | <http://localhost:3001> | Traces / metrics / logs UI. |
| OTLP gRPC | `localhost:4317` | Point host-run API at this, or set `OpenTelemetry__Otlp__Endpoint=http://otel-lgtm:4317` when API runs in compose with the profile. |
| OTLP HTTP | `localhost:4318` | Alternative OTLP ingress. |

Default compose API has OTLP export off (`OpenTelemetry__Otlp__Endpoint=""`) so the stack starts without LGTM.

---

## Daily operations

Run from the repo root (or prefix with `wsl -- bash -lc "cd /path/to/axis && …"` on Windows):

| Goal | Command |
|---|---|
| Stop everything (keep data) | `docker compose stop` |
| Start again | `docker compose start` or `docker compose up -d` |
| Tail logs (all services) | `docker compose logs -f` |
| Tail one service | `docker compose logs -f api` |
| Service status | `docker compose ps` |
| Restart one service | `docker compose restart api` |
| Force recreate (env change) | `docker compose up -d --force-recreate api` |
| Shell in API container | `docker compose exec api bash` |
| `psql` | `docker compose exec postgres psql -U axis -d axis` |

---

## Hot reload

| Stack | Triggers reload | Needs container restart |
|---|---|---|
| Backend (`api`) | Restart `api` after saving `.cs` under `src/`. | Any backend code, `Directory.Packages.props`, or new `<PackageReference>` → `docker compose restart api`. |
| Frontend (`web`) | Saving under `frontend/src/` — Vite HMR. | `vite.config.ts`, `package.json`, `package-lock.json`, or new npm dep → `docker compose restart web`; the container syncs `npm ci` automatically when the lockfile hash changes. |

Vite polling is enabled (`VITE_USE_POLLING=1`) because inotify from Windows bind mounts through WSL is unreliable. Expect ~200–500 ms between frontend save and reload.

---

## Run tests without the full stack

Backend integration tests use **Testcontainers** — no compose stack required for CI-style verification:

```bash
dotnet build && dotnet test
cd frontend && npm run ci && npm run test
```

When `docker info` works in the shell running .NET, Testcontainers uses that
Docker endpoint and no extra configuration is needed. If it does not, but Docker
Engine is running inside WSL2 with its TCP daemon exported, point Testcontainers
at that daemon:

```powershell
$env:DOCKER_HOST = "tcp://127.0.0.1:2375"
dotnet test Axis.sln --nologo
```

Use `http://127.0.0.1:2375/_ping` as the quick WSL2 daemon probe when
`docker.exe` is not available in PowerShell.

---

## EF Core migrations (`dotnet ef`)

Pre-production: each module keeps migrations generated by EF. Do **not** hand-write migration `.cs` files or patch `.Designer.cs` without re-running the tool.

`dotnet` and `dotnet ef` must resolve from `PATH`. If either command is missing,
install the .NET SDK / EF tool or fix your shell `PATH` before generating a
migration; repo scripts should not guess a machine-specific SDK location.

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
dotnet ef migrations add InitialCreate \
  --project src/Modules/Identity/Axis.Identity.Infrastructure/Axis.Identity.Infrastructure.csproj \
  --startup-project src/Modules/Identity/Axis.Identity.Infrastructure/Axis.Identity.Infrastructure.csproj
```

Each migration ships **both** `{timestamp}_{Name}.cs` and `{timestamp}_{Name}.Designer.cs`. Workspace modules use `*DbContextFactory` + `DesignTimePublicSchemaWorkspaceContext` (`public` schema at design time).

---

## Reset the database

When migrations or seed data are broken:

```bash
docker compose down -v   # drops postgres_data + nuget + node_modules volumes
docker compose up -d
```

On the next boot, **Development only**, `IdentityDbContext.Database.MigrateAsync()` runs at startup ([`Program.cs`](../../src/Axis.Api/Program.cs)) before OpenIddict seeding.

Per-workspace module schemas (`workspace_{workspace-id}`) are provisioned on demand by each module's `WorkspaceVerifiedHandler` when Identity publishes `WorkspaceVerifiedEvent` over Kafka (e.g. [`WorkspaceVerifiedHandler`](../../src/Modules/DataModeling/Axis.DataModeling.Infrastructure/Messaging/WorkspaceVerifiedHandler.cs)). Only Identity's public schema migrates at API startup.

Wipe Postgres only (keep npm/NuGet caches):

```bash
docker compose down
docker volume rm axis_postgres_data
docker compose up -d
```

---

## Gotchas

- **`launchSettings.json` is ignored** in the container. Compose passes `--no-launch-profile` to `dotnet run`; without it, the launch profile's `applicationUrl` can override `ASPNETCORE_URLS` and bind only inside the container — "connection reset" on `:5280` from the host.
- **Container build artifacts stay outside the bind mount.** The API compose command passes `--artifacts-path /tmp/axis-artifacts` to `dotnet restore` and `dotnet run`, so Linux `bin/obj` outputs do not overwrite host-side Windows/Rider assets.
- **MailDev health** — compose disables MailDev's baked-in healthcheck (`healthcheck: disable: true`); nothing waits on it.
- **API startup order** — Kafka + Schema Registry + RabbitMQ must be healthy before the API starts; first boot can take ~60s on the API healthcheck `start_period`.
- **Module vs workspace migrations** — API startup migrates each module's public schema in Development; workspace schemas migrate on first workspace provisioning.

---

## Files involved

| File | Role |
|---|---|
| [`docker-compose.yml`](../../docker-compose.yml) | Service graph, env vars, volumes, healthchecks — **port source of truth** |
| `python scripts/axis.py doctor` | Local environment diagnostics for PATH, Docker, WSL, and npm shell issues |
| `python scripts/axis.py check local-dev-docs` | CI/doc drift: verifies this file matches compose |
| [`Dockerfile`](../../Dockerfile) | Production API image (not used by default compose dev) |
| [`frontend/Dockerfile.dev`](../../frontend/Dockerfile.dev) | Node + Vite dev image |
| [`frontend/vite.config.ts`](../../frontend/vite.config.ts) | `VITE_API_PROXY_TARGET`, `VITE_USE_POLLING` |
| [`src/Axis.Api/Program.cs`](../../src/Axis.Api/Program.cs) | Dev Identity `MigrateAsync`, Wolverine AutoProvision, Scalar |
