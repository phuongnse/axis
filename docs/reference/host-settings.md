# Host settings

This page lists the settings of the server and of `Axis.Admin`, with their defaults, their environment
variables and what a wrong value does: startup activation, Studio, the operator console, the users
page, tenancy and preview tenants, data limits, forwarded headers, the admin host, rate limits, AI,
telemetry export and cells. The worker's own settings are in
[worker settings](process-execution.md#worker-settings), and the commands of `Axis.Admin` are in
[administration](administration.md).

## Startup activation

The server can compile and activate application folders when it starts, so a development or E2E
server serves a real application without a separate step. Production leaves it off, and a release
goes live through [`Axis.Admin activate`](administration.md#activate) or the
[publisher](administration.md#publisher).

- **Setting.** `ActivateOnStartup` is an array of application folders, such as
  `"ActivateOnStartup": ["../../samples/apps/purchase-requests"]` or the environment variable
  `ActivateOnStartup__0=/path/to/folder`. Relative paths are resolved against the server content
  root. When the setting is absent or empty, the default, the step does not run.
- **Order.** Tenants are processed in ordinal order of their id. For each tenant, the step applies the
  configuration, data and processes migrations to the tenant database, then compiles each listed
  folder against that database and activates the release, in the listed order. The same folders
  apply to every tenant.
- **Tenant source.** `ActivateOnStartup` is for `Tenancy:Source=configuration` only. Under
  `directory` a non-empty setting stops the start with a message that names `ActivateOnStartup`.
  There, releases are activated with `Axis.Admin activate`, and the server runs no tenant
  migrations either, see [tenancy settings](#tenancy-settings).
- **Packages and modules.** Each folder compiles against the extension packages installed under
  `Extensions:Path` and the modules installed under `Modules:Path`, see
  [extension packages](extension-packages.md#declaring-packages-in-an-application) and
  [modules](modules-and-templates.md#modules). When a newer version in a range was installed since
  the last start, the folder compiles to a new release pinned to it, and that release is activated.
  A release that pins a module also holds the module's files.
- **Release history.** Each activation adds a row with the actor `startup` to the
  [release history](storage.md#release-history), unless the release is already the active one, so a
  restart that finds the same release adds nothing.
- **Before listening.** The step runs before any hosted service starts, so the server accepts no
  request until every tenant is done.
- **Failures.** Any compile or activation diagnostic that is an error, or any exception, stops the
  start and the process exits. Each diagnostic is logged with its code, file and path, together with
  the folder and tenant, and so is an error after provisioning committed. A warning, such as a
  missing binding on a [preview tenant](#preview-tenants), is logged and does not stop the start. The
  previously active release stays active, and the first failing tenant stops the whole start, so no
  server runs with some tenants on old releases and others on new ones.
- **Recompute limit.** `Activation:InlineRecomputeLimit`, or `Activation__InlineRecomputeLimit`, is
  the most affected rows of one entity that activation recomputes in its own transaction when a
  release changes stored computed fields, or when a migration `fill` or `convert` changes the rows
  they read. It defaults to 10,000. Above the limit, the entity becomes pending and the worker
  recomputes the rows in batches, see
  [recomputing computed fields](migrations.md#recomputing-computed-fields). Above the limit,
  activation also leaves a new non-unique index on a table with more rows to a concurrent build.
- **Indexes.** After a folder is activated and seeded in a tenant, the step builds the non-unique
  indexes that activation left out of large tables, over the tenant's schema connection, see
  [schema planning](storage.md#schema-planning). It runs before the server listens, so readiness
  turns healthy only after the build. A failed build stops the start, and the next start builds the
  index again.
- **Database roles.** Migrations, compiling, storing and activating use `SchemaConnectionString`.
  Schedule trigger state and seeding use `ConnectionString`. See
  [database roles](storage.md#database-roles) for the table of steps.
- **Restarts.** Restarting with unchanged folders is safe. An unchanged folder, with the same package
  versions installed, returns its stored release, and activating the active release again only
  updates its activation time. After a failure, the next start activates again safely because
  provisioning is additive. A failed activation with a pending migration leaves nothing applied, so
  the next start runs the migrations again.

### Seeding

After a folder is activated in a tenant, the step writes the records of the application's seed
files, see [resource file shape](resource-files.md#resource-file-shape). A restart never duplicates
a seed record.

- **Inserts.** The step inserts every seed record whose id the entity table does not hold yet. Only
  the id decides, so a seed record that was deleted is inserted again on the next start. A record
  that leaves out a field with a `default` gets the default, with `now()` the start time of the
  transaction, and a required field with a default may be left out. An inserted record gets the
  sequence's next number, like any other create.
- **Existing records without `sync`.** They are left alone, including records edited through the UI.
- **Existing records with `sync`.** A record whose declared values differ from the stored ones is
  updated by the record update command, and its version grows by one. PostgreSQL compares the typed
  values, so `1.5` equals a stored `1.50`. A record that is identical is not written, and its
  version stays the same. Only declared values are compared, so a computed value alone never causes
  a write. Fields the seed does not declare keep their stored values, a `null` value clears the
  stored field, and a record removed from the seed file stays in the table. A synced seed still
  needs every required field that has no default, its update applies no default, and it keeps the
  record's sequence number.
- **Sensitive values.** A sensitive value is compared after decrypting it, since each encryption
  differs. A seed with a sensitive value and no data key for the tenant stops the start and writes
  nothing.
- **Parsing.** Each record goes through the record input parser as a create body, so the rules of
  the record API apply. Every record is parsed before any write. An invalid value is `AXC0033` at
  `/records/{i}/values/<field>`, and so is a value for a
  [computed field](resource-files.md#entity-logic) or a [sequence](resource-files.md#sequences)
  field. The entity's [validations](resource-files.md#entity-logic) do not run on seed records.
- **One transaction.** All inserts and updates of the folder run in one transaction, seed files in
  path order and records in file order, so a reference value must name an existing record or one
  inserted earlier in the same run. Each write computes the record's computed fields, an update from
  the stored record with the declared values on top.
- **Refusals.** Each of these is `AXC0033` and rolls the transaction back: a value that cannot be
  computed, at `/records/{i}/values/<field>`; the first record that storage refuses, for a missing
  reference, a duplicate unique value or a schema conflict; and a write that the lifecycle of a
  [module contract](module-contracts.md#where-the-contract-is-checked) refuses, such as a synced seed
  that changes a stored state along no transition or along one whose guard is not true, at the state
  field, with a message that names the point that refused it. Any `AXC0033` stops the start like any
  other diagnostic, and the release stays active.
- **Log.** The step logs how many seed records it inserted and updated.

## Studio settings

These server settings turn Studio on and say who may author, publish and run tests. What each one
allows in the authoring API, and the Studio fields of `GET /api/me` that follow from them, are in
[authoring API settings](authoring.md#settings).

| Setting                   | Environment variable                                 | Default | Meaning                                                                                                           |
| ------------------------- | ---------------------------------------------------- | ------- | ----------------------------------------------------------------------------------------------------------------- |
| `Studio:Folders`          | `Studio__Folders__0`, `Studio__Folders__1` and so on | empty   | The application folders that Studio may edit. An empty entry is ignored                                           |
| `Studio:ApplicationsPath` | `Studio__ApplicationsPath`                           | none    | The folder where Studio creates new applications. With no value, Studio cannot create one                         |
| `Studio:AuthorRole`       | `Studio__AuthorRole`                                 | none    | The roles claim value, such as `author`, of a signed-in user who may use Studio                                   |
| `Studio:PublisherRole`    | `Studio__PublisherRole`                              | none    | The roles claim value, such as `publisher`, that may also publish and activate a release, and turn on auto-accept |
| `Studio:Tests`            | `Studio__Tests`                                      | `false` | Shows the test controls in Studio to its users                                                                    |

- **When Studio is on.** Studio is on when `Studio:Folders` is not empty or `Studio:ApplicationsPath`
  is set, and only for a user whose roles claim holds `Studio:AuthorRole`. So with the defaults,
  which Production keeps, there is no Studio. A blank role counts as no value, and with no author role
  nobody may author, and with no publisher role nobody may publish or activate a release.
  Application policies play no part in either role, because an author edits them.
- **Paths.** Relative paths in `Studio:Folders` and `Studio:ApplicationsPath` are resolved against the
  server content root. A `Studio:ApplicationsPath` folder that does not exist is created at start,
  and the server stops if it cannot create it. Under several replicas it sits on the shared Studio
  volume, such as `/studio/apps`, and every replica mounts the folders at the same path, because the
  [write lock](authoring.md#versions) is keyed by the full path. The
  [installation guide](installation.md) shows the Compose and Helm mounts.
- **Revisions.** `Studio:RevisionsPath` (`Studio__RevisionsPath`) is the folder that holds the git
  repository of each application folder, outside it, see [revisions](revisions-and-drafts.md#the-repository).
  It defaults to `.revisions` under `Studio:ApplicationsPath`, and a relative value is resolved against
  the server content root. With neither path set, the server keeps no revisions. In a cell it is
  `<Studio:ApplicationsPath>/<tenant id>/.revisions`, and a value stops the start, and the message
  names `Studio:RevisionsPath` and `Cell:Id`.
- **Tests.** `Studio:Tests` grants no right: a `test` request needs the author role only, and the
  publisher runs it, see [test](authoring.md#test). Turn it on together with the publisher's
  `Scenarios:ServerConnectionString`, because the server cannot see the publisher's settings.
- **Development.** The Development settings list every sample under `samples/apps/` in
  `Studio:Folders`, set the author role to `author` and the publisher role to `publisher`, and turn
  `Studio:Tests` on. One development user holds the publisher role, see
  [development users](security.md#development-users). `scripts/dev.sh` and Compose set
  `Studio:ApplicationsPath` to `artifacts/studio-apps`, which git ignores, see
  [development](../development.md#docker-compose), and E2E's journey server uses a temporary folder.
- **In a cell.** When `Cell:Id` is set, each tenant's applications are under
  `<Studio:ApplicationsPath>/<tenant id>/`, and Studio on a tenant's host reads, writes and creates
  only there. Studio creates the folder the first time it uses it. Another tenant's application, by
  name, resource id or path, answers `404`. A non-empty `Studio:Folders` stops the start, and the
  message names `Studio:Folders` and `Cell:Id`, because every tenant would reach those folders. The
  publisher reads `Studio:ApplicationsPath` for the [create job](administration.md#create-job), which
  copies a template into the tenant's folder, the [export job](administration.md#export-job), which
  writes the folder into the tenant's [export](tenant-lifecycle.md#exporting-a-tenant), where an
  unset setting means no entries, and the [purge job](administration.md#purge-job), whose `studio`
  step deletes it when the tenant is [deleted](tenant-lifecycle.md#deleting-a-tenant). The folder
  also joins the tenant's [backup](tenant-backup-and-restore.md#backups-in-a-cell), and a restore
  writes it under the new tenant.

## Console settings

One server setting decides who may use the [operator console](operator-console.md).

- **`Console:OperatorRole`.** A roles claim value, such as `platform-operator`, as the environment
  variable `Console__OperatorRole`. Only a signed-in user whose roles claim holds the value may use
  the console. It has no default, and with no value nobody may. The Development settings set it to
  `platform-operator`, a role apart from the `operator` role of the purchase request sample.
  Application policies play no part. `GET /api/me` answers `console: true` for a user who holds it,
  and `console: false` for everyone when no value is set.

## Users settings

One server setting decides who may use the [users page](user-administration.md).

- **`Users:AdministratorRole`.** A roles claim value, such as `tenant-administrator`, as the
  environment variable `Users__AdministratorRole`. Only a signed-in user who holds the value, from
  the [platform sign-in](identity-provider.md#platform-sign-in) or given on the users page, may use
  the users page. It has no default, and with no value nobody may. The Development settings set it to
  `tenant-administrator`. Application policies play no part. `GET /api/me` answers `users: true` for
  a user who holds it, `users: false` for everyone when no value is set, and `users: false` on a
  preview tenant, which has no users page.

## Tenancy settings

These settings say where the hosts find their tenants: in the `Tenants` section of their own
settings, or in the [tenant directory](../architecture.md#tenant-directory) of the platform
database.

| Setting                            | Default         | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| ---------------------------------- | --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Tenancy:Source`                   | `configuration` | `configuration` reads the `Tenants` section. `directory` reads the tenants from the platform database and ignores a `Tenants` section, with a warning at start.                                                                                                                                                                                                                                                                                                                                                                                       |
| `Tenancy:RefreshInterval`          | `00:00:30`      | How often the server and the worker read the tenant directory again. It must be positive. Only `directory` reads it, and a value that is zero, negative or unreadable stops the start of the server and of the worker, naming the setting.                                                                                                                                                                                                                                                                                                       |
| `Platform:SecretKey`               | none            | The installation key for the encrypted secrets. It is 32 bytes in base64 and is needed by the server under both sources, and by the worker and `Axis.Admin` under `directory`. A missing or invalid key stops the start. The server also uses it under both sources, to encrypt the data protection keys, and a stored key that does not decrypt stops the start.                                                                                                                                                                                  |
| `Platform:PreviousSecretKey`       | none            | The old installation key during a rotation, in the same form as `Platform:SecretKey`. Optional. The server, the worker and `Axis.Admin` read it. A host decrypts with it when `Platform:SecretKey` does not match, and never encrypts with it. A blank value counts as not set. A malformed value stops the start, naming the setting and never its value. `platform rekey` needs it. See [installation key](tenant-setting-commands.md#installation-key).                                                                                           |
| `ConnectionStrings:Platform`       | none            | A role that reads the `platform` schema and writes only these: rows it inserts into `platform.data_protection_keys` and `platform.jobs`, the rows of `platform.tenant_usage` and `platform.tenant_usage_latest`, the rows of `platform.tenant_leases`, and rows it inserts and updates in `platform.tenant_daily_counters`. Both hosts need it under `directory`. Outside Development and Testing the server, and the worker under `directory`, refuse a value with `SSL Mode` below `VerifyFull` or none, see [TLS](installation.md#tls). |
| `ConnectionStrings:PlatformSchema` | none            | The role that owns the `platform` schema. The server reads it at start for the platform migrations, under both sources, and for each `PUT /api/cell/tenants/{id}/quotas`, which opens an unpooled connection for that write alone. `Axis.Admin` reads it to write the tenant directory, see [administration](administration.md). The worker never reads it. Outside Development and Testing the server refuses a value with `SSL Mode` below `VerifyFull` or none, see [TLS](installation.md#tls).                                                   |

As environment variables they are `Tenancy__Source`, `Tenancy__RefreshInterval`,
`Platform__SecretKey`, `Platform__PreviousSecretKey`, `ConnectionStrings__Platform` and
`ConnectionStrings__PlatformSchema`.

- **Values.** `Tenancy:Source` matches exactly, in lower case. A blank value counts as unset. Any
  other value stops the start of the server and of the worker, naming the setting.
- **Default source.** Development, `scripts/dev.sh`, Docker Compose and the tests keep
  `Tenancy:Source=configuration`.
- **The server under both sources.** The server applies the platform migrations at start over
  `ConnectionStrings:PlatformSchema` and limits the role of `ConnectionStrings:Platform` to reading
  the `platform` schema. It refuses a missing or blank `ConnectionStrings:PlatformSchema`, a missing
  or invalid `Platform:SecretKey`, and a `ConnectionStrings:Platform` that names no user or the same
  user as `ConnectionStrings:PlatformSchema`. A platform database that cannot be reached at start
  stops it. Under `configuration` the platform migrations run as the first hosted service, before
  startup activation.
- **Data protection keys.** The server keeps its data protection keys in the platform table,
  encrypted with `Platform:SecretKey`, so that every replica reads the same keys and sessions survive
  a restart. Right after the platform migrations, the server decrypts the stored keys, and a key that
  does not decrypt stops the start with an error that names `Platform:SecretKey`.
- **The server under `directory`.** After the platform migrations, the server reads the directory
  over `ConnectionStrings:Platform`, then again every `Tenancy:RefreshInterval`, without a restart.
  It ignores a `Tenants` section and logs a warning at start. If the first read fails, the start
  stops with an error that names `ConnectionStrings:Platform`. If a later read fails, it keeps
  serving the last good read, logs one error for each run of failed reads and reports Degraded
  readiness until a read works.
- **The worker under `directory`.** The worker needs `ConnectionStrings:Platform` and
  `Platform:SecretKey`, reads its `active` tenants over that connection, and also ignores a `Tenants`
  section with a warning. It never reads `ConnectionStrings:PlatformSchema`. If its first read fails,
  it retries every `Worker:PollInterval` and is not ready until a read works. Then it reads the
  directory again every `Tenancy:RefreshInterval`, keeps the last good read when a refresh fails, and
  reports `/health/ready` Degraded until a refresh works.
- **No tenant schema connection.** Under `directory`, a tenant has no `SchemaConnectionString`. The
  server and the worker hold only the runtime database role of each tenant, and `Axis.Admin` migrates
  and activates tenant databases, see [database roles](storage.md#database-roles). The platform
  schema connection of the server is not a tenant schema connection. `ActivateOnStartup` must be
  empty under `directory`, see [startup activation](#startup-activation).
- **Secrets.** `Platform:SecretKey` comes from the environment or a mounted secret file. Only in
  Development may a committed file hold it. The development settings, Docker Compose,
  `scripts/dev.sh` and the E2E server give the server `ConnectionStrings:PlatformSchema` on the
  `axis_schema` role and a committed development `Platform:SecretKey`, and the worker gets neither.
  Under `directory`, the encrypted values are in the platform database, see
  [platform database](storage.md#platform-database).
- **Site hosts.** Under `configuration`, `Tenants:{id}:SiteHosts:{host}` gives one of the tenant's
  `Hosts` to the site with the path it holds, as the environment variable
  `Tenants__<id>__SiteHosts__<host>`. That host then serves only the site, see
  [sites and sign-in](identity-provider.md#sites-and-sign-in). A host that is not one of the tenant's
  `Hosts`, a path that is not a valid site path or is reserved, and two hosts for one site stop the
  start. Under `directory`, `tenant host add --site` gives it. The E2E server uses this for its site
  host.
- **Legal links.** Under `configuration`, `Tenants:{id}:Legal:PrivacyUrl`,
  `Tenants:{id}:Legal:TermsUrl` and `Tenants:{id}:Legal:TermsVersion` give the tenant's
  [legal links](legal.md#legal-settings), and `Tenants:{id}:Legal:Sites:{path}:PrivacyUrl` and
  `...:TermsUrl` a site's own. Under `directory` the
  [legal links commands](tenant-setting-commands.md#legal-links) set them, and the same checks apply.
  An address that is not an `https` address, a terms address without a version, a version without a
  terms address, a `TermsVersion` on a site, and a site path that is not valid or is reserved stop
  the start, and the message names the setting. A preview tenant takes its main tenant's block, and a
  `Legal` block of its own stops the start.
- **Tenant keys.** Under `configuration`, `Tenants:{id}:DataKeys:{number}` and
  `Tenants:{id}:LookupKeys:{number}` give a tenant's data keys and lookup keys, each 32 bytes in
  base64, as the environment variables `Tenants__<id>__DataKeys__<number>` and
  `Tenants__<id>__LookupKeys__<number>`. A number is an integer of at least 1. A value that is not 32
  bytes in base64, or a bad number, makes the host fail when it first reads that tenant's keys, with a
  message that names the setting and never the value. The development settings, `scripts/dev.sh`,
  Docker Compose and the E2E settings set them for the sample tenants. Under `directory` the keys
  come from `platform.tenant_data_keys`, see [tenant keys](data-encryption.md#tenant-keys).

### Preview tenants

A tenant can be the preview tenant of another tenant, where authors try a release before it goes
live. See [preview tenants](../architecture.md#preview-tenants) for how the hosts treat it. Under
`Tenancy:Source=configuration`, the tenant setting `Tenants:{id}:PreviewOf` names the main tenant, as
the environment variable `Tenants__<id>__PreviewOf`. A blank value counts as unset. Under
`directory`, the operator sets it with [`tenant create --preview-of`](administration.md#tenant-create).

```json
"Tenants": {
  "default": { "Hosts": [ "localhost" ], "ConnectionString": "...", "SchemaConnectionString": "...", "Oidc": { "...": "..." } },
  "default-preview": { "PreviewOf": "default", "Hosts": [ "preview.localhost" ], "ConnectionString": "...", "SchemaConnectionString": "..." }
}
```

- **Its own settings.** A preview tenant has its own `Hosts`, `ConnectionString`,
  `SchemaConnectionString` and `Bindings`, as any tenant does. Its database is never the main
  tenant's.
- **Sign-in.** A preview tenant has no `Oidc` block. It copies its main tenant's `Authority`,
  `ClientId`, `ClientSecret`, `Profile`, `ClientKey`, `ClientKeyId` and `Claims`. It does not copy
  `EndpointAudience`, `ServiceAudience` or `UsersClientSecret`, so the preview accepts no
  machine-caller or service account token and lists no realm users. A preview tenant with its own
  `Oidc` block stops the start.
- **Bindings.** The bindings are empty unless the tenant sets some. A preview never takes its main
  tenant's bindings, so it never calls a real system by accident. See
  [deployment bindings](integration-resources.md#deployment-bindings).
- **Rules.** A tenant has at most one preview tenant, and a preview tenant has none. `PreviewOf` that
  names an unknown tenant or a preview tenant, and two tenants that name the same main tenant, stop
  the start. See [tenant configuration](../architecture.md#tenant-configuration).
- **Development.** `appsettings.Development.json` of the server and of the worker, Docker Compose
  and the E2E script configure one preview tenant, `default-preview`, with `PreviewOf: default`. It
  has its own database `axis_preview` and its own database roles, `axis_preview_schema` and
  `axis_preview_runtime`, see [database roles](storage.md#database-roles). It has no `Oidc` block and
  no bindings, so the purchase request sample logs `AXC0100` warnings on it. Its host is
  `preview.localhost`, and `localhost` under the E2E script, where `default` is on `127.0.0.1`. The
  worker gets only the runtime connection string. The development Keycloak realm accepts the preview
  hosts as redirect and sign-out addresses. An existing Compose volume has no `axis_preview` database
  and needs `docker compose down -v` once.

### Tenant realm

A tenant's catalog entry can carry what activation needs to keep the tenant's realm in step: the
realm name, the identity provider's address, the secret of the `axis-activation` client and the
secret of the platform client `axis`, from which the site client secrets derive. The server's startup
activation and `Axis.Admin` activation use it, see
[keeping the realm in step](identity-provider.md#keeping-the-realm-in-step).

- **Under `configuration`.** The tenant settings `Tenants:{id}:Realm:Name` and
  `Tenants:{id}:Realm:ActivationClientSecret` give it, as the environment variables
  `Tenants__<id>__Realm__Name` and `Tenants__<id>__Realm__ActivationClientSecret`. A blank
  `ActivationClientSecret` means the tenant has no realm details, and then `Name` and the authority
  are not checked.
- **Under `directory`.** `tenant create` stores the secret, and the realm name is the tenant id. A
  tenant whose stored authority does not end in `realms/<id>` has no realm details, because it signs
  in with an outside identity provider.
- **Authority.** With a secret, the tenant needs a `Name`, and `Tenants:{id}:Oidc:Authority` must be
  an http or https address that ends in `/realms/<Name>`. The identity provider's address is the
  authority without that ending. A message names the setting and never shows a value.
- **Platform client secret.** Under `configuration` it is `Tenants:{id}:Oidc:ClientSecret`, and under
  `directory` it is the stored platform secret. It is optional, and a tenant without it keeps the
  site client secrets that Keycloak makes.
- **Users client.** The entry also carries what the server needs to list the realm's users: the
  secret of the `axis-users` client, with the same address and realm name. Under `configuration`,
  `Tenants:{id}:Oidc:UsersClientSecret` gives it, as the environment variable
  `Tenants__<id>__Oidc__UsersClientSecret`, and a blank value means the tenant has no users client.
  The realm name and the identity provider's address both come from `Tenants:{id}:Oidc:Authority`,
  which must be an http or https address that ends in `/realms/<name>`. A secret with any other
  authority stops the start, and the message never shows the secret. Under `directory` the server
  decrypts the stored `realm_users_client_secret`, and the realm name is the tenant id. A tenant with
  no users client has no users page, see [the users page](user-administration.md#the-users-page).
- **Preview tenants.** A preview tenant has no realm details of its own, and a `Realm` block on one
  stops the start. It has no users client either, so it does not copy `UsersClientSecret` from its
  main tenant.

## Data settings

These settings bound how long a statement may run and how much one import, export or upload may
hold, and size each tenant's connection pool.

The server reads `Data:QueryTimeout`, `Data:ExportQueryTimeout`, `Data:WriteTimeout`,
`Data:ExportMaxRows`, `Data:ImportMaxBytes` and `Files:MaxSize`. The worker reads
`Data:ImportMaxRows` and, for the statements of its work items,
[`Worker:StatementTimeout`](process-execution.md#worker-settings). The server reads
`Data:ActivationTimeout` when startup activation is on, and so do `admin activate` and the
publisher. Both the server and the worker read `Data:TenantPoolMaxSize`, `Data:TenantPoolMinSize`
and `Data:TenantPoolIdleLifetime`.

| Setting                       | Default    | Meaning                                                                                                                                                                                                                                                                                  |
| ----------------------------- | ---------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Data:QueryTimeout`           | `00:00:10` | The `statement_timeout` of every interactive read: record lists, single reads and histories, data source rows, lookups and page reads. A read past it is a `503`.                                                                                                                       |
| `Data:ExportQueryTimeout`     | `00:01:00` | The `statement_timeout` of an export's query. An export past it is a `503` and sends no file.                                                                                                                                                                                          |
| `Data:WriteTimeout`           | `00:00:15` | The `statement_timeout` of the write transaction of record create, update and delete, process start, task complete and an inbound endpoint call. A write past it rolls back, writes nothing and is a `503`.                                                                             |
| `Data:ActivationTimeout`      | `00:30:00` | The `statement_timeout` of each statement of the activation transaction (schema changes, migrations and the active release write) at startup, in `admin activate` and in the publisher, for a publish and for the activation of a stored release. It also bounds a wait for a lock. Past it, the activation rolls back and the previous release stays active. It limits each statement, not the whole activation. |
| `Data:ExportMaxRows`          | `100000`   | The most data rows one export file holds, an integer from 1 to 2147483646. The file stops there and the response carries `Axis-Export-Truncated: true`.                                                                                                                                |
| `Data:ImportMaxRows`          | `10000`    | The most data rows one import holds. A larger file fails its dry run. The worker reads it, and a value that is not an integer of at least 1 stops its start.                                                                                                                            |
| `Data:ImportMaxBytes`         | `20971520` | The largest import file, 20 MB. A larger upload is a `413`. The server reads it, and a value that is not a whole number of at least 1 stops its start.                                                                                                                                  |
| `Files:MaxSize`               | `26214400` | The largest file one upload stores, 25 MB. An upload is refused with `413` above the smaller of this and the field's `maxSize`. Publish checks a field's `maxSize` against the 25 MB default, not against this setting. The server reads it, and a value that is not a whole number of at least 1 stops its start. |
| `Data:TenantPoolMaxSize`      | `5`        | The most connections in the runtime role's pool of each tenant. It wins over `Maximum Pool Size` in the tenant's connection string. A value that is not an integer of at least 1 stops the start, and the error names the setting.                                                      |
| `Data:TenantPoolMinSize`      | `0`        | The connections each tenant's pool keeps open when idle. It wins over `Minimum Pool Size` in the tenant's connection string. A value that is not an integer from 0 up to `Data:TenantPoolMaxSize` stops the start, and the error names the setting.                                     |
| `Data:TenantPoolIdleLifetime` | `00:01:00` | How long an idle connection of a tenant's pool stays open before it closes, a duration of whole seconds of at least one second. It wins over `Connection Idle Lifetime` in the tenant's connection string. A shorter value lowers the pruning interval to match. Any other value stops the start, and the error names the setting. |

As environment variables they are `Data__QueryTimeout`, `Files__MaxSize` and so on.

- **Tenant pools.** The three pool settings apply to the runtime role's pools in the server and the
  worker. `Max Auto Prepare` is always 0 on these pools, as transaction-mode PgBouncer needs, see
  [connections](cells.md#connections).
- **Per transaction.** Each timeout is set with `SET LOCAL`, so it never outlives its query's
  transaction and never reaches a pooled connection's next user. An interactive read runs in one
  short read-only transaction that is always rolled back. The worker sets `Worker:StatementTimeout`
  the same way in every transaction of a work item, including each transaction of a handler that
  calls an outside system. A statement such a handler runs outside a transaction has no limit.
- **Page reads.** These are the page metadata route `GET /api/sites/{path}/pages/{page}` and the
  record and data source routes that a page's widgets call. Lookups use the record list route. Form
  state, task, process inspection and site list reads have no timeout.
- **Writes.** Only the six routes of `Data:WriteTimeout` have it. The limit is the first statement of
  their write transaction, before the start lock, so it also bounds a wait behind an activation.
  Activation, imports and Studio keep their own limits, and the worker's statements run under
  [`Worker:StatementTimeout`](process-execution.md#worker-settings).
- **A bad value.** A missing value takes its default. A timeout that is not a duration of at least
  one millisecond, and a `Files:MaxSize` that is not a whole number of bytes of at least 1, stop the
  start. `admin activate` and the publisher exit with a settings error for a bad
  `Data:ActivationTimeout`, before any tenant step runs.
- **Safety net.** Each default is about 30 times the p95 objective of its group in
  [objectives](performance.md#objectives): 300 ms gives 10 s for reads, and 500 ms gives 15 s for
  writes. A timeout stops only a runaway statement and is no budget.

## Forwarded headers

Behind a reverse proxy that ends TLS, the server sees plain HTTP. These two server settings name the
proxies whose forwarded headers it trusts, so that it learns the scheme and host the client used.

| Setting                          | Default | Meaning                                                         |
| -------------------------------- | ------- | --------------------------------------------------------------- |
| `ForwardedHeaders:KnownProxies`  | empty   | An array of IP addresses of the proxies.                        |
| `ForwardedHeaders:KnownNetworks` | empty   | An array of CIDR ranges of the proxies, such as `10.0.0.0/8`.   |

As environment variables they are `ForwardedHeaders__KnownProxies__0` and
`ForwardedHeaders__KnownNetworks__0`, with `__1`, `__2` and so on for more entries.

- **The headers read.** `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`, and only on a
  request that comes from a listed address or network.
- **No setting, no header.** With both settings empty, the default, the server reads no forwarded
  header. Loopback is not trusted either, so a proxy on the same machine must be named. The settings
  also govern ASP.NET's `ASPNETCORE_FORWARDEDHEADERS_ENABLED` switch: with both empty, no forwarded
  header is read even when that switch is on.
- **No client address, no header.** A request that has no client address, such as one on a Unix
  socket, reads no forwarded header, because the address cannot be checked against the list.
- **One hop.** The server reads one proxy, the nearest one, so a client cannot set the scheme or the
  host through a chain of proxies.
- **No prefix.** The server never reads `X-Forwarded-Prefix`.
- **What uses the result.** Tenant resolution by host, the `Secure` flag of the session cookie and
  the OIDC callback and sign-out addresses use the scheme and host that result, see
  [sign-in and the session](security.md#sign-in-and-the-session).
- **An unreadable entry stops the start.** An entry that is not an IP address or a CIDR range stops
  the server at start and names the setting.
- **The worker reads neither setting,** because it serves no forwarded request.

## Admin settings

`Axis.Admin` and the publisher, one of its commands, have settings that the server and the worker
never read. They are listed with the commands that read them in
[administration settings](administration.md#settings).

- **Only the admin host reads `Admin:ConnectionString`.** The server and the worker never read it,
  because only `Axis.Admin` creates databases and roles.
- **The identity provider's admin client.** `IdentityProvider:Url`,
  `IdentityProvider:AdminClientId` and `IdentityProvider:AdminClientSecret` are read by
  `tenant create` of a main tenant and by the publisher in a cell, and never by a preview tenant's
  create. The server reads only `IdentityProvider:Url`, in a cell, see [cell settings](#cell-settings).

## Rate limit settings

These server settings cap how many requests one caller makes, each as a number of requests per
minute for one replica. The rules of the limits are in
[rate limits](headers-and-rate-limits.md#rate-limits).

| Setting                 | Default | Meaning                                                                  |
| ----------------------- | ------- | ------------------------------------------------------------------------ |
| `RateLimits:SignIn`     | 300     | Sign-in and its callback, by client address                              |
| `RateLimits:Session`    | 1200    | Every other route that takes a session, by tenant and user               |
| `RateLimits:Bearer`     | 1200    | Requests with a bearer token, by tenant and caller id                    |
| `RateLimits:Publishing` | 60      | Studio validate, publish, preview, activate and test, by tenant and user |

- **`RateLimits:Mcp`** _(planned for M35)_. MCP tool calls, by tenant and client, 600 by default.
- **Environment variables.** `RateLimits__SignIn`, `RateLimits__Session`, `RateLimits__Bearer` and
  `RateLimits__Publishing`.
- **Per replica.** Each replica counts on its own, so the limit an installation sees is the number
  of replicas times the setting.
- **Bad values.** A value of zero or less, or one that is not a whole number, stops the server's
  start and names the setting.
- **Defaults.** The weekly [load test](performance.md#load-test) never reaches the defaults, and it
  can tune them.

## AI settings

The agent host reads the `Ai` settings: the model bindings and the default and review bindings. Their
table is in [model bindings](model-bindings.md#bindings).

**Tenant keys and the budgets of a request** _(planned for M35)_ are settings of the same section and
of the same table.

## Telemetry export

The server and the worker export traces, logs and metrics with OpenTelemetry, see
[telemetry](../architecture.md#telemetry). They read the standard variables:

| Variable                              | Meaning                                                                                                                         |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `OTEL_EXPORTER_OTLP_ENDPOINT`         | The OTLP endpoint for all signals. With no endpoint, nothing is exported                                                        |
| `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`  | The endpoint for traces only                                                                                                    |
| `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` | The endpoint for metrics only                                                                                                   |
| `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT`    | The endpoint for logs only                                                                                                      |
| `OTEL_EXPORTER_OTLP_PROTOCOL`         | The OTLP protocol                                                                                                               |
| `OTEL_EXPORTER_OTLP_HEADERS`          | Headers sent with every export                                                                                                  |
| `OTEL_SERVICE_NAME`                   | The service name. It defaults to `axis-server` or `axis-worker`, and to `axis-admin` for `cell backup` and `cell restore-check` |
| `OTEL_RESOURCE_ATTRIBUTES`            | More resource attributes                                                                                                        |

With no endpoint, nothing is exported and logs still go to the console. The headers may carry a
collector token, so they come from the environment and never from a committed file. Docker Compose
and `scripts/dev.sh` set no endpoint and start no collector. Outside Development every endpoint
variable that is set must be an absolute `https` URL, or the server and the worker stop at start and
the error names the variable, never its value. Development accepts `http`.

The admin tool's `cell backup` and `cell restore-check` also read these variables, from their own
settings, and export the gauges `axis.backup.last_success` and `axis.backup.restore_check` as
metrics only, see [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell). Outside
Development they apply the same `https` rule and exit with code `2`, naming the variable, before they
upload or change anything.

## Cell settings

These settings make a host part of a [cell](cells.md), an installation that many tenants of Axis
SaaS share. With no `Cell:Id`, the `Cell` and `Quotas` settings do not apply and the host is a
self-hosted installation. The `Worker` settings at the end of the table apply to every installation,
because the worker shares its time the same way everywhere.

- **Checked at start.** The server, the worker and `Axis.Admin`, the publisher included, read and
  check the `Cell` settings at start. With `Cell:Id` set they need `Tenancy:Source=directory`,
  `Cell:Region` and `Cell:TenantDomain`. The id is the region, a hyphen and a number, such as
  `vn-1`, and the region uses lowercase letters and digits. A wrong value stops the start and the
  message names the setting, and `Axis.Admin` refuses every command with exit code 2.
- **Runtime database address.** Only the server and the worker read and check
  `Cell:RuntimeDatabaseHost` and `Cell:RuntimeDatabasePort`, and only with `Cell:Id` set. A port that
  is not a whole number from 1 to 65535, or a port with no host, stops their start and the message
  names the setting. `Axis.Admin` and the publisher never read them, so a wrong value does not stop
  them.
- **Quota defaults.** In a cell the server, the worker and the publisher also read and check
  `Quotas:Defaults`. Each child must be a [quota](tenant-quotas.md#quotas) key and its value a whole
  number of zero or more. An unknown key or a wrong value stops the start, and the message names the
  setting, never its value. A quota with no setting takes the default of the quotas table, and
  `authoringRequests` has none, which is no limit. The publisher uses them for the limit of an export
  that a tenant has no row for, so give it the same settings as the server and the worker. The other
  `Axis.Admin` commands do not read `Quotas:Defaults`.

| Setting                              | Read by           | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ------------------------------------ | ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Cell:Id`                            | All hosts         | The cell id, such as `vn-1`. Setting it turns on quotas, per-tenant Studio folders and the cell console.                                                                                                                                                                                                                                                                                                                      |
| `Cell:Region`                        | All hosts         | The region code, such as `vn`.                                                                                                                                                                                                                                                                                                                                                                                                |
| `Cell:TenantDomain`                  | Server, publisher | The domain of the tenant subdomains, such as `vn.example.com`.                                                                                                                                                                                                                                                                                                                                                                |
| `Cell:InternalDomain`                | Admin, publisher  | A domain no ingress serves, for scratch tenants of the restore check. `cell restore-check` stops with exit code `2` when it is blank.                                                                                                                                                                                                                                                                                          |
| `Cell:HostCertificateIssuer`         | Publisher         | The name of the cert-manager `Issuer` that the publisher uses for the `Certificate` of a verified tenant host, see [hosts and certificates](cells.md#hosts-and-certificates). The chart sets it to `<release>-tenant-hosts` when `cell.certificate.enabled` is on. Blank, the default, writes no `Certificate`. A value with no Kubernetes service account in the pod stops the publisher with exit code `2`, and the message names the setting. |
| `Cell:HostCheckInterval`             | Publisher         | How often the publisher checks the pending [host requests](tenant-lifecycle.md#hosts) against DNS, `00:10:00` by default. Zero, a negative value or one that is not a time span stops the publisher with exit code `2`, and the message names the setting.                                                                                                                                                                   |
| `Cell:TenantCap`                     | Server            | The measured number of tenants the cell holds, see [cell size](cells.md#cell-size).                                                                                                                                                                                                                                                                                                                                           |
| `Cell:ConsoleHost`                   | Server            | The host of the [cell console](cell-console.md).                                                                                                                                                                                                                                                                                                                                                                              |
| `Cell:ConsoleAllowedNetworks`        | Server            | Address ranges in CIDR notation, such as `10.0.0.0/8`, that may reach the console host. Any other address gets `403`. Empty allows every address. A range that is not CIDR stops the start in a cell, and the message names the setting.                                                                                                                                                                                     |
| `Cell:InternalUrl` | Server | The internal listener of the [provisioning API](tenant-lifecycle.md#provisioning-api). It must be an `https` URL when `Cell:InternalClientCa` is set. |
| `Cell:InternalClientCa` | Server | A PEM file with the region's internal certificate authority. With it set, the Kestrel endpoint `Internal` requires a client certificate that chains to it, and the TLS handshake fails otherwise. The public listeners never ask. Without it the `Internal` endpoint serves without client certificates. The start stops, and the message names the setting, when the file cannot be read or holds no certificate, `Cell:InternalUrl` is blank or not `https`, or `Kestrel:Endpoints:Internal:Url` is missing or not `https`. Only the server in a cell reads it. See [the internal listener](tenant-lifecycle.md#provisioning-api). |
| `Cell:OperatorRealm`                 | Server, admin     | The operator realm, `axis-operators` by default.                                                                                                                                                                                                                                                                                                                                                                              |
| `Cell:OperatorRole`                  | Server, admin     | The role of the operator realm that the cell console and the API need, `cell-operator` by default.                                                                                                                                                                                                                                                                                                                            |
| `Cell:ConsoleClientId`               | Server            | The client of the operator realm that the [cell console](cell-console.md#sign-in) signs operators in with, `axis-cell-console` by default. A blank value keeps the default.                                                                                                                                                                                                                                                  |
| `Cell:ConsoleClientSecret`           | Server            | The secret of that client. Without it the server offers no console sign-in, and `GET /api/cell/auth/sign-in` answers `404`. The server reads it from configuration only, so it should come from a secret store or an environment variable (`Cell__ConsoleClientSecret`).                                                                                                                                                      |
| `IdentityProvider:Url`               | Server            | In a cell with `Cell:ConsoleHost` or `Cell:InternalUrl` set, the server reads the identity provider's address, described in the [administration settings](administration.md#settings), and finds the operator realm at `realms/<Cell:OperatorRealm>` under it. A missing or wrong value stops the start, and the message names the setting.                                                                                 |
| `Cell:BackupKeyFile`                 | Admin, publisher  | The file of the cell's backup key, which wraps each tenant's backup key. In a cell, `tenant create`, `tenant backup`, `tenant restore`, `cell backup` and `cell restore-check` need it and stop with exit code `2` without it. The other hosts and commands never read it.                                                                                                                                                    |
| `Cell:BackupStorage:ServiceUrl`      | Admin             | The address of the S3-compatible service that `cell backup` uploads to. Only `cell backup` and `cell restore-check` read the five `Cell:BackupStorage` settings, and they stop with exit code `2` when one is blank, see [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell).                                                                                                                                  |
| `Cell:BackupStorage:Region`          | Admin             | The signing region of that service.                                                                                                                                                                                                                                                                                                                                                                                           |
| `Cell:BackupStorage:Bucket`          | Admin             | The bucket that receives `<tenant id>/<yyyy-mm-dd>.axisbak`.                                                                                                                                                                                                                                                                                                                                                                  |
| `Cell:BackupStorage:AccessKeyId`     | Admin             | The access key id of the service.                                                                                                                                                                                                                                                                                                                                                                                             |
| `Cell:BackupStorage:SecretAccessKey` | Admin             | The secret access key of the service. It is never printed.                                                                                                                                                                                                                                                                                                                                                                    |
| `Cell:ExportStorage:ServiceUrl`      | Publisher, server | The address of the S3-compatible service that the `export` job of the publisher uploads the ZIP of a [tenant export](tenant-lifecycle.md#exporting-a-tenant) to. Only the `export` job and the hourly sweep of the publisher, and the server's download of an export, read the five `Cell:ExportStorage` settings. The server streams the ZIP from this storage. When one is blank, the publisher still starts and runs its other jobs, and the `export` job fails with a message that names the setting. The server still starts and the download answers `503`. |
| `Cell:ExportStorage:Region`          | Publisher, server | The signing region of that service.                                                                                                                                                                                                                                                                                                                                                                                           |
| `Cell:ExportStorage:Bucket`          | Publisher, server | The bucket that receives `exports/<tenant id>/<job id>.zip`, stored with server-side encryption (`AES256`).                                                                                                                                                                                                                                                                                                                   |
| `Cell:ExportStorage:AccessKeyId`     | Publisher, server | The access key id of the service.                                                                                                                                                                                                                                                                                                                                                                                             |
| `Cell:ExportStorage:SecretAccessKey` | Publisher, server | The secret access key of the service. It is never printed.                                                                                                                                                                                                                                                                                                                                                                    |
| `Cell:RuntimeDatabaseHost`           | Server, worker    | The address of every tenant's runtime database connection, such as PgBouncer, see [connections](cells.md#connections). Those connections also take `Maximum Pool Size=5`, `Minimum Pool Size=0`, `Connection Idle Lifetime=60` and `Max Auto Prepare=0`. Without it each connection uses the host of the tenant's directory row. The schema role, the publisher, the worker's `LISTEN` connection and the platform connections never use it. |
| `Cell:RuntimeDatabasePort`           | Server, worker    | The port of that address. It defaults to the port of the tenant's directory row. Setting it without `Cell:RuntimeDatabaseHost` stops the start.                                                                                                                                                                                                                                                                              |
| `Quotas:Defaults:<quota>`            | All hosts         | The default of each [quota](tenant-quotas.md#quotas).                                                                                                                                                                                                                                                                                                                                                                         |
| `Worker:Concurrency`                 | Worker            | The loops that claim work at once, 4 by default, see [fair share](worker-scheduling.md#fair-share).                                                                                                                                                                                                                                                                                                                           |
| `Worker:Quantum`                     | Worker            | The work items a tenant of weight 1 runs in a round, 10 by default.                                                                                                                                                                                                                                                                                                                                                           |
| `Worker:IdlePollCeiling`             | Worker            | The longest wait between two polls of an idle tenant, 30 seconds by default. It must be at least `Worker:PollInterval`, or the worker stops at start.                                                                                                                                                                                                                                                                         |
