# Host settings

Detailed reference for the settings of the server and of `Axis.Admin`: startup activation,
Studio, tenancy and preview tenants, forwarded headers, the admin settings and telemetry export.
The worker's own settings are in [worker settings](process-execution.md#worker-settings).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Startup activation

The server can compile and activate application folders when it starts, so a
development or E2E server serves a real application without a separate step.

- **Setting.** `ActivateOnStartup` is an array of application folders, such
  as `"ActivateOnStartup": ["../../samples/apps/purchase-requests"]` or the
  environment variable `ActivateOnStartup__0=/path/to/folder`. Relative paths
  are resolved against the server content root. When the setting is absent or
  empty the step does not run. That is the default, and so the Production
  behaviour.
- **Order.** Tenants are processed in ordinal order of their id. For each
  tenant, the step applies the configuration, data and processes migrations
  to the tenant database, then compiles each listed folder against that database and
  activates the release, in the listed order. The same folders apply to
  every tenant.
- **Tenant source.** `ActivateOnStartup` is for
  `Tenancy:Source=configuration` only. Under `directory` a non-empty setting
  stops the start with a message that names `ActivateOnStartup`, and releases are activated with
  [`Axis.Admin activate`](administration.md#activate). The server runs no
  tenant migrations under `directory` either, see
  [tenancy settings](#tenancy-settings).
- **Packages.** Each folder compiles against the extension packages
  installed under `Extensions:Path`, see
  [Extension packages](extension-packages.md#declaring-packages-in-an-application). When a newer version in a range
  was installed since the last start, the folder compiles to a new release
  pinned to it, and that release is activated.
- **Modules.** Each folder compiles against the modules installed under
  `Modules:Path`, see [Modules](modules-and-templates.md#modules). When a newer version in a range
  was installed since the last start, the folder compiles to a new release
  that pins it and holds its files, and that release is activated.
- **Release history.** Each activation adds a row with the actor `startup` to
  the [release history](storage.md#release-history), unless the release is
  already the active one. A restart that finds the same release adds nothing.
- **Before listening.** The step runs before any hosted service starts, so
  the server accepts no request until every tenant is done.
- **Failures.** Any compile or activation diagnostic that is an error, or any
  exception, stops the start and the process exits. Each diagnostic is logged
  with its code, file and path, together with the folder and tenant. A warning,
  such as a missing binding on a [preview tenant](#preview-tenants), is logged
  and does not stop the start. An error after provisioning
  committed is logged with the folder and tenant too. The previously active
  release stays active, and the first failing tenant stops the whole start,
  so no server runs with some tenants on old releases and others on new ones.
- **Recompute limit** *(built)*. `Activation:InlineRecomputeLimit`
  is the most affected rows of one entity that activation recomputes in its
  own transaction when a release changes stored computed fields, or when a
  migration `fill` or `convert` changes the rows they read. It defaults to
  10,000. The environment variable is `Activation__InlineRecomputeLimit`.
  Above the limit, the entity becomes pending and the worker recomputes the
  rows in batches. See
  [Recomputing computed fields](migrations.md#recomputing-computed-fields).
- **Seeding.** After a folder is activated in a tenant, the step inserts
  every seed record whose id the entity table does not hold yet, so
  restarts never duplicate seeds. Only the id decides: a seed record that was
  deleted is inserted again on the next start. What happens to a record
  that already exists depends on the seed's `sync` setting:
  - Without `sync`, existing records are left alone, including records
    edited through the UI.
  - With `sync`, a record whose declared values differ from the stored ones
    is updated by the record update command, and its version grows by one.
    PostgreSQL compares the typed values, so `1.5` equals a stored `1.50`.
    A record that is identical is not written, and its version stays the
    same. Fields the seed does not declare keep their stored values, and a
    record removed from the seed file stays in the table. A `null` value
    clears the stored field. Only declared values are compared, so a
    computed value alone never causes a write.

  Each record goes through the record input parser as a create body, so the
  record API's rules apply, and a synced seed still needs every required
  field. Every record is parsed before any write, and every invalid value is
  `AXC0033` at `/records/{i}/values/<field>`, as is a value for a
  [computed field](resource-files.md#entity-logic) or a [sequence](resource-files.md#sequences) field. An
  inserted seed record gets the sequence's next number, like any other
  create, and a synced update keeps the number. Seed records are not run
  through the entity's [validations](resource-files.md#entity-logic) yet. Then all inserts and updates of
  the folder run in one transaction, seed files in path order and records in
  file order, so a reference value must name an existing record or one
  inserted earlier in the same run. Each write computes the record's
  computed fields, an update from the stored record with the declared
  values on top. A value that cannot be computed is `AXC0033` at
  `/records/{i}/values/<field>`, and the transaction is rolled back. The first record that storage refuses,
  for a missing reference, a duplicate unique value or a schema conflict, is
  reported as `AXC0033`, and the transaction is rolled back. Any `AXC0033`
  stops the start like any other diagnostic, and the release stays active.
  The step logs how many seed records it inserted and updated.
- **Database roles.** Migrations, compiling, storing and
  activating use `SchemaConnectionString`. Schedule trigger state and seeding
  use `ConnectionString`. See
  [database roles](storage.md#database-roles) for the table of steps.
- **Restarts.** Restarting with unchanged folders is safe. An unchanged
  folder, with the same package versions installed, returns its stored release, and activating the active release again
  only updates its activation time. After a failure, the next start
  activates again safely because provisioning is additive. A failed activation
  with a pending migration leaves nothing applied, so the next start runs the
  migrations again.

## Studio settings

Studio edits application folders through the
[authoring API](authoring.md), as
[D38](../decisions/d38-studio-edits-application-folders-through-an-authoring-api.md)
agrees. Three server settings control it. They are built, and the authoring
API reads them. `Studio:ApplicationsPath` names the folder for new applications, see below. The third, `Studio:PublisherRole`, controls publishing, see
[D47](../decisions/d47-publishing-from-studio.md). A fourth,
`Studio:Tests`, controls the test controls, see
[D55](../decisions/d55-running-test-scenarios-from-studio.md).
The Studio page lists the resources with a search, edits them in a form
and a JSON view, and shows the diagnostics.

- **`Studio:Folders`.** An array of application folders that Studio may edit,
  such as `"Studio": { "Folders": ["../../samples/apps/purchase-requests"] }`
  or the environment variable `Studio__Folders__0=/path/to/folder`. Relative
  paths are resolved against the server content root. It is empty by default,
  so Production has no Studio. The Development settings list the purchase
  request sample. A folder entry that is empty is ignored.
- **`Studio:ApplicationsPath`**. The folder where Studio
  creates new applications, such as `artifacts/studio-apps`. The environment
  variable is `Studio__ApplicationsPath`. It has no default, and relative paths
  are resolved against the server content root. With no value, Studio cannot
  create an application. A folder that does not exist is created at start, and
  the server stops if it cannot create it. Under several replicas it sits on
  the shared Studio volume, such as `/studio/apps`, see
  [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
  E2E's journey server uses a temporary folder. `scripts/dev.sh` and Compose use
  `artifacts/studio-apps`, which git ignores, see [development](../development.md#docker-compose). Studio is on when
  `Studio:Folders` is not empty or this setting is set. See
  [creating an application](authoring.md#creating-an-application) and
  [D51](../decisions/d51-creating-applications-and-resources-from-studio.md).
- **`Studio:AuthorRole`.** A roles claim value, such as `author`. The
  environment variable is `Studio__AuthorRole`. Only a signed-in user whose
  roles claim holds the value may use Studio. It has no default, and with no
  value nobody may author. The Development settings set it to `author`.
  Application policies play no part, because an author edits them.
- **`Studio:PublisherRole`**. A roles claim value, such as
  `publisher`. The environment variable is `Studio__PublisherRole`. `publish`
  needs it on top of the author role, and `validate` needs the author role
  only. `rollback` needs it too. It has no default, and
  with no value nobody may publish or roll back. A blank value counts as no
  value. The Development settings set it to `publisher`, and one development
  user holds that role, see [development users](security.md#development-users).
  It plays no part in application
  policies, as `Studio:AuthorRole` does not.
- **`Studio:Tests`**. A boolean. The environment variable
  is `Studio__Tests`. `true` makes `GET /api/me` answer `studioTest: true` for an
  author, which shows **Run tests** in Studio. It is `false` by default. Turn it
  on together with the publisher's `Scenarios:ServerConnectionString`, because
  the server cannot see the publisher's settings. It grants no right. A `test`
  request needs the author role only, and the publisher runs it, see
  [test](authoring.md#test). The Development settings turn it on.
- **Where Studio reads and writes.** Studio's edit routes read and write only
  `*.json` files inside the listed folders. A write takes an advisory lock in
  the platform database, over `ConnectionStrings:Platform`, keyed by the
  folder. It holds across server replicas, so every replica must mount the
  folder at the same path. The [installation guide](installation.md) shows the
  Compose and Helm mounts. A write waits up to 10 seconds for the lock. A write
  that cannot take it answers `503` and writes nothing. A read and the
  diagnostics touch no database. See [authoring API](authoring.md#versions) and
  [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
  The `validate` and `publish` routes store a request in the database of the
  tenant that owns the host, over the runtime role, see
  [publish requests](storage.md#publish-requests-and-release-history).
- **Activation.** Studio's edit routes never activate a release. Under
  `scripts/dev.sh`, a saved file restarts the server, which activates the
  folder, see [Startup activation](#startup-activation). Elsewhere a folder is
  committed and deployed as today. Studio asks for an
  activation through a publish request, and the
  [publisher](administration.md#publisher) runs it. The server never activates
  in its own process.
- **Current user.** `GET /api/me` answers `studio: true` when `Studio:Folders`
  is not empty and the user holds the author role. It also answers
  `studio: true` when `Studio:ApplicationsPath` is set, and answers
  `studioCreate: true` when the user may use Studio and the setting is set.
  It also answers
  `studioPublisher: true` when Studio is on
  and the user holds both the author role and the publisher role. It
  also answers `studioPreviewHost`, the first host of the tenant's served
  preview tenant, or null when the user cannot use Studio or the tenant has none.
  It also answers `studioTest`, true when `studio` is true and
  `Studio:Tests` is true.

## Tenancy settings

See
[D42](../decisions/d42-tenant-directory-in-the-platform-database.md)
and [tenant directory](../architecture.md#tenant-directory). The server
reads `Tenancy:Source`, `ConnectionStrings:Platform`,
`ConnectionStrings:PlatformSchema` and `Platform:SecretKey`. Under both
sources the server applies the platform migrations at start and limits the
role of `ConnectionStrings:Platform` to reading the `platform` schema. Under
`directory` it then reads the directory over that role, then again every `Tenancy:RefreshInterval`,
without a restart. It ignores a `Tenants` section and logs a warning at start. If
the first read fails, the start stops with an error that names
`ConnectionStrings:Platform`. If a later read fails, it keeps serving the last
good read, logs one error for each run of failed reads and reports Degraded
readiness until a read works. Under `directory` the worker needs
`ConnectionStrings:Platform` and `Platform:SecretKey`, reads its `active` tenants
over that connection, and also ignores a `Tenants` section with a warning.
It never reads `ConnectionStrings:PlatformSchema`. If its first read fails, it
retries every `Worker:PollInterval` and is not ready until a read works. Then it
reads the directory again every `Tenancy:RefreshInterval`. It keeps the last good
read when a refresh fails, and `/health/ready` is Degraded until a refresh works.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Tenancy:Source` | `configuration` | `configuration` reads the `Tenants` section. `directory` reads the tenants from the platform database and ignores a `Tenants` section, with a warning at start. |
| `Tenancy:RefreshInterval` | `00:00:30` | How often the server and the worker read the tenant directory again. It must be positive. Only `directory` reads it, and a value that is zero, negative or unreadable stops the start of the server and of the worker, naming the setting. |
| `Platform:SecretKey` | none | The installation key for the encrypted secrets. It is 32 bytes in base64 and is needed by the server under both sources, and by the worker and `Axis.Admin` under `directory`. A missing or invalid key stops the start. The server also uses it under both sources, to encrypt the data protection keys, and a stored key that does not decrypt stops the start. |
| `ConnectionStrings:Platform` | none | A role that may only read the `platform` schema, and insert rows into `platform.data_protection_keys`. Both hosts need it under `directory`. |
| `ConnectionStrings:PlatformSchema` | none | The role that owns the `platform` schema. The server reads it only at start, for the platform migrations, under both sources. `Axis.Admin` reads it to write the tenant directory, see [administration](administration.md). The worker never reads it. |

As environment variables they are `Tenancy__Source`,
`Tenancy__RefreshInterval`, `Platform__SecretKey`,
`ConnectionStrings__Platform` and `ConnectionStrings__PlatformSchema`.

- **Secret key source.** `Platform:SecretKey` comes from the environment or a
  mounted secret file. Only in Development may a committed file hold it.
- **Default source.** Development, `scripts/dev.sh`, Docker Compose and the
  tests keep `Tenancy:Source=configuration`.
- **Where secrets live.** Under `directory`, the encrypted values are in the
  platform database, see
  [platform database](storage.md#platform-database).
- **No tenant schema connection.** Under `directory`, a tenant has no
  `SchemaConnectionString`. The server and the worker hold only the runtime
  database role of each tenant. `Axis.Admin` migrates and activates tenant
  databases, see [database roles](storage.md#database-roles).
- **Values.** `Tenancy:Source` matches exactly, in lower case. A blank value
  counts as unset. Any other value stops the start of the server and of the worker,
  naming the setting.
- **Platform schema.** The server applies the platform migrations at
  start over `ConnectionStrings:PlatformSchema`. So "no schema connection"
  under `directory` means no tenant schema connection. Under both sources the
  server refuses a missing or blank `ConnectionStrings:PlatformSchema`, a
  missing or invalid `Platform:SecretKey`, and a `ConnectionStrings:Platform`
  that names no user or the same user as `ConnectionStrings:PlatformSchema`.
  Under `configuration` the platform migrations run as the first hosted
  service, before startup activation. Right after them, the server decrypts the
  stored data protection keys, and a key that does not decrypt stops the start
  with an error that names `Platform:SecretKey`.
  A platform database that cannot be reached at start stops the server. The
  development settings, Docker Compose, `scripts/dev.sh` and the E2E server
  give the server `ConnectionStrings:PlatformSchema` on the `axis_schema` role
  and a committed development `Platform:SecretKey`. The worker gets neither.
  The server keeps its data protection keys in the platform table, encrypted
  with the key. See [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
- **`ActivateOnStartup`.** It must be empty under `directory`, see
  [startup activation](#startup-activation).

### Preview tenants

A tenant can be the preview tenant of another tenant, as
[D48](../decisions/d48-preview-tenants.md) agrees. See
[preview tenants](../architecture.md#preview-tenants) for how the hosts treat
it. Under `Tenancy:Source=configuration`, the tenant setting
`Tenants:{id}:PreviewOf` names the main tenant. As an environment variable it is
`Tenants__<id>__PreviewOf`. A blank value counts as unset. Under `directory`,
the operator sets it with
[`tenant create --preview-of`](administration.md#tenant-create).

```json
"Tenants": {
  "default": { "Hosts": [ "localhost" ], "ConnectionString": "...", "SchemaConnectionString": "...", "Oidc": { "...": "..." } },
  "default-preview": { "PreviewOf": "default", "Hosts": [ "preview.localhost" ], "ConnectionString": "...", "SchemaConnectionString": "..." }
}
```

- **Its own settings.** A preview tenant has its own `Hosts`,
  `ConnectionString`, `SchemaConnectionString` and `Bindings`, as any tenant
  does. Its database is never the main tenant's.
- **Sign-in.** A preview tenant has no `Oidc` block. It copies its main
  tenant's `Authority`, `ClientId`, `ClientSecret` and `Claims`. It does not copy
  `EndpointAudience`, so the preview accepts no machine-caller token. A
  preview tenant with its own `Oidc` block stops the start.
- **Bindings.** The bindings are empty unless the tenant sets some. A preview
  never takes its main tenant's bindings, so it never calls a real system by
  accident. See [deployment bindings](integration-resources.md#deployment-bindings).
- **Rules.** A tenant has at most one preview tenant, and a preview tenant has
  none. `PreviewOf` that names an unknown tenant or a preview tenant, and two
  tenants that name the same main tenant, stop the start. See
  [tenant configuration](../architecture.md#tenant-configuration).
- **Development.** `appsettings.Development.json` of the server and of the
  worker, Docker Compose and the E2E script configure one preview tenant,
  `default-preview`, with `PreviewOf: default`. It has its own database
  `axis_preview` and its own database roles, `axis_preview_schema` and
  `axis_preview_runtime`, see [database roles](storage.md#database-roles). It has
  no `Oidc` block and no bindings, so the purchase request sample logs `AXC0100`
  warnings on it. Its host is `preview.localhost`, and `localhost` under the E2E
  script, where `default` is on `127.0.0.1`. The worker gets only the runtime
  connection string. The development Keycloak realm accepts the preview hosts as
  redirect and sign-out addresses. An existing Compose volume has no
  `axis_preview` database and needs `docker compose down -v` once.

## Forwarded headers

Behind a reverse proxy that ends TLS, the server sees plain
HTTP. These two server settings name the proxies whose forwarded headers it
trusts. See [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).

| Setting | Default | Meaning |
| --- | --- | --- |
| `ForwardedHeaders:KnownProxies` | empty | An array of IP addresses of the proxies. |
| `ForwardedHeaders:KnownNetworks` | empty | An array of CIDR ranges of the proxies, such as `10.0.0.0/8`. |

As environment variables they are `ForwardedHeaders__KnownProxies__0` and
`ForwardedHeaders__KnownNetworks__0`. Use `__1`, `__2` and so on for more
entries.

- **The headers read.** `X-Forwarded-For`, `X-Forwarded-Proto` and
  `X-Forwarded-Host`, and only on a request that comes from a listed address or
  network.
- **No setting, no header.** With both settings empty, the default, the server
  reads no forwarded header. Loopback is not trusted either. A proxy on the
  same machine must be named. The settings also govern ASP.NET's
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED` switch. With both empty, no forwarded
  header is read even when that switch is on.
- **No client address, no header.** A request that has no client address, such
  as one on a Unix socket, reads no forwarded header, because the address
  cannot be checked against the list.
- **One hop.** The server reads one proxy, the nearest one. A client cannot
  set the scheme or the host through a chain of proxies.
- **No prefix.** The server never reads `X-Forwarded-Prefix`.
- **What uses the result.** Tenant resolution by host, the `Secure` flag of the
  session cookie and the OIDC callback and sign-out addresses use the scheme
  and host that result. See
  [sign-in and the session](security.md#sign-in-and-the-session).
- **An unreadable entry stops the start.** An entry that is not an IP address
  or a CIDR range stops the server at start and names the setting.
- **The worker reads neither setting.** It serves no forwarded request.

## Admin settings

See
[D43](../decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md)
and [administration](administration.md). `Axis.Admin` reads `Admin:ConnectionString`
for `tenant create` and `tenant restore`. `migrate`, `tenant suspend`, `tenant resume`, `tenant list` and `tenant backup` do not read it.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Admin:ConnectionString` | none | A database role with `CREATEDB` and `CREATEROLE` on the database server that holds the tenant databases. `Axis.Admin` needs it for `tenant create` and `tenant restore`. |

As an environment variable it is `Admin__ConnectionString`.

- **Not a superuser.** `Axis.Admin` refuses a superuser role and exits with
  code 2. The role only needs the right to create databases and roles.
- **Only the admin host reads it.** The server and the worker never read
  `Admin:ConnectionString`.
- **Not the directory writer.** `Axis.Admin` writes the tenant directory over
  `ConnectionStrings:PlatformSchema`, not over this setting.

## Telemetry export

The server and the worker export traces, logs and metrics with OpenTelemetry,
see [Telemetry](../architecture.md#telemetry). They read the standard
variables:

| Variable | Meaning |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | The OTLP endpoint for all signals. With no endpoint, nothing is exported |
| `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` | The endpoint for traces only |
| `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` | The endpoint for metrics only |
| `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` | The endpoint for logs only |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | The OTLP protocol |
| `OTEL_EXPORTER_OTLP_HEADERS` | Headers sent with every export |
| `OTEL_SERVICE_NAME` | The service name. It defaults to `axis-server` or `axis-worker` |
| `OTEL_RESOURCE_ATTRIBUTES` | More resource attributes |

With no endpoint, nothing is exported and logs still go to the console. The
headers may carry a collector token, so they come from the environment and
never from a committed file. Docker Compose and `scripts/dev.sh` set no
endpoint and start no collector.
