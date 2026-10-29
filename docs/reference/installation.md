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

An installation also needs a PostgreSQL 18 server. Axis does not ship one for
production. The Compose example runs Axis's own identity provider, see
[Axis's identity provider](identity-provider.md#installation), so it needs no
OIDC provider of its own. The Helm chart runs it too.

Every tenant database needs the `pg_trgm` extension, for
[search](../decisions/d80-search-more-aggregates-and-query-timeouts.md).
`tenant create` and `migrate` create it in the `public` schema as the tenant's
schema role. It is a trusted extension, so they need no superuser. The server
must ship the contrib modules, as the official `postgres` images do. A managed
service may need `pg_trgm` on its list of allowed extensions.

## Images

There are three production images: `axis-server`, `axis-worker` and
`axis-admin`. They hold no sample, no sample extension package, no sample
module, no development realm, no credential and no source control metadata
such as `.git`. _(planned for M21)_ A fourth
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
- **Signed images** _(planned for M28)_. A release workflow publishes the images
  to the GitHub container registry, each signed with cosign and with a CycloneDX
  SBOM. Check the signature before an install or an upgrade, see
  [D107](../decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md).

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
| `Platform:PreviousSecretKey` | `Platform__PreviousSecretKey` | server, worker, admin | no | Secret. Set it only during a key rotation, to the old installation key. See [installation key](tenant-setting-commands.md#installation-key). The Helm chart and the Compose example set no value. Add the variable or a secret file yourself. |
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | server | no | Optional. The IP addresses of your reverse proxies. See [several server replicas](#several-server-replicas). |
| `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | server | no | Optional. The CIDR ranges of your reverse proxies. |
| `Admin:ConnectionString` | `Admin__ConnectionString` | admin | yes | A database role with `CREATEDB` and `CREATEROLE`, not a superuser. |
| `IdentityProvider:Url` | `IdentityProvider__Url` | admin | no | The base address of Axis's identity provider. `tenant create` of a main tenant creates the tenant's realm there, and so does `tenant restore` of a tenant whose realm is missing. It must be `https`. |
| `IdentityProvider:AdminClientId` | `IdentityProvider__AdminClientId` | admin | no | The client of the identity provider's `master` realm that `tenant create` signs in as. |
| `IdentityProvider:AdminClientSecret` | `IdentityProvider__AdminClientSecret` | admin | yes | The secret of that client. Only `tenant create` of a main tenant and `tenant restore` read it. |
| none | `AXIS_OIDC_CLIENT_SECRET` | admin | yes | The OIDC client secret of a restored tenant that keeps the archive's sign-in, which a restore under a new realm does not need. `tenant restore` reads it. `tenant create` does not. |
| `Scenarios:ServerConnectionString` | `Scenarios__ServerConnectionString` | publisher | yes | Optional. A database role with `CREATEDB`, not a superuser, that owns no tenant database. It lets the publisher run `test` requests. Empty by default, and a `test` request then fails with `AXC0217`, which names the setting. See [On premises](#on-premises). |
| `Studio:Tests` | `Studio__Tests` | server | no | Optional. `true` turns on `studioTest`, which shows the test controls in Studio. Off by default. Turn it on together with the publisher's `Scenarios:ServerConnectionString`. |
| `Extensions:Path` | `Extensions__Path` | server, worker, admin | no | Set to `/extensions`. |
| `Modules:Path` | `Modules__Path` | server, worker, admin | no | Set to `/modules`. |
| `Templates:Path` | `Templates__Path` | server | no | Optional. Unset in production, so Studio lists no application templates. The folder holds `<id>/<version>/` template folders, and a folder that does not exist stops the server. See [application templates](modules-and-templates.md#application-templates). |
| `Health:Urls` | `Health__Urls` | worker | no | The image keeps `http://127.0.0.1:5208`. The Helm chart sets it to all interfaces so the kubelet can probe it. |
| `ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | server | no | The image default is 8080, plain HTTP. The Compose example and the Helm chart replace it with the Kestrel endpoint below, so nothing listens on 8080. |
| `Kestrel:Endpoints:Https:Url` | `Kestrel__Endpoints__Https__Url` | server | no | The Compose example and the Helm chart set `https://+:8443`. A Kestrel endpoint replaces `ASPNETCORE_HTTP_PORTS`, so the server serves HTTPS only. |
| `Kestrel:Endpoints:Https:Certificate:Path`, `Kestrel:Endpoints:Https:Certificate:KeyPath` | `Kestrel__Endpoints__Https__Certificate__Path`, `Kestrel__Endpoints__Https__Certificate__KeyPath` | server | no | The Compose example and the Helm chart set `/server-tls/tls.crt` and `/server-tls/tls.key`. The key is a private key, so the folder is a mounted volume and never a setting. See [TLS](#tls). |
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

## Outbound connections

Every connection that the server and the worker open, with the setting that
names its address. The TLS rule of each is under [TLS](#tls).

| Host | Destination | Named by | TLS |
| --- | --- | --- | --- |
| server, worker | The platform database | `ConnectionStrings:Platform`. The server also uses `ConnectionStrings:PlatformSchema`, only at start | [PostgreSQL](#tls) |
| server, worker | Each tenant's database | The [tenant directory](../architecture.md#tenant-directory), with the platform connection's TLS settings | [PostgreSQL](#tls) |
| server | Each tenant's identity provider: discovery, signing keys and the token endpoint, with DPoP and pushed authorization requests under `fapi2` | The tenant's OIDC `Authority` in the tenant directory | [Identity provider](#tls) |
| server, worker | The telemetry collector | `OTEL_EXPORTER_OTLP_*`, see [telemetry export](host-settings.md#telemetry-export) | [Telemetry export](#tls) |
| worker | A connector's service | The `url` binding that the connector names as its `baseUrl`, see [deployment bindings](integration-resources.md#deployment-bindings) | [URL bindings](#tls) |
| worker | A package operation's service | A `url` binding that the operation declares in its `capabilities` | [URL bindings](#tls) |
| worker | The mail server | `Email:Smtp:Host`, or the tenant's email settings, see [notifications](notifications.md) | [Mail](#tls) |

The publisher connects to PostgreSQL only, with `ConnectionStrings:PlatformSchema` and
`Scenarios:ServerConnectionString`. It calls no HTTP address. The admin host connects to PostgreSQL with
`Admin:ConnectionString` and `ConnectionStrings:PlatformSchema`. Only `tenant create` of a main tenant also calls an
HTTP address: the identity provider's admin API, at `IdentityProvider:Url`, over HTTPS outside `Development` and
`Testing`. The address is an operator setting, so it is on the allowlist below.

- **The allowlist.** Every address comes from an operator setting or from a
  deployment binding. The operator sets both, and a release holds only binding
  names, never an address. So the settings and the bindings are the allowlist
  of the server and the worker.
- **No user names an address.** No user, author or API caller can give an
  address in a request, a form, a release or a record. A connector or a package
  operation can reach only the host of the binding it names.
- **No redirects.** A connector call never follows one. A `3xx` fails the call,
  see [call outcomes](process-execution.md#call-outcomes).
- **No network rule in the examples.** The Compose example and the Helm chart
  set no egress rule. An operator may add one that allows only the destinations
  of this table.
- **Extension packages are trusted code.** A package runs in the worker's
  process, so installing one means trusting its code. The `url` binding limits
  the client that Axis hands to it, not the package's own code.
- **Planned.** An OAuth2 `tokenUrl` and an outbound webhook _(planned for M19)_
  will name a `url` binding too, and join this table.

## On premises

This installs Axis on one machine with Docker Compose. PostgreSQL runs
elsewhere. Compose runs [Axis's identity provider](identity-provider.md#installation)
beside the server, with its own database on that PostgreSQL server. The example lives in `deploy/compose/`. Its
`compose.yaml` has the server, the worker, the publisher, the identity provider, the TLS proxy and the admin host. One
more service, a second server replica, sits under a profile, see
"TLS proxy and a second replica" below. The server and
the worker run the production images with a read-only root file system. The identity provider cannot, because
Keycloak rebuilds itself at start and writes into its image. The
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
CREATE ROLE axis_identity LOGIN PASSWORD '<password>';
CREATE DATABASE axis_identity OWNER axis_identity;
```

| Database role | Used for |
| --- | --- |
| `axis_platform_schema` | `ConnectionStrings:PlatformSchema`. It owns the `platform` schema. |
| `axis_platform` | `ConnectionStrings:Platform`. The server gives it read access to the `platform` schema and nothing else. |
| `axis_admin` | `Admin:ConnectionString`. It has `CREATEDB` and `CREATEROLE` and is not a superuser. |
| `axis_identity` | The identity provider's own database, `axis_identity`. It owns that database and nothing else. |

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
`tenant create`, not by hand. `tenant create` also creates each tenant's realm in
[Axis's identity provider](identity-provider.md#keeping-the-realm-in-step). The identity provider starts with the admin
client `axis-admin` in its `master` realm, and its secret is the `IdentityProvider__AdminClientSecret` file, which the
admin host reads too. You write that secret once.

**The secret files.** Compose reads one file per secret from
`AXIS_SECRETS_DIR`, which defaults to `./secrets` next to `compose.yaml`. Each
file is named after its setting, and its content is the value.

| File | Mounted in |
| --- | --- |
| `ConnectionStrings__Platform` | server, worker |
| `ConnectionStrings__PlatformSchema` | server, admin, publisher |
| `Platform__SecretKey` | server, worker, admin, publisher |
| `Admin__ConnectionString` | admin |
| `IdentityProvider__AdminClientSecret` | admin, identity |
| `KC_DB_URL` | identity |
| `KC_DB_USERNAME` | identity |
| `KC_DB_PASSWORD` | identity |
| `Scenarios__ServerConnectionString` *(optional, only with `compose.scenarios.yaml`)* | publisher |

Write each file with `printf '%s'`, so it has no trailing newline. Make the
folder and the files readable by UID 1654, the user the images run as, and the
files the identity provider reads readable by UID 1000, the user of the Keycloak image. The
worker never gets `ConnectionStrings__PlatformSchema`. The three `KC_DB_*` files hold Keycloak's
database settings: a JDBC URL such as
`jdbc:postgresql://db.example.com:5432/axis_identity?sslmode=verify-full&sslrootcert=/db-ca/root.crt`, the user name
and the password. Without `sslrootcert`, the PostgreSQL JDBC driver reads `~/.postgresql/root.crt`, which does not
exist, and Keycloak does not start. For a PostgreSQL certificate from a public authority, use
`sslfactory=org.postgresql.ssl.DefaultJavaSSLFactory` instead of `sslrootcert`.
A small entrypoint script turns each file into the variable of the same name, because Keycloak has no `_FILE`
variables. Generate the key once
with `openssl rand -base64 32`. Each connection string sets `SSL Mode=VerifyFull`,
and `Root Certificate` when your PostgreSQL certificate comes from a private
authority, because the hosts refuse anything weaker, see [TLS](#tls). The
connection strings name the database server as the hosts reach it, which is a
name in the PostgreSQL certificate. The admin tool records the host and port of
`Admin__ConnectionString` as the tenant database server, so the server and the
worker must reach it there too.

**The identity provider.** It needs these before the first start.

- **`AXIS_IDENTITY_HOST`.** The one public host name of the identity provider, such as `id.example.com`. It is
  required. The proxy sends requests for this host to the identity provider and every other host to the servers.
  The containers reach the same name through a network alias of the proxy, so a browser and the server use one
  address. Each realm's authority is `https://<AXIS_IDENTITY_HOST>/realms/<tenant>`.
- **The certificate.** Put `tls.crt` and `tls.key` in `AXIS_IDENTITY_TLS_DIR`, which defaults to `./identity-tls`.
  The certificate is issued for the name `identity` by the authority of `server-ca.crt`, the authority that the
  proxy trusts. Compose mounts the folder read-only at `/identity-tls` on the identity provider and on no other
  service. Both files must be readable by UID 1000. Keycloak listens on HTTPS only, on port 8443, and publishes no
  port. The proxy certificate must also cover `AXIS_IDENTITY_HOST`.
- **The database authority.** Put `root.crt`, the authority certificate of your PostgreSQL server, in
  `AXIS_IDENTITY_DB_CA_DIR`, which defaults to `./db-ca`. Compose mounts the folder read-only at `/db-ca` on the
  identity provider and on no other service, and `sslrootcert=/db-ca/root.crt` in `KC_DB_URL` names the file. It
  must be readable by UID 1000. It is a public certificate, not a secret.
- **The theme.** Build the Axis theme from the release's source with
  `npm run build:keycloak-theme --prefix web`. Set `AXIS_IDENTITY_THEME_DIR` to the output folder,
  `artifacts/keycloak-theme`. Compose mounts it read-only at `/opt/keycloak/themes`, so sign-in pages show the Axis
  theme. No release carries the built theme yet.
- **The breached-password list.** Compose mounts `deploy/keycloak/password-blacklists` read-only at
  `/opt/keycloak/data/password-blacklists`. Realms made by `tenant create` check new passwords against it. Set
  `AXIS_PASSWORD_BLACKLISTS_DIR` to use another folder.
- **The admin console.** Keycloak's admin console and admin API answer on the public identity host, and Keycloak
  authentication protects them. Limit them by network in front of the proxy if you need to.

Put extension packages in `AXIS_EXTENSIONS_DIR` and modules in
`AXIS_MODULES_DIR`, mounted read-only at `/extensions` and `/modules`. Put the
application folders for `activate` in `AXIS_APPS_DIR`, mounted at `/apps` in
the admin container only.

**The order.**

1. Put the secret files in place.
2. Run `docker compose run --rm admin migrate`. On a first install it has
   nothing to migrate and exits with code 0.
3. Start the identity provider and the proxy with `docker compose up -d identity proxy`. Wait until
   `https://<AXIS_IDENTITY_HOST>/realms/master/.well-known/openid-configuration` answers `200`. Then
   start the server and the worker with `docker compose up -d server worker`.
   Wait until `/health/ready` answers `200` on both. The server creates the
   `platform` tables at start, and `tenant create` needs them. Then start the
   publisher with `docker compose up -d publisher`. It exits with code 1 until
   the server has created those tables, so start it only after `/health/ready`.
4. Create a tenant with
   `docker compose run --rm admin tenant create <id> --host <host>`.
   The command creates the tenant's realm in the identity provider. The compose file points the admin host at
   `https://<AXIS_IDENTITY_HOST>/` and sets the admin client id `axis-admin`, so you set no shell variable. The
   admin client secret is the `IdentityProvider__AdminClientSecret` file, so it is in no
   command line. Every `admin` run needs that file, because Compose refuses to start a service whose secret file is
   missing. A new realm has no accounts and no redirect addresses yet, so until a later part of M17 adds them, add
   them to the realm in the identity provider yourself.
5. Activate an application with
   `docker compose run --rm admin activate /apps/<folder>`.

The hosts serve a tenant after their next refresh. `Tenancy__RefreshInterval`
sets it, 30 seconds by default.

- **The OIDC authority must be `https`.** The server runs in `Production`,
  which refuses an identity provider over plain `http`. So `tenant create` refuses a plain `http`
  `IdentityProvider__Url` there too.
- **A private certificate authority.** When the proxy's certificate
  comes from your own authority, add its certificate to the trust store of the server, of the admin host and of the publisher,
  because they reach the identity host through the proxy. Do not replace the store: pointing `SSL_CERT_FILE` at one file makes
  the server trust only that file.
- **Ports.** The server listens on `127.0.0.1:8080` of the host, the second
  replica on `127.0.0.1:8081`, and the worker's health endpoints on
  `127.0.0.1:5208`. Both servers serve HTTPS only, on container port 8443, and
  the worker's health endpoint stays plain HTTP because it carries no application
  traffic. The proxy is the only service published beyond the machine,
  on port 443. To use a reverse proxy of your own instead, put it on the same
  machine in front of the server, and make it connect over HTTPS and check the
  server certificate. It must also reach the identity provider. See
  [forwarded headers](#several-server-replicas). The images hold no `curl`, so the
  example has no container health check. Probe `/health/ready` from the host, for
  example `curl --cacert <ca> --resolve servers:8080:127.0.0.1 https://servers:8080/health/ready`.
- **The server certificate.** Put `tls.crt` and `tls.key` in
  `AXIS_SERVER_TLS_DIR`, which defaults to `./server-tls`. The certificate is
  issued for the name `servers`, the network alias of both replicas. Compose
  mounts the folder read-only at `/server-tls` on both servers and on no other
  service, so only the servers hold the private key. Both files must be readable
  by UID 1654, the user of the image. A server does not start without them.
- **The purchase request sample** needs a purchasing system binding, which
  `tenant binding set` sets, see
  [deployment bindings](tenant-setting-commands.md#deployment-bindings). This
  guide activates the leave request sample.

**TLS proxy and a second replica.** The `proxy` service, which `docker compose up`
starts, is a pinned Caddy proxy that ends TLS. It accepts only the versions and ciphers that [TLS](#tls)
states. It is the only way to reach the identity provider. It routes by host: requests for `AXIS_IDENTITY_HOST` go to
`identity` on port 8443 over TLS, and every other host goes to the servers. The `replicas` profile starts `server-2`, configured like
`server`.

- **The certificate.** Put `tls.crt` and `tls.key` in `AXIS_TLS_DIR`, which
  defaults to `./tls`. Compose mounts the folder read-only at `/certs`, so no
  compose file holds a certificate or a key. The files must be readable by the
  proxy. The certificate must cover every tenant host and `AXIS_IDENTITY_HOST`, see [TLS](#tls). Put
  `server-ca.crt` in the same folder: the certificate of the authority that
  issued the server and identity certificates. The proxy trusts that one certificate for the
  servers and the identity provider and no other.
- **The ports.** The proxy publishes `AXIS_HTTPS_PORT`, 443 by default.
  `server-2` publishes `127.0.0.1:${AXIS_SERVER_2_PORT}`, 8081 by default, for
  health probes.
- **The replicas.** Both servers answer to the network alias `servers`. The
  proxy looks that name up again every five seconds, so a restarted replica
  rejoins on its own. A request to a replica that is down is retried on the
  other one. The proxy connects to port 8443 over TLS and checks that the
  certificate is for the name `servers` and comes from the authority in
  `server-ca.crt`.
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
  `/health/ready`. The proxy is already running.
- **Upgrading an existing install.** `docker compose up` now starts the proxy and the identity provider, because the
  `proxy` profile is gone. An install that started the proxy with `--profile proxy` keeps working. Before the
  upgrade, set `AXIS_IDENTITY_HOST`, create the `axis_identity` role and database, write the three `KC_DB_*` files,
  and put the identity certificate in place. Then point each tenant's sign-in at its realm there. The admin host no longer reads `IdentityProvider__Url` and
  `IdentityProvider__AdminClientId` from the shell.
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
steps say, and `scripts/smoke.sh` runs it. It builds the three production images
and starts a test PostgreSQL over TLS and the example's own identity provider, with a certificate
authority it makes for the run, and imports a copy of the development realm that
adds the tenants' `https://` redirect addresses. The identity provider runs from `compose.yaml` with the built Axis theme and
`deploy/keycloak/password-blacklists`, in its own database, and the proxy serves it on the host `id.axis.test`. The admin host, the server and the publisher trust the authority too. PostgreSQL serves a certificate
of the same authority for the name `postgres`, and every connection string sets
`SSL Mode=VerifyFull` and that authority as `Root Certificate`. A static check of the rendered configuration shows that
`compose.yaml` alone gives no service the scenario secret or `Studio__Tests`, and that adding
`compose.scenarios.yaml` gives the secret to the publisher only and `Studio__Tests` to both servers only. The
smoke test runs no `test` request against the installed images. An override file in a temporary folder holds
everything the test adds, so `deploy/compose/compose.yaml` stays as an
operator uses it. The test then:

1. Creates the database roles with the SQL above and writes the secret files,
   each connection string with `SSL Mode=VerifyFull`.
2. Starts the identity provider and the proxy, and waits until the development realm answers through the proxy on the
   identity host. Checks that its sign-in page contains the Axis theme's `axis-sign-in-context`. Runs `migrate`, starts
   the server and the worker, waits for `/health/ready`, and then starts the publisher.
3. Runs `tenant create` for two tenants, `one` and `two`, with their own hosts and no `--oidc-*` option. Each
   command creates the tenant's realm in the identity provider.
4. Activates the leave request sample, `samples/apps/leave-requests`, for both.
   The sample needs the `departments` module, so the test mounts it from
   `artifacts/modules` at `/modules` for the test only. The production images
   still hold no sample module.
5. Checks that both hosts are ready, that each tenant's host serves `/leave`
   and holds the active site in its database, and that each tenant's sign-in
   redirects to the authorization endpoint of that tenant's realm on the identity host and returns to that tenant's host. Checks in
   `pg_stat_ssl` that at least one Axis connection uses TLS and that none
   does not.
6. Checks that a host no tenant holds answers `404`. Suspends tenant `two`,
   checks that it answers `503` while `one` still serves, then resumes it and
   checks that it serves again.
7. Queues one publish request of the leave request sample in each tenant, over
   `psql`, and checks that the installed publisher runs both to `succeeded`.
   The publisher's log shows `one claim ok` and `two claim ok`, and the
   `publisher` service is still running.
8. Adds a redirect address, a `roles` mapper, the four client roles and the accounts `requester` and `author` to
   the realm of tenant `one` through the identity provider's admin API, because a new realm has none. Starts the second replica, with a proxy certificate and a
   server certificate from the test's authority, and that authority's certificate
   as `server-ca.crt`. Checks that a plain HTTP request to a server port gets no
   HTTP response, and that the servers answer on their own ports over HTTPS for the
   name `servers`. Signs an employee in through the proxy over HTTPS with plain
   HTTP requests, in the realm of tenant `one`, posting the Axis theme's login form to its `loginAction`. Checks that the
   redirect goes to `/realms/one/protocol/openid-connect/auth` and carries `redirect_uri=https://one.axis.test/api/auth/callback` even
   when the client sends its own `X-Forwarded-Proto` and `X-Forwarded-Host`,
   that every `__Host-Axis.Session` cookie is `Secure` with `Path=/` and no domain, and that `GET /api/me`
   answers `200`. Then restarts `server` and checks that `GET /api/me` with the
   same cookie answers `200` through the proxy and on the HTTPS ports of `server` and
   `server-2`. Also checks that a default handshake with the proxy negotiates
   TLS 1.3, that a TLS 1.2 handshake with an ECDHE GCM cipher succeeds and that
   a TLS 1.2 handshake offering only CBC ciphers is refused.
9. Turns Studio on for the author over a copy of the leave request sample in a
   temporary folder that every user may write. Signs the author in through the proxy
   and saves a text through `server`. Checks that the read of the same
   resource through `server-2` answers `200` with the new text and the same
   `ETag`. Then checks that a second save through `server-2` with the old
   version answers `409`.

Other tests check that nothing under `deploy/compose/` holds a secret, that the
rendered configuration takes every connection string, the installation key, the identity provider's database settings
and its admin client secret only from `secrets:` files and holds no password or secret in any
environment, that neither the proxy nor the identity provider has a profile, that the identity provider runs the
Keycloak image that `KeycloakFixture.Image` pins, that only it mounts the identity certificate folder, read-only, that every published port of both servers is on
`127.0.0.1`, that the proxy mounts its certificate folder read-only, that
both servers trust the proxy's fixed address only, and that both servers mount
the same Studio folder read-write at `/studio` while the worker, the
publisher, the admin host and the proxy do not. They also check that the
Caddyfile sets `protocols tls1.2 tls1.3`, lists only ECDHE AEAD ciphers and has
`:443` as its only site address, and that the proxy publishes only container
port 443. They check that both servers set the three Kestrel HTTPS settings and
publish only container port 8443, that both mount the server certificate folder
read-only and that no other service does, and that the Caddyfile forwards to
port 8443 over TLS with the server name `servers` and sends the identity host to `identity:8443` with the server
name `identity`, trusts only `/certs/server-ca.crt` in both places and never skips the verification.

## In the cloud

This installs Axis on Kubernetes with the Helm chart in `deploy/helm/axis/`.
PostgreSQL and the source of secrets are external. The chart runs
[Axis's identity provider](identity-provider.md#installation), so it needs no
OIDC provider of its own.

The chart has one Deployment for the server and one for the worker, and a
Service for the server. It also has a Deployment for the publisher, with one
replica, that runs the `axis-admin` image with the command `publisher`. The
Deployment uses the `Recreate` strategy, so a rollout never runs two
publishers. It has no Service, no container port and no probe, because the
publisher has no HTTP endpoint. Its termination grace period is 360 seconds,
longer than the five-minute lease. It mounts the same secrets as the migrate
Job. It has no Ingress. The server and worker Deployments each have a startup,
liveness and readiness probe, see
[health checks](../architecture.md#health-checks). The server's probes use
HTTPS, and the worker's use plain HTTP. The chart sets
`Health__Urls` to `http://0.0.0.0:5208` for the worker, so the kubelet can
reach it. The server alone mounts the Studio volume and gets the Studio
settings. The server and the identity provider get the forwarded headers
settings. The pods run as a non-root user with a read-only root file system,
except the identity provider, see below.

**The identity provider.** The chart also has a Deployment and a Service named
`<release>-identity`. There is no switch to turn them off, as `docker compose up`
always starts the identity provider too. The Deployment runs one replica of
Keycloak at `identity.image`, which defaults to the version that development, the
tests and the Compose example pin. Axis does not cluster Keycloak. It runs
`start` on HTTPS only, with the certificate of `secrets.identityCertificate`,
and `identity.hostname` as its public host name. Its database settings and the
`axis-admin` client secret are secret files, which a small script in the
container command turns into Keycloak's variables, because Keycloak has no
`_FILE` variables. The Axis theme and the breached-password list come from
volumes that you name, see the table. The pod has a startup, liveness and
readiness probe over HTTPS on Keycloak's management port, 9000. Keycloak
rebuilds itself at start and writes into its image, so this one container has
no read-only root file system. The Service has port 443, named `https`, and
forwards to port 8443. It does not expose the management port. The chart renders no Ingress, so your own
Ingress sends the identity host to this Service, as it sends the server's host
to the server Service. Keycloak reads the forwarded headers only from
`forwardedHeaders.knownProxies` and `knownNetworks`. It sets no proxy headers
when both are empty. A cluster that runs `tenant create` still needs its own
Job with the `IdentityProvider__*` settings, which the chart does not render.

**Before the first install.** Create the database roles of
[On premises](#on-premises). Then create the Kubernetes Secrets. The chart
creates none and never takes a secret value. Each reference in the values names
a Secret and a key. Several references may name the same Secret.

```sh
kubectl create secret generic axis-platform \
  --from-literal=ConnectionStrings__Platform='<connection string>' \
  --from-literal=ConnectionStrings__PlatformSchema='<connection string>' \
  --from-literal=Platform__SecretKey='<32 bytes in base64>'
kubectl create secret generic axis-identity \
  --from-literal=KC_DB_URL='<JDBC URL of the identity provider database>' \
  --from-literal=KC_DB_USERNAME='<database role>' \
  --from-literal=KC_DB_PASSWORD='<password>' \
  --from-literal=IdentityProvider__AdminClientSecret='<client secret of axis-admin>'
```

The `axis-identity` Secret holds the four settings of the identity provider, as
in [Axis's identity provider](identity-provider.md#installation). The JDBC URL
may name the PostgreSQL authority certificate with `sslrootcert=/db-ca/root.crt`
when you set `identity.databaseAuthority.volume`.

To let Studio run test scenarios, add
`--from-literal=Scenarios__ServerConnectionString='<connection string>'` to the
same Secret, or to another one. Then set `secrets.scenarioServerConnection` and
`studio.tests`, see the table below.

A secret store works too, if it syncs into a Kubernetes Secret.

The server serves HTTPS only, so it needs a certificate for the DNS name of its
Service, such as `axis-server.<namespace>.svc`. Create it as a Kubernetes TLS
Secret, from a certificate that your Ingress trusts:

```sh
kubectl create secret tls axis-server-tls --cert=tls.crt --key=tls.key
```

The identity provider also serves HTTPS only. Create a second TLS Secret for the
DNS name of the identity Service, such as `axis-identity.<namespace>.svc`:

```sh
kubectl create secret tls axis-identity-tls --cert=tls.crt --key=tls.key
```

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
  serverCertificate:
    name: axis-server-tls
  identityDatabaseUrl:
    name: axis-identity
  identityDatabaseUsername:
    name: axis-identity
  identityDatabasePassword:
    name: axis-identity
  identityAdminClientSecret:
    name: axis-identity
  identityCertificate:
    name: axis-identity-tls
identity:
  hostname: id.example.com
  theme:
    volume:
      persistentVolumeClaim:
        claimName: axis-keycloak-theme
  passwordBlacklists:
    volume:
      persistentVolumeClaim:
        claimName: axis-password-blacklists
```

Each pod mounts only the secrets it needs as files in `/run/secrets`. The
server gets the first three, the worker gets the first and the third, and the migrate
Job and the publisher get the second and the third. The publisher also gets
`Scenarios__ServerConnectionString` when `secrets.scenarioServerConnection` is
set. Neither has an `Admin:ConnectionString`. The identity provider gets the four
`axis-identity` files and no other secret, and no other pod gets them.
The server also mounts the `serverCertificate` Secret read-only at `/server-tls`, and no
other pod does. The identity provider mounts the `identityCertificate` Secret
read-only at `/identity-tls` in the same way.
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
| `forwardedHeaders.knownProxies`, `forwardedHeaders.knownNetworks` | none | Server and identity provider. IP addresses and CIDR ranges of your proxies, rendered on the server as `ForwardedHeaders__KnownProxies__N` and `ForwardedHeaders__KnownNetworks__N`, and on the identity provider as one `KC_PROXY_TRUSTED_ADDRESSES` list with `KC_PROXY_HEADERS=xforwarded`. Empty trusts no proxy, and the identity provider sets no proxy headers. |
| `server.resources`, `worker.resources`, `migrate.resources` | none | Kubernetes resource requests and limits. |
| `server.service.type`, `server.service.port` | `ClusterIP`, 443 | The Service of the server. Its port is named `https` and forwards to the server's HTTPS port, 8443. Put your own Ingress in front of it, see [TLS](#tls). |
| `secrets.serverCertificate` | none, required | Secret `name` of a Kubernetes TLS Secret with `tls.crt` and `tls.key`, for the DNS name of the server Service. It has no `key`. The server serves HTTPS only, and rendering fails with a message that names `secrets.serverCertificate.name` when it is missing. |
| `identity.image` | `quay.io/keycloak/keycloak:26.4` | The whole image reference of the identity provider. `image.registry` and `image.tag` do not apply to it. |
| `identity.hostname` | none, required | The public host name of the identity provider, without a scheme. It becomes `KC_HOSTNAME=https://<hostname>`. Rendering fails with a message that names it when it is missing. |
| `identity.resources` | none | Kubernetes resource requests and limits of the identity provider. |
| `identity.service.type`, `identity.service.port` | `ClusterIP`, 443 | The Service of the identity provider. Its port is named `https` and forwards to Keycloak's HTTPS port, 8443. Put your own Ingress in front of it. |
| `identity.theme.volume` | none, required | A Kubernetes volume source with the built Axis theme, the output of `npm run build:keycloak-theme --prefix web`, mounted read-only at `/opt/keycloak/themes`. No release ships the built theme. Rendering fails with a message that names the value when it is missing. |
| `identity.passwordBlacklists.volume` | none, required | A Kubernetes volume source with the breached-password list of `deploy/keycloak/password-blacklists`, mounted read-only at `/opt/keycloak/data/password-blacklists`. Without it `tenant create` fails at its realm step. Rendering fails with a message that names the value when it is missing. |
| `identity.databaseAuthority.volume` | empty | Optional. A volume source with `root.crt`, the authority certificate of the PostgreSQL server, mounted read-only at `/db-ca`. A database with a public certificate can use `sslfactory=org.postgresql.ssl.DefaultJavaSSLFactory` in the JDBC URL instead. |
| `secrets.identityDatabaseUrl`, `secrets.identityDatabaseUsername`, `secrets.identityDatabasePassword` | keys `KC_DB_URL`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` | Identity provider only. Secret `name` and `key` of Keycloak's database settings. All three names are required. |
| `secrets.identityAdminClientSecret` | key `IdentityProvider__AdminClientSecret` | Identity provider only. Secret `name` and `key` of the `axis-admin` client secret. Required. |
| `secrets.identityCertificate` | none, required | Secret `name` of a Kubernetes TLS Secret with `tls.crt` and `tls.key`, for the DNS name of the identity Service. It has no `key`. Rendering fails with a message that names `secrets.identityCertificate.name` when it is missing. |
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
Job, the publisher Deployment, the secret references, the Studio volume, the forwarded headers, that the server alone listens on HTTPS with its certificate Secret and is probed over HTTPS, that the Service port is named `https`, that rendering fails without `secrets.serverCertificate.name`, and that the chart renders no Ingress and a `ClusterIP` server Service. It also checks that the identity Deployment runs the Keycloak image that the tests pin with `start`, mounts the theme and the password list read-only, has startup, liveness and readiness probes over HTTPS on its management port, and has a Service with port 443 named `https`. It checks that the identity secret files and certificate are mounted in the identity pod only and in no env entry, and that rendering fails, naming the value, without the identity host, the identity certificate, the theme volume or the password-list volume. `scripts/setup.sh` installs the pinned Helm,
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
  `Platform:SecretKey` on an installation that holds keys follows the steps of
  the [installation key](tenant-setting-commands.md#installation-key) rotation,
  which signs nobody out. The first start after this change also signs
  everyone out once.
- **Forwarded headers.** Behind a proxy that ends TLS, the server does not see
  the client's connection, only the proxy's. Name your proxies so the server can
  read the real scheme and host:

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
  proxy. See "TLS proxy and a second replica" in [on premises](#on-premises)
  and [TLS](#tls).
- **Platform settings under both tenant sources.** The server needs
  `ConnectionStrings:PlatformSchema` and `Platform:SecretKey` also under
  `Tenancy:Source=configuration`, because it creates the platform tables there
  too. A missing one stops the start. See [tenancy settings](host-settings.md#tenancy-settings).

## TLS

The rule for traffic that comes in, and for every connection Axis makes.

**Incoming traffic.**

- **Versions.** Only TLS 1.2 and TLS 1.3. TLS 1.3 is preferred, and a normal
  client gets it.
- **Ciphers.** On TLS 1.2 only ECDHE ciphers with AES-GCM or
  ChaCha20-Poly1305, so every session has forward secrecy. All TLS 1.3 suites
  are AEAD and forward-secret.
- **The certificate.** Use a publicly trusted certificate that covers every
  tenant host.
- **No plain HTTP.** The hosts publish only on `127.0.0.1`, and only the TLS
  endpoint faces clients. The server sends HSTS on https, see
  [security headers](headers-and-rate-limits.md#security-headers).
- **No client certificate.** Mutual TLS is not offered.

**Connections Axis makes.**

- **The identity provider.** Outside Development and Testing the server
  accepts an identity provider over `https` only (`RequireHttpsMetadata`). It
  validates the certificate against the system trust store. The admin host and the publisher
  update each tenant's realm, so they trust it the same way. Add a private
  authority to that store. Do not replace the store with `SSL_CERT_FILE`, which
  makes the server trust only that file, see [on premises](#on-premises).
- **PostgreSQL.** Set `SSL Mode=VerifyFull` in the host's own connection
  string, plus `Root Certificate` for a private authority. Outside Development
  and Testing, the server, the worker, the publisher and the admin tool stop at
  start when one of their own connection strings sets a lower mode or none, and
  the error names the setting and never shows its value. The server checks
  `ConnectionStrings:Platform` and `ConnectionStrings:PlatformSchema`. The
  worker checks `ConnectionStrings:Platform` under
  `Tenancy:Source=directory`. Every admin command, `publisher` included, checks
  `Admin:ConnectionString`, `ConnectionStrings:PlatformSchema` and
  `Scenarios:ServerConnectionString` when set, and exits with code 2. Every tenant
  connection copies `SSL Mode`, `Root Certificate` and
  `Check Certificate Revocation` from `ConnectionStrings:Platform` (the server
  and the worker), `Admin:ConnectionString` (`tenant create` and
  `tenant restore`) or `ConnectionStrings:PlatformSchema` (the other admin
  commands and the publisher). `pg_dump` and `pg_restore` get `PGSSLMODE` and
  `PGSSLROOTCERT`, with `system` under `verify-full` when no root certificate
  is named. A connection string with no `SSL Mode` counts as `Prefer`, which
  falls back to plain text, so it is refused. Tenants under
  `Tenancy:Source=configuration` carry their own settings in their connection
  strings, and the hosts do not check them.
- **URL bindings.** A `url` binding must be `https` outside Development.
  Activation refuses an `http` binding, and the worker never sends a call to
  one, see [deployment bindings](integration-resources.md#deployment-bindings).
- **Mail.** Outside Development the worker sends no email while `Email:Smtp:UseTls`
  is off, and `tenant email set` refuses `--tls false`. Both name the setting. See
  [mail settings](notifications.md#mail-settings).
- **Telemetry export.** Outside Development every OTLP endpoint that is set must be an
  `https` URL, or the server and the worker stop at start. See
  [telemetry export](host-settings.md#telemetry-export).
- **Proxy to server.** The server serves HTTPS only and has no plain HTTP
  listener, so traffic between the proxy and the server never falls back to
  unencrypted. The certificate comes from you. The Compose proxy connects over
  TLS, checks that the certificate is for the name `servers` and trusts only
  `server-ca.crt`, the authority you name. With Helm, your own Ingress must
  connect to the Service over HTTPS and verify the server certificate. The
  worker's health endpoint stays plain HTTP. Only the host or the kubelet
  probes it, and it carries no application traffic.

**What the Compose example sets.** The Caddyfile sets `protocols tls1.2 tls1.3`
and the `ciphers` list above. The proxy publishes container port 443 only. Both
servers set a Kestrel HTTPS endpoint on port 8443 with the certificate in
`AXIS_SERVER_TLS_DIR`, and the proxy forwards to port 8443 over TLS with the
server name `servers` and `/certs/server-ca.crt` as its only trusted authority.

**What the Helm chart leaves to you.** The chart has no Ingress, and the
server Service is `ClusterIP`. Your own Ingress or load balancer ends TLS, so
it must meet the rules for incoming traffic above. The chart cannot enforce
versions or ciphers there. It also cannot make the Ingress connect over HTTPS
and verify the server certificate. The chart sets a Kestrel HTTPS endpoint from
the Secret in `secrets.serverCertificate`, probes it over HTTPS and names the
Service port `https`.

The proxy follows
[D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).

## Hardening checklist

_(planned for M28)_ A list of every control that the [ASVS](asvs.md) tracking or the threat model
delegates to the operator, such as disk encryption, malware scanning of uploads and access to the
logs, with how to meet each one and how to check it. Outside Development, the server and the
worker warn at start about each item they can see is missing, see
[D110](../decisions/d110-security-alerts-and-a-hardening-checklist-for-the-operator.md).

## Upgrade

Only the latest version of Axis gets security fixes, so an operator upgrades to get one, see
[D108](../decisions/d108-reporting-a-vulnerability-and-security-fixes.md).

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

**PostgreSQL over TLS.** A host outside Development and Testing stops at start
when a connection string of its own sets `SSL Mode` below `VerifyFull`, or sets
none, see [TLS](#tls). An existing install whose strings use the default
`Prefer` stops at the next start. Before you roll out the hosts, make
PostgreSQL serve TLS and set `SSL Mode=VerifyFull` in `ConnectionStrings__Platform`,
`ConnectionStrings__PlatformSchema` and `Admin__ConnectionString`. Add
`Root Certificate` when the certificate comes from a private authority. The admin
image checks the same strings, so `migrate` in step 2 needs them too.

**The server certificate.** The server serves HTTPS only, so it does not start
without a certificate. An existing install must create it before the rollout.
With Compose, put `tls.crt` and `tls.key` for the name `servers` in
`AXIS_SERVER_TLS_DIR`, and `server-ca.crt` in `AXIS_TLS_DIR`. The server does
not start without the first two, and the proxy cannot reach the server without
the third. The server's host port now serves HTTPS.
With Helm, create the TLS Secret and set `secrets.serverCertificate.name`.
Rendering fails without it. The Service port is now 443 and named `https`, so
point your Ingress at it over HTTPS.

**The Helm identity provider.** The chart now also runs the identity provider, so
an existing install fails to render until you set the new required values:
`identity.hostname`, `identity.theme.volume`, `identity.passwordBlacklists.volume`,
`secrets.identityCertificate.name`, and the names of `secrets.identityDatabaseUrl`,
`secrets.identityDatabaseUsername`, `secrets.identityDatabasePassword` and
`secrets.identityAdminClientSecret`. Create the `axis-identity` and
`axis-identity-tls` Secrets first, see [in the cloud](#in-the-cloud).

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
