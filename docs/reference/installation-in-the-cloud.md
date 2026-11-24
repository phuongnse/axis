# Installation in the cloud

This page installs Axis on Kubernetes with the Helm chart in `deploy/helm/axis/`. It belongs with
[installation](installation.md), which lists the images, settings and secrets, and the TLS rules. In
this page, "database role" always means the PostgreSQL role.

## In the cloud

This section installs Axis on Kubernetes with the Helm chart in `deploy/helm/axis/`. PostgreSQL and the
source of secrets are external, and the chart runs
[Axis's identity provider](identity-provider.md#installation). The same chart installs a
[cell](cells.md) of Axis SaaS through its `cell.*` values.

The chart has these objects:

- **The server and the worker.** One Deployment each, and a Service for the server. Each has a
  startup, liveness and readiness probe, see [health checks](health-checks.md). The server's probes use
  HTTPS and the worker's use plain HTTP, and the chart sets `Health__Urls` to `http://0.0.0.0:5208` for
  the worker so that the kubelet can reach it. The server mounts the Studio volume and gets the Studio
  settings. In a cell the worker mounts it read-only at `/studio` with `Studio:ApplicationsPath`, to count applications for the [usage](tenant-quotas.md#usage). The server and the identity provider get the forwarded headers settings.
- **The agent host.** A Deployment with a startup, liveness and readiness probe over plain HTTP on port
  5209, and the chart sets `Health__Urls` to `http://0.0.0.0:5209` so that the kubelet can reach it. It
  has no Service. It mounts only `platformConnection` and `platformSecretKey`, which it reads with AI authoring on.
- **The publisher.** A Deployment with one replica that runs the `axis-admin` image with the command
  `publisher`. It uses the `Recreate` strategy, so a rollout never runs two publishers. It has no
  Service, no container port, no probe and no Ingress, because the publisher has no HTTP endpoint. Its
  termination grace period is 360 seconds, longer than the five-minute lease. Outside a cell it mounts
  the same secrets as the migrate Job. When `studio.folders` is set, it also mounts the Studio volume
  and gets the folders and `platformConnection`, as the server does.
- **The package hosts.** For each of the C#, the Node.js and the Python hosts, a Deployment, a Service on port 5310 and a NetworkPolicy, all named `<release>-package-host-dotnet`, `<release>-package-host-node` or `<release>-package-host-python`, see [package runners](package-runners.md#package-runners). The worker gets `Extensions__Runners__Dotnet`, `Extensions__Runners__Node` and `Extensions__Runners__Python`, one for each Service. **The application package host** is the exception: only with `applicationPackageHost.enabled`, which is off by default, so a cluster without a gVisor RuntimeClass still installs. A Deployment, a Service on port 5310 and a NetworkPolicy, all named `<release>-application-package-host`, rendered by the templates of the package hosts from the `axis-package-host-node` image. Its pod sets `runtimeClassName` from `applicationPackageHost.runtimeClassName`, `gvisor` by default, and the pods of the other package hosts set none, see [an application's packages](package-runners.md#an-applications-packages). The worker and the publisher get `Extensions__Runners__Application` for its Service, and its NetworkPolicy admits publisher pods on port 5310 as well as worker pods. The chart does not set `Extensions__ApplicationPackages`.
- **The identity provider**, see [below](#the-identity-provider-in-the-chart).
- **PgBouncer**, in a cell that turns it on, see [PgBouncer](#pgbouncer).
- **The migrate Job and the CronJobs** of a cell, see [jobs](#jobs).

The pods run as a non-root user with a read-only root file system, except the identity provider.

### The publisher in a cell

With `cell.id` set, the publisher also gets what the `create` job reads:

- the `adminConnection` and `identityAdminClientSecret` Secrets as files in `/run/secrets`;
- `IdentityProvider:Url` as `https://<identity.hostname>/` and `IdentityProvider:AdminClientId` as
  `axis-admin`;
- the `cellBackupKey` Secret read-only at `/cell-backup-key/key`, which `Cell:BackupKeyFile` names
  whatever `cell.backupKeyFile` says;
- `studio.persistence` read-write at `/studio` and `templates.volume` read-only at `/templates`, with
  `Studio:ApplicationsPath` and `Templates:Path` set to them;
- the `platformConnection` Secret, for the Studio write lock.

The server also mounts `studio.persistence` at `/studio` and sets `Studio:ApplicationsPath`, so the
applications that the job writes are the ones that Studio shows, and a cell needs a `ReadWriteMany`
volume source even with one server replica. The server gets no `Templates:Path`, so Studio lists no
templates there. Rendering fails with a message that names the value when `secrets.adminConnection`,
`secrets.cellBackupKey`, `studio.persistence` or `templates.volume` is missing. Outside a cell the
publisher gets none of these, unless `studio.folders` is set, which gives it the volume, the folders and
`platformConnection` only.

### The identity provider in the chart

The chart has a Deployment and a Service named `<release>-identity`, and no switch to turn them off,
as `docker compose up` always starts the identity provider too.

- **The pod.** One replica of Keycloak at `identity.image`, which defaults to the image that
  development, the tests and the Compose example pin. Axis does not cluster Keycloak. It runs `start`
  on HTTPS only, with the certificate of `secrets.identityCertificate`, and `identity.hostname` as its
  public host name. Keycloak rebuilds itself at start and writes into its image, so this one container
  has no read-only root file system.
- **Its settings.** Its database settings and the `axis-admin` client secret are secret files, which a
  small script in the container command turns into Keycloak's variables, because Keycloak has no
  `_FILE` variables. The Axis theme and the breached-password list come from volumes that you name,
  see the values table.
- **Probes and Service.** The pod has a startup, liveness and readiness probe over HTTPS on Keycloak's
  management port, 9000. The Service has port 443, named `https`, which forwards to port 8443, and it
  does not expose the management port. The chart renders no Ingress, so your own Ingress sends the
  identity host to this Service, as it sends the server's host to the server Service.
- **Forwarded headers.** Keycloak reads them only from `forwardedHeaders.knownProxies` and
  `knownNetworks`, and sets no proxy headers when both are empty.
- **Creating tenants.** A cluster that runs `tenant create` still needs its own Job with the
  `IdentityProvider__*` settings, which the chart does not render.

### Before the first install

Create the database roles of [On premises](installation-on-premises.md#database-roles). Then create the Kubernetes Secrets. The
chart creates none and never takes a secret value: each reference in the values names a Secret and a
key, and several references may name the same Secret. A secret store works too, if it syncs into a
Kubernetes Secret.

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

The `axis-identity` Secret holds the four settings of the identity provider, as in
[Axis's identity provider](identity-provider.md#installation). The JDBC URL may name the PostgreSQL
authority certificate with `sslrootcert=/db-ca/root.crt` when you set
`identity.databaseAuthority.volume`.

The cell backup CronJob also needs the connection of the read-only role `axis_identity_reader` of the
identity provider's database, so every daily archive holds the tenant's realm. Keycloak creates its
`credential` table at its first start, so give the role its `SELECT` after that, see
[the database roles](installation-on-premises.md#database-roles). Create it in its own Secret and set
`secrets.identityCredentialsConnection`:

```sh
kubectl create secret generic axis-identity-reader \
  --from-literal=IdentityProvider__DatabaseConnectionString='<connection string with SSL Mode=VerifyFull>'
```

To let Studio run test scenarios, add
`--from-literal=Scenarios__ServerConnectionString='<connection string>'` to the same Secret, or to
another one. Then set `secrets.scenarioServerConnection` and `studio.tests`, see the values table.

The server serves HTTPS only, so it needs a certificate for the DNS name of its Service, such as
`axis-server.<namespace>.svc`. Create it as a Kubernetes TLS Secret, from a certificate that your
Ingress trusts. The identity provider also serves HTTPS only, so create a second TLS Secret for the DNS
name of the identity Service, such as `axis-identity.<namespace>.svc`:

```sh
kubectl create secret tls axis-server-tls --cert=tls.crt --key=tls.key
kubectl create secret tls axis-identity-tls --cert=tls.crt --key=tls.key
```

### PgBouncer

A cell sets `cell.pgbouncer.enabled` to `true` to run PgBouncer in transaction mode in front of
PostgreSQL, see [connections](cells.md#connections). The chart then renders a ConfigMap, a Deployment
and a Service named `<release>-pgbouncer`, and none of them when it is off.

- **Who connects through it.** The Service has port 6432. The server and the worker send their
  runtime connections to it, with `Cell__RuntimeDatabaseHost` and `Cell__RuntimeDatabasePort`. The
  publisher and the migrate Job connect to PostgreSQL directly.
- **One replica**, because each replica adds its own `max_db_connections` to PostgreSQL.
- **TLS on both legs.** PgBouncer asks the hosts for TLS and verifies PostgreSQL's certificate in
  full. The hosts verify PgBouncer's certificate with the `SSL Mode` and `Root Certificate` of
  `ConnectionStrings:Platform`, so the certificate must name `<release>-pgbouncer` and chain to that
  authority.

PgBouncer looks passwords up with one auth user. As a superuser, create the user, then a lookup
function in `cell.pgbouncer.authDatabase`. The function returns a password only for the tenant runtime
roles, `axis_<id>_runtime`, so the schema role cannot connect through PgBouncer. It reads `pg_authid`,
so a managed service may need another way to grant that right.

```sql
CREATE ROLE axis_pgbouncer LOGIN PASSWORD '<password>';
CREATE SCHEMA pgbouncer;
CREATE FUNCTION pgbouncer.user_lookup(p_user text, OUT uname text, OUT phash text)
RETURNS record LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
  SELECT rolname::text, rolpassword::text FROM pg_authid
  WHERE rolname = p_user AND rolcanlogin AND rolname LIKE 'axis\_%\_runtime'
$$;
REVOKE ALL ON FUNCTION pgbouncer.user_lookup(text) FROM PUBLIC;
GRANT USAGE ON SCHEMA pgbouncer TO axis_pgbouncer;
GRANT EXECUTE ON FUNCTION pgbouncer.user_lookup(text) TO axis_pgbouncer;
```

`userlist.txt` holds the auth user's line only: `"axis_pgbouncer" "<password or SCRAM verifier>"`.
The second Secret is a TLS Secret:

```sh
kubectl create secret generic axis-pgbouncer-auth --from-file=userlist.txt
kubectl create secret tls axis-pgbouncer-tls --cert=tls.crt --key=tls.key
```

### Install

Write a values file that names the Secrets, then run:

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

### Which pod gets which secret

Each pod mounts only the secrets it needs, as files in `/run/secrets` unless this table says
otherwise. Rendering fails with a message that names the value when a `name` is missing, and the
values schema rejects any field in a reference other than `name` and `key`.

| Pod | Secrets |
| --- | --- |
| Server | `platformConnection`, `platformSchemaConnection` and `platformSecretKey`, and `serverCertificate` read-only at `/server-tls`, which no other pod mounts |
| Worker | `platformConnection` and `platformSecretKey` |
| Agent host | `platformConnection` and `platformSecretKey`, and no other secret |
| C#, Node.js, Python and application package hosts | None, and no env entry |
| Migrate Job | `platformSchemaConnection` and `platformSecretKey` |
| Publisher | `platformSchemaConnection` and `platformSecretKey`, and `scenarioServerConnection` when it is set, and `platformConnection` when `studio.folders` is set. In a cell also `platformConnection`, `adminConnection`, `identityAdminClientSecret` and `cellBackupKey`, see [the publisher in a cell](#the-publisher-in-a-cell) |
| Identity provider | The four `axis-identity` files, and `identityCertificate` read-only at `/identity-tls`, and no other secret |
| PgBouncer | `pgbouncerAuth` and `pgbouncerCertificate`, which no other pod mounts |
| Cell backup CronJob | `platformSchemaConnection`, `platformSecretKey`, `backupStorageAccessKeyId`, `backupStorageSecretAccessKey` and `identityCredentialsConnection`, and `cellBackupKey` read-only at `/cell-backup-key/key`, outside `/run/secrets` |
| Cell restore-check CronJob | The backup CronJob's secrets except `identityCredentialsConnection`, `adminConnection`, `identityAdminClientSecret`, and `scenarioServerConnection` when it is set |

So outside a cell no pod has an `Admin:ConnectionString`, and in a cell only the publisher and the
restore-check CronJob have the admin role. Only those two, besides the identity provider, get
`IdentityProvider__AdminClientSecret`, and only the two CronJobs get the storage credentials. The cell backup CronJob alone gets
`identityCredentialsConnection`.

### The key service

Set `keyService.kind` to `aws-kms` in production. The hosts then unwrap the installation key and the agent host's private key at AWS KMS, see
[key policy](keys-and-data-protection.md#key-policy). With it empty the chart still renders, but a host outside Development and Testing
stops at start, naming `KeyService:Kind`, so a production install always sets it. Make the wrapped values with `platform key wrap`, run with
`KeyService__Kind=aws-kms` and `KeyService__KeyId` under an AWS identity that may call KMS Encrypt. Store the installation key's
output in the `platformSecretKey` Secret, and the agent host key's output in the file that the agent host reads as `Ai:HostKeyFile`.
The chart sets no `Ai:HostKeyFile`, so you mount that file and set the setting yourself.

Each host calls KMS Decrypt under its own AWS identity, which the AWS SDK finds through its default credential chain, such as an
IAM role for a service account. Write the KMS key policy so that only each host's identity may decrypt, with the encryption
context `axis:purpose` set to the key's purpose. CloudTrail then logs every unwrap. The chart gives no pod a service account of its
own except the publisher in a cell, so all other pods run under the default service account of the namespace and share one AWS
identity.

### Values

The values of the chart, with their defaults:

| Value | Default | Meaning |
| --- | --- | --- |
| `image.registry` | empty | Registry prefix of the Axis images. |
| `image.tag` | the chart's `appVersion` | Tag of all Axis images. |
| `image.pullPolicy` | `IfNotPresent` | Pull policy. |
| `image.pullSecrets` | none | Names of image pull Secrets. |
| `server.repository`, `worker.repository`, `agent.repository`, `migrate.repository`, `packageHost.dotnet.repository`, `packageHost.node.repository`, `packageHost.python.repository`, `applicationPackageHost.repository` | `axis-server`, `axis-worker`, `axis-agent`, `axis-admin`, `axis-package-host-dotnet`, `axis-package-host-node`, `axis-package-host-python`, `axis-package-host-node` | Repository of each image. `migrate.repository` is also the publisher's image. |
| `server.replicas` | 1 | Server replicas. The key ring is shared, so sessions work on every replica. With Studio on, the replicas also share the `studio.persistence` volume, see [several server replicas](several-server-replicas.md). |
| `worker.replicas` | 1 | Worker replicas. |
| `agent.replicas` | 1 | Agent host replicas. |
| `packageHost.dotnet.replicas`, `packageHost.dotnet.resources`, `packageHost.node.replicas`, `packageHost.node.resources`, `packageHost.python.replicas`, `packageHost.python.resources`, `applicationPackageHost.replicas`, `applicationPackageHost.resources` | 1, limits of 1 CPU and `1Gi` | Replicas and resources of the C#, the Node.js, the Python and, when it is on, the application package host. Rendering fails, naming the value, without `limits.cpu` or `limits.memory`. |
| `applicationPackageHost.enabled`, `applicationPackageHost.runtimeClassName` | `false`, `gvisor` | Runs the application package host and gives the worker and the publisher `Extensions__Runners__Application`, see [an application's packages](package-runners.md#an-applications-packages). The RuntimeClass of gVisor that its pod runs under must exist in the cluster. With the host on, an empty `runtimeClassName` fails rendering and names the value. |
| `alerts.enabled`, `alerts.labels` | `false`, none | Renders a PrometheusRule with the alert rules of `alerts/axis-alerts.yaml`, and gives it `labels` for a Prometheus Operator that selects rules by a label such as `release`. It needs the Prometheus Operator's CRDs, so it fails on a cluster without them. An install without the chart loads the same file in Prometheus, see [the stale backup alert](tenant-backup-and-restore.md#daily-backup). |
| `studio.folders` | none | The Studio folders, as paths relative to `/studio`. Each renders as `Studio__Folders__N` with the value `/studio/<path>` on the server and on the publisher. Setting it turns Studio on. |
| `studio.authorRole`, `studio.publisherRole` | empty | Server only. `Studio__AuthorRole` and `Studio__PublisherRole`. Rendered only when set. Without one, nobody may author or publish. |
| `studio.persistence` | none | A Kubernetes volume source for the Studio folders, mounted read-write at `/studio` on every server replica and on the publisher. In a cell the server and the publisher use `/studio` as `Studio:ApplicationsPath`. It is required when `studio.folders` is set and in a cell, and rendering fails with a message that names it when it is missing. Outside a cell, on its own it does nothing. The chart creates no claim. With several replicas, and in a cell, it needs a `ReadWriteMany` storage class. |
| `forwardedHeaders.knownProxies`, `forwardedHeaders.knownNetworks` | none | Server and identity provider. IP addresses and CIDR ranges of your proxies, rendered on the server as `ForwardedHeaders__KnownProxies__N` and `ForwardedHeaders__KnownNetworks__N`, and on the identity provider as one `KC_PROXY_TRUSTED_ADDRESSES` list with `KC_PROXY_HEADERS=xforwarded`. Empty trusts no proxy, and the identity provider sets no proxy headers. |
| `server.resources`, `worker.resources`, `agent.resources`, `migrate.resources` | none | Kubernetes resource requests and limits. |
| `server.service.type`, `server.service.port` | `ClusterIP`, 443 | The Service of the server. Its port is named `https` and forwards to the server's HTTPS port, 8443. Put your own Ingress in front of it, see [TLS](installation.md#tls). |
| `secrets.serverCertificate` | none, required | Secret `name` of a Kubernetes TLS Secret with `tls.crt` and `tls.key`, for the DNS name of the server Service. It has no `key`. The server serves HTTPS only, and rendering fails with a message that names `secrets.serverCertificate.name` when it is missing. |
| `identity.image` | the Keycloak image that `KeycloakFixture.Image` pins | The whole image reference of the identity provider. `image.registry` and `image.tag` do not apply to it. |
| `identity.hostname` | none, required | The public host name of the identity provider, without a scheme. It becomes `KC_HOSTNAME=https://<hostname>`. Rendering fails with a message that names it when it is missing. |
| `identity.resources` | none | Kubernetes resource requests and limits of the identity provider. |
| `identity.service.type`, `identity.service.port` | `ClusterIP`, 443 | The Service of the identity provider. Its port is named `https` and forwards to Keycloak's HTTPS port, 8443. Put your own Ingress in front of it. |
| `identity.theme.volume` | none, required | A Kubernetes volume source with the built Axis theme, the output of `npm run build:keycloak-theme --prefix web`, mounted read-only at `/opt/keycloak/themes`. A release does not ship the built theme. Rendering fails with a message that names the value when it is missing. |
| `identity.passwordBlacklists.volume` | none, required | A Kubernetes volume source with the breached-password list of `deploy/keycloak/password-blacklists`, mounted read-only at `/opt/keycloak/data/password-blacklists`. Without it `tenant create` fails at its realm step. Rendering fails with a message that names the value when it is missing. |
| `identity.databaseAuthority.volume` | empty | Optional. A volume source with `root.crt`, the authority certificate of the PostgreSQL server, mounted read-only at `/db-ca`. A database with a public certificate can use `sslfactory=org.postgresql.ssl.DefaultJavaSSLFactory` in the JDBC URL instead. |
| `secrets.identityDatabaseUrl`, `secrets.identityDatabaseUsername`, `secrets.identityDatabasePassword` | keys `KC_DB_URL`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` | Identity provider only. Secret `name` and `key` of Keycloak's database settings. All three names are required. |
| `secrets.identityAdminClientSecret` | key `IdentityProvider__AdminClientSecret` | Identity provider, the cell restore-check CronJob and, in a cell, the publisher. Secret `name` and `key` of the `axis-admin` client secret. Required. |
| `secrets.identityCertificate` | none, required | Secret `name` of a Kubernetes TLS Secret with `tls.crt` and `tls.key`, for the DNS name of the identity Service. It has no `key`. Rendering fails with a message that names `secrets.identityCertificate.name` when it is missing. |
| `publisher.resources` | none | Kubernetes resource requests and limits of the publisher. |
| `secrets.scenarioServerConnection` | empty | Publisher and the cell restore-check CronJob. Secret `name` and `key` of `Scenarios:ServerConnectionString`. The chart mounts it only when it is set. A cell whose tenants have Studio applications needs it set for the weekly restore check to pass. Create the role as in [the scenario role](installation-on-premises.md#the-scenario-role). |
| `studio.tests` | `false` | Server only. `true` renders `Studio__Tests`, which turns on `studioTest`. Set it together with `secrets.scenarioServerConnection`. |
| `ai.allowTenantKeys` | `false` | Server and agent host. `true` renders `Ai__AllowTenantKeys` on both, so a tenant administrator can set the tenant's own model key and the agent host uses it. See [a tenant's own key](model-bindings.md#a-tenants-own-key). |
| `ai.defaultBinding` | `""` | Server only. A value renders `Ai__DefaultBinding`, which turns on `studioAi`. The chart sets no `Ai:Bindings` on the agent host, so configure them yourself. |
| `publisher.terminationGracePeriodSeconds` | 360 | Seconds the publisher gets to finish its running request on `SIGTERM`. Keep it longer than the five-minute lease. |
| `migrate.parallel` | 1 | Tenant databases the Job migrates at once. |
| `keyService.kind`, `keyService.keyId` | empty | The key service of the hosts, see [the key service](#the-key-service). `kind` is `aws-kms`, and production requires it. With a kind, `KeyService__Kind` and `KeyService__KeyId` render on the server, the worker, the agent host, the publisher, the migrate Job and both cell CronJobs, which are the pods that mount `platformSecretKey`. Rendering fails with a message that names `keyService.keyId` when `kind` is set and `keyId` is empty. Empty renders neither, and the hosts then stop at start. |
| `tenancy.refreshInterval` | empty | `Tenancy__RefreshInterval`. Empty keeps the host default. Use the time span format `hh:mm:ss`, such as `00:00:30`. "30s" stops the hosts at start. |
| `cell.*` | empty | `Cell__*` on the server, the worker and the publisher, one variable for each value that is set: `id`, `region`, `tenantDomain`, `internalDomain` (required with `backup.bucket`, because the scratch tenant of the restore check lives under it), `tenantCap`, `consoleHost`, `consoleAllowedNetworks` (a list), `internalUrl`, `operatorRealm`, `operatorRole` and `backupKeyFile`. Empty is a self-hosted installation. With `cell.id` set the hosts need `cell.region` and `cell.tenantDomain`, see [cell settings](host-settings.md#cell-settings). The migrate Job gets none. The `Cell__RuntimeDatabaseHost` and `Cell__RuntimeDatabasePort` pair comes from `cell.pgbouncer` and goes to the server and the worker only. |
| `backup.bucket` | empty | In a cell, setting it renders the cell backup CronJob and the cell restore-check CronJob, see [jobs](#jobs). It is `Cell__BackupStorage__Bucket`. Empty, or outside a cell, renders neither. |
| `backup.serviceUrl` | empty | `Cell__BackupStorage__ServiceUrl`, the endpoint of the object storage. Required with `backup.bucket`: rendering fails with a message that names it when it is empty. |
| `backup.region` | empty | `Cell__BackupStorage__Region`, the region of the object storage. Required with `backup.bucket`: rendering fails with a message that names it when it is empty. |
| `backup.timeZone` | empty | The IANA time zone of the region, such as `Asia/Ho_Chi_Minh`, in which the backup CronJob runs at 01:00 and the restore-check CronJob runs on Sundays at 03:00. Required with `backup.bucket`: rendering fails with a message that names it when it is empty. |
| `backup.resources` | none | Kubernetes resource requests and limits of the container of both CronJobs. |
| `secrets.backupStorageAccessKeyId` | key `Cell__BackupStorage__AccessKeyId` | Secret `name` and `key` of the access key id of the backup storage. The cell backup and cell restore-check CronJobs alone mount it. Required with `backup.bucket`. |
| `secrets.backupStorageSecretAccessKey` | key `Cell__BackupStorage__SecretAccessKey` | Secret `name` and `key` of the secret access key of the backup storage. The cell backup and cell restore-check CronJobs alone mount it. Required with `backup.bucket`. |
| `secrets.cellBackupKey` | key `cell-backup-key` | Secret `name` and `key` of the cell's backup key. The cell backup and cell restore-check CronJobs and, in a cell, the publisher alone mount it, at `/cell-backup-key/key`, and set `Cell__BackupKeyFile` to that path. Required in a cell: rendering fails with a message that names it when the `name` is empty. |
| `secrets.identityCredentialsConnection` | key `IdentityProvider__DatabaseConnectionString` | Secret `name` and `key` of `IdentityProvider:DatabaseConnectionString`, the connection of the read-only role of the identity provider's database. The cell backup CronJob alone mounts it, so `tenant backup` writes `realm.json` into every archive. Required with `backup.bucket`: rendering fails with a message that names it when the `name` is empty. |
| `secrets.adminConnection` | key `Admin__ConnectionString` | Secret `name` and `key` of `Admin:ConnectionString`, the connection of the admin role. The publisher in a cell and the cell restore-check CronJob alone mount it. Required in a cell: rendering fails with a message that names it when the `name` is empty. |
| `cell.pgbouncer.enabled` | `false` | `true` renders PgBouncer, see [PgBouncer](#pgbouncer). Rendering then fails with a message that names the value when `cell.id`, `cell.pgbouncer.databaseHost`, `cell.pgbouncer.authUser`, `secrets.pgbouncerAuth.name` or `secrets.pgbouncerCertificate.name` is empty. The schema rejects any field under `cell.pgbouncer` that this table does not list. |
| `cell.pgbouncer.image` | the PgBouncer image that `PgBouncerServer.Image` pins | The whole image reference of PgBouncer. `image.registry` and `image.tag` do not apply to it. The pod runs as user 70 of that image, with a read-only root file system, and one replica. |
| `cell.pgbouncer.databaseHost`, `cell.pgbouncer.databasePort` | none, required, and 5432 | The address of PostgreSQL, the target of the wildcard `* = host=... port=...` database line. |
| `cell.pgbouncer.authUser`, `cell.pgbouncer.authDatabase` | none, required, and `postgres` | The role that PgBouncer looks passwords up with, and the database that holds its `pgbouncer.user_lookup` function. The `auth_query` is fixed: `SELECT uname, phash FROM pgbouncer.user_lookup($1)`. |
| `cell.pgbouncer.defaultPoolSize`, `cell.pgbouncer.maxDbConnections`, `cell.pgbouncer.maxClientConn` | 3, 10 and 10000 | `default_pool_size`, `max_db_connections` and `max_client_conn`, the starting values of [connections](cells.md#connections). |
| `cell.pgbouncer.resources` | none | Kubernetes resource requests and limits of PgBouncer. |
| `cell.pgbouncer.databaseAuthority.volume` | empty | Optional. A volume source with `root.crt`, the authority certificate of the PostgreSQL server, mounted read-only at `/db-ca` and set as `server_tls_ca_file`. Without it PgBouncer verifies PostgreSQL against the system store. |
| `secrets.pgbouncerAuth` | key `userlist.txt` | PgBouncer only. Secret `name` and `key` of the auth user's line of `userlist.txt`, and only that line. Required with PgBouncer. |
| `secrets.pgbouncerCertificate` | none, required with PgBouncer | Secret `name` of a Kubernetes TLS Secret with `tls.crt` and `tls.key`, for the DNS name of the PgBouncer Service. It has no `key`. |
| `cell.certificate.enabled` | `false` | `true` renders a cert-manager `Issuer` and a `Certificate` named `<release>-tenant-subdomains` for `*.<cell.tenantDomain>`, see [hosts and certificates](cells.md#hosts-and-certificates). cert-manager must be installed, or the install fails because its resource types are missing. The chart renders no Ingress for the cell's own subdomains. The ingress of the cell serves the TLS Secret. With the certificate on, rendering fails with a message that names the value when `cell.tenantDomain`, `cell.certificate.secretName`, `cell.certificate.issuer.server` or `cell.certificate.issuer.dns01.tokenSecret.name` is empty. With the certificate on, the chart also renders the HTTP-01 `Issuer` `<release>-tenant-hosts` for the certificates of the tenants' own hosts. In a cell it renders the ServiceAccount `<release>-publisher`, a Role that may only `create` and `delete` `certificates.cert-manager.io` in the release namespace, which the purge job uses to delete the Certificates of a purged tenant, a Role `<release>-publisher-routes` that may only `create` and `delete` `ingresses.networking.k8s.io` in the release namespace, and a RoleBinding of each Role to it. It sets `Cell:HostCertificateIssuer`, `Cell:HostRouteService`, `Cell:HostRouteIngressClass` and `Cell:HostRouteAnnotations` on the publisher and mounts the service account token into the publisher only. The schema rejects any field under `cell.certificate` that this table does not list, so no value can hold a token. |
| `cell.certificate.secretName` | empty | The Kubernetes TLS Secret that cert-manager writes the certificate into. Required when the certificate is on. |
| `cell.certificate.issuer.server`, `cell.certificate.issuer.email` | Let's Encrypt production directory, empty | The ACME directory URL of the `Issuer`, and the optional contact email. The email renders only when set. cert-manager creates the ACME account key in the Secret `<release>-tenant-subdomains-acme-account`. |
| `cell.certificate.issuer.dns01`, `cell.certificate.http01.ingressClassName`, `cell.certificate.route.annotations` | `provider: cloudflare`, `tokenSecret.key: api-token`; empty; `{}` | The one DNS-01 solver. `provider` is `cloudflare` or `digitalocean`. `tokenSecret` is the Secret `name` and `key` of the provider's API token. The name is required. The token is never a value. `http01.ingressClassName` is the ingress class of the temporary Ingress that cert-manager creates for the HTTP-01 challenge of the `<release>-tenant-hosts` `Issuer`. Empty leaves the class to the cluster's default. It is also the ingress class of the Ingress that the publisher, not the chart, creates for each verified host. `route.annotations` are the annotations of that Ingress, a map of strings such as `nginx.ingress.kubernetes.io/backend-protocol: HTTPS` when the ingress controller reaches the server over TLS. The chart passes them as JSON in `Cell:HostRouteAnnotations`, the publisher's `axis/tenant` annotation wins over one of the same name, and a value that is not a string fails the schema. |
| `extensions.volume`, `modules.volume` | empty | Any Kubernetes volume source, mounted read-only at `/extensions` and `/modules`. Empty uses the empty folders of the image. |
| `templates.volume` | empty | Any Kubernetes volume source that holds the installed templates, mounted read-only at `/templates` on the publisher in a cell, with `Templates__Path` set to it. Required in a cell: rendering fails with a message that names it when it is empty. Unused outside a cell, and the server never gets it. |
| `secrets.platformConnection` | key `ConnectionStrings__Platform` | Secret `name` and `key` of `ConnectionStrings:Platform`. |
| `secrets.platformSchemaConnection` | key `ConnectionStrings__PlatformSchema` | Secret `name` and `key` of `ConnectionStrings:PlatformSchema`. |
| `secrets.platformSecretKey` | key `Platform__SecretKey` | Secret `name` and `key` of the installation key. |

### Jobs

The chart runs the migration before every upgrade, and in a cell it runs the backups and the weekly
restore check. It has no Job for tenants and releases.

- **The migrate Job.** A `pre-upgrade` hook Job runs `axis-admin migrate` with the new admin image
  before the Deployments roll out. A failed migration fails the upgrade, and the Deployments stay as
  they are. The Job never retries. Helm keeps a failed Job for its logs and deletes a successful one.
  The Job does not run on the first install, because a first install has no tenants, see
  [the first install](installation-open-points.md#open-points). Helm waits 5 minutes for a hook, so an installation with many
  tenants needs a longer wait, such as `helm upgrade --install --timeout 30m`.
- **The cell backup CronJob.** In a cell with `backup.bucket` set, the chart renders a CronJob named
  `<release>-cell-backup` that runs `axis-admin cell backup` with the admin image every day at 01:00
  in `backup.timeZone`, see [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell). It
  never runs two backups at once and never retries, and Kubernetes stops the Job after 6 hours 15
  minutes. It gets the platform schema connection, the installation key, the storage credentials, the
  identity provider's read-only connection and the cell backup key from Secret references, and the
  storage settings from `backup.*`. The job needs all five `Cell__BackupStorage__*` settings, so the
  chart requires the service URL and the region too. The job has no Studio volume, so its archives hold no Studio files. The operator sets the
  bucket's object lock in compliance mode for 30 days and its lifecycle rule that deletes an archive
  after 30 days, and the chart does not create or change the bucket.
- **The cell restore-check CronJob.** In a cell with `backup.bucket` set, the chart also renders a
  CronJob named `<release>-cell-restore-check` that runs `axis-admin cell restore-check` every Sunday
  at 03:00 in `backup.timeZone`, see [the cell restore check](administration.md#cell-restore-check).
  It never runs two checks at once and never retries, and Kubernetes stops it after 6 hours, so a hung
  run does not block the next week. It gets the storage settings, credentials, cell backup key and
  `backup.resources` of the backup job. It also gets `secrets.adminConnection`,
  `secrets.identityAdminClientSecret`, `IdentityProvider:Url` as `https://<identity.hostname>/`,
  `IdentityProvider:AdminClientId` as `axis-admin`, `Cell:InternalDomain`, and
  `secrets.scenarioServerConnection` when set. A cell whose tenants have Studio applications needs that
  Secret set for the weekly check to pass, because the scenario step fails with `AXC0217` without a
  scenario server. `Studio:ApplicationsPath` is `/studio` on an `emptyDir`, so the scratch tenant's
  Studio files never touch the server's Studio volume. The admin connection and the client secret are
  the widest credentials of the chart. The chart passes no OTLP settings, so the metric
  `axis.backup.restore_check` goes only to the job's output until you set an OTLP endpoint.
- **Tenants and releases.** Run `tenant create` and `activate` from a Job or a pod that uses the admin
  image, with the settings of the [settings table](installation.md#settings-and-secrets) and the application folder
  mounted.

### Checks of the chart

`scripts/lint.sh` runs `helm lint --strict` and validates the rendered manifests against the Kubernetes schemas and the pinned cert-manager
and PrometheusRule schemas with kubeconform. It renders the Axis chart with the application package host and the alerts off and on. `scripts/helm.test.mjs` renders the chart
and checks the rules of this section: the probes and HTTPS endpoints, the objects that render and that do not render inside and outside a
cell, that the PrometheusRule renders only with `alerts.enabled` and holds the rule file, which pod gets which secret and setting, that the key service settings render on every pod that mounts the installation key and nowhere else, that secrets are files and never env entries, that a secret value in a reference is rejected,
and that rendering fails, naming the value, without each required value. `scripts/test.sh` runs the alert rules through `promtool test rules`. `scripts/setup.sh` installs the pinned Helm, kubeconform, promtool,
Kubernetes schemas and cert-manager and PrometheusRule schemas into `~/.cache/axis-tools`, and downloads any schema file that a cached folder lacks. Axis has
no test against a live cluster, so no test runs PgBouncer, the identity provider or a package host, or checks that a cluster enforces its
NetworkPolicy.
