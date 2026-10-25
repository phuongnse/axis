# Installation

How to install and upgrade Axis on premises and in the cloud. It follows
[D44](../decisions/d44-packaging-for-on-premises-and-cloud-installation.md).
The three production images, the Compose example and the Helm chart are built.
The default target of the `Dockerfile` and `compose.yaml` at the repository
root build the development images instead. Those hold the samples and
development credentials, and they are not for production. See
[development](../development.md#docker-compose) for them.

The conventions of [architecture.md](../architecture.md) apply, and Dn refers
to [decisions.md](../decisions.md). In this page, "database role" always means
the PostgreSQL role.

An installation has three hosts:

- The server serves the API, the sites and the SPA.
- The worker runs background work.
- The admin host, `Axis.Admin`, is a command-line tool. A platform operator
  runs it to create tenants, migrate tenant databases and activate releases.
  See [administration](administration.md).
- The publisher, `axis-admin publisher`, is a
  long-running command of the same host. It runs the publish requests that
  Studio stores, see [publisher](administration.md#publisher). It holds the
  schema role of every active tenant, so run it apart from the server. It has
  no HTTP endpoint.

An installation also needs a PostgreSQL 18 server and an OIDC provider. Axis
does not ship either one for production.

## Images

There are three production images: `axis-server`, `axis-worker` and
`axis-admin`. They hold no sample, no sample extension package, no sample
module, no development realm and no credential. _(planned for M17)_ A fourth
image, `axis-package-host`, runs the
[package runners](extension-packages.md#package-runners) in the
`package-host` container beside the worker. Compose and the Helm chart give
that container no tenant settings, a read-only root file system, CPU and
memory limits, and a network that reaches only the worker.

Build one from the repository root with
`docker build --target axis-server --build-arg AXIS_VERSION=<version> .`.
Use `axis-worker` or `axis-admin` as the target for the other two. `AXIS_VERSION`
defaults to `0.0.0-dev`.

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
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | server | no | Optional. The IP addresses of your reverse proxies. See [several server replicas](#several-server-replicas). |
| `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | server | no | Optional. The CIDR ranges of your reverse proxies. |
| `Admin:ConnectionString` | `Admin__ConnectionString` | admin | yes | A database role with `CREATEDB` and `CREATEROLE`, not a superuser. |
| none | `AXIS_OIDC_CLIENT_SECRET` | admin | yes | The OIDC client secret of the new tenant. Only `tenant create` reads it. |
| `Scenarios:ServerConnectionString` | `Scenarios__ServerConnectionString` | publisher | yes | Optional. A database role with `CREATEDB`, not a superuser, that owns no tenant database. It lets the publisher run `test` requests. Empty by default, and a `test` request then fails with `AXC0217`, which names the setting. See [On premises](#on-premises). |
| `Studio:Tests` | `Studio__Tests` | server | no | Optional. `true` turns on `studioTest`, which shows the test controls in Studio. Off by default. Turn it on together with the publisher's `Scenarios:ServerConnectionString`. |
| `Extensions:Path` | `Extensions__Path` | server, worker, admin | no | Set to `/extensions`. |
| `Modules:Path` | `Modules__Path` | server, worker, admin | no | Set to `/modules`. |
| `Templates:Path` | `Templates__Path` | server | no | Optional. Unset in production, so Studio lists no application templates. The folder holds `<id>/<version>/` template folders, and a folder that does not exist stops the server. See [application templates](modules-and-templates.md#application-templates). |
| `Health:Urls` | `Health__Urls` | worker | no | The image keeps `http://127.0.0.1:5208`. The Helm chart sets it to all interfaces so the kubelet can probe it. |
| `ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | server | no | The image default is 8080. |
| `OTEL_*` | `OTEL_*` | server, worker | maybe | Optional. See [telemetry export](host-settings.md#telemetry-export). A collector token in the headers is secret. |

Read the rules of each setting in
[tenancy settings](host-settings.md#tenancy-settings) and
[admin settings](host-settings.md#admin-settings). They are not repeated here.

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
provider run elsewhere. The example lives in `deploy/compose/`. Its
`compose.yaml` has the server, the worker, the publisher and the admin host. Two
more services sit under profiles: a TLS proxy and a second server replica, see
"TLS proxy and a second replica" below. The server and
the worker run the production images with a read-only root file system. The
admin host runs only when asked, through `docker compose run --rm admin`, so
`docker compose up` does not start it. The file holds no password, client
secret or key. The `publisher` service runs the `axis-admin` image with the
command `publisher`, and `docker compose up` starts it. It gets the
`ConnectionStrings__PlatformSchema` and `Platform__SecretKey` files and not
`Admin__ConnectionString`. It publishes no port. Its `stop_grace_period` is six
minutes, longer than the five-minute lease, so a stop lets the running request
finish.

Set `AXIS_VERSION` to the tag of the three images. `AXIS_IMAGE_PREFIX` adds a
registry prefix. The comment at the top of `compose.yaml` lists every variable.

**Before the first start.** Create these database roles on the PostgreSQL
server, and the platform database. See [database roles](storage.md#database-roles)
for what each may do.

```sql
CREATE ROLE axis_platform_schema LOGIN PASSWORD '<password>';
CREATE ROLE axis_platform LOGIN PASSWORD '<password>';
CREATE ROLE axis_admin LOGIN PASSWORD '<password>' CREATEDB CREATEROLE;
CREATE DATABASE axis_platform OWNER axis_platform_schema;
```

| Database role | Used for |
| --- | --- |
| `axis_platform_schema` | `ConnectionStrings:PlatformSchema`. It owns the `platform` schema. |
| `axis_platform` | `ConnectionStrings:Platform`. The server gives it read access to the `platform` schema and nothing else. |
| `axis_admin` | `Admin:ConnectionString`. It has `CREATEDB` and `CREATEROLE` and is not a superuser. |

**The scenario role.** To let Studio run test scenarios, create one more role.
It has `CREATEDB`, is not a superuser and owns no tenant database. Put its
connection string in the secret file `Scenarios__ServerConnectionString`, and
add `deploy/compose/compose.scenarios.yaml` to the Compose files, as
`docker compose -f compose.yaml -f compose.scenarios.yaml ...` or with
`COMPOSE_FILE=compose.yaml:compose.scenarios.yaml`. That file mounts the secret
in the publisher only and sets `Studio__Tests=true` on both servers, so one
cannot be on without the other. Without it, nothing changes: the publisher gets
no scenario secret and Studio shows no test controls. `compose.yaml` does not
declare the secret itself, because Compose fails to start a service whose secret
file is missing, and that would force every operator to create it. The role owns
no database, so its connection string names the `postgres` database. See
[D55](../decisions/d55-running-test-scenarios-from-studio.md).

```sql
CREATE ROLE axis_scenario LOGIN PASSWORD '<password>' CREATEDB;
```

Point it at a PostgreSQL server that holds no tenant data. If it shares the
server with tenant databases, run
`REVOKE CONNECT ON DATABASE <tenant database> FROM PUBLIC` for each one, because
PostgreSQL lets every role connect by default. The scenario role can then
connect to a tenant database but read nothing in it. The publisher drops each
scenario database after use. After a crash, drop the `axis_scenario_*`
databases that the role owns, see [publisher](administration.md#publisher).

The names are examples. Create the tenant database roles through
`tenant create`, not by hand. Also create the OIDC client for each tenant in
your identity provider.

**The secret files.** Compose reads one file per secret from
`AXIS_SECRETS_DIR`, which defaults to `./secrets` next to `compose.yaml`. Each
file is named after its setting, and its content is the value.

| File | Mounted in |
| --- | --- |
| `ConnectionStrings__Platform` | server, worker |
| `ConnectionStrings__PlatformSchema` | server, admin, publisher |
| `Platform__SecretKey` | server, worker, admin, publisher |
| `Admin__ConnectionString` | admin |
| `Scenarios__ServerConnectionString` *(optional, only with `compose.scenarios.yaml`)* | publisher |

Write each file with `printf '%s'`, so it has no trailing newline. Make the
folder and the files readable by UID 1654, the user the images run as. The
worker never gets `ConnectionStrings__PlatformSchema`. Generate the key once
with `openssl rand -base64 32`. The connection strings name the database server
as the hosts reach it. The admin tool records the host and port of
`Admin__ConnectionString` as the tenant database server, so the server and the
worker must reach it there too.

Put extension packages in `AXIS_EXTENSIONS_DIR` and modules in
`AXIS_MODULES_DIR`, mounted read-only at `/extensions` and `/modules`. Put the
application folders for `activate` in `AXIS_APPS_DIR`, mounted at `/apps` in
the admin container only.

**The order.**

1. Put the secret files in place.
2. Run `docker compose run --rm admin migrate`. On a first install it has
   nothing to migrate and exits with code 0.
3. Start the server and the worker with `docker compose up -d server worker`.
   Wait until `/health/ready` answers `200` on both. The server creates the
   `platform` tables at start, and `tenant create` needs them. Then start the
   publisher with `docker compose up -d publisher`. It exits with code 1 until
   the server has created those tables, so start it only after `/health/ready`.
4. Create a tenant with
   `docker compose run --rm admin tenant create <id> --host <host> --oidc-authority <url> --oidc-client-id <id>`.
   Put the client secret in `AXIS_OIDC_CLIENT_SECRET`. It passes through from
   your shell, so it is in no file and no command line.
5. Activate an application with
   `docker compose run --rm admin activate /apps/<folder>`.

The hosts serve a tenant after their next refresh. `Tenancy__RefreshInterval`
sets it, 30 seconds by default.

- **The OIDC authority must be `https`.** The server runs in `Production`,
  which refuses an identity provider over plain `http`.
- **A private certificate authority.** When the identity provider's certificate
  comes from your own authority, add its certificate to the server's trust
  store. Do not replace the store: pointing `SSL_CERT_FILE` at one file makes
  the server trust only that file.
- **Ports.** The server listens on `127.0.0.1:8080` of the host, the second
  replica on `127.0.0.1:8081`, and the worker's health endpoints on
  `127.0.0.1:5208`. The proxy is the only service published beyond the machine,
  on port 443. Without the proxy, put a reverse proxy of your own on the same
  machine in front of the server. See
  [forwarded headers](#several-server-replicas). The images hold no `curl`, so the
  example has no container health check. Probe `/health/ready` from the host.
- **The purchase request sample** needs a purchasing system binding, which no
  admin command sets, see [open points](#open-points). This guide activates
  the leave request sample.

**TLS proxy and a second replica.** The `proxy` profile starts a pinned Caddy
proxy that ends TLS. The `replicas` profile starts `server-2`, configured like
`server`. Use either profile alone or both.

- **The certificate.** Put `tls.crt` and `tls.key` in `AXIS_TLS_DIR`, which
  defaults to `./tls`. Compose mounts the folder read-only at `/certs`, so no
  compose file holds a certificate or a key. The files must be readable by the
  proxy. The certificate must cover every tenant host.
- **The ports.** The proxy publishes `AXIS_HTTPS_PORT`, 443 by default.
  `server-2` publishes `127.0.0.1:${AXIS_SERVER_2_PORT}`, 8081 by default, for
  health probes.
- **The replicas.** Both servers answer to the network alias `servers`. The
  proxy looks that name up again every five seconds, so a restarted replica
  rejoins on its own. A request to a replica that is down is retried on the
  other one.
- **The forwarded headers.** The Compose network has a fixed subnet,
  `AXIS_NETWORK_SUBNET`, 172.31.250.0/24 by default. The proxy has a fixed
  address in it, `AXIS_PROXY_ADDRESS`, 172.31.250.10 by default. Both servers
  get it as `ForwardedHeaders__KnownProxies__0`, so they trust forwarded
  headers from that address only. The proxy replaces any `X-Forwarded-For`,
  `X-Forwarded-Proto` and `X-Forwarded-Host` a client sends. Behind it, sign-in
  returns to `https://<tenant host>/api/auth/callback` and the session cookie
  is `Secure`.
- **The address range.** Docker hands out addresses without a fixed one only
  from `AXIS_NETWORK_IP_RANGE`, 172.31.250.128/25 by default. A short-lived
  `admin` container then cannot take the proxy's address.
- **The start order.** Start `server` first and wait for `/health/ready`. Then
  run `docker compose --profile replicas up -d server-2` and wait for its
  `/health/ready`. Then run `docker compose --profile proxy up -d proxy`.
- **An existing install.** The network gains fixed address settings, and
  Compose does not change an existing network in place. Run
  `docker compose down` once before the first start with this file. If the
  default subnet overlaps a network of the machine, `docker compose up` fails.
  Set `AXIS_NETWORK_SUBNET`, `AXIS_NETWORK_IP_RANGE` and `AXIS_PROXY_ADDRESS`.
- **Studio.** Studio is off until you turn it on. Both servers mount
  `AXIS_STUDIO_DIR`, `./studio` by default, read-write at `/studio`. No other
  service mounts it. To turn Studio on, set `Studio__Folders__0=/studio/<folder>`
  in the shell, where `<folder>` is an application folder inside
  `AXIS_STUDIO_DIR`. Set `Studio__AuthorRole` and `Studio__PublisherRole` too.
  Without them nobody may author or publish. All three pass through from the
  shell to both servers. More folders need an override file that adds
  `Studio__Folders__1` and so on. Create the folder before the first start,
  because Docker creates a missing one owned by root. It must be writable by
  UID 1654, the user of the image, and it must be the same folder for every
  replica. See [Studio](host-settings.md#studio-settings).

**The smoke test.** `scripts/onprem.test.mjs` installs the example as the five
steps say, and `scripts/e2e.sh` runs it. It builds the three production images
and starts a test PostgreSQL and a Keycloak over HTTPS, with a certificate
authority it makes for the run, and imports a copy of the development realm that
adds the tenants' `https://` redirect addresses. A static check of the rendered configuration shows that
`compose.yaml` alone gives no service the scenario secret or `Studio__Tests`, and that adding
`compose.scenarios.yaml` gives the secret to the publisher only and `Studio__Tests` to both servers only. The
smoke test runs no `test` request against the installed images. An override file in a temporary folder holds
everything the test adds, so `deploy/compose/compose.yaml` stays as an
operator uses it. The test then:

1. Creates the database roles with the SQL above and writes the secret files.
2. Runs `migrate`, starts the server and the worker, waits for `/health/ready`,
   and then starts the publisher.
3. Runs `tenant create` for two tenants, `one` and `two`, with their own hosts.
4. Activates the leave request sample, `samples/apps/leave-requests`, for both.
   The sample needs the `departments` module, so the test mounts it from
   `artifacts/modules` at `/modules` for the test only. The production images
   still hold no sample module.
5. Checks that both hosts are ready, that each tenant's host serves `/leave`
   and holds the active site in its database, and that each tenant's sign-in
   redirects to Keycloak and returns to that tenant's host.
6. Checks that a host no tenant holds answers `404`. Suspends tenant `two`,
   checks that it answers `503` while `one` still serves, then resumes it and
   checks that it serves again.
7. Queues one publish request of the leave request sample in each tenant, over
   `psql`, and checks that the installed publisher runs both to `succeeded`.
   The publisher's log shows `one claim ok` and `two claim ok`, and the
   `publisher` service is still running.
8. Starts the second replica and the TLS proxy, with a proxy certificate from
   the test's authority. Signs an employee in through the proxy over HTTPS with plain
   HTTP requests, posting Keycloak's login form directly. Checks that the
   redirect carries `redirect_uri=https://one.axis.test/api/auth/callback` even
   when the client sends its own `X-Forwarded-Proto` and `X-Forwarded-Host`,
   that every `Axis.Session` cookie is `Secure`, and that `GET /api/me`
   answers `200`. Then restarts `server` and checks that `GET /api/me` with the
   same cookie answers `200` through the proxy and on the ports of `server` and
   `server-2`.
9. Turns Studio on for the author over a copy of the leave request sample in a
   temporary folder that every user may write. Signs the author in through the proxy
   and saves a text through `server`. Checks that the read of the same
   resource through `server-2` answers `200` with the new text and the same
   `ETag`. Then checks that a second save through `server-2` with the old
   version answers `409`.

Other tests check that nothing under `deploy/compose/` holds a secret, that the
rendered configuration takes every connection string and the installation key
only from `secrets:` files, that every published port of both servers is on
`127.0.0.1`, that the proxy mounts its certificate folder read-only, that
both servers trust the proxy's fixed address only, and that both servers mount
the same Studio folder read-write at `/studio` while the worker, the
publisher, the admin host and the proxy do not.

## In the cloud

This installs Axis on Kubernetes with the Helm chart in `deploy/helm/axis/`.
PostgreSQL, the OIDC provider and the source of secrets are external.

The chart has one Deployment for the server and one for the worker, and a
Service for the server. It also has a Deployment for the publisher, with one
replica, that runs the `axis-admin` image with the command `publisher`. The
Deployment uses the `Recreate` strategy, so a rollout never runs two
publishers. It has no Service, no container port and no probe, because the
publisher has no HTTP endpoint. Its termination grace period is 360 seconds,
longer than the five-minute lease. It mounts the same secrets as the migrate
Job. It has no Ingress. The server and worker Deployments each have a startup,
liveness and readiness probe, see
[health checks](../architecture.md#health-checks). The chart sets
`Health__Urls` to `http://0.0.0.0:5208` for the worker, so the kubelet can
reach it. The server alone mounts the Studio volume and gets the Studio and
forwarded headers settings. The pods run as a non-root user with a read-only root
file system.

**Before the first install.** Create the database roles of
[On premises](#on-premises). Then create the Kubernetes Secrets. The chart
creates none and never takes a secret value. Each reference in the values names
a Secret and a key. Several references may name the same Secret.

```sh
kubectl create secret generic axis-platform \
  --from-literal=ConnectionStrings__Platform='<connection string>' \
  --from-literal=ConnectionStrings__PlatformSchema='<connection string>' \
  --from-literal=Platform__SecretKey='<32 bytes in base64>'
```

To let Studio run test scenarios, add
`--from-literal=Scenarios__ServerConnectionString='<connection string>'` to the
same Secret, or to another one. Then set `secrets.scenarioServerConnection` and
`studio.tests`, see the table below.

A secret store works too, if it syncs into a Kubernetes Secret.

**Install.** Write a values file that names the Secrets, then run:

```sh
helm upgrade --install axis deploy/helm/axis -f my-values.yaml
```

```yaml
secrets:
  platformConnection:
    name: axis-platform
  platformSchemaConnection:
    name: axis-platform
  platformSecretKey:
    name: axis-platform
```

Each pod mounts only the secrets it needs as files in `/run/secrets`. The
server gets all three, the worker gets the first and the last, and the migrate
Job and the publisher get the last two. The publisher also gets
`Scenarios__ServerConnectionString` when `secrets.scenarioServerConnection` is
set. Neither has an `Admin:ConnectionString`.
Rendering fails with a message that names the value when a `name` is missing.
The values schema rejects any field in a reference other than `name` and `key`.

| Value | Default | Meaning |
| --- | --- | --- |
| `image.registry` | empty | Registry prefix of the three images. |
| `image.tag` | the chart's `appVersion` | Tag of all three images. |
| `image.pullPolicy` | `IfNotPresent` | Pull policy. |
| `image.pullSecrets` | none | Names of image pull Secrets. |
| `server.repository`, `worker.repository`, `migrate.repository` | `axis-server`, `axis-worker`, `axis-admin` | Repository of each image. `migrate.repository` is also the publisher's image. |
| `server.replicas` | 1 | Server replicas. The key ring is shared, so sessions work on every replica. With Studio on, the replicas also share the `studio.persistence` volume, see [several server replicas](#several-server-replicas). |
| `worker.replicas` | 1 | Worker replicas. |
| `studio.folders` | none | Server only. The Studio folders, as paths relative to `/studio`. Each renders as `Studio__Folders__N` with the value `/studio/<path>`. Setting it turns Studio on. |
| `studio.authorRole`, `studio.publisherRole` | empty | Server only. `Studio__AuthorRole` and `Studio__PublisherRole`. Rendered only when set. Without one, nobody may author or publish. |
| `studio.persistence` | none | A Kubernetes volume source for the Studio folders, mounted read-write at `/studio` on every server replica. It is required when `studio.folders` is set, and rendering fails with a message that names it when it is missing. On its own it does nothing. The chart creates no claim. With several replicas it needs a `ReadWriteMany` storage class. |
| `forwardedHeaders.knownProxies`, `forwardedHeaders.knownNetworks` | none | Server only. IP addresses and CIDR ranges of your proxies, rendered as `ForwardedHeaders__KnownProxies__N` and `ForwardedHeaders__KnownNetworks__N`. Empty trusts no proxy. |
| `server.resources`, `worker.resources`, `migrate.resources` | none | Kubernetes resource requests and limits. |
| `server.service.type`, `server.service.port` | `ClusterIP`, 80 | The Service of the server. Put your own Ingress in front of it. |
| `publisher.resources` | none | Kubernetes resource requests and limits of the publisher. |
| `secrets.scenarioServerConnection` | empty | Publisher only. Secret `name` and `key` of `Scenarios:ServerConnectionString`. The chart mounts it only when it is set. Create the role as in [On premises](#on-premises). |
| `studio.tests` | `false` | Server only. `true` renders `Studio__Tests`, which turns on `studioTest`. Set it together with `secrets.scenarioServerConnection`. |
| `publisher.terminationGracePeriodSeconds` | 360 | Seconds the publisher gets to finish its running request on `SIGTERM`. Keep it longer than the five-minute lease. |
| `migrate.parallel` | 1 | Tenant databases the Job migrates at once. |
| `tenancy.refreshInterval` | empty | `Tenancy__RefreshInterval`. Empty keeps the host default. Use the time span format `hh:mm:ss`, such as `00:00:30`. "30s" stops the hosts at start. |
| `extensions.volume`, `modules.volume` | empty | Any Kubernetes volume source, mounted read-only at `/extensions` and `/modules`. Empty uses the empty folders of the image. |
| `secrets.platformConnection` | key `ConnectionStrings__Platform` | Secret `name` and `key` of `ConnectionStrings:Platform`. |
| `secrets.platformSchemaConnection` | key `ConnectionStrings__PlatformSchema` | Secret `name` and `key` of `ConnectionStrings:PlatformSchema`. |
| `secrets.platformSecretKey` | key `Platform__SecretKey` | Secret `name` and `key` of the installation key. |

**The migrate Job.** A `pre-upgrade` hook Job runs `axis-admin migrate` with the
new admin image before the Deployments roll out. A failed migration fails the
upgrade and the Deployments stay as they are. The Job never retries. Helm keeps
a failed Job for its logs and deletes a successful one. The Job does not run on
the first install, because a first install has no tenants, see
[the first install](#open-points). Helm waits 5 minutes for a hook. An
installation with many tenants needs a longer wait, such as
`helm upgrade --install --timeout 30m`.

**Tenants and releases.** Run `tenant create` and `activate` from a Job or a pod
that uses the admin image, with the settings of the
[settings table](#settings-and-secrets) and the application folder mounted.
The chart has no Job for them.

**Checks.** `scripts/lint.sh` runs `helm lint --strict` and validates the
rendered manifests against the Kubernetes schemas with kubeconform.
`scripts/helm.test.mjs` renders the chart and checks the probes, the migrate
Job, the publisher Deployment, the secret references, the Studio volume and the forwarded headers. `scripts/setup.sh` installs the pinned Helm,
kubeconform and Kubernetes schemas into `~/.cache/axis-tools`. Axis has no test
against a live cluster.

## Several server replicas

The server can run as several replicas behind a reverse proxy that ends TLS.
See [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
The shared key ring, the forwarded headers and the platform settings under both
tenant sources, the Studio write lock, the Helm chart's `studio.persistence`
and the Compose example with two replicas behind a TLS proxy and a shared
Studio folder, with its smoke test, are built.

- **A shared key ring.** The server keeps its data protection keys in the
  platform database, encrypted with `Platform:SecretKey`. See
  [platform database](storage.md#platform-database). Sessions survive a
  restart and work on every replica. Every replica needs the same
  `Platform:SecretKey`. A replica with another one fails to start, and the
  error names the setting. The server sets the data protection application name
  to `Axis`, so replicas installed at different paths share the keys. Changing
  `Platform:SecretKey` on an installation that holds keys means deleting the
  rows of `platform.data_protection_keys` by hand, which signs everyone out.
  The first start after this change also signs everyone out once.
- **Forwarded headers.** Behind a proxy that ends TLS, the server sees plain
  HTTP. Name your proxies so the server can read the real scheme and host:

  | Setting | Environment variable | Meaning |
  | --- | --- | --- |
  | `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | IP addresses of the proxies. Use `__1`, `__2` and so on for more. |
  | `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | CIDR ranges of the proxies, such as `10.0.0.0/8`. |

- **What the proxy must send.** It must set `X-Forwarded-For`,
  `X-Forwarded-Proto` and `X-Forwarded-Host`. It must replace any value a
  client sent. The server reads these headers only from a named address, one
  hop, and never reads `X-Forwarded-Prefix`. With both settings empty, it
  reads none. Loopback is not trusted by default. The rules are in
  [forwarded headers](host-settings.md#forwarded-headers).
- **Studio folders.** Put them on a shared volume. Mount it at the same path on
  every replica, because the write lock is keyed by the folder path. The lock
  is built. It lives in the platform database. In
  Kubernetes that needs a `ReadWriteMany` storage class, set with
  `studio.persistence`. In Compose, set `AXIS_STUDIO_DIR`. Both servers mount
  it. The volume must be writable by the non-root user of the image, UID 1654,
  and the chart sets no `fsGroup`. See [Studio](host-settings.md#studio-settings).
- **On premises.** The Compose example runs two server replicas behind a TLS
  proxy. See "TLS proxy and a second replica" in [on premises](#on-premises).
- **Platform settings under both tenant sources.** The server needs
  `ConnectionStrings:PlatformSchema` and `Platform:SecretKey` also under
  `Tenancy:Source=configuration`, because it creates the platform tables there
  too. A missing one stops the start. See [tenancy settings](host-settings.md#tenancy-settings).

## Upgrade

An upgrade has a fixed order. A host serves only tenants on its own schema
version, as [D43](../decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md)
says. So migrate first and roll out the hosts second.

1. Back up the platform database and every tenant database. For example, run
   `pg_dump` from the admin image. Back up each tenant with
   [`tenant backup`](administration.md#tenant-backup).
2. Run `migrate` with the new admin image.
3. Roll out the new server, the new worker and the new publisher.

Between steps 2 and 3, migrated tenants answer `503`. A tenant whose migration
failed keeps its old version. See [migrate](administration.md#migrate) for the
window and for failed tenants.

**No downgrade.** Migrations are not reversed. To roll back, restore the backup
from step 1 and run the old images.

## Open points

These are not decided yet.

- **The first install.** The server applies the platform migrations at start,
  see [platform database](storage.md#platform-database). `tenant create` does
  not create the `platform` tables and exits with code 2 without them, so start
  the server once first. `migrate` before the `platform` schema exists does
  nothing, because there is no tenant to migrate. It writes a note to standard
  error and exits with code 0, so running it first, as the order above does, is
  safe on a first install.
- **Deployment bindings.** No admin command sets a tenant's deployment
  bindings, see [administration](administration.md#not-built-yet). So an
  installation cannot set the purchasing system binding of the purchase request
  sample through the admin tool alone.
- **Rotating the installation key.** Axis has no rotation, see
  [D42](../decisions/d42-tenant-directory-in-the-platform-database.md).
  After a leaked `Platform:SecretKey`, every stored secret must be set again by
  hand. Changing `Platform:SecretKey` also means deleting the rows of
  `platform.data_protection_keys`, which signs everyone out. See
  [several server replicas](#several-server-replicas).
