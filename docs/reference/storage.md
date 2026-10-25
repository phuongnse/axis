# Storage

Detailed reference for the storage layout, physical names, column types,
database roles and schema planning. Migrations and recomputing stored computed
fields are in [migrations.md](migrations.md). The conventions of [architecture.md](../architecture.md)
apply: sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

- **Platform tables.** These are managed by EF Core migrations shipped with
  Axis.
- **System tables.** These live in the `axis` schema of each tenant database.
  Each module owns its tables and manages them with its own EF Core context
  and migrations. Each module context keeps its migration history in its own
  table, `axis.__<module>_migrations`, so the contexts sharing the schema do
  not collide.
  - `Axis.Configuration` owns `axis.releases`, `axis.release_resources`,
    `axis.release_packages` (the extension package versions each release
    pins, as release id, package id and version, unique per release and
    package), `axis.active_releases` (the active release of each application `id`,
    with its name, unique ignoring letter case) and `axis.active_sites` (the
    site paths of each active release, as application `id` and path, unique
    on path), with history in `axis.__configuration_migrations`. It also owns
    `axis.release_computed_fingerprints`, the hash of the
    computed definitions of each entity in each release. It also owns
    `axis.release_activations` (the activation history with release numbers),
    see [Release history](#release-history). It also owns
    `axis.publish_requests` (the requests that Studio stores and the publisher
    runs). The server inserts and reads them, and the publisher runs them. See
    [Publish requests and release history](#publish-requests-and-release-history). The caller
    supplies the tenant database connection. Other modules read and set the
    active release and its site paths only through `IActiveReleaseStore`;
    they never use `axis.active_releases`, `axis.active_sites` or the
    configuration context directly.
    Files of a module are stored in
    `axis.release_resources` with their `modules/<id>/` path, see
    [Modules](modules-and-templates.md#modules).
  - `Axis.Data` owns `axis.provisioned_entities` (each provisioned entity
    with its application and table), `axis.provisioned_enum_values` (each
    recorded enum value) and `axis.sequence_counters` (the last number of
    each sequence and period), with history in `axis.__data_migrations`.
    It also owns `axis.applied_migrations`, the migrations applied once per
    tenant. See [Migrations](migrations.md#migrations). It also owns
    `axis.computed_field_errors`, for the
    [recompute of computed fields](migrations.md#recomputing-computed-fields). It also owns
    `axis.pending_computed_fields`, the entities whose recompute the worker
    finishes in batches.
  - `Axis.Processes` owns `axis.process_instances` (one row per process
    instance), `axis.process_step_history` (one row per step occurrence),
    `axis.process_tasks` (one row per human task),
    `axis.connector_calls` (one row per staged connector call),
    `axis.package_calls` (one row per staged package call),
    `axis.process_call_attempts` (one row per attempt and per lookup of a
    connector or package call),
    `axis.events` (one row per event occurrence that a step raised),
    `axis.process_start_receipts` (the stored response of each
    `Idempotency-Key` of a start), `axis.endpoint_receipts` (the record and
    instance of each endpoint call's `Idempotency-Key`, unique per
    application, endpoint, caller and key, with a hash of the input) and
    `axis.process_work_items` (the work that
    workers claim and run), with history in `axis.__processes_migrations`.
    See [processes](processes.md#tables).
  - `Axis.Data` also owns `axis.audit_records`, with history in
    `axis.__data_migrations`. Other modules append and read audit records
    only through its contract. `AuditRecords.AppendAsync` takes the
    caller's open transaction, and `AuditRecords.ListForRecordAsync` reads
    one record's history. A process start and each process step write
    their audit records this way, inside their own transaction. See
    [Audit records and sequence counters](#audit-records-and-sequence-counters).
  - Releases are immutable, enforced through the context's change tracking:
    a release and its resources are only inserted together. Saving fails when
    a stored release or resource is modified or deleted, or when a resource is
    added to a stored release. Bulk operations (`ExecuteUpdate`,
    `ExecuteDelete`) and raw SQL bypass this guard, so a database trigger backs
    it: it refuses `UPDATE`, `DELETE` and `TRUNCATE` on the release tables with
    SQLSTATE `23001`. The guard still fails first, before any SQL runs. See
    [Database roles](#database-roles).
- **Entity tables.** These are generated per entity in the tenant database and
  live in the `entities` schema, separate from the `axis` system tables.
  `Axis.Data` plans them (see [Schema planning](#schema-planning)) and
  applies the plan.
  - In M1, plan and apply run together in one transaction. One fixed
    advisory transaction lock (`pg_advisory_xact_lock`) serializes entity
    schema changes, so the catalog and records read for the plan are still
    current when it is applied.
  - The provisioning records are written in the same transaction as the DDL.
    When the plan has any diagnostic, the transaction is rolled back and
    nothing is applied.
  - The lock does not block runtime writes to entity tables, so a statement
    can still fail, for example adding a `NOT NULL` column after a row was
    inserted following the catalog read. Any failing statement rolls back the
    whole transaction, DDL and records, and the exception propagates.
- **Physical names.** Table and column names are derived from stable IDs and
  names, never from labels. Renaming a label, or renaming an entity while
  keeping its `id`, never touches storage.

  | Object                  | Name                                                                      | Length     |
  | ----------------------- | ------------------------------------------------------------------------- | ---------- |
  | Table                   | `e_` + entity `id` as 32 lowercase hex characters                         | 34         |
  | Primary key column      | `id` (`uuid`)                                                             | 2          |
  | Version column          | `version` (`bigint`)                                                      | 7          |
  | Computed release column | `computed_release_id` (`uuid`, null), on tables that are not child tables | 19         |
  | Field column            | `f_` + field name in lowercase                                            | at most 62 |
  | Primary key             | `pk_` + table                                                             | 37         |
  | Unique constraint       | `uq_` + table + `_` + hash of the column name                             | 54         |
  | Foreign key             | `fk_` + table + `_` + hash of the column name                             | 54         |
  | Index _(planned for M15)_ | `ix_` + table + `_` + hash of the column list                           | 54         |
  | Junction table _(planned for M15)_ | `m_` + entity `id` + `_` + hash of the field name              | 51         |

  The hash is the first 16 lowercase hex characters of SHA-256 over the UTF-8
  column name. For an index it is over the column names joined by commas, in
  order, and for a junction table over the field name in lowercase. Because names are ASCII and at most 60 characters, every
  identifier fits PostgreSQL's 63-byte limit by construction and is never
  truncated. Identifiers are always double-quoted in SQL.

- **Module resource ids.** The compiler builds the derived id into the model.
  Activation provisions a module entity's table for each application that
  uses the module. The id of a module resource
  inside an application is a UUID v5 (RFC 9562). The namespace is the
  application `id`. The name is the resource's own declared id, as lower-case
  hyphenated UTF-8 text. A module entity's table is `e_` + the derived id, and
  `axis.provisioned_entities` records the application that uses it. So two
  applications in one tenant that use one module get different `e_` tables,
  and `AXC0018` never fires between them. The derived id stays the same across
  module versions, so an upgrade maps onto the same table. See
  [Modules](modules-and-templates.md#modules).
- **Column types.** Each field type maps to one PostgreSQL type, spelled as
  `format_type` renders it, so a catalog column matches by string equality.

  | Field type         | Column type                                                                                               |
  | ------------------ | --------------------------------------------------------------------------------------------------------- |
  | `text`             | `character varying(n)` with `maxLength`, otherwise `text`                                                 |
  | `integer`          | `bigint`                                                                                                  |
  | `decimal`          | `numeric(p,s)` with `precision` (`s` is 0 when `scale` is omitted), otherwise `numeric`                   |
  | `boolean`          | `boolean`                                                                                                 |
  | `date`             | `date`                                                                                                    |
  | `date-time`        | `timestamp with time zone`                                                                                |
  | `enum`             | `text`; the values are recorded, not enforced by a `CHECK`                                                |
  | `reference`        | `uuid` with a foreign key to the target table's `id`                                                      |
  | `child-collection` | No column on the owner table. See [Child tables and computed columns](#child-tables-and-computed-columns) |
  | `money` | `numeric(p,s)`, as `decimal` with its defaults. The currency is the field's or the `currencyField` column's |
  | `file` _(planned for M15)_ | `uuid` with a foreign key to `axis.files`. See [file fields](file-fields.md#storage-and-clean-up) |
  | `references` _(planned for M15)_ | No column on the owner table. A junction table holds `owner_id` and `target_id`, see [Schema planning](#schema-planning) |

- **SQL safety.** Every SQL statement for entity data is built by the data
  module from compiled metadata. Identifiers are resolved and quoted by the
  module; values are always parameters. Configuration can never supply raw SQL.
  Request text, such as the entity segment, `sort` and `values` names, is
  matched against the model's declared names and never used as an identifier.
- **Database credentials.** Each tenant has two connection strings.
  `SchemaConnectionString` serves startup activation only, and
  `ConnectionString` serves everything else. The integration tests and the
  development environments run activation as the schema role and everything
  else as the runtime role. See [Database roles](#database-roles).

## Platform database

See
[D42](../decisions/d42-tenant-directory-in-the-platform-database.md).
The `platform` schema, its migrations, the read-only grant, the secret cipher
and the directory source of the server and the worker are built. `Axis.Admin
tenant create` writes the tables and encrypts the stored passwords and the OIDC
client secret. `tenant suspend` and `tenant resume` change a tenant's status,
and `tenant restore` adds a suspended tenant. The platform tables exist
under both tenant sources: the server creates them at start and sets the
read-only grant. Under both sources the server keeps its data protection keys
there, see
[D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
Under `Tenancy:Source=directory` the server also reads the `active` tenants, their hosts and their bindings over
`ConnectionStrings:Platform`. Both hosts read them again every
`Tenancy:RefreshInterval`. The server and the worker keep each password, the client
secret and each `secret` binding encrypted in memory, and decrypt one only when a
connection opens, the sign-in client is set up or a binding is resolved. Neither
reads any `schema_password`. The worker reads the same rows over the same role,
and creates no tables. See
[tenant directory](../architecture.md#tenant-directory).

- **The `platform` schema.** Its EF Core migrations ship with Axis, with their
  history in `platform.__platform_migrations`. The server applies them at
  start over `ConnectionStrings:PlatformSchema` under both tenant sources, and
  uses that setting for nothing else.
- **Read-only hosts.** The server reads the directory over
  `ConnectionStrings:Platform`. Its role has `USAGE` on the `platform` schema
  and `SELECT` on its tables, and nothing else. The server sets this in one
  transaction after the migrations, and revokes any wider right the role had
  there. It refuses a role that is the role of `ConnectionStrings:PlatformSchema`.
  It does not yet check whether the role is a superuser, owns the schema or
  inherits rights through membership. The `platform.audit_records` table is
  the one exception: the role gets no right on it at all. The same step revokes
  the right that the default privileges would give it on that table.
- **One write grant.** The same role also gets `INSERT` on
  `platform.data_protection_keys`, and on no other table. Key rotation happens
  while the server runs, so the server must add keys. It never updates or
  deletes one. At start the server decrypts every stored key, and a key that
  does not decrypt stops the start. The error names `Platform:SecretKey`. A
  replica with another `Platform:SecretKey` fails this way. Changing the key on
  an installation that holds key rows means deleting the rows by hand, which
  signs everyone out, until the key rotation of M16 replaces this step
  _(planned for M16)_, see
  [installation key](tenant-setting-commands.md#installation-key). The Studio write lock is a PostgreSQL advisory lock. It needs
  no grant, see [authoring API](authoring.md#versions).
- **The writer.** `Axis.Admin` writes the directory over
  `ConnectionStrings:PlatformSchema`, see
  [administration](administration.md) and
  [D43](../decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md).

The tables:

| Table                                               | Columns                                                                                                                                                                                                                                                                                                                     |
| --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `platform.tenants`                                  | The tenant `id`, `status` (`provisioning`, `active` or `suspended`), `version`, the database host, port and name, the runtime and schema user names, the encrypted runtime and schema passwords, the OIDC authority, client id and encrypted client secret, the claim names, the endpoint audience, the service audience, the `schema_version` and `preview_of`, the id of the main tenant of a preview tenant, which is unique and refers to `platform.tenants`. The sign-in profile and the encrypted client keys are _(planned for M16)_ |
| `platform.tenant_hosts`                             | The tenant id and the host. The host is unique on `lower(host)` across all tenants                                                                                                                                                                                                                                          |
| `platform.tenant_bindings`                          | The tenant id, the binding name, the `kind` and the value: a URL for a `url` binding, an encrypted value for a `secret` binding. The name is unique per tenant ignoring letter case                                                                                                                                         |
| `platform.data_protection_keys`                     | The key's `id`, its `friendly_name`, unique, and the encrypted key XML. Rows are only inserted, never updated or deleted. Every server replica reads them                                                                                                                                                                   |
| `platform.audit_records`                            | The `id`, `occurred_at`, `command`, the `tenant_id`, which can be empty and has no foreign key, `os_user`, `machine` and `changed` (`text[]`), the names of what changed. Triggers reject every update, delete and truncate with SQLSTATE `23001`. Only `Axis.Admin` writes it, see [platform audit records](tenant-setting-commands.md#platform-audit-records) |

The tenant `version` grows on every change to the tenant, its hosts or its
bindings.

The `schema_version` is text. It is the id of the newest EF Core migration
across the configuration, data and processes contexts that `Axis.Admin` applied
to the tenant database, and it is empty until the first migration. A host serves
a tenant only when this value is the one the host was built for, see
[administration](administration.md#migrate).

- **Where secrets are stored.** Each of these is stored encrypted, and nowhere
  else:
  - the runtime and schema database passwords, in `platform.tenants`;
  - the OIDC client secret, in `platform.tenants`;
  - the value of a `secret` binding, in `platform.tenant_bindings`;
  - the client keys of a tenant under the `fapi2` sign-in profile, in
    `platform.tenants`, with the field name `oidc_client_key` and the key id
    _(planned for M16)_, see [FAPI 2.0 profile](security.md#fapi-20-profile).
- **Encrypted format.** An encrypted value is a `bytea` that holds a 12-byte
  nonce, the ciphertext and a 16-byte tag. The cipher is AES-256-GCM with the
  installation key `Platform:SecretKey`, see
  [tenancy settings](host-settings.md#tenancy-settings). The associated data
  is the tenant id and the field name, so a value copied to another tenant or
  field does not decrypt. The associated data is the UTF-8 bytes of the tenant
  id, one zero byte, and the UTF-8 bytes of the field name. The field names are
  `runtime_password`, `schema_password`, `oidc_client_secret`, and `binding:`
  followed by the binding name in lower case. Every writer must use these
  exact bytes and names. A data protection key uses an
  empty tenant id, so the associated data is one zero byte and the field name
  `data_protection_key:` followed by the key's `id`. A tenant id is never
  empty, so a key and a tenant field cannot be swapped.
- **Decryption.** A value is decrypted only at the moment of use, as
  `SecretValue` does today. No API, log, problem details or backup manifest
  holds a decrypted value.
- **No key rotation.** Axis has none. After a leaked key, every stored secret
  must be set again by hand. Rotating the installation key with
  `Platform:PreviousSecretKey` and `platform rekey` is _(planned for M16)_,
  see [installation key](tenant-setting-commands.md#installation-key) and
  [D87](../decisions/d87-tenant-setting-commands-and-key-rotation.md).

## Database roles

See [D31](../decisions/d31-separate-database-roles.md). The two
settings, the connection each step uses, the grants that limit the runtime
role and the release trigger are built. The integration tests create the two
database roles, `axis_schema` and `axis_runtime`, own each test database with
`axis_schema`, run the server and worker as `axis_runtime`, and run activation
as `axis_schema`. Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the
same: `samples/postgres/init-roles.sql` creates the roles and grants them
nothing. It also creates the development [preview tenant](host-settings.md#preview-tenants)'s
database `axis_preview`, owned by its own schema role `axis_preview_schema`, and its runtime role
`axis_preview_runtime`, both with the development password `axis`. An existing Compose volume
needs `docker compose down -v` once to get them. The server and the worker run the start checks for a superuser, a
schema owner or a role that may create objects, also when the right comes
through role membership.

- **Two database roles per tenant.** The schema database role owns the `axis`
  and `entities` schemas. The runtime database role reads and writes data, and
  can change nothing about the schema. "Database role" always means this
  PostgreSQL role. It is not the application `role` resource of
  [D24](../decisions/d24-roles-and-policies.md).
- **Two settings.** `ConnectionString` is for the runtime database role, and
  `SchemaConnectionString` is for the schema database role. See
  [tenant configuration](../architecture.md#tenant-configuration). There is no
  fallback to one connection string.
- **A scenario role.** A third kind of database role runs
  [test scenarios](test-scenarios.md) for Studio, as
  [D55](../decisions/d55-running-test-scenarios-from-studio.md)
  agrees. The scenario role has `CREATEDB`, is not a superuser and owns no
  tenant database. Its development name is `axis_scenario`, with the password
  `axis`. The integration fixtures create it, and so does
  `samples/postgres/init-roles.sql`. PostgreSQL runs that file only on an empty
  volume, so an existing Compose volume has no `axis_scenario` and a `test`
  request fails there until `docker compose down -v`, which deletes the local
  data. Only the publisher holds
  it, through the setting `Scenarios:ServerConnectionString`. It creates one
  database `axis_scenario_<uuid>` for each scenario, owns it and drops it after
  the scenario. The server holds no right to create databases, and neither does
  the worker. They hold no role that may. On a shared PostgreSQL server every
  role may connect to a database by default, so the scenario role can connect to
  a tenant database, though it can read nothing there. See
  [installation](installation.md#on-premises) for how to limit that.

What each database role may do:

| Objects                                                                                                                                                                                                                                     | Schema database role                                             | Runtime database role                                             |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------------- |
| `entities` tables                                                                                                                                                                                                                           | Owns them                                                        | Read and write rows                                               |
| `axis` tables that runtime writes: `process_*`, `connector_calls`, `package_calls`, `events`, the receipts, `trigger_schedules`, `trigger_starts`, `delivery_failures`, `dead_letters`, `audit_records`, `sequence_counters`, `computed_field_errors`, `pending_computed_fields` | Owns them                                                        | Read and write rows                                               |
| Tables that activation writes: `active_releases`, `active_sites`, `provisioned_entities`, `provisioned_enum_values`, `applied_migrations`, `release_computed_fingerprints`, the `__*_migrations` tables                                     | Owns them                                                        | Read only                                                         |
| Release tables: `releases`, `release_resources`, `release_packages`                                                                                                                                                                         | Insert and read. The trigger refuses update, delete and truncate | Read only                                                         |
| `publish_requests`                                                                                                                                                                                                                          | Owns it. The publisher changes the status and the lease | `SELECT` and `INSERT` only. It never updates or deletes a request |
| `release_activations`                                                                                                                                                                                                                       | Insert and read. The trigger refuses update, delete and truncate | Read only                                                         |
| Sequences in `axis` and `entities`                                                                                                                                                                                                          | Owns them                                                        | `USAGE`, `SELECT`                                                 |
| Create, alter, drop                                                                                                                                                                                                                         | Yes                                                              | No                                                                |

- **Default privileges.** The schema database role sets default privileges. A
  new table in `entities` gives the runtime database role `SELECT`, `INSERT`,
  `UPDATE` and `DELETE`. A new table in `axis` gives it `SELECT` only. The
  grant step also clears the database-wide default privileges that older
  setups set, because a per-schema default cannot narrow them.
- **Explicit grants.** The grant step holds the list of `axis` tables that
  runtime writes. It grants row access on those and revokes `INSERT`, `UPDATE`,
  `DELETE` and `TRUNCATE` on every other `axis` table, so a database with
  older, wider grants is repaired. A new system table is read-only until
  someone adds it to the list. The list has a second
  category, tables where runtime may only insert. `publish_requests` is the
  only one. It grants `SELECT` and `INSERT` and revokes `UPDATE`, `DELETE` and
  `TRUNCATE`, so the server cannot change the status of a request.
- **Role name.** The runtime database role is the user of `ConnectionString`.
  The name comes from configuration, so a migration cannot hold it. A grant
  step runs over the schema connection at every server start, both before and
  after the EF Core migrations. The first run sets the default privileges for
  the tables the migrations create. The second run grants the tables that now
  exist. The step refuses a runtime database role that is the schema database
  role. It holds an advisory lock until it ends, so servers that start together
  run their grant steps one after the other.
- **Release trigger.** A trigger refuses every `UPDATE`, `DELETE` and
  `TRUNCATE` on the three release tables, for every database role, with
  SQLSTATE `23001`. An `Axis.Configuration` migration creates it. It backs the
  change-tracking guard described above, which stays and fails first. A stored
  release can no longer be deleted at all.
  Cleaning up old releases needs a later decision. A superuser or the table
  owner can still disable the trigger. It guards against mistakes by Axis, not
  against a database administrator.
- **Start checks.** A missing or blank setting stops the start, naming the
  tenant and the setting. The server does this today. So does a
  `ConnectionString` that names no user, and two settings that name the same
  user. So does a runtime database role that is a superuser, owns the `axis` or
  `entities` schema, or may create objects in either. A role that is a member
  of the role that owns a schema counts as its owner, because it can `SET ROLE`
  to the owner. The same goes for a member of a superuser role and for a
  member of any role with `CREATE` on either schema, whatever the `INHERIT`
  option of the membership. The message names the tenant, the role and the
  first failed check, in the order superuser, owner, `CREATE`, with `axis`
  before `entities`. When the right comes through another role, it also names
  that role. It never holds the connection string or the password. The
  server runs the role check after its grant step, so a fresh database with the
  right roles starts, and again in its start-time release check, which runs for
  every tenant even when no folder is activated. The worker runs it once for
  each tenant, when the tenant's database counts as migrated and before it
  claims any work of that tenant. The server skips the check for a tenant
  whose database it cannot reach, as the release check does. The checks do not
  cover `CREATE` on the database itself, ownership of single tables or
  `CREATEROLE`. The server runs all the other checks. The worker validates
  `ConnectionString` and the runtime database role. The server also validates
  `SchemaConnectionString` and that the two settings name different users,
  because the worker holds no `SchemaConnectionString`.

Which connection string each step uses. The server does this today:

| Step                                                                                                                               | Connection string        |
| ---------------------------------------------------------------------------------------------------------------------------------- | ------------------------ |
| EF Core migrations of configuration, data and processes                                                                            | `SchemaConnectionString` |
| Compiling a folder and storing its release                                                                                         | `SchemaConnectionString` |
| Activation: migrations, provisioning, active release, site paths                                                                   | `SchemaConnectionString` |
| Grants for the runtime database role, before and after the migrations                                                              | `SchemaConnectionString` |
| Schedule trigger state                                                                                                             | `ConnectionString`       |
| Seed records                                                                                                                       | `ConnectionString`       |
| Publish routes of the server: insert a request, read requests                                                                      | `ConnectionString`       |
| Release history route of the server: read the release history                                                                      | `ConnectionString`       |
| Publisher: claim and finish a request, compile, store, activate, write the history and the audit record                            | `SchemaConnectionString` |
| Publisher: seed records                                                                                                            | `ConnectionString`       |
| Publisher, `test` request: create, migrate, run and drop the scenario databases                                                    | `Scenarios:ServerConnectionString` |
| Publisher, `test` request: store the status and the outcomes                                                                       | `SchemaConnectionString` |
| Start release check (`AXC0146`) and the runtime database role check, in the server and the worker                                  | `ConnectionString`       |
| Server requests: record API, data sources, process starts, endpoints, tasks, sites                                                 | `ConnectionString`       |
| Worker                                                                                                                             | `ConnectionString` only  |

The server holds both connection strings for its whole life. It opens the
schema connection only during startup activation, and closes it when each
tenant is done. Under `Tenancy:Source=directory`, the
server holds no tenant schema connection, see
[under the directory source](#under-the-directory-source).

The publisher, `axis-admin publisher`, runs every publish request. Under
`Tenancy:Source=configuration` it uses each tenant's `SchemaConnectionString`,
so it holds the schema role. It waits until the server has migrated the tenant
database, and seeds over `ConnectionString`. The server still opens that
connection only during startup activation, and runs no publish request itself.
The production installs, `scripts/dev.sh`, the root Compose file and the E2E script each start one publisher. The last three give it the `default-preview` tenant as well, so it runs preview requests.

### Under the directory source

See
[D43](../decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md).
Under `Tenancy:Source=directory`, `tenant create` creates
the two database roles of a tenant, its database and the ownership, with no
superuser. See [administration](administration.md) for the admin commands. Each step below runs on one
host with one database role:

| Step                                                                                                                    | Host                            | Database role                      |
| ----------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ---------------------------------- |
| Create the database and the two roles                                                                                   | `Axis.Admin`                    | `Admin:ConnectionString`           |
| Directory writes                                                                                                        | `Axis.Admin`                    | `ConnectionStrings:PlatformSchema` |
| Platform migrations, at start, under both tenant sources                                                                | Server                          | `ConnectionStrings:PlatformSchema` |
| Grants, EF Core migrations, compile, store and activate                                                                 | `Axis.Admin`                    | The tenant's schema role           |
| Seeding and schedule trigger state                                                                                      | `Axis.Admin`                    | The tenant's runtime role          |
| Claim, run and finish a publish request: compile, store, activate, release history and audit record | `axis-admin publisher`          | The tenant's schema role           |
| Seeding of a publish request                                                                        | `axis-admin publisher`          | The tenant's runtime role          |
| Create, migrate, run and drop the scenario databases of a `test` request                            | `axis-admin publisher`          | `Scenarios:ServerConnectionString` |
| Store the status and the outcomes of a `test` request                                               | `axis-admin publisher`          | The tenant's schema role           |
| Insert and read publish requests                                                                                        | Server                          | The tenant's runtime role          |
| Read the release history                                                                                                | Server                          | The tenant's runtime role          |
| Runtime role check                                                                                                      | `Axis.Admin`, server and worker | The tenant's runtime role          |
| Requests and work items                                                                                                 | Server and worker               | The tenant's runtime role          |

- **No tenant schema connection.** The server holds no tenant schema
  connection. It runs no tenant migration and no grant step. The worker never
  held one. This stays true when Studio publishes: the
  server only inserts a request over the runtime role, and `axis-admin
publisher` runs it with the tenant's schema role.
- **The publisher.** It reads the tenants from the
  directory as the server does and holds the schema role of every active
  tenant. It becomes the most valuable host to attack, so run it apart from the
  server and give it no inbound network. It has no HTTP endpoint, see
  [publisher](administration.md#publisher).
- **Platform schema.** The server still holds `ConnectionStrings:PlatformSchema`
  at start, for the platform migrations. So "no schema connection" means no
  tenant schema connection.
- **Tenant schema version.** Each tenant database migrates on its own, with
  `Axis.Admin migrate`, which records the version in the directory. A host
  serves a tenant only when the tenant's schema version is the host's own, see
  [tenant directory](../architecture.md#tenant-directory).

## Schema planning

`Axis.Data` compares a compiled application with a snapshot of the tenant
catalog (entity tables, their columns, types, `NOT NULL`, single-column
unique constraints, foreign key targets and whether the table has rows) and
with the provisioning records (each provisioned entity with its application
and table, and each recorded enum value). The records cover the application's
entities and any entity recorded with one of its entity `id`s, whatever its
application. The planner itself has no database
access. It returns diagnostics, SQL statements and the new records to write.

- **Migrations.** The planner first applies the pending `renameField`,
  `dropField`, `dropEntity`, `fill` and `convert` [migrations](migrations.md#migrations) to
  its catalog snapshot and its copy of the provisioning records, in ordinal path
  order and file order. It then plans as today. A `convert` makes the planner
  accept a type change, or enum values the release removes, where it gives
  `AXC0016` without one. A `fill` or `convert` clears the release stamp of the
  rows it changes. On a child entity it also increments the owner's `version`
  and clears the owner's stamp, for each owner of a changed row. Activation then
  recomputes those rows, see
  [Recomputing computed fields](migrations.md#recomputing-computed-fields). A `fill` or
  `convert` that names, in its value, a field that is not a column of the stored table is
  `AXC0171` at `/changes/{i}/value`. A `fill` whose value leaves a `null` is
  `AXC0176` at `/changes/{i}/value` when activation applies it. A `convert`
  that fails for a stored row, or maps an enum value to one the field does not
  list, is `AXC0177` at `/changes/{i}/value`. Switching a field between `enum`
  and another type stays `AXC0016`, even with a `convert`. Removals and incompatible
  changes not covered by a migration keep `AXC0015`, `AXC0016` and
  `AXC0017`. A change that does not match the stored schema is skipped: the
  table does not exist, the `from` column is missing or the `to` column
  already exists. The migration is still recorded when activation succeeds,
  so a fresh tenant activates a release that has migrations. A skip cannot
  lose data, because a leftover column or table still gives `AXC0015` or
  `AXC0017`. A migration's `entity` name is looked up first in the
  application's active release, read from its stored resources, because that
  release describes the tables that hold the data now and only it has a
  dropped entity. It is then looked up in the release being activated. A
  rename also renames the field in the recorded enum values, and a drop
  deletes them.
- **System columns.** `id`, `version` and `computed_release_id` are created
  with the table, or `id`, `owner_id` and `position` for a child table (see
  **Child table** below). `version` is added to an existing table other than a
  child table that lacks it as
  `bigint NOT NULL DEFAULT 1`, even when the table has rows, because the
  default fills them. `computed_release_id` is added to such a table as
  `uuid`, nullable and with no default, so the rows are kept and stay
  unstamped. See [Recomputing computed fields](migrations.md#recomputing-computed-fields).
  System columns are never compared or changed otherwise:
  only Axis DDL creates them, and an author cannot fix them through
  configuration.
- **Missing table.** It is created with the system columns and every field
  column, `NOT NULL` for required fields and a unique constraint for unique
  fields. A `child-collection` field has no column. The entity and its enum
  values are recorded.
- **Child table.** The owner of a child entity is the entity whose
  `child-collection` field names it; the compiler ensures there is exactly
  one.
  - A missing child table is created with `id`, `owner_id`, `position` and
    its field columns, and no `version`. Its owner foreign key is added with
    the other foreign keys, so the owner table exists whatever the entity
    order. Adding a child collection and its child entity to an entity that
    already has rows only creates the child table: the owner table and its
    rows are untouched.
  - An existing child table whose `owner_id` has no foreign key gets one. A
    foreign key that points at another table than the owner's is `AXC0016`
    at the owner's `/fields/{i}/target`.
  - An existing table without `owner_id` cannot become a child table, because
    adding a `NOT NULL` owner column needs a migration. It is `AXC0016` at the
    owner's `/fields/{i}/target`.
  - An entity that stops being a child keeps `owner_id` and `position`. They
    are reported as removed fields (`AXC0015`), like any column without a
    field.
- **Missing column.** It is added. `NOT NULL` is added only when the table
  has no rows; a required field added to a table with rows is `AXC0016` at
  `/fields/{i}/required`, unless a pending `fill` covers it. The `fill` adds
  the column as nullable and fills it, and the planner then makes it
  `NOT NULL`. A unique field also gets its unique constraint.
- **Existing column.**
  - The type must equal the expected type. Widening text
    (`character varying(n)` to a larger `n` or to `text`) is applied.
    Narrowing it, including `text` to `character varying(n)`, is `AXC0016` at
    `/fields/{i}/maxLength`. Any other type difference is `AXC0016` at
    `/fields/{i}/type`.
  - Dropping `required` drops `NOT NULL`, and dropping `unique` drops the
    unique constraint. Adding either to an existing column is `AXC0016` at
    `/fields/{i}/required` or `/fields/{i}/unique`. A pending `fill` of the
    field allows `required`: the column becomes `NOT NULL` after the `fill`.
  - A reference column without a foreign key gets one. A foreign key that
    points at another table than the target's is `AXC0016` at
    `/fields/{i}/target`.
- **Enum values.** Values are compared ordinally, so letter case matters.
  Recorded values missing from the field's `values` are one `AXC0016` at
  `/fields/{i}/values` listing them; new values become new records and need
  no SQL. A column switching between `enum` and another type (recorded values
  present for a non-enum field, or absent for an enum field) is `AXC0016` at
  `/fields/{i}/type`.
- **Removed field.** A column other than the table's system columns without
  a matching field is `AXC0015` at `/fields`, naming the column.
- **Removed entity.** An entity recorded for the application but missing from
  it is `AXC0017` at `application.json` with an empty path and the entity's
  `id` as `resourceId`.
- **Entity owned by another application.** Tables are keyed by entity `id`,
  so an entity whose `id` is recorded for another application, for example in
  an application copied with only its manifest `id` changed, would share that
  application's table. It is `AXC0018` at `/id` of the entity file, and the
  entity is not planned.
- **Labels.** A label change produces no statements.
- **Order.** Every `CREATE TABLE` comes first, then the `ALTER TABLE` column
  changes, then every `ADD CONSTRAINT ... FOREIGN KEY`, so references between
  entities, cycles and self-references need no further ordering. Pending
  migration statements run before these additive statements. Then stored
  computed fields are recomputed inline, or marked pending above the inline
  limit for the worker to recompute.
  See [Order of statements](migrations.md#migrations) and
  [Recomputing computed fields](migrations.md#recomputing-computed-fields).
- **Errors.** When any diagnostic exists, the plan has no statements and no
  new records; nothing is applied.
- **Indexes** _(planned for M15)_. See
  [D77](../decisions/d77-indexes-defaults-and-ranges.md).
  - Every `reference` column and every child table's `owner_id` gets an
    `ix_` index without being declared. So do a junction table's `target_id`
    and every `searchable` field, whose index is a `pg_trgm` GIN index on
    `lower()` of the column.
  - The catalog snapshot also reads the `ix_` indexes of each table. The
    planner adds an index the release declares and the table lacks, and drops
    an `ix_` index the release no longer declares. A drop needs no migration.
  - A unique index added to a table with rows is `AXC0016`, as for `unique`.
    It is built inside the activation transaction.
  - Another new index on a table with at most
    `Activation:InlineRecomputeLimit` rows is built inside the activation
    transaction. On a larger table, activation commits without it and the
    worker builds it with `CREATE INDEX CONCURRENTLY`. An index the build
    leaves `INVALID` is dropped and built again. Until it is valid, queries
    work without it.
- **Junction tables** _(planned for M15)_. A `references` field gets a
  junction table with `owner_id`, a foreign key to the owner deleted with it,
  and `target_id`, a foreign key to the target that refuses a delete while
  linked. Its primary key is both columns. Removing the field drops nothing
  and is `AXC0015`, as for a column. See
  [D78](../decisions/d78-references-from-child-rows-and-many-to-many.md).

## Child tables and computed columns

Child tables and computed columns are built (D17). The planning rules of
child tables are in [Schema planning](#schema-planning).

- **Child table.** The entity that a `child-collection` field names has a
  table with the usual names. Besides `id` (`uuid`) and its field columns, it
  has two system columns:

  | Column     | Type               | Meaning                                                                                                                              |
  | ---------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
  | `owner_id` | `uuid NOT NULL`    | The owner record's `id`. A foreign key to the owner table's `id` with `ON DELETE CASCADE`, named `fk_` + table + `_` + hash as usual |
  | `position` | `integer NOT NULL` | The row's order in the collection, the zero-based index in the array                                                                 |

  Rows are deleted with their owner.

- **No version.** A child table has no `version` column. Its rows live under
  the owner's version, and a write to the rows increments the owner's
  `version`.
- **Computed column.** A computed field is an ordinary
  column of its type. It is never `NOT NULL`, because a computed field cannot
  be `required`. It is written in the same transaction as the record, and for
  a child row before the owner's computed fields.
- **Existing rows.** A computed field added to a table that has rows is
  filled at activation through
  [Recomputing computed fields](migrations.md#recomputing-computed-fields). A row whose
  expression fails gets `NULL` and an error row, and activation goes on.

## Audit records and sequence counters

`Axis.Data` owns both tables (D21). They are system tables in the `axis`
schema of the tenant database.

- **Audit records.** `axis.audit_records` is append-only. Each row is
  written in the same transaction as the action it records. The record API
  and the process [start endpoint](processes.md#start-endpoint) write them,
  and so do [process steps](process-execution.md#running-a-step) and
  [task completions](task-api.md#completing-a-task). So
  do the [operator commands](process-inspection.md#operator-commands), with the user as
  actor. The BFF session writes them too for sign-in, sign-out and a refused
  sign-in, see the session actions below.

  | Column                | Type                             | Meaning                                                                                                                                                                                                                                          |
  | --------------------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
  | `id`                  | `uuid`                           | A version 7 UUID                                                                                                                                                                                                                                 |
  | `occurred_at`         | `timestamptz`                    | When the action happened: the transaction time, `now()`, so the audit records of one transaction share it                                                                                                                                        |
  | `actor`               | `text`                           | A user id, or `system` for the worker. The record API and the process start endpoint need a session, so they never write `anonymous`. Only process starts from before M4, with nobody signed in, have it. `session.signInRefused` also writes it |
  | `actor_name`          | `text`, null                     | The actor's display name at the time of the action. It is null for `system` and `anonymous`, and for audit records written before it was added                                                                                                   |
  | `action`              | `text`                           | The action, such as `record.created`                                                                                                                                                                                                             |
  | `application_id`      | `uuid`, null for session actions | The application `id`. A session action belongs to no application                                                                                                                                                                                 |
  | `entity_id`           | `uuid`, null                     | The entity `id`, when the action concerns a record                                                                                                                                                                                               |
  | `record_id`           | `uuid`, null                     | The record id                                                                                                                                                                                                                                    |
  | `process_instance_id` | `uuid`, null                     | The process instance id                                                                                                                                                                                                                          |
  | `details`             | `jsonb`                          | An object that depends on the action                                                                                                                                                                                                             |
  - Application and entity are stored by their stable resource ids, never by
    name, so a rename keeps the history.
  - A `publish` and a `rollback` write an audit record
    when they succeed and when they fail, see
    [publish requests and release history](#publish-requests-and-release-history).
  - **Session actions.** The
    BFF session writes three actions, see
    [D50](../decisions/d50-sign-in-audit-records-and-security-scanning-in-ci.md)
    and [sign-in and the session](security.md#sign-in-and-the-session).
    Each is written in the tenant database of the resolved host.

    | Action                  | When                                     | `actor` / `actor_name`           | `details`                       |
    | ----------------------- | ---------------------------------------- | -------------------------------- | ------------------------------- |
    | `session.signedIn`      | The callback creates a session           | The user id and the display name | `{ "clientAddress" }`           |
    | `session.signedOut`     | `POST /api/auth/sign-out` with a session | The user id and the display name | `{ "clientAddress" }`           |
    | `session.signInRefused` | The callback is refused                  | `anonymous`, null                | `{ "reason", "clientAddress" }` |
    - `reason` is one of `otherTenant`, `invalidToken`, `invalidState` or
      `providerError`. The last covers a person who cancels at the identity
      provider, and a provider error.
    - `application_id`, `entity_id`, `record_id` and `process_instance_id` are
      null.
    - `clientAddress` is always present. It is the address as text, with an
      IPv4-mapped IPv6 address written as IPv4, or `null` when the server sees
      no address.
    - A `session.signedIn` or `session.signedOut` record commits before its
      action takes effect. If it cannot be written, the callback fails and
      creates no session, or sign-out fails with a `500` and the session stays.
      If a `session.signInRefused` record cannot be written, the server logs a
      warning and the callback still redirects to the shell's sign-in page.
    - `otherTenant` means the callback's `state` came from a sign-in that
      another tenant's client started.
    - They never hold a token, a cookie or the user agent.
    - A session that expires writes nothing. A sign-out with no session writes
      nothing.

  - An index on `(entity_id, record_id, occurred_at desc, id desc)` serves
    the [record history](record-api.md#audit-records-and-history).
  - A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE`
    statement trigger raise an exception with SQLSTATE `23001`
    (`restrict_violation`), so no row can be changed or removed, even by
    raw SQL.

- **Sequence counters.** `axis.sequence_counters` holds one row per
  [sequence](resource-files.md#sequences) and period. The row is created by
  the first number of the period.

  | Column           | Type      | Meaning                                            |
  | ---------------- | --------- | -------------------------------------------------- |
  | `sequence_id`    | `uuid`    | The `sequence` resource `id`                       |
  | `application_id` | `uuid`    | The application `id`                               |
  | `period`         | `integer` | The UTC year, or 0 when the format has no `{yyyy}` |
  | `last_value`     | `bigint`  | The last number handed out                         |
  - The primary key is `(sequence_id, period)`.
  - For a module sequence, `sequence_id` is the id derived
    for the application, see the module resource ids under physical names. The
    compiler builds that id into the model. So two
    applications that use one module get separate `axis.sequence_counters`
    rows and count on their own.
  - Taking a number is one
    `INSERT ... ON CONFLICT DO UPDATE SET last_value = last_value + 1 RETURNING last_value`
    in the caller's transaction.
  - Its row lock makes concurrent creates of the same sequence wait for each
    other. A create that rolls back uses no number, so there are no gaps.

## Publish requests and release history

`Axis.Configuration` owns both tables, in the `axis`
schema of each tenant database. Both tables are built, and so are the
[publisher](administration.md#publisher), the release history and the authoring
routes: validate, publish, preview and rollback. The server inserts `validate`,
`publish` and `rollback` requests through the
[authoring API](authoring.md#publishing). A preview is a `publish` request with
the target `preview`. See
[D47](../decisions/d47-publishing-from-studio.md) and the
[authoring API](authoring.md#publishing).

### Publish requests

`axis.publish_requests` holds one row per request. The server inserts a row
over the runtime role, for `validate`, `publish`, `rollback` and `test`. Only the publisher changes a row,
over the schema role.
An index on `(application_id, status, requested_at, id)` serves the claim.

| Column              | Type                | Meaning                                                                                                                                |
| ------------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                | `uuid`              | Primary key, a version 7 UUID                                                                                                          |
| `application_id`    | `uuid`              | The application `id`                                                                                                                   |
| `kind`              | `text`              | `validate`, `publish`, `rollback` or `test`                                                                  |
| `target`            | `text`              | `tenant` or `preview`. It defaults to `tenant`. See the preview note below |
| `status`            | `text`              | `queued`, `running`, `succeeded` or `failed`                                                                                           |
| `snapshot`          | `jsonb`, null       | For `validate`, `publish` and `test`, an array of `{ file, version, content }`, one for every `*.json` file of the folder. Null for `rollback` |
| `release_id`        | `uuid`, null        | For `rollback`, the release to activate. For `publish`, set when the release is stored                                                 |
| `label`             | `text`, null        | The optional label, up to 100 characters                                                                                               |
| `note`              | `text`, null        | The optional note, up to 2000 characters                                                                                               |
| `requested_by`      | `text`              | The requesting user's id                                                                                                               |
| `requested_by_name` | `text`, null        | The user's display name at the time                                                                                                    |
| `requested_at`      | `timestamptz`       | When the request was stored                                                                                                            |
| `claimed_by`        | `text`, null        | The publisher that holds the claim                                                                                                     |
| `lease_until`       | `timestamptz`, null | When the claim runs out unless the publisher renews it                                                                                 |
| `claims`            | `integer`           | How many times a publisher claimed the request, from 0                                                                                 |
| `started_at`        | `timestamptz`, null | When it was first claimed                                                                                                              |
| `finished_at`       | `timestamptz`, null | When it reached `succeeded` or `failed`                                                                                                |
| `diagnostics`       | `jsonb`, null       | The diagnostics, as an array                                                                                                           |
| `scenario_names`    | `jsonb`, null       | For `test`, the names of the scenarios to run, as an array of strings in the order sent. Null means every scenario, and it is null for every other kind. See [Test](authoring.md#test) |
| `scenario_results`  | `jsonb`, null       | For `test`, an array of `{ name, resourceId, file, outcome, durationMs, step, path, message }`, one for each scenario that ran, in file order. The publisher sets it, to an empty array when no scenario ran. Null for every other kind. See [the request](authoring.md#the-request) |

- **A snapshot.** `content` is the file's text as a JSON string, not an embedded
  object, so the file reads back exactly as the author saw it. `version` is the
  SHA-256 of the file's bytes, as in [Versions](authoring.md#versions). A later
  edit never changes a stored snapshot. The publisher compiles the snapshot from
  these texts alone.
- **Claim.** The publisher takes the oldest `queued` `publish`, `validate` or `rollback` row of an application with
  `FOR UPDATE SKIP LOCKED`, sets `running`, `claimed_by`, `lease_until` and
  adds 1 to `claims`. It skips an application that has a `running` row.
  A `running` row whose `lease_until` has passed is claimed again the same way.
  A row with `claims` of 3 and an expired lease becomes `failed` with `AXC0215`.
- **Test requests.** A `test` row has its own queue for each
  application: at most one runs at a time, oldest first, and it does not wait
  behind a `publish`, `validate` or `rollback` row. The claim of each queue
  skips an application only for a `running` row of the same queue. The row has
  the target `tenant`, no `release_id` and no audit record.
  See [test](authoring.md#test).
- **Preview target.** A row with `target` `preview` is
  stored in the session tenant's database and run against that tenant's preview
  tenant, see [preview](authoring.md#preview). Its history row lives in the
  preview tenant's `release_activations` and keeps this row's `id` as
  `request_id`. That `request_id` points at a request in another database, so no
  foreign key can hold it. The publisher sets the row `failed` with `AXC0216`
  when the main tenant no longer has a preview tenant. The publisher claims only
  a `publish` row with target `preview`. A `validate` or `rollback` row with that
  target is never claimed. While the preview tenant exists but the publisher does
  not serve it, the row stays `queued`.
- **Final.** A `succeeded` or `failed` row is never changed again.
- **Size and age.** No limit on the size of a snapshot is set, and nothing
  deletes old requests. Large folders or many requests grow the table. A later
  decision sets both.

### Release history

`axis.release_activations` holds one row per activation, and is append-only.

| Column           | Type          | Meaning                                                              |
| ---------------- | ------------- | -------------------------------------------------------------------- |
| `id`             | `uuid`        | Primary key, a version 7 UUID                                        |
| `release_id`     | `uuid`        | The activated release                                                |
| `application_id` | `uuid`        | The application `id`                                                 |
| `number`         | `integer`     | The release number: 1, 2, 3… per application                         |
| `content_hash`   | `varchar(64)` | The release's content hash                                           |
| `activated_at`   | `timestamptz` | When the activation committed                                        |
| `actor`          | `text`        | A user id, `startup`, `operator`, or `unknown` for a backfilled row  |
| `actor_name`     | `text`, null  | The user's display name, or null                                     |
| `label`          | `text`, null  | The label of the publish, or null                                    |
| `note`           | `text`, null  | The note of the publish, or null                                     |
| `request_id`     | `uuid`, null  | The publish request, or null for a startup and `axis-admin activate` |

- **Append only.** A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE
TRUNCATE` statement trigger raise SQLSTATE `23001`, as for `audit_records`.
  The runtime role has `SELECT` only.
- **Numbers.** A release gets its number the first time it is activated in the
  tenant. Every later row of that release holds the same number, and no two
  releases of one application share one. The activation takes the number in
  the transaction that holds the schema lock, so two activations cannot give
  one number to two releases.
- **No copies.** Activating the release that is already active adds no row and
  keeps the number. A restart under `scripts/dev.sh` and a request that runs
  again therefore add nothing.
- **Every path.** Startup activation, `axis-admin activate`, a publish and a
  rollback each add a row. The actor is the user for a publish and a rollback,
  `startup` for a startup and `operator` for `axis-admin activate`. A publish
  passes its user, label, note and request to the activation, which stores them
  as given. A rollback passes its user and its request, and no label and no note.
  The release keeps the number it got the first time.
- **Backfill.** The migration that creates the table adds one row for each
  application's current active release, with number 1, the actor `unknown`, the
  activation time of `axis.active_releases` and no request. Without it the
  release that is live today would have no number and no row to roll back to.
  Older releases that are not active are not in the history, so no rollback can
  name them.
- **Per tenant.** Numbers belong to one tenant. The same content can have
  different numbers in two tenants.

### Rollback refusals

A `rollback` activates the stored release as it is and never compiles it
again. The publisher refuses it, and the request ends `failed` with one of these
diagnostics. Nothing is changed. Every other refusal of activation applies too,
such as `AXC0015` for a field that the release lacks and the tables hold.

| Case                                                                                                                  | Code      |
| --------------------------------------------------------------------------------------------------------------------- | --------- |
| A migration in [`axis.applied_migrations`](migrations.md#applied-migrations) was applied after that release and is missing from it | `AXC0174` |
| An extension package version that the release pins is not installed under `Extensions:Path`                           | `AXC0146` |

The second check is the start check of
[extension packages](extension-packages.md). Both codes have the meaning they
have at activation. See also [rollback](authoring.md#rollback).

### Audit records of publishing

Each `publish` writes one record in `axis.audit_records`, in the
transaction that sets the request's final status. So does each `rollback`. The actor is the requesting
user, and `application_id` is the application `id`. A
preview request writes no audit record. The preview tenant's release history
records who published it.

| Action                   | When                   |
| ------------------------ | ---------------------- |
| `release.published`      | A `publish` succeeded  |
| `release.publishFailed`  | A `publish` failed     |
| `release.rolledBack`     | A `rollback` succeeded |
| `release.rollbackFailed` | A `rollback` failed    |

`details` is `{ requestId, releaseId, number }`. `releaseId` and `number` are
null when the request failed before a release was known. `number` is the
release's number in the [release history](#release-history), and it is also null
when the release was never activated. A `validate` writes no audit record.
