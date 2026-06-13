# Local dev — `docker compose up`

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

The full dev stack runs from one `docker compose up -d`: **Postgres**, **Redis**, **MailDev**, **LocalStack**, **Kafka** (KRaft), **Schema Registry**, **RabbitMQ**, **Vault** (dev mode), the **.NET API**, and the **Vite SPA**. Backend hot-reloads via `dotnet watch`; frontend hot-reloads via Vite. Source is bind-mounted — edit on the host, containers pick up changes.

**Canonical port list:** [`docker-compose.yml`](../../docker-compose.yml) is the source of truth; this doc explains how to use it. CI runs `python scripts/axis.py check local-dev-docs` to catch drift.

---

## Prerequisites

- **Docker** — Docker Engine + Compose v2.
  - **Linux / macOS:** run commands from the repo root.
  - **Windows:** use the Docker endpoint available to your shell when `docker info` works. If it does not, but Docker Engine lives inside WSL2, run compose commands from WSL (`wsl -- bash -lc "cd /path/to/axis && …"`) or open a WSL shell and `cd` there.
- Host ports free (default compose bindings):

  `3000`, `5280`, `5432`, `6379`, `1025`, `1080`, `4566`, `29092`, `8081`, `5672`, `15672`, `8200`

  Optional observability profile also uses `3001`, `4317`, `4318` — see [Observability (optional)](#observability-optional).
- Do not run a host-side `dotnet build` against `src/` while the API container is up (they fight over `bin/obj`).

---

## Start everything

From the repo root:

```bash
docker compose up -d
```

**Windows (WSL)** — same command inside WSL:

```powershell
wsl -- bash -lc "cd /mnt/d/projects/axis && docker compose up -d"
```

First boot:

- Infrastructure images pull (Postgres, Redis, Kafka, RabbitMQ, … — several hundred MB total).
- **API** waits for Postgres, Redis, LocalStack, Kafka, Schema Registry, and RabbitMQ to be healthy, then runs `dotnet restore` and `dotnet watch run` — first restore ~1–2 min; NuGet cache lives in the `nuget_packages` volume.
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
| Postgres | `localhost:5432` | User `axis` / password `axis_dev_pass`. Module DBs created on first cluster init via [`infra/postgres/init.d/`](../../infra/postgres/init.d/): `axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_formbuilder`, `axis_workflowengine`, `axis_pagebuilder` (PageBuilder module not started yet — DB reserved). |
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
| Backend (`api`) | Saving `.cs` under `src/` — `dotnet watch` rebuilds/restarts. | `Directory.Packages.props` or new `<PackageReference>` → `docker compose restart api`. |
| Frontend (`web`) | Saving under `frontend/src/` — Vite HMR. | `vite.config.ts`, `package.json`, `package-lock.json`, or new npm dep → `docker compose restart web`; the container syncs `npm ci` automatically when the lockfile hash changes. |

Polling is enabled (`DOTNET_USE_POLLING_FILE_WATCHER=1`, `VITE_USE_POLLING=1`) because inotify from Windows bind mounts through WSL is unreliable. Expect ~200–500 ms between save and reload.

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

Each migration ships **both** `{timestamp}_{Name}.cs` and `{timestamp}_{Name}.Designer.cs`. Tenant modules use `*DbContextFactory` + `DesignTimePublicSchemaTenantContext` (`public` schema at design time).

---

## Reset the database

When migrations or seed data are broken:

```bash
docker compose down -v   # drops postgres_data + nuget + node_modules volumes
docker compose up -d
```

On the next boot, **Development only**, `IdentityDbContext.Database.MigrateAsync()` runs at startup ([`Program.cs`](../../src/Axis.Api/Program.cs)) before OpenIddict seeding.

Per-tenant module schemas (`tenant_{org-id}`) are provisioned on demand by each module's `OrganizationVerifiedHandler` when Identity publishes `OrganizationVerifiedEvent` over Kafka (e.g. [`OrganizationVerifiedHandler`](../../src/Modules/DataModeling/Axis.DataModeling.Infrastructure/Messaging/OrganizationVerifiedHandler.cs)). Only Identity's public schema migrates at API startup.

Wipe Postgres only (keep npm/NuGet caches):

```bash
docker compose down
docker volume rm axis_postgres_data
docker compose up -d
```

---

## Gotchas

- **`launchSettings.json` is ignored** in the container. Compose passes `--no-launch-profile` to `dotnet watch`; without it, the launch profile's `applicationUrl` can override `ASPNETCORE_URLS` and bind only inside the container — "connection reset" on `:5280` from the host.
- **`bin/` and `obj/` under `src/`** contain Linux artifacts from the container. Stop the API (`docker compose stop api`) before a host-side Windows/Rider build, or expect file-lock errors.
- **MailDev health** — compose disables MailDev's baked-in healthcheck (`healthcheck: disable: true`); nothing waits on it.
- **API startup order** — Kafka + Schema Registry + RabbitMQ must be healthy before the API starts; first boot can take ~60s on the API healthcheck `start_period`.
- **Cross-module migrations** — tenant DbContexts migrate on first org provisioning, not at API startup.

---

## Files involved

| File | Role |
|---|---|
| [`docker-compose.yml`](../../docker-compose.yml) | Service graph, env vars, volumes, healthchecks — **port source of truth** |
| `python scripts/axis.py check local-dev-docs` | CI/doc drift: verifies this file matches compose |
| [`Dockerfile`](../../Dockerfile) | Production API image (not used by default compose dev) |
| [`frontend/Dockerfile.dev`](../../frontend/Dockerfile.dev) | Node + Vite dev image |
| [`frontend/vite.config.ts`](../../frontend/vite.config.ts) | `VITE_API_PROXY_TARGET`, `VITE_USE_POLLING` |
| [`src/Axis.Api/Program.cs`](../../src/Axis.Api/Program.cs) | Dev Identity `MigrateAsync`, Wolverine AutoProvision, Scalar |
