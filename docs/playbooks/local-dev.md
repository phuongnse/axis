# Local dev — `docker compose up`

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

The entire stack (Postgres, Redis, MailDev, LocalStack, .NET API, Vite SPA) runs from one `docker compose` command. Backend hot-reloads via `dotnet watch`; frontend hot-reloads via Vite. Source code is bind-mounted — you edit on the host, the container picks up changes.

---

## Prerequisites

- **Docker** — installed in WSL2 (Docker Engine inside an Ubuntu/Debian WSL distro). All commands below run via `wsl --`.
- Ports `3000`, `5280`, `5432`, `6379`, `1025`, `1080`, `4566` free on the host.
- No Windows-side `dotnet build` running against `src/` while the API container is up (they fight over `bin/obj`).

---

## Start everything

```powershell
wsl -- bash -lc "cd /mnt/d/projects/axis && docker compose up -d"
```

First boot:
- Postgres / Redis / MailDev / LocalStack images pull (~600 MB total).
- API container runs `dotnet restore` then `dotnet watch run` — first restore takes 1–2 min, cached afterward in the `nuget_packages` volume.
- Web container runs `npm ci` then `vite` — first install ~1 min, cached in `web_node_modules` volume.

Add `--build` only if you change the `frontend/Dockerfile.dev` or the root `Dockerfile` (prod image).

---

## URLs

| Service | URL | Notes |
|---|---|---|
| Web (SPA) | <http://localhost:3000> | Vite dev server. `/api/*` and `/connect/*` are proxied to the API container. |
| API | <http://localhost:5280> | Container port `8080` mapped to host `5280`. |
| API health | <http://localhost:5280/health> | Anonymous, used by the compose healthcheck. |
| API ready | <http://localhost:5280/health/ready> | Includes Postgres + Redis probes. |
| Scalar / Swagger | <http://localhost:5280/scalar/v1> | OpenAPI explorer (dev/staging only). |
| MailDev UI | <http://localhost:1080> | Outbound email lands here; nothing leaves the box. |
| Postgres | `localhost:5432` | User `axis` / password `axis_dev_pass`. Module DBs: `axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_formbuilder`, `axis_workflowengine`, `axis_pagebuilder` (created on first `docker compose up` via `infra/postgres/init.d/`). |
| Redis | `localhost:6379` | No auth. |
| LocalStack | <http://localhost:4566> | S3 only (used for avatars). |

---

## Daily operations

All commands assume you're invoking through WSL (`wsl -- bash -lc "cd /mnt/d/projects/axis && <cmd>"`).

| Goal | Command |
|---|---|
| Stop everything (keep data) | `docker compose stop` |
| Start again (no rebuild) | `docker compose start` or `docker compose up -d` |
| Tail logs (all services) | `docker compose logs -f` |
| Tail one service | `docker compose logs -f api` |
| Service status | `docker compose ps` |
| Restart one service | `docker compose restart api` |
| Force recreate (e.g. env change) | `docker compose up -d --force-recreate api` |
| Open a shell in the API container | `docker compose exec api bash` |
| Open `psql` | `docker compose exec postgres psql -U axis -d axis` |

---

## Hot reload

| Stack | What triggers it | What doesn't |
|---|---|---|
| Backend (`api`) | Saving any `.cs` file under `src/` — `dotnet watch` rebuilds and restarts (or hot-reloads if the edit is compatible). | Changes to `Directory.Packages.props` or adding a new `<PackageReference>` need a `docker compose restart api` so `dotnet restore` re-runs. |
| Frontend (`web`) | Saving any file under `frontend/src/` — Vite HMR pushes the update to the browser without losing state. | Changes to `vite.config.ts`, `package.json`, or installing a new npm dep need a `docker compose restart web`. |

Polling is enabled (`DOTNET_USE_POLLING_FILE_WATCHER=1`, `VITE_USE_POLLING=1`) because inotify events from a Windows bind mount don't reach the Linux container reliably. Expect 200–500 ms latency between save and reload.

---

## Reset the database

When migrations or seed data get into a bad state:

```bash
docker compose down -v   # drops the postgres_data + nuget + node_modules volumes
docker compose up -d
```

On the next boot, `IdentityDbContext.Database.MigrateAsync()` runs at startup (dev only — see [`src/Axis.Api/Program.cs`](../../src/Axis.Api/Program.cs)) to recreate Identity + OpenIddict tables. Per-tenant module schemas (`tenant_{org-id}`) are provisioned on demand by each module's `OrganizationVerifiedHandler` (e.g. [`src/Modules/DataModeling/Axis.DataModeling.Infrastructure/Messaging/OrganizationVerifiedHandler.cs`](../../src/Modules/DataModeling/Axis.DataModeling.Infrastructure/Messaging/OrganizationVerifiedHandler.cs)), triggered by Identity's `OrganizationVerifiedEvent` (Kafka, ADR-019).

If you only want to wipe the DB and keep cached npm/nuget:

```bash
docker compose down
docker volume rm axis_postgres_data
docker compose up -d
```

---

## Gotchas

- **`launchSettings.json` is ignored** in the container. The compose command passes `--no-launch-profile` to `dotnet watch`; without it, the launch profile's `applicationUrl` overrides `ASPNETCORE_URLS` and the app only binds to `localhost:5280` *inside* the container — unreachable from the host. If you ever see "Connection reset by peer" against `:5280`, check that flag survived.
- **`bin/` and `obj/` under `src/`** now contain Linux build artifacts produced by the container. If you also want to build on Windows (Rider, `dotnet build` from PowerShell), stop the API container first (`docker compose stop api`) or you'll get file-lock errors. Re-`dotnet build` on Windows will overwrite the Linux artifacts; the container will rebuild on next save.
- **MailDev shows `unhealthy`** if you don't disable its baked-in healthcheck. The compose file disables it (`healthcheck: disable: true`) — nothing depends on its health.
- **Migrations across modules** — the per-tenant DbContexts (DataModeling, WorkflowBuilder, FormBuilder, WorkflowEngine) apply their own migrations the first time their own `OrganizationVerifiedHandler` runs for an org (consuming Identity's Kafka event). There is no "migrate everything on startup" step; that only happens for the public-schema Identity context.

---

## Files involved

| File | Role |
|---|---|
| [`docker-compose.yml`](../../docker-compose.yml) | Service graph, env vars, volumes, healthchecks |
| [`Dockerfile`](../../Dockerfile) | Production build of the API (not used by `docker compose up` — kept for future deploy) |
| [`frontend/Dockerfile.dev`](../../frontend/Dockerfile.dev) | Node + Vite dev server image |
| [`frontend/vite.config.ts`](../../frontend/vite.config.ts) | Reads `VITE_API_PROXY_TARGET` + `VITE_USE_POLLING` |
| [`src/Axis.Api/Program.cs`](../../src/Axis.Api/Program.cs) | Dev-only `EnsureCreated` bootstrap (lines just after `builder.Build()`) |
