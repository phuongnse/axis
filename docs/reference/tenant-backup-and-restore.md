# Tenant backup and restore

`tenant backup` writes one tenant's data into one encrypted archive, and `tenant restore` makes a new
tenant from such an archive. In a [cell](cells.md), every tenant is also backed up each day, and a
restore is tried each week. The reasons are in [D45](../decisions/d45-tenant-backup-and-restore.md),
and those of the encryption in
[D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) and
[D116](../decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md). The settings that the
commands read are in [settings](administration.md#settings), and the output lines and exit codes that
every command shares are in [output and exit codes](administration.md#output-and-exit-codes).

## Tenant backup

`tenant backup <id> --out <file> --backup-key-file <file>` writes one encrypted archive of one tenant.
In a cell the command takes no key file, because it uses the tenant's own backup key, see
[backups in a cell](#backups-in-a-cell).

| Option                     | Meaning                                                                                                                                                                                                                                                                                                         |
| -------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<id>`                     | The tenant id.                                                                                                                                                                                                                                                                                                  |
| `--out <file>`             | The archive to write. The file must not exist, and its folder must exist. Only its owner can read it, because it holds all of the tenant's business data.                                                                                                                                                      |
| `--backup-key-file <file>` | The backup key: a file that holds 32 bytes in base64, such as the output of `openssl rand -base64 32`. Surrounding white space is ignored. The key is read from the file only, never from the command line or a setting. The operator keeps it outside the installation. Required outside a cell, and refused in a cell. |

A tenant in any status except `provisioning` can be backed up, also a `suspended` one. A
`provisioning` tenant has no complete database.

### The archive

The archive is a tar of these entries, in this order, and the whole file is encrypted with the backup
key, so it shows nothing of the dump, the manifest or the tenant id. The format is in
[encrypted backups](data-encryption.md#encrypted-backups).

| Entry           | What it holds                                                                                                                       |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `manifest.json` | The [manifest](#manifest).                                                                                                          |
| `realm.json`    | The tenant's realm with its users and their credentials, written by the `realm` step below. Only for a tenant with its own realm. |
| `studio/<path>` | In a cell only, one entry for each file of the tenant's Studio folder.                                                             |
| `tenant.dump`   | The tenant database, as `pg_dump` writes it.                                                                                        |

- **No secret but the keys.** The manifest holds the tenant's data keys and lookup keys, which is why
  the archive is encrypted. No database password, OIDC client secret or `secret` binding value is ever
  in it.
- **A lost backup key.** A lost backup key loses every archive made with it, and nothing in Axis can
  recover them.
- **The Studio folder.** In a cell the archive holds one `studio/<path>` entry for each file of
  `<Studio:ApplicationsPath>/<tenant id>/`, with `/` as separator and in ordinal order of the path. A
  symbolic link is skipped, so a link cannot pull in a file from outside the folder. A tenant with no
  folder adds no entry. `tenant backup` and `tenant restore` read `Studio:ApplicationsPath` themselves
  and resolve a relative path against their working directory.

### Steps

The command runs these steps in order, and each writes its own line.

1. `dump`: run `pg_dump --format=custom --no-owner --no-privileges` over the tenant database as its
   schema role. It reads one consistent snapshot, so the tenant keeps running and its hosts keep
   answering. The tool reads the manifest data inside the same snapshot, so a release that is activated
   during the backup is in both the dump and the manifest or in neither. The row counts of the manifest
   are read in that snapshot too. The schema role's password goes to `pg_dump` in its environment, never
   in its arguments. The tool holds one transaction open until the dump ends, so vacuum on the tenant
   database waits, and so does an activation that changes tables.
2. `realm`: only for a tenant with its own realm, and only when
   `IdentityProvider:DatabaseConnectionString` is set, see [settings](administration.md#settings). It
   builds `realm.json` in memory.
   - The file holds the realm's roles (the realm roles and the roles of the `axis` and `site-` clients),
     its groups, and its users. Each user comes with its `id`, role mappings, groups, required actions
     and credentials. It holds no client and no service-account user. The field names are those of the
     identity provider's realm export.
   - Roles come from the identity provider's admin API as the realm's `axis-activation` client, and
     groups and users as its `axis-users` client. The credentials, which are password hashes and
     authenticator secrets that the admin API never returns, come from the `credential` table of the
     identity provider's database, through that setting, for the users that the admin API returned.
   - The entry is [secret](keys-and-data-protection.md#secret) data: it is written only inside the
     encrypted archive, and no file, output line or error line holds any part of it.
   - The users and the credentials are two reads, not one snapshot, so a credential added between them
     can be missing.
   - A tenant without its own realm, such as a preview tenant or one that signs in elsewhere, gets no
     `realm` line. Without the setting, no tenant does.
3. `archive`: write the manifest, `realm.json` when there is one, the Studio files in a cell and the
   dump into `--out`, encrypted with the backup key. The dump is first written to a temporary file next
   to `--out` that only its owner can read, and the tool deletes it when the step ends.

### Manifest

`manifest.json` says what the archive holds, so that a restore can check it before it changes
anything. It holds these fields:

| Field           | Meaning                                                                                                                                                                                                                                           |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `format`        | The archive format version. It is `1`.                                                                                                                                                                                                            |
| `tenantId`      | The id of the backed-up tenant.                                                                                                                                                                                                                   |
| `axisVersion`   | The version of the `Axis.Admin` build that wrote the archive.                                                                                                                                                                                     |
| `schemaVersion` | The tenant's schema version at backup, see [Migrate](administration.md#migrate).                                                                                                                                                                  |
| `createdAt`     | The time of the backup, in UTC.                                                                                                                                                                                                                   |
| `directory`     | The directory entry without any secret, see below.                                                                                                                                                                                                |
| `packages`      | Every extension package version that a stored release pins. Each has `id`, `version` and `required`.                                                                                                                                             |
| `modules`       | Every module version that a stored release pins. Each has `id` and `version`.                                                                                                                                                                     |
| `keys`          | The tenant's data keys and lookup keys. Each has `kind` (`data` or `lookup`), `number` and `key`, the base64 text of its 32 bytes. A restore stores them under its own installation key with the same numbers. An archive with no `keys` gets number 1 of each kind. |
| `counts`        | The row count of each ordinary table of the tenant database outside the system schemas, keyed `schema.table`, read in the dump's snapshot. `tenant restore` does not use it, and the [weekly restore check](#weekly-restore-check) compares it. |

`directory` holds these fields:

| Field              | Meaning                                                                                                                                                             |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `status`           | The status at backup, `active` or `suspended`.                                                                                                                      |
| `previewOf`        | The id of the main tenant for a [preview tenant](../architecture.md#preview-tenants), or `null` for a main tenant.                                                 |
| `hosts`, `sites`   | The tenant's hosts, in ordinal order. `sites` lists the hosts that belong to a site, each with `path` and `host`, in ordinal order of the path.                      |
| `oidcAuthority`    | The OIDC authority.                                                                                                                                                 |
| `oidcClientId`     | The OIDC client id.                                                                                                                                                 |
| `realm`            | `true` when the tenant signs in through its own realm in Axis's identity provider, see [identity provider](identity-provider.md).                                   |
| `claimUserId`      | The claim name of the user id, or `null` for the default.                                                                                                           |
| `claimDisplayName` | The claim name of the display name, or `null` for the default.                                                                                                      |
| `claimRoles`       | The claim name of the roles, or `null` for the default.                                                                                                             |
| `claimEmail`       | The claim name of the email, or `null` for the default.                                                                                                             |
| `claimLocale`      | The claim name of the locale, or `null` for the default.                                                                                                            |
| `endpointAudience` | The endpoint audience, or `null` for the default.                                                                                                                   |
| `serviceAudience`  | The service audience, or `null` when the tenant accepts no service account token.                                                                                   |
| `bindings`         | The bindings, in ordinal order of the name. Each has `name`, `kind` and, for a `url` or `storage` binding, `url`.                                                   |

- **Only named fields.** The tool builds the manifest from these named fields, so no other column of
  the directory reaches the archive. A `secret` binding holds only its name and kind. The database
  server, the database name, the role names and the `version` of the entry are not in the manifest,
  because a restore creates new ones.
- **Required packages.** `required` is `true` for a package version that an active release, or a
  release with a running or waiting process instance, pins. These are the releases that the start
  check `AXC0146` rebuilds, see [extension packages](extension-packages.md). Other stored releases are
  listed with `required` set to `false`.
- **A field that is missing.** An archive without `counts` restores the same way. One without `sites`
  reads as no sites, and one without `claimEmail`, `claimLocale` or `serviceAudience` reads each as
  `null`. One without `realm` reads as `false`, and a restore of it keeps its sign-in as it is.

### Failures and exit codes

A failed dump, realm read or write exits `1` and leaves no partial file, and a `--out` that appears
during the backup is never overwritten.

- **A failed `realm` step** writes `<id> realm failed: <message>`. The message names the identity
  provider step and its status, or the database error, and never a response body, a host or a secret.
- **Exit codes.** `0` when the archive is written. `1` when the dump, the realm read or the write
  failed. `2` for an unknown tenant, a `provisioning` tenant, an `--out` file that exists, a missing
  option, a missing or malformed `--backup-key-file`, a missing tool or a missing setting, and also for
  an `IdentityProvider:DatabaseConnectionString` that is not a connection string or, outside
  Development and Testing, does not set `SSL Mode=VerifyFull`.
- **In a cell,** `2` also covers a `--backup-key-file`, which is refused, a missing or unreadable
  `Cell:BackupKeyFile` and a tenant that has no backup key.
- **A key file problem** goes to standard error.

## Tenant restore

`tenant restore <file> --tenant <id> --host <host>... --backup-key-file <file>` restores an archive into
a new tenant. It needs the backup key that `tenant backup` used, and it refuses an archive that is not
encrypted, see [encrypted backups](data-encryption.md#encrypted-backups). In a cell the command is
`tenant restore <file> --tenant <id> --host <host>... --from <tenant id>`, see
[backups in a cell](#backups-in-a-cell).

| Option                     | Meaning                                                                                                                                                        |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<file>`                   | The archive that `tenant backup` wrote.                                                                                                                        |
| `--tenant <id>`            | The id of the new tenant. It follows the rule of `tenant create`.                                                                                              |
| `--host <host>`            | A host of the new tenant. Repeat it for more hosts. At least one is needed.                                                                                    |
| `--backup-key-file <file>` | The backup key of the archive, in the same form as for `tenant backup`. Required outside a cell, and refused in a cell.                                       |
| `--from <tenant id>`       | In a cell only: the tenant the archive came from. Its backup key opens the archive. The archive does not show its tenant id, so the command must be told. Refused outside a cell. |

### Sign-in of the restored tenant

The authority and client id come from the manifest. The restored tenant either keeps the archive's
sign-in or gets a new realm:

- **Keeps the archive's sign-in.** This is the case when the archive's `realm` is `false`, and when it
  is `true` and the realm that its authority names exists on the address in `IdentityProvider:Url`. The
  tenant keeps that realm and its users. The OIDC client secret comes from `AXIS_OIDC_CLIENT_SECRET`.
- **Gets a new realm.** This is the case when the archive's `realm` is `true` and that realm does not
  exist there, because it is another installation's or was deleted. The `realm` step creates a realm
  named after the new tenant id, as `tenant create` does. The tenant's sign-in settings point at it and
  its client secrets are generated, so `AXIS_OIDC_CLIENT_SECRET` is not used.

### Refusals

Each refusal happens before anything changes, exits `2` and names what is missing:

- The archive is unreadable, has no `manifest.json` or `tenant.dump`, or has a `format` this build does
  not know.
- The archive is not an encrypted backup. The message is `the archive is not an encrypted backup`.
- The backup key does not open the archive. The first chunk does not decrypt, so the key is wrong or
  the file is cut or changed. The message is `the backup key does not open the archive`.
- The archive holds an unsafe Studio path, in a cell or not. A `studio/` entry is unsafe when it is not
  a regular file, when its path is empty or rooted, holds a backslash or a control character, or has an
  empty, `.` or `..` segment, or when its name repeats. `tenant backup` never writes one. The message
  is `the archive holds an unsafe Studio path: <name>`, one line for each.
- The archive's `schemaVersion` is newer than this build's, or is not the id of one of its migrations.
- The tenant id exists in the directory as `active` or `suspended`.
- A host belongs to another tenant, ignoring letter case.
- The archive is of a preview tenant. Preview data is disposable, so the operator creates the preview
  again with [`tenant create --preview-of`](administration.md#tenant-create). An archive without
  `previewOf` is recognised as a preview by its empty OIDC authority and client id.
- A package version with `required` set to `true` is not installed under `Extensions:Path`. The message
  lists every missing version, not only the first.
- The archive's `realm` is `true` and `IdentityProvider:Url` is not set, or the identity provider
  cannot say whether the realm exists.
- The tenant gets a new realm and the archive's `realm.json` is larger than 64 MiB, or is not a valid
  realm document: it is not JSON, or it lacks `realm`, `roles`, `groups` or `users` in the shape that
  `tenant backup` writes, or a user has no `id` or `username`. The messages are
  `the archive's realm.json is larger than 64 MiB`, which is decided from the entry's length before
  anything is read, and `the archive's realm.json is not valid`. Neither shows any content of the file.
  A restore that keeps the archive's realm never uses the file, so it never refuses it.
- In a cell, the archive holds Studio files and `Studio:ApplicationsPath` is not set. The message is
  `the archive holds Studio files and the setting Studio:ApplicationsPath is not set`.
- The restored tenant keeps the archive's sign-in and `AXIS_OIDC_CLIENT_SECRET` is missing. This one is
  written to standard error, before any `check` line.

Every failed check is reported, not only the first. Each one is a `<id> check failed: <message>` line
on standard output, in the order of the list above. A refusal of the archive, the tenant id, the hosts
or the packages is written this way. The message names the cause: the tenant id, the host as the
operator typed it (it does not say which tenant holds it), the schema version and whether it is newer
than this build's or unknown to it, or the package id and version.

- **An archive that cannot be trusted.** When the archive is unreadable, is not encrypted, is not
  opened by the key, or its format is unknown, the schema version and package checks are skipped,
  because the manifest cannot be trusted. The tenant id and host checks still run.
- **Standard error.** A missing or invalid setting, a missing `pg_restore`, a missing or malformed
  `--backup-key-file`, and a usage error go to standard error. In a cell so do a `--backup-key-file`, a
  missing `--from`, a missing or unreadable `Cell:BackupKeyFile` and a `--from` tenant with no backup
  key. They exit `2` too, and nothing has changed.
- **Modules.** A module version that is not installed under `Modules:Path` is listed in the manifest but
  never refuses a restore. A stored release holds its module files, so it keeps working without the
  module installed.

### Restore steps

The command runs these steps in order, and each writes its own line.

1. `check`: read the archive and make every check above. It decrypts the archive only as far as the
   manifest and the start of `tenant.dump`. It reads `realm.json`, which sits right after the manifest,
   and keeps it in memory, and it reads the `studio/` entries before the dump and no other entry. It
   asks the identity provider whether the archive's realm exists.
2. `entry`: write the directory entry with status `provisioning`. It takes the new id and hosts, and
   everything else from `directory` in the manifest.
   - A `--host` that matches a `sites` entry's `host`, ignoring letter case, gets its site.
   - A tenant that gets a new realm has empty sign-in settings until the `realm` step.
   - A `secret` binding is restored by name and kind, without a value. A `url` or `storage` binding is
     restored with its value. The check refuses an archive whose `storage` binding has a value other
     than `database`.
   - The tenant's data keys and lookup keys come back from `keys` with the same numbers, encrypted by
     this installation's key, in the same transaction. An archive with no keys gets number 1 of each
     kind.
3. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime` with new random passwords, as
   `tenant create` does.
4. `database`: create `axis_<id>`, owned by the schema role.
5. `restore`: run `pg_restore --single-transaction --no-owner --no-privileges` as the schema role. It
   restores everything or nothing. The tool decrypts the archive and streams `tenant.dump` to the
   standard input of `pg_restore`, so no unpacked copy of the data is left on disk. It then decrypts the
   rest of the file, so a chunk that is changed, missing or out of order anywhere fails this step with
   `the archive does not decrypt`, and nothing is restored. The schema role's password goes in the
   environment of `pg_restore`, never in its arguments. A database that already holds tables counts as
   restored, and the step runs no `pg_restore`.
6. `grants`: run the grant step. This builds every privilege for the new roles, as
   [D31](../decisions/d31-separate-database-roles.md) describes.
7. `migrations`: run the EF Core migrations. This step runs only when the archive's schema version is
   older than this build's.
8. `grants`: run the grant step again.
9. `work`: give every work item the new tenant id. The worker claims only the work items of its own
   tenant, so without this the work that was due at backup would never run.
10. `sessions`: empty `axis.sessions`, so no session of the archive signs anyone in. The migrations ran
    before this step, so an archive older than the table has it here.
11. `role-check`: run the runtime role check of D31 over the runtime role.
12. `realm`: only for a tenant that gets a new realm. See [the new realm](#the-new-realm).
13. `studio`: only in a cell, and only when the archive holds `studio/` entries. Write each of them as a
    file under `<Studio:ApplicationsPath>/<new id>/`, creating its folders. A file of the same path is
    overwritten and any other file of the folder stays, so a rerun writes them again. Outside a cell, a
    restore of an archive with Studio files runs no `studio` step and writes one `<id> warning ...` line
    on standard error: the files are skipped. An archive without `studio/` entries has no `studio` line
    and no warning, in a cell or not.
14. `version`: record the schema version and set the entry `suspended`. The step fails unless the
    database is on the schema of this build, which catches an archive whose manifest does not match its
    dump.

### The new realm

The `realm` step gives a tenant that gets a new realm its realm, its clients and, when the archive
holds `realm.json`, its people.

1. It creates the realm named after the new tenant id, with the clients `axis`, `axis-activation` and
   `axis-users`, and saves the generated client secrets, encrypted, as `tenant create` does.
2. It brings the realm in step with the tenant's active releases: a `site-` client for each site, and a
   brokered provider for each `oidc` identity provider. The archive holds no secret value, so a provider
   whose bindings have no value yet is left out, with a `<id> warning ...` line on standard error that
   names the provider and the binding. The next `activate`, publish or server start adds it once the
   operator has set those bindings.
3. When the archive holds `realm.json`, it imports the file into the new realm with the identity
   provider's partial import, as the master admin client, and skips what already exists. It imports the
   roles and groups first, then the users in batches of 100. A failed import fails the step.

- **The people sign in as before.** Each user keeps the id, so the `sub` claim and every reference in
  the tenant database still match. Each keeps the password hash and the authenticators, so people sign
  in without an invitation.
- **Roles.** The old realm's default role, `default-roles-<archive realm name in lower case>`, is mapped
  to the new realm's default role. A role of a client that the new realm lacks, such as a `site-` client
  that no active release makes, is dropped from the roles, the composites and the mappings.
- **What goes back to backup time.** Users, password changes and authenticators added after the backup
  are lost, and people deleted since come back. The file holds no link to a brokered provider, so such a
  person is not linked again. Sessions, brute-force counters and consents are not restored.

### Running it again

On a `provisioning` entry, the same command skips every step already done and finishes the rest, as
`tenant create` does. On an `active` or `suspended` tenant it is refused.

- **A new realm.** An entry that already holds the secrets of a new realm goes on with the new realm and
  imports `realm.json` again, which skips what the first run imported, so the import can finish after a
  failure in the middle.
- **The data.** A failed `restore` step restored nothing, so the rerun restores again, with the same
  archive or a good copy of it. A database that already holds tables counts as restored, so the rerun
  does not restore it a second time.
- **The keys.** A rerun writes the tenant's keys again from the archive.

The command exits `0` when the tenant is `suspended` and restored, `1` when a step failed, and the
entry stays `provisioning`, and `2` for a refusal above, a missing option or a missing setting.

### After a restore

The restored tenant is `suspended`, and its hosts answer `503` until the operator finishes it:

1. Set every `secret` binding again with
   [`tenant binding set <id> <name> --secret`](tenant-setting-commands.md#deployment-bindings), because
   the archive holds none of them. The restore takes the OIDC client secret from
   `AXIS_OIDC_CLIENT_SECRET`, and a tenant with a new realm has its client secret already.
2. Run [`tenant resume <id>`](tenant-status-commands.md#tenant-resume).

A tenant with a new realm has the people of `realm.json` when the archive holds one, and they sign in
as before. An archive without it gives a realm with no accounts but the service accounts, and the
people need new invitations. A provider that the `realm` step left out comes with the next activation,
once its `secret` bindings are set.

### Restoring over an existing tenant

A restore never overwrites a tenant in place. To put an archive in the place of a tenant:

1. Run [`tenant suspend`](tenant-status-commands.md#tenant-suspend) on the tenant.
2. Restore the archive under a new id, with a temporary host.
3. Set the secrets again and resume the new tenant.
4. Move the hosts from the old tenant to the new one with
   [`tenant host move`](tenant-setting-commands.md#hosts).

The old tenant stays in the directory, because `Axis.Admin` has no command that deletes a tenant. The
new tenant keeps the old tenant's realm and its users, so its people sign in as before once the hosts
move.

### What a restore does to running work

Work goes back to backup time, see [execution](../domain/knowledge.md#execution):

- Process instances, tasks, work items and connector calls are as they were at backup. The worker runs
  them again once the tenant resumes.
- A call that was staged before the backup and sent after it is sent again, with the same idempotency
  key, so a system that honours the key does not act twice.
- A call that was staged after the backup is not in the archive. Its step runs again and stages a new
  call, with a new operation identity and so a new idempotency key. The system may already have acted
  on the first call, so even a system that honours the key can act twice. See
  [staging and the call](process-execution.md#staging-and-the-call).
- A connector without `idempotency` sends no key, so the system may act twice in either case.
- Sequence counters, inbound endpoint receipts and schedule state go back too. A number issued after the
  backup can be issued again, and an inbound request accepted after it is lost.

## Backups in a cell

In a [cell](cells.md), every tenant is backed up each day with a key of its own, and each week a restore
of one archive is tried, so that any one tenant can be recovered and a deleted tenant's archives can no
longer be read. The reasons are in
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md). The
archive and the steps are those above.

- **Two layers.** The managed PostgreSQL keeps point-in-time recovery of the whole cluster for 7 days,
  in a second data centre of the region. It recovers the cell after a lost server. The daily archives
  recover one tenant, and are kept 30 days.
- **A backup key for each tenant.** In a cell, `tenant create` makes a 32-byte backup key for the tenant
  and stores it in `platform.tenant_backup_keys`, encrypted by the cell's backup key, which
  `Cell:BackupKeyFile` names, and bound to the tenant id so that a row copied to another tenant does not
  unwrap. A rerun keeps the key it made. The cell's key is a Kubernetes Secret that comes from the cloud
  provider's key service, and it is never in the platform database or in an archive.
- **The tenant's key, never a key file.** In a cell, `tenant backup` and `tenant restore` take the
  tenant's key from the platform and refuse `--backup-key-file` with exit code `2`. `tenant restore`
  takes `--from <tenant id>`, the tenant the archive came from, and opens the archive with that tenant's
  key. An archive does not open with another tenant's key, and the restore stops with the usual
  `the backup key does not open the archive` refusal. The restored tenant gets a new backup key of its
  own.
- **Refusals.** `tenant create`, `tenant backup` and `tenant restore` stop with exit code `2` when
  `Cell:BackupKeyFile` is missing or cannot be read. A tenant without a backup key cannot be backed up.
  `platform rekey` leaves `platform.tenant_backup_keys` alone, because the installation key does not
  wrap it.
- **The realm in every daily archive.** Every archive that `cell backup` writes for a tenant with its
  own realm holds the tenant's `realm.json`, so a restore gives the tenant back with its people. The
  chart gives the cell backup CronJob `IdentityProvider__DatabaseConnectionString`, the connection of a
  read-only role of the identity provider's database, from the Secret in `secrets.identityCredentialsConnection`,
  see [which pod gets which secret](installation.md#which-pod-gets-which-secret). The chart requires the Secret with
  `backup.bucket`. The [weekly restore check](#weekly-restore-check) fails an archive of such a tenant
  that holds no `realm.json`.
- **Crypto-shredding.** [Deleting a tenant](tenant-lifecycle.md#deleting-a-tenant) destroys its backup
  key in the purge's `keys` step. Its archives stay in object storage until their lock ends, and nothing
  can read them. One leaked tenant key opens only that tenant's archives.
- **Object lock.** The bucket has object lock in compliance mode for 30 days and a lifecycle rule that
  deletes an archive after 30 days. Nobody, the operator included, can delete or change an archive
  before then. The operator sets both on the bucket, and the chart neither creates nor changes it.

### Daily backup

`cell backup` backs up every tenant of the cell once. In a cell with `backup.bucket` set, the chart's
CronJob runs it at 01:00 in `backup.timeZone`, never two at once, with no retry and a deadline of 6
hours 15 minutes, see [the Helm chart](installation.md#settings-and-secrets).

- **Which tenants.** It backs up every `active` and `suspended` tenant, four at a time. A `provisioning`
  tenant is skipped.
- **Where.** It writes each archive to the region's object storage as
  `<tenant id>/<yyyy-mm-dd>.axisbak`, where the date is the UTC date when the run starts. Each archive is
  encrypted with the tenant's own backup key, written to a temporary folder that only its owner can use,
  uploaded and deleted.
- **Retries and the limit.** A failed tenant is tried again once, at once, and the others go on. The
  run ends within 6 hours: after that it stops and exits with code `1`.
- **Audit.** The command writes one platform audit record for each tenant, `cell backup`, before the
  first try, and a retry writes none.
- **Last success.** After a tenant's archive is uploaded, the command stores the time of the upload,
  from the clock of the admin tool, in `platform.tenant_backups`. A tenant that fails keeps the time it
  had. The [provisioning API's tenant reads](tenant-lifecycle.md#routes) answer the stored time as
  `newestArchiveAt`. At the end of the run, also when it stopped at its limit, the command records the gauge
  `axis.backup.last_success` for each `active` and `suspended` tenant that has a stored time. The value
  is in Unix seconds and the tag `axis.tenant` names the tenant. A tenant that never succeeded has no
  point. When `OTEL_EXPORTER_OTLP_ENDPOINT` or `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` is set, the command
  exports the gauge over OTLP before it exits, see [telemetry export](host-settings.md#telemetry-export).
  With no endpoint, nothing is exported.
- **An alert on a stale backup** _(planned for M31)_. An alert fires on a gauge that is too old.
- **Settings.** `Cell:BackupStorage:ServiceUrl`, `:Region`, `:Bucket`, `:AccessKeyId` and
  `:SecretAccessKey` name an S3-compatible service, reached with the AWS SDK and path-style addressing,
  see [host settings](host-settings.md#cell-settings). All five are required. The command's other
  settings and its exit codes are in [cell backup](administration.md#cell-backup).
- **Output.** Each line starts with the tenant id. A try writes the lines of the `tenant backup` steps,
  `dump`, `realm` (only with `IdentityProvider:DatabaseConnectionString` set, for a tenant with its own
  realm) and `archive`, and then `upload ok`. A step that fails writes `<step> failed: <message>`, where
  the step is `key` (the tenant has no backup key or it does not unwrap), `check` (the tenant cannot be
  backed up, for example because it is no longer in the directory), `dump`, `realm`, `archive` or
  `upload`. The message of `upload` is the error code of the service or the name of the error type,
  never the address or a key. No line holds the secret access key. A tenant that is tried again writes
  its lines again, so its failed line comes twice.
- **The last lines.** When the time runs out, the last line is
  `cell backup stopped: the run reached its 6-hour limit`. A tenant whose upload time cannot be stored
  writes `<tenant id> record failed: <message>` after its upload, counts as failed and is not uploaded
  again. When the gauge cannot be recorded, the last line is `cell backup metric failed: <message>`, and
  the command exits `1`.

### Weekly restore check

A backup that is never restored is not known to work, so each Sunday at 03:00 in `backup.timeZone` the
chart's [CronJob](installation.md#settings-and-secrets) runs `cell restore-check`. It runs once, takes
no argument and runs only in a cell. It does these steps:

1. **Pick.** It lists the bucket and keeps the archives dated today or yesterday (UTC). It picks at
   random a tenant that is not a preview, has a backup key and has such an archive, and takes its newest
   one. A tenant whose id starts with `restore-check-` is never picked, because `cell backup` also backs
   up a scratch tenant that an earlier run left behind. When no tenant qualifies, the command writes
   `cell restore-check failed: no tenant has an archive of today or yesterday`, records no point, writes
   no audit record and exits `1`.
2. **Audit.** It writes one platform audit record for the source tenant, before the first change. The
   record lists `tenant: <scratch tenant id>` and does not hold the result, which goes to the stored
   row, the metric and the output.
3. **Clean up.** It removes every `restore-check-*` tenant that an earlier run left behind. It finds them
   by their directory rows and their `axis_restore-check-*` databases, and always removes today's id.
4. **Download.** It downloads the archive to a folder that only its owner can use, and deletes the
   folder at the end.
5. **Restore.** It restores the archive with the logic of `tenant restore` into the scratch tenant
   `restore-check-<yyyymmdd>`, with `--from` the source tenant, the host
   `restore-check-<yyyymmdd>.<Cell:InternalDomain>`, which no ingress serves, and always a new realm,
   though the source tenant's realm exists. It does not write the audit record of `tenant restore`.
6. **Compare.** It compares the row count of each table in `counts` with the restored database, leaving
   out `axis.sessions` and the tables that `counts` does not hold. It compares the usernames of the
   scratch realm with those of `realm.json`, leaving out service accounts. An archive of a tenant
   that signs in elsewhere has no `realm.json` and counts as zero users. An archive whose manifest says
   `realm: true` and that holds no `realm.json` fails the users comparison, and an archive without
   `counts` fails.
7. **Scenarios.** It runs the scenarios of each application of the scratch tenant's Studio folder, see
   below.
8. **Remove.** Whatever the result, it removes the scratch tenant at once, with no grace window: its
   database, roles, realm, keys and Studio folder.
9. **Result.** It stores one row in `platform.restore_checks` with the time the run started, the source
   tenant and whether the check passed. It records the gauge `axis.backup.restore_check`, `1` for a pass and `0` for a failure,
   with the tag `axis.tenant` naming the source tenant, and exports it over OTLP as
   `axis.backup.last_success` is, see [telemetry export](host-settings.md#telemetry-export). Any failure
   after the pick, also a refused restore, a failed download, a compile error, a failed scenario of an
   application or a scratch tenant that cannot be removed, records `0` and exits `1`. A pass records `1`
   and exits `0`, unless the row cannot be stored, which also exits `1` and still records the gauge.

- **New user ids.** The users of `realm.json` and their credentials get new ids from the identity
  provider. The ids of a user and of a credential are unique across all realms, so importing them while
  the source realm is live would fail. A normal `tenant restore` keeps the ids.
- **After an upgrade.** The migration history and the tables that a migration seeds can differ right
  after an upgrade, because the restore migrates an archive of an older schema.
- **Which applications.** An application folder under `<Studio:ApplicationsPath>/<scratch id>/` is one
  that does not start with `.`, is not a symbolic link and holds `application.json`, taken in ordinal
  order of name. The command compiles each one with the packages of `Extensions:Path` and the modules of
  `Modules:Path`, and runs its scenarios, then those of its pinned modules, with the logic of the
  publisher's `test` request.
- **How the scenarios run.** Each scenario runs in its own temporary database on
  `Scenarios:ServerConnectionString` and is dropped at the end. The scenarios run from the Studio files,
  not against the restored releases or the restored data. Each application may run for 10 minutes.
  Every application runs, also after one failed, and they run also when the counts or the users failed,
  as long as the restore succeeded. A tenant without Studio applications runs nothing and needs no
  scenario server. A tenant with an application and no scenario server fails with `AXC0217`.
- **Output.** Each line starts with the tenant id: `<source> download ok|failed: <message>`, the lines of
  the `tenant restore` steps, `<scratch> counts ok` or
  `<scratch> counts failed: <schema.table> has <n> rows and the archive <m>`, `<scratch> users ok` or
  `<scratch> users failed: the realm has <n> users and the archive <m>, <k> differ` or
  `<scratch> users failed: the archive holds no realm.json`, for each
  application `<scratch> version ok`, `<scratch> scenario <name> passed|failed: <message>` for each
  scenario and `<scratch> test ok|failed: <message>`, `<scratch> cleanup ok|failed: <message>`,
  `<scratch> remove ok|failed: <message>`, `<source> record failed: <message>` when the row cannot be
  stored, and last `<source> restore-check passed|failed`. No line holds a row or a username.
- **Shown on the console.** The [cell console](cell-console.md#views) shows the newest stored result.
- **Settings and exit codes.** They are in
  [cell restore check](administration.md#cell-restore-check).
