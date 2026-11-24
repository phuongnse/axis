# Installation

This page says how a platform operator installs and upgrades Axis, on one machine with Docker Compose
and on Kubernetes with the Helm chart. It lists the images, every setting and secret, the TLS rules
and, in [upgrade](upgrade.md), the upgrade order. The Compose and Helm steps are in
[installation on premises](installation-on-premises.md) and [installation in the cloud](installation-in-the-cloud.md).
In this page, "database role" always means the PostgreSQL role.

An installation runs four hosts, from four production images. The Compose example runs a fifth, a sixth and a seventh container, the C#, the Python and the Node.js package hosts, each from its own image, and the Helm chart runs the C#, the Python and the Node.js package hosts from their own images and, when `applicationPackageHost.enabled` is set, an application package host from the Node.js image, see [on premises](installation-on-premises.md#on-premises) and [in the cloud](installation-in-the-cloud.md#in-the-cloud):

| Host                     | Image         | What it does                                                                                                                                                                                                                                                               |
| ------------------------ | ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| The server               | `axis-server` | Serves the API, the sites and the SPA.                                                                                                                                                                                                                                     |
| The worker               | `axis-worker` | Runs background work.                                                                                                                                                                                                                                                      |
| The agent host           | `axis-agent`  | Runs [AI authoring requests](authoring-requests.md#the-agent-host). It starts idle, and with AI authoring on it reads the tenants.                                                                                                                                         |
| The admin host           | `axis-admin`  | `Axis.Admin`, a command-line tool that a platform operator runs to create tenants, migrate tenant databases and activate releases, see [administration](administration.md).                                                                                                 |
| The publisher            | `axis-admin`  | `axis-admin publisher`, a long-running command of the admin host that runs the publish requests that Studio stores, see [publisher](publisher.md#publisher). It holds the schema role of every active tenant, so it runs apart from the server. It has no HTTP endpoint. |

An installation also needs a PostgreSQL 18 server, which Axis does not ship for production. The
Compose example and the Helm chart both run [Axis's identity provider](identity-provider.md#installation),
so an installation needs no OIDC provider of its own.

Every tenant database needs the `pg_trgm` extension, for [search](record-api.md#paging-and-sorting).
`tenant create` and `migrate` create it in the `public` schema as the tenant's schema role. It is a
trusted extension, so they need no superuser. The PostgreSQL server must ship the contrib modules, as
the official `postgres` images do, and a managed service may need `pg_trgm` on its list of allowed
extensions.

## Images

Axis ships four host images, `axis-server`, `axis-worker`, `axis-agent` and `axis-admin`, and three package host images that
build extension packages. All seven are production images. They hold no sample, no sample extension package, no sample module,
no development realm, no credential and no source control metadata such as `.git`. The default target of the `Dockerfile` and
the `compose.yaml` at the repository root build the development images instead, which hold the samples and development credentials and are not for production, see
[development](../development.md#docker-compose). The `compose.yaml` also builds a development Node.js package host that holds the sample packages. `axis-controlplane` is the image of the Axis SaaS control plane and not part of
an installation, see [control plane](control-plane.md#image-and-chart).

Build an image from the repository root with `docker build --target axis-server --build-arg AXIS_VERSION=<version> .`, and use
`axis-worker`, `axis-agent`, `axis-admin` or a package host image as the target for another. `AXIS_VERSION` defaults to `0.0.0-dev`.

- **Environment.** The images run the `Production` environment.
- **User.** All run as a non-root user, UID 1654. The four host images work with a read-only root file system.
- **Settings.** The four host images read settings only from environment variables and from files in `/run/secrets`.
  Each file is named after a setting in its environment variable form, such as `Platform__SecretKey`,
  and its content is the value. Compose `secrets:` and Kubernetes secret volumes put files there.
- **Packages and modules.** Mount extension packages at `/extensions` and modules at `/modules` in the
  host images. The agent host loads neither, so `axis-agent` has no mount point for them.
- **Application folders.** Mount the folder into the admin container, then run `activate` on it.
- **No curl.** The images hold no `curl`, so they have no container health check. Use the probes of
  the orchestrator, see [health checks](health-checks.md).
- **PostgreSQL tools.** `axis-admin` holds the PostgreSQL 18 client tools, such as `pg_dump`, for
  backup and restore.
- **Version.** Every image carries the Axis version in the OCI label
  `org.opencontainers.image.version`, and `axis-admin version` prints it.
- **The package host images.** `axis-package-host-dotnet`, `axis-package-host-node` and `axis-package-host-python` build
  extension packages, see [building a package](extension-packages.md#building-a-package). Each holds `Axis.PackageHost`, Trivy
  at the pinned version and one runtime: .NET, Node.js 24 with npm, or Python 3 with pip. Unlike the host images, they need a
  writable root file system for the build, Trivy and npm, read only environment variables and have no `/modules`. Only the build
  needs that root: the `host` command runs on a read-only root.
- **The package runners.** The `dotnet` image runs the C# runner from `Axis.PackageHost` itself, the `node` image holds the Node.js runner in `/app/sdk/node/` and the `python` image holds the Python runner in `/app/sdk/python/`.
  Compose's `package-host-dotnet`, `package-host-python` and `package-host-node` and the chart's `<release>-package-host-dotnet`, `<release>-package-host-python` and `<release>-package-host-node` run the host with `host` on a read-only root, see [on premises](installation-on-premises.md#on-premises) and [in the cloud](installation-in-the-cloud.md#in-the-cloud).
- **At the customer.** Every package host image takes `connect`, so a customer runs it in its own network as a
  [customer-hosted runner](package-runners.md#customer-hosted-runners).
- **Signed images** _(planned for M32)_. A release workflow publishes the images to the GitHub
  container registry, each signed with cosign and with a CycloneDX SBOM. Check the signature before
  an install or an upgrade.

## Settings and secrets

Every installation sets these settings. A value marked secret comes from a file in `/run/secrets` or
from a secret store of the platform, never from a committed file.

| Setting | Environment variable | Hosts | Secret | Source |
| --- | --- | --- | --- | --- |
| `Tenancy:Source` | `Tenancy__Source` | server, worker, agent | no | Set to `directory`. The default `configuration` reads a `Tenants` section, which is for development. The agent host reads the settings marked `agent` only with AI authoring on, see [AI settings](host-settings.md#ai-settings). |
| `Tenancy:RefreshInterval` | `Tenancy__RefreshInterval` | server, worker, agent | no | Optional. It defaults to 30 seconds. |
| `ConnectionStrings:Platform` | `ConnectionStrings__Platform` | server, worker, agent, publisher | yes | The platform database role. It reads the `platform` schema, and writes only the keys of the server, the usage tables, the leases and the daily counters. The publisher holds it for the Studio write lock only, and starts without it. |
| `ConnectionStrings:PlatformSchema` | `ConnectionStrings__PlatformSchema` | server, admin | yes | The role that owns the `platform` schema. The server uses it at start for the platform migrations, and for the unpooled connection that `PUT /api/cell/tenants/{id}/quotas` opens for each write. Revoking or blocking this role stops that route. The worker never gets it. |
| `Platform:SecretKey` | `Platform__SecretKey` | server, worker, agent, admin | yes | The installation key, 32 bytes in base64, or wrapped with `KeyService:Kind`. Generate it once and give every host the same value. |
| `Platform:PreviousSecretKey` | `Platform__PreviousSecretKey` | server, worker, agent, admin | yes | Set it only during a key rotation, to the old installation key. See [installation key](tenant-setting-commands.md#installation-key). The Helm chart and the Compose example set no value, so add the variable or a secret file yourself. |
| `KeyService:Kind` | `KeyService__Kind` | server, worker, agent, admin | no | Set it to `file` or to `aws-kms`. Outside Development and Testing a host that reads the installation key or the agent host's key does not start without it, see [host settings](host-settings.md). `Platform:SecretKey`, `Platform:PreviousSecretKey` and the agent host's key file then hold wrapped values. The Compose example sets `file`. The Helm chart sets `aws-kms` with its `keyService` value, which production requires. |
| `KeyService:KeyFile` | `KeyService__KeyFile` | server, worker, agent, admin | yes | Required with `file`. The file of 32 random bytes in base64 that unwraps the keys. Mount it as a secret file, and set this setting to its path. A file in `/run/secrets` would set the setting to the key's text, so Compose mounts it at `/run/key-service/key`. |
| `KeyService:KeyId` | `KeyService__KeyId` | server, worker, agent, admin | no | Required with `aws-kms`. The AWS KMS key that unwraps the keys, as a key id, an alias or an ARN. Each host calls KMS under its own AWS identity, so no setting holds an AWS credential. The Helm chart sets it with its `keyService` value. |
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | server | no | Optional. The IP addresses of your reverse proxies. See [several server replicas](several-server-replicas.md). |
| `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | server | no | Optional. The CIDR ranges of your reverse proxies. |
| `Admin:ConnectionString` | `Admin__ConnectionString` | admin, publisher (in a cell) | yes | A database role with `CREATEDB` and `CREATEROLE`, not a superuser. |
| `IdentityProvider:Url` | `IdentityProvider__Url` | admin, publisher (in a cell) | no | The base address of Axis's identity provider. `tenant create` of a main tenant creates the tenant's realm there, and so does `tenant restore` of a tenant whose realm is missing. It must be `https` outside `Development` and `Testing`. |
| `IdentityProvider:AdminClientId` | `IdentityProvider__AdminClientId` | admin, publisher (in a cell) | no | The client of the identity provider's `master` realm that `tenant create` signs in as. |
| `IdentityProvider:AdminClientSecret` | `IdentityProvider__AdminClientSecret` | admin, publisher (in a cell) | yes | The secret of that client. Only `tenant create` of a main tenant and `tenant restore` read it. |
| `IdentityProvider:DatabaseConnectionString` | `IdentityProvider__DatabaseConnectionString` | admin, cell backup CronJob | yes | A read-only role of the identity provider's database, with `SELECT` on Keycloak's `credential` table only. With it, `tenant backup` writes the realm's users and credentials into the archive as `realm.json`, which a `tenant restore` into a new realm imports. The Compose example mounts it in the admin host, and the chart requires it for the cell backup CronJob, so every daily archive of a tenant with its own realm holds `realm.json`. Without it, no archive holds `realm.json`. Outside `Development` and `Testing` it must set `SSL Mode=VerifyFull`. |
| none | `AXIS_OIDC_CLIENT_SECRET` | admin | yes | The OIDC client secret of a restored tenant that keeps the archive's sign-in, which a restore under a new realm does not need. `tenant restore` reads it. `tenant create` does not. |
| `Scenarios:ServerConnectionString` | `Scenarios__ServerConnectionString` | publisher, `cell restore-check` | yes | Optional. A database role with `CREATEDB`, not a superuser, that owns no tenant database. It lets the publisher run `test` requests and the contract scenarios of `publish` and `validate` requests, and `cell restore-check` run the scenarios of a restored application. Empty by default, and a `test` request, or a `publish` or `validate` with contract scenarios, then fails with `AXC0217`, which names the setting. See [the scenario role](installation-on-premises.md#the-scenario-role). |
| `Studio:Tests` | `Studio__Tests` | server | no | Optional. `true` turns on `studioTest`, which shows the test controls in Studio. Off by default. Turn it on together with the publisher's `Scenarios:ServerConnectionString`. |
| `Extensions:Path` | `Extensions__Path` | server, worker, admin | no | Set to `/extensions`. |
| `Modules:Path` | `Modules__Path` | server, worker, admin | no | Set to `/modules`. |
| `Templates:Path` | `Templates__Path` | server, publisher | no | Optional. Unset in production, so Studio lists no application templates. In a cell the publisher reads it for the create job, which fails without it. The folder holds `<id>/<version>/` template folders, and a folder that does not exist stops the server. See [application templates](modules-and-templates.md#application-templates). |
| `Health:Urls` | `Health__Urls` | worker, agent | no | The worker's image keeps `http://127.0.0.1:5208` and the agent host's image keeps `http://127.0.0.1:5209`. The Helm chart sets each to all interfaces so the kubelet can probe it. |
| `ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | server | no | The image default is 8080, plain HTTP. The Compose example and the Helm chart replace it with the Kestrel endpoint below, so nothing listens on 8080. |
| `Kestrel:Endpoints:Https:Url` | `Kestrel__Endpoints__Https__Url` | server | no | The Compose example and the Helm chart set `https://+:8443`. A Kestrel endpoint replaces `ASPNETCORE_HTTP_PORTS`, so the server serves HTTPS only. |
| `Kestrel:Endpoints:Https:Certificate:Path`, `Kestrel:Endpoints:Https:Certificate:KeyPath` | `Kestrel__Endpoints__Https__Certificate__Path`, `Kestrel__Endpoints__Https__Certificate__KeyPath` | server | no | The Compose example and the Helm chart set `/server-tls/tls.crt` and `/server-tls/tls.key`. The key is a private key, so the folder is a mounted volume and never a setting. See [TLS](#tls). |
| `Kestrel:Endpoints:Internal:Url` | `Kestrel__Endpoints__Internal__Url` | server | no | A cell's internal listener, with its own certificate under `Kestrel:Endpoints:Internal:Certificate:Path` and `:KeyPath`. See [the internal listener](tenant-lifecycle.md#provisioning-api). |
| `OTEL_*` | `OTEL_*` | server, worker | maybe | Optional. See [telemetry export](host-settings.md#telemetry-export). A collector token in the headers is secret. |

The rules of each setting are in [tenancy settings](host-settings.md#tenancy-settings) and
[admin settings](host-settings.md#admin-settings). Three of them matter most for an installation:

- **`ActivateOnStartup` must be empty** under `directory`, or the server stops at start. The admin
  host activates releases, see [administration](administration.md#activate).
- **Tenant settings are not host settings.** A tenant's OIDC settings, database passwords and
  bindings live in the tenant directory, where the secrets are encrypted, see
  [platform database](storage.md#platform-database). No host setting holds them.
- **The worker and the agent host need no schema connection** of any kind, and they get no
  `ConnectionStrings:PlatformSchema`.
## TLS

This section states the TLS rule for traffic that comes in, and for every connection that Axis makes.
[Outbound connections](outbound-connections.md) lists those connections with the settings that name
their addresses.

### Incoming traffic

- **Versions.** Only TLS 1.2 and TLS 1.3. TLS 1.3 is preferred, and a normal client gets it.
- **Ciphers.** On TLS 1.2 only ECDHE ciphers with AES-GCM or ChaCha20-Poly1305, so every session has
  forward secrecy. All TLS 1.3 suites are AEAD and forward-secret.
- **The certificate.** Use a publicly trusted certificate that covers every tenant host.
- **No plain HTTP.** The hosts publish only on `127.0.0.1`, and only the TLS endpoint faces clients.
  The server sends HSTS on https, see [security headers](headers-and-rate-limits.md#security-headers).
- **No client certificate.** Mutual TLS is not offered on the public listeners. The one exception is
  the cell's [internal listener](tenant-lifecycle.md#provisioning-api), which the control plane calls.

### Connections Axis makes

- **The identity provider.** Outside Development and Testing the server accepts an identity provider
  over `https` only (`RequireHttpsMetadata`), and validates its certificate against the system trust
  store. The admin host and the publisher update each tenant's realm, so they trust it the same way.
  Add a private authority to that store. Do not replace the store with `SSL_CERT_FILE`, which makes
  the server trust only that file, see [the order](installation-on-premises.md#the-order).
- **PostgreSQL.** Set `SSL Mode=VerifyFull` in the host's own connection string, plus
  `Root Certificate` for a private authority. Outside Development and Testing, the server, the worker,
  the agent host, the publisher and the admin tool stop at start when one of their own connection strings sets a lower
  mode or none, and the error names the setting and never shows its value. A connection string with
  no `SSL Mode` counts as `Prefer`, which falls back to plain text, so it is refused.
  - The server checks `ConnectionStrings:Platform` and `ConnectionStrings:PlatformSchema`.
  - The worker checks `ConnectionStrings:Platform` under `Tenancy:Source=directory`. The agent host
    does the same when `Ai:DefaultBinding` is set, see [AI settings](host-settings.md#ai-settings).
  - Every admin command, `publisher` included, checks `Admin:ConnectionString`,
    `ConnectionStrings:PlatformSchema`, `ConnectionStrings:Platform` and `Scenarios:ServerConnectionString` when set, and exits with
    code 2.
  - Every tenant connection copies `SSL Mode`, `Root Certificate` and `Check Certificate Revocation`
    from `ConnectionStrings:Platform` (the server, the worker and the agent host), `Admin:ConnectionString`
    (`tenant create` and `tenant restore`) or `ConnectionStrings:PlatformSchema` (the other admin
    commands and the publisher).
  - `pg_dump` and `pg_restore` get `PGSSLMODE` and `PGSSLROOTCERT`, with `system` under `verify-full`
    when no root certificate is named.
  - Tenants under `Tenancy:Source=configuration` carry their own settings in their connection strings,
    and the hosts do not check them.
- **URL bindings.** A `url` binding must be `https` outside Development. Activation refuses an `http`
  binding, and the worker never sends a call to one, see
  [deployment bindings](integration-resources.md#deployment-bindings).
- **Model provider.** The agent host calls the `Endpoint` of a model binding with the scheme that the
  operator sets, and it checks no scheme. An empty `Endpoint` is the provider's own `https` address.
  A binding with a key sends it in a header on every call, so use an `https` endpoint for any binding
  that has a `KeyFile`. Use `http` only for a self-served model on a network that you trust. See
  [bindings](model-bindings.md#bindings).
- **Mail.** Outside Development the worker sends no email while `Email:Smtp:UseTls` is off, and
  `tenant email set` refuses `--tls false`. Both name the setting. See
  [mail settings](notifications.md#mail-settings).
- **Telemetry export.** Outside Development every OTLP endpoint that is set must be an `https` URL, or
  the server and the worker stop at start. See [telemetry export](host-settings.md#telemetry-export).
- **Proxy to server.** The server serves HTTPS only and has no plain HTTP listener, so traffic between
  the proxy and the server never falls back to unencrypted. The certificate comes from you. The
  Compose proxy connects over TLS, checks that the certificate is for the name `servers` and trusts
  only `server-ca.crt`, the authority you name. With Helm, your own Ingress must connect to the Service
  over HTTPS and verify the server certificate. The worker's health endpoint stays plain HTTP: only the
  host or the kubelet probes it, and it carries no application traffic.

### What the Compose example sets

The Caddyfile sets `protocols tls1.2 tls1.3` and the `ciphers` list above, and `:443` is its only site
address, so Caddy turns on no automatic HTTPS and no HTTP listener. The proxy publishes container port
443 only. Both servers set a Kestrel HTTPS endpoint on port 8443 with the certificate in
`AXIS_SERVER_TLS_DIR` and publish only that container port. The proxy forwards to port 8443 over TLS
with the server name `servers`, and sends the identity host to `identity:8443` with the server name
`identity`. It trusts only `/certs/server-ca.crt` in both places and never skips the verification.

### What the Helm chart leaves to you

The chart has no Ingress, and the server Service is `ClusterIP`. Your own Ingress or load balancer
ends TLS, so it must meet the rules for incoming traffic above, because the chart cannot enforce
versions or ciphers there. The chart also cannot make the Ingress connect over HTTPS and verify the
server certificate. The chart sets a Kestrel HTTPS endpoint from the Secret in
`secrets.serverCertificate`, probes it over HTTPS and names the Service port `https`.
