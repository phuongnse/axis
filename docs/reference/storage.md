# Storage

This page says where Axis keeps its data and how it changes the schema: the system tables and the
entity tables of each tenant database, their physical names and column types, the platform database,
the database roles, the schema planning of activation, the audit records, and the publish requests
and release history. Migrations and the recompute of stored computed fields are in
[migrations](migrations.md), and child tables in [child collections in storage](child-tables-and-computed-columns.md).

## System tables

Each tenant database holds the system tables of Axis in its `axis` schema. Each module owns its
tables and manages them with its own EF Core context and migrations, and keeps its migration history
in a table of its own, `axis.__<module>_migrations`, so the contexts that share the schema do not
collide. The tables of the [platform database](#platform-database) are managed the same way, by EF
Core migrations shipped with Axis.

| Table                                | Module               | One row is                                                                                                                                                                  |
| ------------------------------------ | -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `axis.releases`                      | `Axis.Configuration` | A stored release                                                                                                                                                            |
| `axis.release_resources`             | `Axis.Configuration` | A file of a release. A module's files keep their `modules/<id>/` path, see [modules](modules-and-templates.md#modules)                                                      |
| `axis.release_packages`              | `Axis.Configuration` | An extension package version that a release pins, as release id, package id and version, unique per release and package                                                     |
| `axis.active_releases`               | `Axis.Configuration` | The active release of an application `id`, with the application's name, unique ignoring letter case                                                                         |
| `axis.active_sites`                  | `Axis.Configuration` | A site path of an active release, as application `id` and path, unique on path                                                                                              |
| `axis.release_computed_fingerprints` | `Axis.Configuration` | The hash of an entity's computed definitions in a release, see [recomputing](migrations.md#recompute-tables)                                                                |
| `axis.release_activations`           | `Axis.Configuration` | An activation, with its release number, see [release history](#release-history)                                                                                             |
| `axis.publish_requests`              | `Axis.Configuration` | A request that Studio stores and the publisher runs, see [publish requests](#publish-requests)                                                                              |
| `axis.application_settings`          | `Axis.Configuration` | A setting of an application that lives in no resource file, keyed by the manifest's `id`. Today that is [auto-accept](ai-authoring-in-studio.md#auto-accept). No row means the defaults|
| `axis.authoring_requests`            | `Axis.Configuration` | A request that an author sends and the agent host works on, see [authoring request storage](authoring-request-storage.md#authoring-requests)                                                                   |
| `axis.authoring_request_steps`       | `Axis.Configuration` | A step of an authoring request, in order                                                                                                                                    |
| `axis.provisioned_entities`          | `Axis.Data`          | A provisioned entity, with its application and table                                                                                                                        |
| `axis.provisioned_enum_values`       | `Axis.Data`          | A recorded enum value                                                                                                                                                       |
| `axis.sequence_counters`             | `Axis.Data`          | The last number of a sequence and period, see [sequence counters](#sequence-counters)                                                                                       |
| `axis.applied_migrations`            | `Axis.Data`          | A migration applied once in the tenant, see [applied migrations](migrations.md#applied-migrations)                                                                          |
| `axis.computed_field_errors`         | `Axis.Data`          | A record and field whose computed expression failed, see [recomputing](migrations.md#recompute-tables)                                                                      |
| `axis.pending_computed_fields`       | `Axis.Data`          | An entity whose recompute the worker finishes in batches, see [recomputing](migrations.md#recompute-tables)                                                                 |
| `axis.record_imports`                | `Axis.Data`          | An upload of a [record import](import-export.md#import), with its file until it is deleted, its format (`csv` or `xlsx`), its state and the errors of its dry run or commit |
| `axis.audit_records`                 | `Axis.Data`          | An audited action, see [audit records](#audit-records)                                                                                                                      |
| `axis.sessions`                      | `Axis.Data`          | A signed-in session                                                                                                                                                         |
| `axis.given_roles`                   | `Axis.Data`          | A role that an administrator gave a person                                                                                                                                  |
| `axis.files`, `axis.file_contents`   | `Axis.Data`          | The details and the bytes of an uploaded file of a [file field](file-fields.md#storage-and-clean-up)                                                                        |
| `axis.user_profiles`                 | `Axis.Data`          | A user who signed in, see [user profiles](notifications.md#user-profiles)                                                                                                   |
| `axis.process_*` and the others      | `Axis.Processes`     | Process instances, steps, tasks, staged calls, events, notifications, emails, webhook deliveries, receipts and work items, see [process tables](process-tables.md#tables)   |

The tables of `Axis.Processes` include `axis.process_instances`, `axis.process_step_history`,
`axis.process_tasks`, `axis.connector_calls`, `axis.package_calls`, `axis.process_call_attempts` (one
row per attempt and per lookup of a connector or package call), `axis.events` (one row per event
occurrence that a step raised), `axis.process_event_matches` (one row per use of an occurrence by a
`waitEvent` step), `axis.notifications` (one row per in-app notification and recipient),
`axis.notification_reads` (one row per notification and user who read it), `axis.email_outbox`,
`axis.webhook_deliveries` (one row per occurrence and webhook that a `raiseEvent` step staged),
`axis.process_start_receipts` (the stored response of each `Idempotency-Key` of a start),
`axis.endpoint_receipts` and `axis.process_work_items` (the work that workers claim and run). A row
of `axis.endpoint_receipts` holds the record and the instance of an endpoint call's
`Idempotency-Key`, the instance null for an endpoint without a `process`, with a hash of the input,
and is unique per application, endpoint, caller and key.

- **Releases are immutable.** A release and its resources are only inserted together, and the
  configuration context's change tracking makes a save fail when a stored release or resource is
  modified or deleted, or when a resource is added to a stored release. Bulk operations
  (`ExecuteUpdate`, `ExecuteDelete`) and raw SQL bypass this guard, so the
  [release trigger](#database-roles) backs it. The guard still fails first, before any SQL runs.
- **The active release store.** Other modules read and set the active release and its site paths
  only through `IActiveReleaseStore`, and never use `axis.active_releases`, `axis.active_sites` or
  the configuration context directly. The caller supplies the tenant database connection.
- **Audit records.** Other modules append and read audit records only through the contract of
  `Axis.Data`. `AuditRecords.AppendAsync` takes the caller's open transaction, and
  `AuditRecords.ListForRecordAsync` reads one record's history. A process start and each process
  step write their audit records this way, inside their own transaction.
- **Sessions.** A row of `axis.sessions` holds `id_hash` (`bytea`, the primary key, the SHA-256 hash
  of the session id), `user_id`, `display_name`, `roles` (`text[]`), `signed_in_at`,
  `last_request_at`, `authenticated_at` (nullable, the ID token's `auth_time`), `site_path`
  (nullable, a site's own host), `identity_provider` (nullable, the provider's alias) and
  `idp_session_id` (nullable, the ID token's `sid`). The server reads, writes and deletes rows, the
  worker deletes expired ones, and `tenant restore` empties the table. See
  [sign-in and the session](security.md#sign-in-and-the-session).
- **Given roles.** A row of `axis.given_roles` holds `id` (`uuid`, the primary key), `user_id`,
  `application_id` (null for a platform role) and `role` (for a platform role, its kind, such as
  `operator`), with a unique index on those three that counts nulls as equal. A session reads its
  person's rows at every request, see [roles](user-administration.md#roles).
- **Files.** A row of `axis.files` holds `id` (the primary key), `application_id`, `entity_id`,
  `field_path` (the field name, or `collection/field` for a field of a child entity),
  `owner_record_id` (null until the file is attached), `name`, `media_type`, `size`, `sha256`
  (`bytea`), `storage_kind` (`database`), `state` (`pending`, `attached` or `deleted`),
  `uploaded_by` and `uploaded_at`. A row of `axis.file_contents` holds the bytes (`content`,
  `bytea`) of the file with the same `file_id`, and is deleted with it. The server reads, inserts,
  updates and deletes their rows.
- **User profiles.** A row of `axis.user_profiles` holds the display name, email, locale and the
  first and last sign-in of a user. Each sign-in upserts it through `UserProfiles.UpsertAsync`.

## Entity tables

Each entity has a table in the tenant database, in the `entities` schema, apart from the `axis`
system tables. `Axis.Data` plans the tables, as [schema planning](#schema-planning) says, and applies
the plan.

- **One transaction.** Plan and apply run together in one transaction. One fixed advisory
  transaction lock (`pg_advisory_xact_lock`) serializes entity schema changes, so the catalog and
  records read for the plan are still current when it is applied.
- **Records with the DDL.** The provisioning records are written in the same transaction as the
  DDL. When the plan has any diagnostic, the transaction is rolled back and nothing is applied.
- **Runtime writes go on.** The lock does not block runtime writes to entity tables, so a statement
  can still fail, for example adding a `NOT NULL` column after a row was inserted following the
  catalog read. Any failing statement rolls back the whole transaction, DDL and records, and the
  exception propagates.
- **SQL safety.** The data module builds every SQL statement for entity data from compiled
  metadata. It resolves and quotes the identifiers, and values are always parameters, so
  configuration can never supply raw SQL. Request text, such as the entity segment, `sort` and
  `values` names, is matched against the model's declared names and never used as an identifier.

### Physical names

Table and column names are derived from stable ids and names, never from labels, so renaming a
label, or renaming an entity while keeping its `id`, never touches storage.

| Object                       | Name                                                                      | Length     |
| ---------------------------- | ------------------------------------------------------------------------- | ---------- |
| Table                        | `e_` + entity `id` as 32 lowercase hex characters                         | 34         |
| Primary key column           | `id` (`uuid`)                                                             | 2          |
| Version column               | `version` (`bigint`)                                                      | 7          |
| Computed release column      | `computed_release_id` (`uuid`, null), on tables that are not child tables | 19         |
| Field column                 | `f_` + field name in lowercase                                            | at most 62 |
| Primary key                  | `pk_` + table                                                             | 37         |
| Unique constraint            | `uq_` + table + `_` + hash of the column name                             | 54         |
| Foreign key                  | `fk_` + table + `_` + hash of the column name                             | 54         |
| Index                        | `ix_` + table + `_` + hash of the column list                             | 54         |
| Trigram index                | `ix_` + table + `_` + hash of `trgm:` + column name                       | 54         |
| Junction table               | `m_` + entity `id` + `_` + hash of the field name                         | 51         |
| Junction foreign key / index | `fk_` or `ix_` + junction table + `_` + first 8 hex of the hash           | 63         |

- **The hash** is the first 16 lowercase hex characters of SHA-256 over the UTF-8 column name. For an
  index it is over the column names joined by commas, in order. For a trigram index it is over
  `trgm:` and the column name, so its name differs from a plain index on the same column. For a
  junction table it is over the field name in lowercase. A junction table's foreign keys and index
  use only the first 8 hex characters of the hash, because the full hash after the 51-character
  table name would be 71 bytes.
- **No truncation.** Names are ASCII and at most 60 characters, so every identifier fits
  PostgreSQL's 63-byte limit by construction and is never truncated. Identifiers are always
  double-quoted in SQL.
- **Module resource ids.** The id of a module resource inside an application is a UUID v5 (RFC
  9562), whose namespace is the application `id` and whose name is the resource's own declared id,
  as lower-case hyphenated UTF-8 text. The compiler builds the derived id into the model. Activation
  provisions a module entity's table for each application that uses the module: the table is `e_` +
  the derived id, and `axis.provisioned_entities` records the application that uses it. So two
  applications in one tenant that use one module get different `e_` tables, and `AXC0018` never
  fires between them. The derived id stays the same across module versions, so an upgrade maps onto
  the same table. See [modules](modules-and-templates.md#modules).

### Column types

Each field type maps to one PostgreSQL type, spelled as `format_type` renders it, so a catalog column
matches by string equality. A `sensitive` field's column is `bytea`, whatever its type.

| Field type         | Column type                                                                                                                          |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `text`             | `character varying(n)` with `maxLength`, otherwise `text`                                                                            |
| `integer`          | `bigint`                                                                                                                             |
| `decimal`          | `numeric(p,s)` with `precision` (`s` is 0 when `scale` is omitted), otherwise `numeric`                                              |
| `boolean`          | `boolean`                                                                                                                            |
| `date`             | `date`                                                                                                                               |
| `date-time`        | `timestamp with time zone`                                                                                                           |
| `enum`             | `text`. The values are recorded, not enforced by a `CHECK`                                                                           |
| `reference`        | `uuid` with a foreign key to the target table's `id`                                                                                 |
| `child-collection` | No column on the owner table. See [child tables](child-tables-and-computed-columns.md#child-tables-and-computed-columns)             |
| `money`            | `numeric(p,s)`, as `decimal` with its defaults. The currency is the field's, or the `currencyField` column's                         |
| `file`             | `uuid` with a foreign key to `axis.files (id)`. See [file fields](file-fields.md#storage-and-clean-up)                               |
| `references`       | No column on the owner table. A junction table holds `owner_id`, `target_id` and `position`, see [junction tables](#junction-tables) |

## Platform database

The platform database holds the tenant directory that every host reads: the tenants, their hosts,
bindings and keys, and the provisioning jobs. `Axis.Admin` writes it, and the server and the worker
read it. The design is in [tenant directory](../architecture.md#tenant-directory).

- **Under both tenant sources.** The server creates the platform tables at start, sets the
  read-only grant, and keeps its data protection keys there.
- **Under the directory source.** Under `Tenancy:Source=directory` the server also reads the
  `active` tenants, their hosts and their bindings over `ConnectionStrings:Platform`, and the worker
  reads the same rows over the same role and creates no tables. Both read them again every
  `Tenancy:RefreshInterval`.
- **Secrets in memory.** The server and the worker keep each password, the client secret and each
  `secret` binding encrypted in memory, and decrypt one only when a connection opens, the sign-in
  client is set up or a binding is resolved. Neither reads any `schema_password`.
- **The platform schema.** Its EF Core migrations ship with Axis, with their history in
  `platform.__platform_migrations`. The server applies them at start over
  `ConnectionStrings:PlatformSchema` under both tenant sources. `Axis.Admin migrate` applies them too,
  before it migrates the tenants, so an upgrade that runs `migrate` first has every new platform
  table before the new server starts. `migrate` does not run the reader role's grants, which the
  server runs at its next start.
- **Read-only hosts.** The server reads the directory over `ConnectionStrings:Platform`, whose role
  has `USAGE` on the `platform` schema and `SELECT` on its tables, and nothing else. The server sets
  this in one transaction after the migrations, and revokes any wider right the role had there. The
  step holds an advisory lock until it ends, so servers that start together run it one after the
  other. It refuses a role that is the role of `ConnectionStrings:PlatformSchema`. It does not check
  whether the role is a superuser, owns the schema or inherits rights through membership. The role
  gets no right at all on `platform.audit_records`, and the same step revokes the right that the
  default privileges would give it there.
- **Write grants.** The reader role also gets `INSERT` on `platform.data_protection_keys`; `INSERT`,
  `UPDATE` and `DELETE` on the [usage](tenant-quotas.md#usage) tables, on `platform.tenant_leases`
  and on `platform.operator_sessions`; `INSERT` on `platform.jobs`, where the provisioning API queues
  its jobs, and on `platform.tenant_host_requests`; and `INSERT` and `UPDATE` on
  `platform.tenant_daily_counters`; and on no other table. Key rotation happens while the server
  runs, so the server must add data protection keys, but it never updates or deletes one. The Studio
  write lock is a PostgreSQL advisory lock, which needs no grant, see
  [authoring API](authoring.md#versions).
- **Data protection keys at start.** The server decrypts every stored data protection key with
  `Platform:SecretKey`, and then with `Platform:PreviousSecretKey` when that is set. A key that
  decrypts with neither stops the start, and the error names the setting or settings it tried. A
  replica with another `Platform:SecretKey`, and no previous key that matches, fails this way.
- **The writers.** `Axis.Admin` writes the directory over `ConnectionStrings:PlatformSchema`, see
  [administration](administration.md). The server also writes
  `PUT /api/cell/tenants/{id}/quotas` over it, on an unpooled connection for that write alone, and
  uses it for nothing else but the platform migrations at start. `platform rekey`
  is the one writer that updates encrypted keys, over the schema role, see
  [installation key](tenant-setting-commands.md#installation-key).

The tables:

| Table                                | Columns                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `platform.tenants`                   | The tenant `id`, `status` (`provisioning`, `active`, `suspended` or `readOnly`), `version`, the database host, port and name, the runtime and schema user names, the encrypted runtime and schema passwords, the OIDC authority, client id and encrypted client secret, the claim names, the endpoint audience, the service audience, the `schema_version`, and `preview_of`, the id of the main tenant of a preview tenant, which is unique and refers to `platform.tenants`. Also the sign-in profile (`standard` or `fapi2`); two encrypted client keys, the current and the previous one, each with its key id, where a previous key needs a current key; the encrypted secrets of the realm's activation client and users client; `platform_accounts`, whether the realm's accounts may sign in to the platform; the [legal links](tenant-setting-commands.md#legal-links) `privacy_url`, `terms_url` and `terms_version`, each null when not set, where the terms address and the version are set together; and `delete_after`, a nullable `timestamptz`, the time after which the purge removes the tenant, set while a [deletion](tenant-lifecycle.md#deleting-a-tenant) is scheduled |
| `platform.tenant_hosts`              | The tenant id, the host and `site_path`, the lower case path of the site the host belongs to, or null. The host is unique on `lower(host)` across all tenants, and a non-null `site_path` is unique per tenant                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `platform.tenant_bindings`           | The tenant id, the binding name, the `kind` and the value: a URL for a `url` binding, an encrypted value for a `secret` binding. The name is unique per tenant ignoring letter case                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| `platform.tenant_platform_providers` | The tenant id and the provider `name`, the primary key, the `type` (`oidc` or `saml`), the names of the issuer, client id, client secret, client key and metadata bindings, the claim names for the display name, email and locale, the roles claim and `role_values`, a `jsonb` object that maps a roles claim value to a platform role. It holds no secret. Rows go with their tenant                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `platform.data_protection_keys`      | The key's `id`, its `friendly_name`, unique, and the encrypted key XML. The server only inserts rows and never updates or deletes one, `platform rekey` updates the encrypted XML, and every server replica reads them                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `platform.tenant_data_keys`          | The tenant id, the `kind` (`data` or `lookup`), the `number` and the encrypted key, the primary key being the first three. The kind is checked, the number is at least 1, and a row goes with its tenant. `platform rekey` updates the encrypted key. See [data encryption](data-encryption.md#tenant-keys)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `platform.tenant_backup_keys`        | The tenant id, the primary key, and the tenant's backup key, wrapped by the cell's backup key. A cell's `tenant create` and `tenant restore` write a row, and `tenant backup` and `tenant restore` read it. A row goes with its tenant, and `platform rekey` does not touch it. See [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `platform.tenant_backups`            | The tenant id, the primary key, and `last_success_at` (`timestamp with time zone`), when the last `cell backup` upload of the tenant succeeded. A tenant that never succeeded has no row, and a failed backup keeps the time it had. A row goes with its tenant. See [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `platform.tenant_leases`             | The `lease_id`, the primary key, the tenant id, the `quota` key, the `holder` (the host and process) and `expires_at` (`timestamp with time zone`). One row is one piece of work that runs under a concurrency quota, see [work at once](tenant-quotas.md#work-at-once). A lease lasts 60 seconds and its holder renews it every 20. The reader role inserts, updates and deletes rows. A row goes with its tenant                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `platform.operator_sessions`         | The `id_hash`, the primary key (`bytea`, the SHA-256 hash of the random session id that the operator's cookie holds), the operator's id (`operator_id`, the `sub` of the operator realm), `display_name`, the `roles` of the access token at sign-in (`text[]`), and `signed_in_at` and `last_request_at` (`timestamp with time zone`). One row is the session of a platform operator who signed in to the [cell console](cell-console.md#sign-in), and belongs to no tenant. The row never holds the session id or a token. The reader role inserts, updates and deletes rows. A new sign-in deletes the rows that are past the 15-minute idle limit or the 8-hour absolute limit                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `platform.tenant_daily_counters`     | The tenant id, the UTC `day`, the `counter` name and the `value` (`bigint`, at least 0), the primary key being the first three. A cell's worker counts the emails it sends in the counter `emails`, see [emails a day](tenant-quotas.md#emails-a-day). A row goes with its tenant. The reader role inserts and updates rows and never deletes one                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `platform.tenant_quotas`             | The tenant id and the `quota` key, the primary key together, the `value` (`bigint`, null means no limit) and `updated_at`. One row is one quota the tenant overrides, and a quota with no row takes the cell's default. The value is null or at least 0, and a row goes with its tenant. Hosts read the rows with the directory, see [tenant quotas](tenant-quotas.md#quotas). The provisioning API writes them over the schema role                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `platform.tenant_site_legal_links`   | The tenant id and the lower case `site_path`, the primary key together, and the `privacy_url` and `terms_url` of that site, of which at least one is set. A site does not need to exist, and a row goes with its tenant. See [legal links](tenant-setting-commands.md#legal-links)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `platform.jobs`                      | One provisioning job: the `id`, the `kind` (`suspend`, `resume`, `read-only`, `create`, `export`, `delete`, `cancel-deletion` or `purge`), the `tenant_id`, which has no foreign key so that a job outlives its tenant, the `state` (`queued`, `running`, `succeeded` or `failed`), the `steps` (`jsonb`, an array of `{ name, state, error }`), the `error`, the `input` (`jsonb`: a create's `{ application, administrator }`, a purge's `{ actor }`, which the publisher writes when it queues the job, and null for the other kinds; the job read never returns it), the `idempotency_key`, the `request_hash` (`text`, the hex SHA-256), the `actor`, `created_at`, `started_at` and `finished_at`, and the lease: `claimed_by`, `lease_until`, `claims` and `waits` (the times the job waited for a tenant lease, which do not count as claims). The [publisher](administration.md#publisher) claims and runs the jobs under the directory source, see [provisioning API](tenant-lifecycle.md#provisioning-api)                                                                                                                                                                         |
| `platform.tenant_host_requests`      | A request for a host of a tenant's own: the tenant id, the `host` (lower case) and `requested_at` (`timestamp with time zone`), the primary key together, the `token` (32 random bytes as base64url), the `state` (`pending`, `verified` or `expired`), `expires_at` (7 days after `requested_at`), `checked_at` (null until a DNS check runs) and the `actor`, the operator's id. A pending request does not hold the host, only `platform.tenant_hosts` does, and a request that finds none pending inserts a new row. The reader role inserts rows and never updates or deletes one, and the [publisher](administration.md#publisher) updates `state` and `checked_at` over the schema role. A row goes with its tenant. See [hosts](tenant-lifecycle.md#hosts)                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `platform.deleted_tenants`           | The tombstone of a purged tenant, a preview tenant's included: the tenant id, the primary key, `deleted_at` (`timestamp with time zone`), the `actor` that asked for the deletion and the `hosts` (`text[]`) the tenant had, with no other data. It has no foreign key, so a row outlives its tenant. See [deleting a tenant](tenant-lifecycle.md#deleting-a-tenant)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `platform.restore_checks`            | One run of `cell restore-check` after its pick: the `id` (`uuid`), the primary key, `ran_at` (`timestamp with time zone`), the time the run started, the source `tenant_id`, which has no foreign key so that a row outlives its tenant, and `passed` (`boolean`). `ran_at` has an index. The command inserts a row over the schema role, and the provisioning API reads the newest. See [weekly restore check](tenant-backup-and-restore.md#weekly-restore-check) |
| `platform.tenant_usage`              | The tenant id and the UTC `day`, the primary key together, and the usage of that day. The reader role writes it. See [usage](tenant-quotas.md#usage) |
| `platform.tenant_usage_latest`       | The tenant id, the primary key, and the newest storage sample and failed work counts of the tenant. The reader role writes it. See [usage](tenant-quotas.md#usage) |
| `platform.audit_records`             | The `id`, `occurred_at`, `command`, the `tenant_id`, which can be empty and has no foreign key, `os_user`, `machine`, `changed` (`text[]`), the names of what changed, and `actor`, which can be empty and names the operator or machine caller of a job. Triggers reject every update, delete and truncate with SQLSTATE `23001`. `Axis.Admin` writes it, and so does the quotas route of the provisioning API, see [platform audit records](tenant-setting-commands.md#platform-audit-records)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |

- **Tenant version.** The tenant `version` grows on every change to the tenant, its hosts, its
  bindings or its platform providers.
- **Schema version.** The `schema_version` is text: the id of the newest EF Core migration across
  the configuration, data and processes contexts that `Axis.Admin` applied to the tenant database,
  empty until the first migration. A host serves a tenant only when this value is the one the host
  was built for, see [administration](administration.md#migrate).

### Secrets in the platform database

The platform database stores each tenant secret encrypted with the installation key, and nowhere
else. The [cryptographic inventory](keys-and-data-protection.md#cryptographic-inventory) lists every
key.

| Secret                                                                                                                                                                     | Table                           | Field name                                                                 |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------- | -------------------------------------------------------------------------- |
| The runtime and schema database passwords                                                                                                                                  | `platform.tenants`              | `runtime_password`, `schema_password`                                      |
| The OIDC client secret, the realm's platform client for a tenant that `tenant create` made                                                                                 | `platform.tenants`              | `oidc_client_secret`                                                       |
| The SMTP password of the tenant's own [mail settings](notifications.md#mail-settings)                                                                                      | `platform.tenants`              | `smtp_password`                                                            |
| The secrets of the realm's activation client and users client, written by `tenant create`, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step) | `platform.tenants`              | `realm_activation_client_secret`, `realm_users_client_secret`              |
| The client keys under the `fapi2` sign-in profile, see [FAPI 2.0 profile](fapi2-profile.md)                                                                                | `platform.tenants`              | `oidc_client_key`, with the key id                                         |
| The value of a `secret` binding                                                                                                                                            | `platform.tenant_bindings`      | `binding:` followed by the binding name in lower case                      |
| A data protection key                                                                                                                                                      | `platform.data_protection_keys` | `data_protection_key:` followed by the key's `id`, with an empty tenant id |
| The data keys and lookup keys of a tenant                                                                                                                                  | `platform.tenant_data_keys`     |                                                                            |

A tenant of a cell also has a backup key in `platform.tenant_backup_keys`, wrapped by the cell's key
(`Cell:BackupKeyFile`) and not by the installation key.

- **Encrypted format.** An encrypted value is a `bytea` that holds a 12-byte nonce, the ciphertext
  and a 16-byte tag. The cipher is AES-256-GCM with the installation key `Platform:SecretKey`, see
  [tenancy settings](host-settings.md#tenancy-settings).
- **Associated data.** The associated data is the UTF-8 bytes of the tenant id, one zero byte, and
  the UTF-8 bytes of the field name, so a value copied to another tenant or field does not decrypt.
  Every writer must use these exact bytes and names. A data protection key uses an empty tenant id,
  so its associated data starts with the zero byte, and since a tenant id is never empty a key and a
  tenant field cannot be swapped.
- **Decryption.** A value is decrypted only at the moment of use, as `SecretValue` does. No API,
  log, problem details or backup manifest holds a decrypted value.
- **Key rotation.** The installation key rotates without signing anyone out and without setting a
  secret again. The hosts accept the old key as `Platform:PreviousSecretKey`, and `platform rekey`
  re-encrypts every tenant secret and every data protection key with the new key, in one
  transaction. The format and the associated data do not change. See
  [installation key](tenant-setting-commands.md#installation-key).

## Database roles

Each tenant database is used by two PostgreSQL roles, so that the code that serves requests cannot
change the schema. "Database role" always means such a PostgreSQL role, never an application `role`
resource.

- **Two database roles per tenant.** The schema database role owns the `axis` and `entities`
  schemas. The runtime database role reads and writes data, and can change nothing about the schema.
- **Two settings.** `ConnectionString` is for the runtime database role, and
  `SchemaConnectionString` is for the schema database role. See
  [tenant configuration](../architecture.md#tenant-configuration). There is no fallback to one
  connection string.
- **A scenario role.** A third kind of database role runs [test scenarios](test-scenarios.md) for
  Studio. It has `CREATEDB`, is not a superuser and owns no tenant database. Only the publisher and
  `cell restore-check` hold it, through the setting `Scenarios:ServerConnectionString`. Each creates
  one database `axis_scenario_<uuid>` for each scenario, owns it and drops it after the scenario. The
  server and the worker hold no right to create databases, and no role that may. On a shared
  PostgreSQL server every role may connect to a database by default, so the scenario role can
  connect to a tenant database, though it can read nothing there. See
  [installation](installation.md#on-premises) for how to limit that.
- **Development roles.** The integration tests create the roles `axis_schema` and `axis_runtime`,
  own each test database with `axis_schema`, run the server and the worker as `axis_runtime`, and run
  activation as `axis_schema`. Docker Compose, `scripts/dev.sh` and `scripts/e2e.sh` do the same:
  `samples/postgres/init-roles.sql` creates the roles and grants them nothing. It also creates the
  scenario role `axis_scenario`, the database `axis_preview` of the development
  [preview tenant](host-settings.md#preview-tenants), owned by its own schema role
  `axis_preview_schema`, and its runtime role `axis_preview_runtime`, all with the development
  password `axis`. The integration fixtures create `axis_scenario` too. PostgreSQL runs that file
  only on an empty volume, so an existing Compose volume needs `docker compose down -v`, which
  deletes the local data, before it has these roles, and a `test` request fails until then.

What each database role may do:

| Objects                                                                                                                                                                                                                                                                                                                                                                                                                                               | Schema database role                                             | Runtime database role                                             |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------------- |
| `entities` tables                                                                                                                                                                                                                                                                                                                                                                                                                                     | Owns them                                                        | Read and write rows                                               |
| `axis` tables that runtime writes: `process_*`, `connector_calls`, `package_calls`, `events`, `notifications`, `notification_reads`, `email_outbox`, `webhook_deliveries`, the receipts, `trigger_schedules`, `trigger_starts`, `delivery_failures`, `dead_letters`, `audit_records`, `user_profiles`, `sequence_counters`, `computed_field_errors`, `pending_computed_fields`, `sessions`, `given_roles`, `application_settings`, `record_imports`, `files`, `file_contents`, `authoring_requests`, `authoring_request_steps` | Owns them                                                        | Read and write rows                                               |
| Tables that activation writes: `active_releases`, `active_sites`, `provisioned_entities`, `provisioned_enum_values`, `applied_migrations`, `release_computed_fingerprints`, the `__*_migrations` tables                                                                                                                                                                                                                                               | Owns them                                                        | Read only                                                         |
| Release tables: `releases`, `release_resources`, `release_packages`                                                                                                                                                                                                                                                                                                                                                                                   | Insert and read. The trigger refuses update, delete and truncate | Read only                                                         |
| `publish_requests`                                                                                                                                                                                                                                                                                                                                                                                                                                    | Owns it. The publisher changes the status and the lease          | `SELECT` and `INSERT` only. It never updates or deletes a request |
| `release_activations`                                                                                                                                                                                                                                                                                                                                                                                                                                 | Insert and read. The trigger refuses update, delete and truncate | Read only                                                         |
| Sequences in `axis` and `entities`                                                                                                                                                                                                                                                                                                                                                                                                                    | Owns them                                                        | `USAGE`, `SELECT`                                                 |
| Create, alter, drop                                                                                                                                                                                                                                                                                                                                                                                                                                   | Yes                                                              | No                                                                |

- **Default privileges.** The schema database role sets default privileges: a new table in
  `entities` gives the runtime database role `SELECT`, `INSERT`, `UPDATE` and `DELETE`, and a new
  table in `axis` gives it `SELECT` only. The grant step also clears the database-wide default
  privileges that older setups set, because a per-schema default cannot narrow them.
- **Explicit grants.** The grant step holds the list of `axis` tables that runtime writes. It grants
  row access on those and revokes `INSERT`, `UPDATE`, `DELETE` and `TRUNCATE` on every other `axis`
  table, so a database with wider grants is repaired, and a new system table is read-only until
  someone adds it to the list. The list has a second category, tables where runtime may only
  insert, whose one table is `publish_requests`: the step grants `SELECT` and `INSERT` and revokes
  `UPDATE`, `DELETE` and `TRUNCATE`, so the server cannot change the status of a request.
- **Role name.** The runtime database role is the user of `ConnectionString`. The name comes from
  configuration, so a migration cannot hold it. A grant step runs over the schema connection at
  every server start, both before and after the EF Core migrations: the first run sets the default
  privileges for the tables the migrations create, and the second grants the tables that now exist.
  The step refuses a runtime database role that is the schema database role. It holds an advisory
  lock until it ends, so servers that start together run their grant steps one after the other.
- **Release trigger.** A trigger refuses every `UPDATE`, `DELETE` and `TRUNCATE` on the three
  release tables, for every database role, with SQLSTATE `23001`, so a stored release cannot be
  deleted. An `Axis.Configuration` migration creates it. It backs the change-tracking guard of
  [immutable releases](#system-tables), which fails first. A superuser or the table owner can still
  disable the trigger: it guards against mistakes by Axis, not against a database administrator.
- **Start checks.** A missing or blank setting stops the start, naming the tenant and the setting.
  So does a `ConnectionString` that names no user, two settings that name the same user, and a
  runtime database role that is a superuser, owns the `axis` or `entities` schema, or may create
  objects in either.
  - A role that is a member of the role that owns a schema counts as its owner, because it can
    `SET ROLE` to the owner. The same goes for a member of a superuser role and for a member of any
    role with `CREATE` on either schema, whatever the `INHERIT` option of the membership.
  - The message names the tenant, the role and the first failed check, in the order superuser,
    owner, `CREATE`, with `axis` before `entities`. When the right comes through another role, it
    also names that role. It never holds the connection string or the password.
  - The server runs the role check after its grant step, so a fresh database with the right roles
    starts, and again in its start-time release check, which runs for every tenant even when no
    folder is activated. It skips the check for a tenant whose database it cannot reach, as the
    release check does. The worker runs it once for each tenant, when the tenant's database counts
    as migrated and before it claims any work of that tenant.
  - The worker validates `ConnectionString` and the runtime database role. The server runs all the
    other checks as well, `SchemaConnectionString` and that the two settings name different users
    among them, because the worker holds no `SchemaConnectionString`.
  - The checks do not cover `CREATE` on the database itself, ownership of single tables or
    `CREATEROLE`.

Which connection string each step uses:

| Step                                                                                                      | Connection string                  |
| --------------------------------------------------------------------------------------------------------- | ---------------------------------- |
| EF Core migrations of configuration, data and processes                                                   | `SchemaConnectionString`           |
| Compiling a folder and storing its release                                                                | `SchemaConnectionString`           |
| Activation: migrations, provisioning, active release, site paths                                          | `SchemaConnectionString`           |
| Grants for the runtime database role, before and after the migrations                                     | `SchemaConnectionString`           |
| Schedule trigger state                                                                                    | `ConnectionString`                 |
| Seed records                                                                                              | `ConnectionString`                 |
| Publish routes of the server: insert a request, read requests                                             | `ConnectionString`                 |
| Release history route of the server: read the release history                                             | `ConnectionString`                 |
| Publisher: claim and finish a request, compile, store, activate, write the history and the audit record   | `SchemaConnectionString`           |
| Publisher: seed records                                                                                   | `ConnectionString`                 |
| Publisher, `test` request, and `cell restore-check`: create, migrate, run and drop the scenario databases | `Scenarios:ServerConnectionString` |
| Publisher, `test` request: store the status and the outcomes                                              | `SchemaConnectionString`           |
| Start release check (`AXC0146`) and the runtime database role check, in the server and the worker         | `ConnectionString`                 |
| Server requests: record API, data sources, process starts, endpoints, tasks, sites                        | `ConnectionString`                 |
| Worker                                                                                                    | `ConnectionString` only            |

The server holds both connection strings for its whole life, opens the schema connection only during
startup activation, and closes it when each tenant is done. Under `Tenancy:Source=directory` it
holds no tenant schema connection, see [under the directory source](#under-the-directory-source).

The publisher, `axis-admin publisher`, runs every publish request, and the server runs none. Under
`Tenancy:Source=configuration` it uses each tenant's `SchemaConnectionString`, so it holds the schema
role. It waits until the server has migrated the tenant database, and seeds over
`ConnectionString`. The production installs, `scripts/dev.sh`, the root Compose file and the E2E
script each start one publisher, and the last three give it the `default-preview` tenant as well, so
it runs preview requests.

### Under the directory source

Under `Tenancy:Source=directory`, `tenant create` creates the two database roles of a tenant, its
database and the ownership, with no superuser, see [administration](administration.md). Each step
runs on one host with one database role:

| Step                                                                                                | Host                                         | Database role                      |
| --------------------------------------------------------------------------------------------------- | -------------------------------------------- | ---------------------------------- |
| Create the database and the two roles                                                               | `Axis.Admin`                                 | `Admin:ConnectionString`           |
| Directory writes                                                                                    | `Axis.Admin`                                 | `ConnectionStrings:PlatformSchema` |
| Platform migrations, at start, under both tenant sources                                            | Server                                       | `ConnectionStrings:PlatformSchema` |
| Platform migrations, before the tenants are migrated                                                | `Axis.Admin migrate`                         | `ConnectionStrings:PlatformSchema` |
| Platform audit records                                                                              | `Axis.Admin`, server (quotas)                | `ConnectionStrings:PlatformSchema` |
| Grants, EF Core migrations, compile, store and activate                                             | `Axis.Admin`                                 | The tenant's schema role           |
| Seeding and schedule trigger state                                                                  | `Axis.Admin`                                 | The tenant's runtime role          |
| Claim, run and finish a publish request: compile, store, activate, release history and audit record | `axis-admin publisher`                       | The tenant's schema role           |
| Seeding of a publish request                                                                        | `axis-admin publisher`                       | The tenant's runtime role          |
| Create, migrate, run and drop the scenario databases of a `test` request or a restore check         | `axis-admin publisher`, `cell restore-check` | `Scenarios:ServerConnectionString` |
| Store the status and the outcomes of a `test` request                                               | `axis-admin publisher`                       | The tenant's schema role           |
| Insert and read publish requests                                                                    | Server                                       | The tenant's runtime role          |
| Read the release history                                                                            | Server                                       | The tenant's runtime role          |
| Runtime role check                                                                                  | `Axis.Admin`, server and worker              | The tenant's runtime role          |
| Requests and work items                                                                             | Server and worker                            | The tenant's runtime role          |

- **No tenant schema connection.** The server holds no tenant schema connection, and runs no tenant
  migration and no grant step. The worker holds none either. When Studio publishes, the server only
  inserts a request over the runtime role, and `axis-admin publisher` runs it with the tenant's
  schema role. The server still holds `ConnectionStrings:PlatformSchema`, for the platform
  migrations at start and the quotas write of the provisioning API.
- **The publisher.** It reads the tenants from the directory as the server does and holds the schema
  role of every active tenant, which makes it the most valuable host to attack. Run it apart from
  the server and give it no inbound network. It has no HTTP endpoint, see
  [publisher](administration.md#publisher).
- **Tenant schema version.** Each tenant database migrates on its own, with `Axis.Admin migrate`,
  which records the version in the directory. A host serves a tenant only when the tenant's schema
  version is the host's own, see [tenant directory](../architecture.md#tenant-directory).

## Schema planning

Activation compares what a release declares with what the tenant database holds, and plans the
statements that bring the entity tables in line, or the diagnostics that say why it cannot.
`Axis.Data` compares the compiled application with a snapshot of the tenant catalog and with the
provisioning records. The planner itself has no database access: it returns diagnostics, SQL
statements and the new records to write.

- **The catalog snapshot.** It holds the entity tables, their columns, types, `NOT NULL`,
  single-column unique constraints, foreign key targets, their `ix_` indexes with whether each is
  valid, and whether the table has rows.
- **The provisioning records.** They hold each provisioned entity with its application and table,
  and each recorded enum value. They cover the application's entities and any entity recorded with
  one of its entity `id`s, whatever its application.
- **Errors.** When any diagnostic exists, the plan has no statements and no new records, and nothing
  is applied.

### Migrations first

The planner first applies the pending `renameField`, `dropField`, `dropEntity`, `fill` and `convert`
[migrations](migrations.md#migrations) to its catalog snapshot and its copy of the provisioning
records, in ordinal path order and file order, and then plans the additive changes.

- **What a migration allows.** A `convert` makes the planner accept a type change, or enum values
  the release removes, where it gives `AXC0016` without one. Switching a field between `enum` and
  another type stays `AXC0016`, even with a `convert`. Removals and incompatible changes not covered
  by a migration keep `AXC0015`, `AXC0016` and `AXC0017`.
- **Recompute.** A `fill` or `convert` clears the release stamp of the rows it changes. On a child
  entity it also increments the owner's `version` and clears the owner's stamp, for each owner of a
  changed row. Activation then recomputes those rows, see
  [recomputing computed fields](migrations.md#recomputing-computed-fields).
- **Diagnostics.** A `fill` or `convert` whose value names a field that is not a column of the
  stored table is `AXC0171` at `/changes/{i}/value`. A `fill` whose value leaves a `null` is
  `AXC0176` at `/changes/{i}/value` when activation applies it. A `convert` that fails for a stored
  row, or maps an enum value to one the field does not list, is `AXC0177` at `/changes/{i}/value`.
- **Skipped changes.** A change that does not match the stored schema is skipped: the table does
  not exist, the `from` column is missing or the `to` column already exists. The migration is still
  recorded when activation succeeds, so a fresh tenant activates a release that has migrations. A
  skip cannot lose data, because a leftover column or table still gives `AXC0015` or `AXC0017`.
- **Which entity.** A migration's `entity` name is looked up first in the application's active
  release, read from its stored resources, because that release describes the tables that hold the
  data now and only it has a dropped entity. It is then looked up in the release being activated.
- **Enum values.** A rename also renames the field in the recorded enum values, and a drop deletes
  them.

### Tables and columns

The planner creates what is missing and changes an existing column only where no data can be lost.

- **System columns.** `id`, `version` and `computed_release_id` are created with the table, or
  `id`, `owner_id` and `position` for a child table. `version` is added to an existing table other
  than a child table that lacks it as `bigint NOT NULL DEFAULT 1`, even when the table has rows,
  because the default fills them. `computed_release_id` is added to such a table as `uuid`, nullable
  and with no default, so the rows are kept and stay unstamped. System columns are never compared or
  changed otherwise: only Axis DDL creates them, and an author cannot fix them through
  configuration.
- **Missing table.** It is created with the system columns and every field column, `NOT NULL` for
  required fields and a unique constraint for unique fields. A `child-collection` field has no
  column. The entity and its enum values are recorded.
- **Child table.** The owner of a child entity is the entity whose `child-collection` field names
  it, and the compiler ensures there is exactly one. The columns of a child table are in
  [child collections in storage](child-tables-and-computed-columns.md#child-tables-and-computed-columns).
  - A missing child table is created with `id`, `owner_id`, `position` and its field columns, and no
    `version`. Its owner foreign key is added with the other foreign keys, so the owner table exists
    whatever the entity order. Adding a child collection and its child entity to an entity that
    already has rows only creates the child table, and the owner table and its rows are untouched.
  - An existing child table whose `owner_id` has no foreign key gets one. A foreign key that points
    at another table than the owner's is `AXC0016` at the owner's `/fields/{i}/target`.
  - An existing table without `owner_id` cannot become a child table, because adding a `NOT NULL`
    owner column needs a migration. It is `AXC0016` at the owner's `/fields/{i}/target`.
  - An entity that stops being a child keeps `owner_id` and `position`, which are reported as
    removed fields (`AXC0015`), like any column without a field.
- **Missing column.** It is added. `NOT NULL` is added only when the table has no rows, so a
  required field added to a table with rows is `AXC0016` at `/fields/{i}/required`, unless a pending
  `fill` covers it: the `fill` adds the column as nullable and fills it, and the planner then makes
  it `NOT NULL`. A unique field also gets its unique constraint.
- **Existing column.**
  - The type must equal the expected type. Widening text (`character varying(n)` to a larger `n` or
    to `text`) is applied. Narrowing it, including `text` to `character varying(n)`, is `AXC0016` at
    `/fields/{i}/maxLength`. Any other type difference is `AXC0016` at `/fields/{i}/type`. A field
    that becomes or stops being `sensitive` gets a new column that activation fills, see
    [changing the flag](data-encryption.md#changing-the-flag).
  - Dropping `required` drops `NOT NULL`, and dropping `unique` drops the unique constraint. Adding
    either to an existing column is `AXC0016` at `/fields/{i}/required` or `/fields/{i}/unique`. A
    pending `fill` of the field allows `required`, and the column becomes `NOT NULL` after the
    `fill`.
  - A reference column without a foreign key gets one. A foreign key that points at another table
    than the target's is `AXC0016` at `/fields/{i}/target`.
- **Enum values.** Values are compared ordinally, so letter case matters. Recorded values missing
  from the field's `values` are one `AXC0016` at `/fields/{i}/values` that lists them, and new
  values become new records and need no SQL. A column switching between `enum` and another type
  (recorded values present for a non-enum field, or absent for an enum field) is `AXC0016` at
  `/fields/{i}/type`.
- **Removed field.** A column other than the table's system columns without a matching field is
  `AXC0015` at `/fields`, naming the column.
- **Removed entity.** An entity recorded for the application but missing from it is `AXC0017` at
  `application.json` with an empty path and the entity's `id` as `resourceId`.
- **Entity owned by another application.** Tables are keyed by entity `id`, so an entity whose `id`
  is recorded for another application, for example in an application copied with only its manifest
  `id` changed, would share that application's table. It is `AXC0018` at `/id` of the entity file,
  and the entity is not planned.
- **Labels.** A label change produces no statements.

### Statement order

The planner orders its statements so that references between entities, cycles and self-references
need no further ordering:

1. The pending migration statements, as [order of statements](migrations.md#order-of-statements)
   says.
2. Every `CREATE TABLE`.
3. The `ALTER TABLE` column changes.
4. The `DROP INDEX` statements, then the `CREATE INDEX` statements.
5. Every `ADD CONSTRAINT ... FOREIGN KEY`.
6. The recompute of stored computed fields inline, or the mark that leaves them pending for the
   worker above the inline limit, see
   [recomputing computed fields](migrations.md#recomputing-computed-fields).

### Indexes

Activation builds the indexes that an entity declares and those that Axis adds itself, without
blocking a large table.

- **When an index is built.** A unique index, an index on an empty table and an index on a table with
  at most `Activation:InlineRecomputeLimit` rows are built inside the activation transaction. A new
  non-unique index on a table with more rows is left out: activation commits without it, and
  `EntityIndexBuilder` creates it later with `CREATE INDEX CONCURRENTLY`, so reads and writes on the
  table go on.
- **Invalid indexes.** The catalog snapshot reads whether each index is valid. An `ix_` index that
  PostgreSQL marks `INVALID`, for example after a cancelled concurrent build, counts as missing, so
  activation and the builder drop it and create it again.
- **The builder.** It runs without a transaction, holds the schema lock that activation takes, and
  uses the schema role. The worker cannot build, because its runtime role cannot create indexes, see
  [database roles](#database-roles). Every host that activates calls the builder after activation
  commits: `Axis.Admin activate`, the publisher's publish and activation of a stored release, and the
  server's startup activation. A publisher validate builds nothing.
- **Automatic indexes.** Every `reference` column and every child table's `owner_id` gets an `ix_`
  index without being declared, and a junction table's `target_id` index is created with the table.
  Every `searchable` field and every entity's display field gets a `pg_trgm` GIN `ix_` index on
  `lower()` of the column, named by the trigram index name, and a field that is both has one. The
  statement names the operator class as `public.gin_trgm_ops`, because `tenant create` and
  `migrate` install `pg_trgm` in `public`. A table that lacks an automatic index gets it on its next
  activation, and removing `searchable` drops the index on the next activation. The catalog reads
  such an index, which is a GIN index built on an expression, and takes its column from
  `pg_depend`.
- **Declared indexes.** An entity's `indexes` add the declared indexes, with the columns in the order
  of `fields`. A declared index over one `reference` field has the same name as the automatic index
  on that field, so they are one index, which is unique when the declared one is.
- **Changes.** The planner adds an index the release declares and the table lacks, and drops an
  `ix_` index the release no longer declares, which needs no migration. Changing the field list of an
  index is a drop and a create under the new name. An index of the same name with another `unique`
  flag is dropped and created again.
- **Unique indexes.** A unique index added to a table with rows is `AXC0016` at
  `/indexes/{i}/unique`, as for `unique`, even when all of its columns are new in the release.
  Removing `unique` from an index is allowed. A second record that repeats the values of a unique
  index is a `409`, as for a `unique` field, see [record API](record-api.md#create-update-and-delete).
- **Migrations.** A `renameField` change renames the `ix_` indexes over the column, trigram ones
  included, so a renamed field keeps its unique index. A `dropField` change drops them with the
  column, and the planner does not drop them again.

### Junction tables

A `references` field stores its ids in a junction table of its own rather than in a column.

- **Columns.** The table has `owner_id`, a foreign key to the owner that is deleted with it, and
  `target_id`, a foreign key to the target that refuses a delete while linked. Its primary key is
  both columns, and `target_id` has an `ix_` index. `position` holds the zero-based index of the id
  in the written array. The planner adds no `position` column to a junction table that lacks one.
- **Writes and reads.** The record API writes the whole set of a field in one write and reads it in
  `position` order.
- **Planning.** A junction table whose `target_id` foreign key is missing gets it, and one that
  points at another table than the field's target is `AXC0016` at `/fields/{i}/target`. Junction
  tables are not recorded in `provisioned_entities`: the planner finds them in the catalog by the
  `m_` + entity `id` prefix. Removing the field drops nothing and is `AXC0015` at `/fields`, as for
  a column.

## Audit records and sequence counters

`Axis.Data` owns the audit records, the trail of who did what, and the sequence counters that number
records. Both are system tables in the `axis` schema of the tenant database.

### Audit records

`axis.audit_records` is append-only, and each row is written in the same transaction as the action
it records. The record API, the process [start endpoint](processes.md#start-endpoint),
[process steps](process-execution.md#running-a-step), [task completions](task-api.md#completing-a-task)
and the [operator commands](process-inspection.md#operator-commands) write them, the last with the
user or service account as actor, and so do the actions that [audit actions](#audit-actions) lists.

| Column                | Type                             | Meaning                                                                                                                                                                                                                           |
| --------------------- | -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                  | `uuid`                           | A version 7 UUID                                                                                                                                                                                                                  |
| `occurred_at`         | `timestamptz`                    | When the action happened: the transaction time, `now()`, so the audit records of one transaction share it                                                                                                                         |
| `actor`               | `text`                           | A user id, `system` for the worker, or `anonymous` for `session.signInRefused`. The record API and the process start endpoint need a session, so they never write `anonymous`                                                     |
| `actor_name`          | `text`, null                     | Null in every row that Axis writes, because history keeps no names: a history view reads the name from the user's profile, see [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) |
| `action`              | `text`                           | The action, such as `record.created`                                                                                                                                                                                              |
| `application_id`      | `uuid`, null for session actions | The application `id`. A session action belongs to no application                                                                                                                                                                  |
| `entity_id`           | `uuid`, null                     | The entity `id`, when the action concerns a record                                                                                                                                                                                |
| `record_id`           | `uuid`, null                     | The record id                                                                                                                                                                                                                     |
| `process_instance_id` | `uuid`, null                     | The process instance id                                                                                                                                                                                                           |
| `details`             | `jsonb`                          | An object that depends on the action                                                                                                                                                                                              |

- **Stable ids.** Application and entity are stored by their stable resource ids, never by name, so
  a rename keeps the history.
- **History index.** An index on `(entity_id, record_id, occurred_at desc, id desc)` serves the
  [record history](record-audit.md#audit-records-and-history).
- **Append only.** A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE` statement trigger
  raise an exception with SQLSTATE `23001` (`restrict_violation`), so no row can be changed or
  removed, even by raw SQL.

### Audit actions

Besides the record and process actions, these actions write audit records. A `publish` and an
`activate` write theirs as [audit records of publishing](#audit-records-of-publishing) says.

- **Import actions.** The commit of a [record import](import-export.md#import) writes
  `records.imported`, after the `record.created` or `record.updated` audit record of each row, in the
  transaction that writes the rows. The actor is the uploader's user id. `entity_id` is set, and
  `record_id` and `process_instance_id` are null. `details` is `{ "created", "updated" }`, the
  counts of records, and holds no value.
- **Session actions.** The BFF session writes three actions, each in the tenant database of the
  resolved host, see [sign-in and the session](security.md#sign-in-and-the-session). Their
  `application_id`, `entity_id`, `record_id` and `process_instance_id` are null.

  | Action                  | When                                     | `actor`     | `details`                       |
  | ----------------------- | ---------------------------------------- | ----------- | ------------------------------- |
  | `session.signedIn`      | The callback creates a session           | The user id | `{ "clientAddress" }`           |
  | `session.signedOut`     | `POST /api/auth/sign-out` with a session | The user id | `{ "clientAddress" }`           |
  | `session.signInRefused` | The callback is refused                  | `anonymous` | `{ "reason", "clientAddress" }` |
  - `reason` is one of these:

    | Reason                | The callback                                                                                                                                                                       |
    | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
    | `otherTenant`         | Has a `state` that came from a sign-in that another tenant's client started                                                                                                        |
    | `invalidToken`        | Has an invalid ID token, including one with a nonce that the sign-in did not send                                                                                                  |
    | `invalidState`        | Has a `state` that is missing, unreadable or fails the correlation check                                                                                                           |
    | `invalidIssuer`       | Has provider metadata that names an issuer other than the tenant's `Authority`, in any profile, or is a `fapi2` callback whose `iss` is missing or is not the tenant's `Authority` |
    | `providerError`       | Reports a provider error, including a person who cancels at the identity provider                                                                                                  |
    | `providerNotAccepted` | Has a valid token whose provider the tenant's platform sign-in does not accept and no active site that shares the tenant's host allows                                             |
    | `providerNotAllowed`  | Has a valid token whose provider the site that started the sign-in does not allow, or comes from a site that is no longer active                                                   |
    | `acrNotAccepted`      | Has a valid token whose provider the site allows, but whose upstream `acr` the provider's `acr` list does not accept or the token lacks                                            |

  - `clientAddress` is always present. It is the address as text, with the last octet of IPv4 and
    the last 80 bits of IPv6 set to zero, such as `203.0.113.0` and `2001:db8:1::`. An IPv4-mapped
    IPv6 address is written as IPv4 first. It is `null` when the server sees no address.
  - A `session.signedIn` or `session.signedOut` record commits before its action takes effect. If
    it cannot be written, the callback fails and creates no session, or sign-out fails with a `500`
    and the session stays. If a `session.signInRefused` record cannot be written, the server logs a
    warning and the callback still redirects to the shell's sign-in page.
  - They never hold a token, a cookie or the user agent. A session that expires writes nothing, and
    neither does a sign-out with no session.

- **User actions.** An administrator's command on a user of the tenant's realm writes one action,
  see [user administration](user-administration.md#audit): `user.invited`,
  `user.invitationResent`, `user.disabled`, `user.enabled`, `user.authenticatorReset`,
  `user.roleGiven` or `user.roleRemoved`.
  - The `actor` is the administrator's user id. `entity_id`, `record_id` and `process_instance_id`
    are null, and so is `application_id`, except that a role action on an application role holds
    it.
  - `details` is `{ "userId" }`, the id of the realm user the command acted on. A role action adds
    `role`, and a platform role action adds `"platform": true` and has the kind as its `role`. An
    invitation adds `email`, and a resend adds `previousUserId`, the id before the realm user was
    created again. No record holds the invitation link.
  - A role action commits with the row it changes. An invitation commits with its given roles before
    the email is sent, and a resend with the move of the given roles. The others commit with the end
    of the user's Axis sessions, after the realm steps succeeded.
- **Webhook actions.** Each [webhook](process-events.md#webhook-delivery) request that a receiver
  answers `2xx` writes `webhook.delivered`, with actor `system`. The `application_id`, the event's
  `entity_id`, the subject `record_id` and the `process_instance_id` of the raising instance are
  set, so the delivery shows in the record's history. The `details` are
  `{ "webhookId", "webhook", "eventId", "statusCode" }`, and never hold the body, the URL or a
  payload value.
- **Dead letter actions.** A redeliver or a discard of a [dead letter](process-events.md#routes)
  writes `deadLetter.redelivered` or `deadLetter.discarded`, with the user as `actor`. The
  `application_id` is set, and `entity_id`, `record_id` and `process_instance_id` are null, because a
  dead letter has no subject record. The `details` are
  `{ "deadLetterId", "kind", "triggerId", "eventId", "occurrenceAt", "reason" }`, with null for the
  ids that the kind lacks.
- **Export actions.** An [export](import-export.md#export) of an entity or a data source writes
  `records.exported`, with the user as `actor`. The `application_id` is set, and `record_id` and
  `process_instance_id` are null. An entity export sets `entity_id`, and a data source export leaves
  it null and puts the data source's id in the `details`. The `details` are
  `{ "format", "rowCount", "truncated" }` and, for a data source, `"dataSourceId"`. `format` is
  `csv` or `xlsx`, and `truncated` says whether the file stopped at `Data:ExportMaxRows`. They never
  hold a field value.

- **Application setting actions.** A change of an [application setting](authoring.md#application-settings)
  writes `application.autoAcceptChanged`, with the user as `actor`. The `application_id` is set, and
  `entity_id`, `record_id` and `process_instance_id` are null. The `details` are `{ "autoAccept" }`,
  the new value. It is written only when the value changes, in the transaction that saves the value.

- **Authoring request actions.** Sending, approving and dropping an
  [authoring request](authoring-requests.md#routes) write `authoringRequest.sent`,
  `authoringRequest.approved` and `authoringRequest.dropped`, with the author as `actor`. The
  `application_id` is set, `details` is `{ "requestId" }`, and the other ids are null. Each commits
  with the row it changes.

### Sequence counters

`axis.sequence_counters` holds one row per [sequence](resource-files.md#sequences) and period, which
the first number of the period creates.

| Column           | Type      | Meaning                                              |
| ---------------- | --------- | ---------------------------------------------------- |
| `sequence_id`    | `uuid`    | The `sequence` resource `id`                         |
| `application_id` | `uuid`    | The application `id`                                 |
| `period`         | `integer` | The local year, or 0 when the format has no `{yyyy}` |
| `last_value`     | `bigint`  | The last number handed out                           |

- **Key.** The primary key is `(sequence_id, period)`.
- **Module sequences.** For a module sequence, `sequence_id` is the id derived for the application,
  as [module resource ids](#physical-names) says, so two applications that use one module count on
  their own.
- **Taking a number.** Taking a number is one
  `INSERT ... ON CONFLICT DO UPDATE SET last_value = last_value + 1 RETURNING last_value` in the
  caller's transaction. Its row lock makes concurrent creates of the same sequence wait for each
  other, and a create that rolls back uses no number, so there are no gaps.

## Publish requests and release history

Studio publishes by storing a request that the [publisher](administration.md#publisher) runs, and
every activation adds a row to the release history, from which a release can be activated again.
`Axis.Configuration` owns both tables, in the `axis` schema of each tenant database. The server
inserts `validate`, `publish`, `activate` and `test` requests through the
[authoring API](authoring.md#publishing), and a preview is a `publish` request with the target
`preview`.

### Publish requests

`axis.publish_requests` holds one row per request. The server inserts a row over the runtime role,
and only the publisher changes a row, over the schema role. An index on
`(application_id, status, requested_at, id)` serves the claim.

| Column              | Type                | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ------------------- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                | `uuid`              | Primary key, a version 7 UUID                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `application_id`    | `uuid`              | The application `id`                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `kind`              | `text`              | `validate`, `publish`, `activate` or `test`, see [publishing](authoring.md#publishing) for `activate`                                                                                                                                                                                                                                                                                                                                                    |
| `target`            | `text`              | `tenant` or `preview`, by default `tenant`. See the preview target below                                                                                                                                                                                                                                                                                                                                                                                 |
| `status`            | `text`              | `queued`, `running`, `succeeded` or `failed`                                                                                                                                                                                                                                                                                                                                                                                                             |
| `snapshot`          | `jsonb`, null       | For `validate`, `publish` and `test`, an array of `{ file, version, content }`, one for every `*.json` file of the folder. Null for `activate`                                                                                                                                                                                                                                                                                                           |
| `release_id`        | `uuid`, null        | For `activate`, the release to activate. For `publish`, set when the release is stored                                                                                                                                                                                                                                                                                                                                                                   |
| `label`             | `text`, null        | The optional label, up to 100 characters                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `note`              | `text`, null        | The optional note, up to 2000 characters                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `requested_by`      | `text`              | The requesting user's id                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `requested_by_name` | `text`, null        | Null in every row that Axis writes, because a view reads the name from the user's profile                                                                                                                                                                                                                                                                                                                                                                |
| `requested_at`      | `timestamptz`       | When the request was stored                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `claimed_by`        | `text`, null        | The publisher that holds the claim                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `lease_until`       | `timestamptz`, null | When the claim runs out unless the publisher renews it                                                                                                                                                                                                                                                                                                                                                                                                   |
| `claims`            | `integer`           | How many times a publisher claimed the request, from 0                                                                                                                                                                                                                                                                                                                                                                                                   |
| `started_at`        | `timestamptz`, null | When it was first claimed                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `finished_at`       | `timestamptz`, null | When it reached `succeeded` or `failed`                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `diagnostics`       | `jsonb`, null       | The diagnostics, as an array                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `scenario_names`    | `jsonb`, null       | For `test`, the names of the scenarios to run, as an array of strings in the order sent. Null means every scenario, and it is null for every other kind. See [test](authoring.md#test)                                                                                                                                                                                                                                                                   |
| `scenario_results`  | `jsonb`, null       | For `test`, an array of `{ name, resourceId, file, outcome, durationMs, step, path, message }`, one for each scenario that ran, in file order, and an empty array when none ran. For `publish` and `validate`, the same form for each contract scenario that ran, and `{ name, file, outcome: "skipped", point }` for each one that a relax entry skipped, or null when none ran. Null for every other kind. See [the request](authoring.md#the-request) |

- **A snapshot.** `content` is the file's text as a JSON string, not an embedded object, so the file
  reads back exactly as the author saw it. `version` is the SHA-256 of the file's bytes, as in
  [versions](authoring.md#versions). A later edit never changes a stored snapshot, and the publisher
  compiles the snapshot from these texts alone.
- **Claim.** The publisher takes the oldest `queued` `publish`, `validate` or `activate` row of an
  application with `FOR UPDATE SKIP LOCKED`, sets `running`, `claimed_by` and `lease_until`, and adds
  1 to `claims`. It skips an application that has a `running` row. A `running` row whose
  `lease_until` has passed is claimed again the same way, and a publisher that lost its connection
  sets `lease_until` to now. A row with `claims` of 3 and an expired lease becomes `failed` with
  `AXC0215`.
- **Test requests.** A `test` row has its own queue for each application: at most one runs at a time,
  oldest first, and it does not wait behind a `publish`, `validate` or `activate` row. The claim of
  each queue skips an application only for a `running` row of the same queue. The row has the target
  `tenant`, no `release_id` and no audit record. See [test](authoring.md#test).
- **Preview target.** A row with `target` `preview` is stored in the session tenant's database and
  run against that tenant's preview tenant, see [preview](authoring.md#preview). Its history row
  lives in the preview tenant's `release_activations` and keeps this row's `id` as `request_id`,
  which points at a request in another database, so no foreign key can hold it. The publisher claims
  only a `publish` row with target `preview`, and never claims a `validate` or `activate` row with
  that target. It sets the row `failed` with `AXC0216` when the main tenant no longer has a preview
  tenant. While the preview tenant exists but the publisher does not serve it, the row stays
  `queued`.
- **Final.** A `succeeded` or `failed` row is never changed again.
- **Size and age.** No limit is set on the size of a snapshot, and nothing deletes old requests, so
  large folders or many requests grow the table.

### Release history

`axis.release_activations` holds one row per activation, and is append-only.

| Column           | Type          | Meaning                                                                                   |
| ---------------- | ------------- | ----------------------------------------------------------------------------------------- |
| `id`             | `uuid`        | Primary key, a version 7 UUID                                                             |
| `release_id`     | `uuid`        | The activated release                                                                     |
| `application_id` | `uuid`        | The application `id`                                                                      |
| `number`         | `integer`     | The release number: 1, 2, 3… per application                                              |
| `content_hash`   | `varchar(64)` | The release's content hash                                                                |
| `activated_at`   | `timestamptz` | When the activation committed                                                             |
| `actor`          | `text`        | A user id, `startup`, `operator`, or `unknown` for a backfilled row                       |
| `actor_name`     | `text`, null  | Null in every row that Axis writes, because a view reads the name from the user's profile |
| `label`          | `text`, null  | The label of the publish, or null                                                         |
| `note`           | `text`, null  | The note of the publish, or null                                                          |
| `request_id`     | `uuid`, null  | The publish request, or null for a startup and `axis-admin activate`                      |

- **Append only.** A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE` statement trigger
  raise SQLSTATE `23001`, as for `audit_records`. The runtime role has `SELECT` only.
- **Numbers.** A release gets its number the first time it is activated in the tenant. Every later
  row of that release holds the same number, and no two releases of one application share one. The
  activation takes the number in the transaction that holds the schema lock, so two activations
  cannot give one number to two releases. Numbers belong to one tenant, so the same content can have
  different numbers in two tenants.
- **No copies.** Activating the release that is already active adds no row and keeps the number, so
  a restart under `scripts/dev.sh` and a request that runs again add nothing.
- **Every path.** Startup activation, `axis-admin activate`, a publish and an `activate` request each
  add a row. The actor is the user for a publish and an `activate`, `startup` for a startup and
  `operator` for `axis-admin activate`. A publish passes its user, label, note and request to the
  activation, which stores them as given. An `activate` passes its user and its request, and no label
  and no note, and the release keeps the number it got the first time.
- **Backfill.** The migration that creates the table adds one row for each application's active
  release, with number 1, the actor `unknown`, the activation time of `axis.active_releases` and no
  request, so the live release has a number and a row to activate again. Releases that were not
  active then are not in the history, so no request can name them for activation.

### Refusals to activate a stored release

An `activate` request activates the stored release as it is and never compiles it again. The publisher
refuses it with one of these diagnostics, the request ends `failed`, and nothing is changed. Every
other refusal of activation applies too, such as `AXC0015` for a field that the release lacks and
the tables hold, because those checks protect stored data. So a stored release can be activated only
across releases that did not change the storage.

| Case                                                                                                                               | Code      |
| ---------------------------------------------------------------------------------------------------------------------------------- | --------- |
| A migration in [`axis.applied_migrations`](migrations.md#applied-migrations) was applied after that release and is missing from it | `AXC0174` |
| An extension package version that the release pins is not installed under `Extensions:Path`                                        | `AXC0146` |

The second check is the start check of [extension packages](extension-packages.md). Both codes have
the meaning they have at activation. See also
[activating a release](authoring.md#activating-a-release).

### Audit records of publishing

Each `publish` and each `activate` writes one record in `axis.audit_records`, in the transaction that
sets the request's final status. The actor is the requesting user, and `application_id` is the
application `id`. A `validate` writes no audit record, and neither does a preview request, whose
publisher the preview tenant's release history records.

| Action                   | When                   |
| ------------------------ | ---------------------- |
| `release.published`      | A `publish` succeeded  |
| `release.publishFailed`  | A `publish` failed     |
| `release.rolledBack`     | An `activate` succeeded |
| `release.rollbackFailed` | An `activate` failed    |

`details` is `{ requestId, releaseId, number }`. `releaseId` and `number` are null when the request
failed before a release was known. `number` is the release's number in the
[release history](#release-history), and it is also null when the release was never activated.
