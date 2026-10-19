# Installation

*(Planned for M9.)* How to install and upgrade Axis on premises and in the
cloud. It follows
[D44](../decisions.md#d44-packaging-for-on-premises-and-cloud-installation--agreed).
None of it is built. Today only the development `Dockerfile` and
`compose.yaml` exist. They hold the samples and development credentials, and
they are not for production. See [AGENTS.md](../../AGENTS.md#commands) for them.

The conventions of [architecture.md](../architecture.md) apply, and Dn refers
to [decisions.md](../decisions.md). In this page, "database role" always means
the PostgreSQL role.

An installation has three hosts:

- The server serves the API, the sites and the SPA.
- The worker runs background work.
- The admin host, `Axis.Admin`, is a command-line tool. A platform operator
  runs it to create tenants, migrate tenant databases and activate releases.
  See [administration](administration.md).

An installation also needs a PostgreSQL 18 server and an OIDC provider. Axis
does not ship either one for production.

## Images

There are three production images: `axis-server`, `axis-worker` and
`axis-admin`. They hold no sample, no sample extension package, no sample
module, no development realm and no credential.

- **Environment.** They run the `Production` environment.
- **User.** They run as a non-root user and work with a read-only root file
  system.
- **Settings.** They read settings only from environment variables and from
  files in `/run/secrets`. Each file is named after a setting in its
  environment variable form, such as `Platform__SecretKey`. Its content is the
  value. Compose `secrets:` and Kubernetes secret volumes put files there.
- **Packages and modules.** Mount extension packages at `/extensions` and
  modules at `/modules`.
- **Application folders.** Mount the folder into the admin container, then run
  `activate` on it.
- **No curl.** The images hold no `curl`, so they have no container health
  check. Use the probes of the orchestrator, see
  [health checks](../architecture.md#health-checks).
- **PostgreSQL tools.** `axis-admin` holds the PostgreSQL 18 client tools, such
  as `pg_dump`, for backup and restore.
- **Version.** Every image carries the Axis version in the OCI label
  `org.opencontainers.image.version`. `axis-admin version` prints it.

## Settings and secrets

Every installation sets these. A value marked secret comes from a file in
`/run/secrets` or from a secret store of the platform, never from a committed
file.

| Setting | Environment variable | Hosts | Secret | Source |
| --- | --- | --- | --- | --- |
| `Tenancy:Source` | `Tenancy__Source` | server, worker | no | Set to `directory`. The default `configuration` reads a `Tenants` section, which is for development. |
| `Tenancy:RefreshInterval` | `Tenancy__RefreshInterval` | server, worker | no | Optional. It defaults to 30 seconds. |
| `ConnectionStrings:Platform` | `ConnectionStrings__Platform` | server, worker | yes | The read-only platform database role. |
| `ConnectionStrings:PlatformSchema` | `ConnectionStrings__PlatformSchema` | server, admin | yes | The role that owns the `platform` schema. The server uses it only at start. The worker never gets it. |
| `Platform:SecretKey` | `Platform__SecretKey` | server, worker, admin | yes | The installation key, 32 bytes in base64. Generate it once and give every host the same value. |
| `Admin:ConnectionString` | `Admin__ConnectionString` | admin | yes | A database role with `CREATEDB` and `CREATEROLE`, not a superuser. |
| none | `AXIS_OIDC_CLIENT_SECRET` | admin | yes | The OIDC client secret of the new tenant. Only `tenant create` reads it. |
| `Extensions:Path` | `Extensions__Path` | server, worker, admin | no | Set to `/extensions`. |
| `Modules:Path` | `Modules__Path` | server, worker, admin | no | Set to `/modules`. |
| `Health:Urls` | `Health__Urls` | worker | no | The image keeps `http://127.0.0.1:5208`. The Helm chart sets it to all interfaces so the kubelet can probe it. |
| `ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | server | no | The image default is 8080. |
| `OTEL_*` | `OTEL_*` | server, worker | maybe | Optional. See [telemetry export](configuration.md#telemetry-export). A collector token in the headers is secret. |

Read the rules of each setting in
[tenancy settings](configuration.md#tenancy-settings) and
[admin settings](configuration.md#admin-settings). They are not repeated here.

- **`ActivateOnStartup` must be empty** under `directory`, or the server stops
  at start. The admin host activates releases, see
  [administration](administration.md#activate).
- **Tenant settings are not host settings.** A tenant's OIDC settings, database
  passwords and bindings live in the tenant directory, and the secrets are
  encrypted there. See [platform database](storage.md#platform-database). No
  host setting holds them.
- **The worker needs no schema connection** of any kind and gets no
  `ConnectionStrings:PlatformSchema`.

## On premises

This installs Axis on one machine with Docker Compose. PostgreSQL and the OIDC
provider run elsewhere.

**Before the first start.** Create these database roles on the PostgreSQL
server. See [database roles](storage.md#database-roles) for what each may do.

| Database role | Used for |
| --- | --- |
| Platform schema owner | `ConnectionStrings:PlatformSchema`. It owns the `platform` schema. |
| Platform read-only role | `ConnectionStrings:Platform`. It reads the `platform` schema and nothing else. |
| Admin role | `Admin:ConnectionString`. It has `CREATEDB` and `CREATEROLE` and is not a superuser. |

Create the tenant database roles through `tenant create`, not by hand. Also
create the OIDC client for each tenant in your identity provider.

**The files.** The example lives in `deploy/compose/`. It holds a
`compose.yaml` with the server, the worker and the admin host, and the
`secrets:` entries that point at the secret files. The server and the worker
run the production images with a read-only root file system. The admin host
runs only when asked, so `docker compose up` does not start it.

**The order.**

1. Put the secret files in place.
2. Run `docker compose run --rm admin migrate`.
3. Start the server and the worker.
4. Create a tenant with
   `docker compose run --rm admin tenant create <id> --host <host> --oidc-authority <url> --oidc-client-id <id>`.
   Put the client secret in `AXIS_OIDC_CLIENT_SECRET`.
5. Activate an application with
   `docker compose run --rm admin activate /apps/<folder>`. The folder is
   mounted into the admin container at `/apps`.

The hosts serve the tenant after their next refresh.

**The smoke test.** A planned test starts the example with a test PostgreSQL
and Keycloak. It creates a tenant and activates the leave request sample,
`samples/apps/leave-requests`. It checks that the tenant's host serves its site
at `/leave` and that both hosts are ready. The leave request sample needs the
`departments` module, so the test mounts it from `artifacts/modules` at
`/modules` for the test only. The production images still hold no sample
module.

## In the cloud

This installs Axis on Kubernetes with a Helm chart. PostgreSQL, the OIDC
provider and the source of secrets are external.

- **The chart.** It lives in `deploy/helm/axis/`.
- **Deployments.** One for the server and one for the worker. Each has
  `httpGet` probes on `/health/live` and `/health/ready`, see
  [health checks](../architecture.md#health-checks). The chart sets
  `Health__Urls` for the worker so the kubelet can reach it.
- **The migrate Job.** A pre-upgrade hook Job runs `migrate` with the new admin
  image before the Deployments roll out.
- **Secrets.** Mount them at `/run/secrets` from a Kubernetes Secret, or from
  your secret store through it. The names are those of the
  [settings table](#settings-and-secrets).
- **Tenants and releases.** Run `tenant create` and `activate` from a Job or a
  pod that uses the admin image, with the application folder mounted.
- **Checks.** `helm lint` and `helm template`, validated against the Kubernetes
  schemas. M9 has no test against a live cluster.

## Upgrade

An upgrade has a fixed order. A host serves only tenants on its own schema
version, as [D43](../decisions.md#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed)
says. So migrate first and roll out the hosts second.

1. Back up the platform database and every tenant database. For example, run
   `pg_dump` from the admin image. A backup command comes with the separate M9
   backup and restore issue.
2. Run `migrate` with the new admin image.
3. Roll out the new server and the new worker.

Between steps 2 and 3, migrated tenants answer `503`. A tenant whose migration
failed keeps its old version. See [migrate](administration.md#migrate) for the
window and for failed tenants.

**No downgrade.** Migrations are not reversed. To roll back, restore the backup
from step 1 and run the old images.

## Open points

These are not decided yet.

- **The data protection key ring.** Sessions across replicas and restarts need
  a shared ASP.NET data protection key ring. A read-only root file system has
  nowhere to keep one. D44 does not decide where it lives.
- **Forwarded headers.** A server behind a reverse proxy that ends TLS needs
  forwarded headers for secure cookies and OIDC redirect addresses. The server
  does not handle them today.
- **The first install.** The server applies the platform migrations at start,
  see [platform database](storage.md#platform-database). The docs do not yet
  say how `migrate` and `tenant create` behave before the `platform` schema
  exists, so the first-install order may change.
- **Deployment bindings.** No admin command sets a tenant's deployment
  bindings, see [administration](administration.md#not-in-m9). So an
  installation cannot set the purchasing system binding of the purchase request
  sample through the admin tool alone.
- **Rotating the installation key.** M9 has no rotation, see
  [D42](../decisions.md#d42-tenant-directory-in-the-platform-database--agreed).
  After a leaked `Platform:SecretKey`, every stored secret must be set again by
  hand.
