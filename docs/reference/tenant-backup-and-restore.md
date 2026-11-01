# Tenant backup and restore

Detailed reference for `tenant backup` and `tenant restore` of `Axis.Admin`. They follow
[D45](../decisions/d45-tenant-backup-and-restore.md), and the encryption follows
[D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) and
[D116](../decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md). The settings that the commands read are in
[settings](administration.md#settings), and the output lines and exit codes that every command shares are in
[output and exit codes](administration.md#output-and-exit-codes). The conventions of [architecture.md](../architecture.md) apply.

## Tenant backup

`tenant backup <id> --out <file> --backup-key-file <file>` writes one
encrypted archive of one tenant. See [D45](../decisions/d45-tenant-backup-and-restore.md).

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |
| `--out <file>` | The archive to write. The file must not exist, and its folder must exist. Only its owner can read it, because it holds all of the tenant's business data. |
| `--backup-key-file <file>` | The backup key: a file that holds 32 bytes in base64, such as the output of `openssl rand -base64 32`. Surrounding white space is ignored. The key is read from the file only, never from the command line or a setting. The operator keeps it outside the installation. |

The archive holds a tar with two entries, `manifest.json` and `tenant.dump`, and the whole file is encrypted with the backup key.
It starts with `AXISBAK1` and 8 random bytes. The tar follows in chunks of 1 MiB, each encrypted with AES-256-GCM, so the file
shows nothing of the dump, the manifest or the tenant id. See [encrypted backups](data-encryption.md#encrypted-backups).
The manifest holds the tenant's data keys and lookup keys, which is why the archive is encrypted. No database password, OIDC
client secret or `secret` binding value is ever in it. A lost backup key loses every archive made with it, and nothing in Axis
can recover them.

**Steps, in order.**

1. `dump`: run `pg_dump --format=custom --no-owner --no-privileges` over the
   tenant database as its schema role. It reads one consistent snapshot, so the
   tenant keeps running and its hosts keep answering. The tool reads the
   manifest data inside the same snapshot, so a release that is activated during
   the backup is in both the dump and the manifest or in neither. The schema
   role's password goes to `pg_dump` in its environment, never in its
   arguments. The tool holds one transaction open until the dump ends, so
   vacuum on the tenant database waits, and so does an activation that changes
   tables.
2. `archive`: write the manifest and the dump into `--out`, encrypted with the backup key. The dump is first written to a temporary
   file next to `--out` that only its owner can read. The tool deletes it when the step ends.

A tenant in any status except `provisioning` can be backed up, also a
`suspended` one. A `provisioning` tenant has no complete database.

**Manifest.** `manifest.json` holds these fields:

| Field | Meaning |
| --- | --- |
| `format` | The archive format version. It is `1`. |
| `tenantId` | The id of the backed-up tenant. |
| `axisVersion` | The version of the `Axis.Admin` build that wrote the archive. |
| `schemaVersion` | The tenant's schema version at backup, see [Migrate](administration.md#migrate). |
| `createdAt` | The time of the backup, in UTC. |
| `directory` | The directory entry without any secret, see below. |
| `packages` | Every extension package version that a stored release pins. Each has `id`, `version` and `required`. |
| `modules` | Every module version that a stored release pins. Each has `id` and `version`. |
| `keys` | The tenant's data keys and lookup keys. Each has `kind` (`data` or `lookup`), `number` and `key`, the base64 text of its 32 bytes. A restore stores them under its own installation key with the same numbers. An archive with no `keys` gets number 1 of each kind. |

`directory` holds these fields:

| Field | Meaning |
| --- | --- |
| `status` | The status at backup, `active` or `suspended`. |
| `previewOf` | The id of the main tenant for a [preview tenant](../architecture.md#preview-tenants), or `null` for a main tenant. |
| `hosts`, `sites` | The tenant's hosts, in ordinal order. `sites` lists the hosts that belong to a site, each with `path` and `host`, in ordinal order of the path. An archive from before `sites` reads as no sites. |
| `oidcAuthority` | The OIDC authority. |
| `oidcClientId` | The OIDC client id. |
| `realm` | `true` when the tenant signs in through its own realm in Axis's identity provider, see [identity provider](identity-provider.md). An archive from before the field reads as `false`, and a restore of it keeps its sign-in as it is. |
| `claimUserId` | The claim name of the user id, or `null` for the default. |
| `claimDisplayName` | The claim name of the display name, or `null` for the default. |
| `claimRoles` | The claim name of the roles, or `null` for the default. |
| `claimEmail` | The claim name of the email, or `null` for the default. |
| `claimLocale` | The claim name of the locale, or `null` for the default. |
| `endpointAudience` | The endpoint audience, or `null` for the default. |
| `serviceAudience` | The service audience, or `null` when the tenant accepts no service account token. An archive from before the field reads as `null`. |
| `bindings` | The bindings, in ordinal order of the name. Each has `name`, `kind` and, for a `url` or `storage` binding, `url`. |

An archive written before `claimEmail` and `claimLocale` existed has neither, and a
restore reads both as `null`.

A `secret` binding holds only its name and kind. The database server, the
database name, the role names and the `version` of the entry are not in the
manifest, because a restore creates new ones. The tool builds the manifest from
these named fields, so no other column of the directory reaches the archive.

`required` is `true` for a package version that an active release, or a release
with a running or waiting process instance, pins. These are the releases that the
start check `AXC0146` rebuilds, see
[extension packages](extension-packages.md). Other stored releases are listed
with `required` set to `false`.

**Failures.** A failed dump or write exits `1` and leaves no partial file. A
`--out` that appears during the backup is never overwritten.

**Exit codes.** `0` when the archive is written. `1` when the dump or the write
failed. `2` for an unknown tenant, a `provisioning` tenant, an `--out` file that
exists, a missing option, a missing or malformed `--backup-key-file`, a missing tool or a missing setting.
A key file problem goes to standard error.

## Tenant restore

`tenant restore <file> --tenant <id> --host <host>... --backup-key-file <file>`
restores an archive into a new tenant. See [D45](../decisions/d45-tenant-backup-and-restore.md). It needs the backup key that
`tenant backup` used, and refuses an archive that is not encrypted, see [encrypted backups](data-encryption.md#encrypted-backups).

| Option | Meaning |
| --- | --- |
| `<file>` | The archive that `tenant backup` wrote. |
| `--tenant <id>` | The id of the new tenant. It follows the rule of `tenant create`. |
| `--host <host>` | A host of the new tenant. Repeat it for more hosts. At least one is needed. |
| `--backup-key-file <file>` | The backup key of the archive, in the same form as for `tenant backup`. |

The authority and client id come from the manifest. The restored tenant either keeps the archive's sign-in or gets a new realm:

- **Keeps the archive's sign-in.** This is the case when the archive's `realm` is `false`, and when it is `true` and the
  realm that its authority names exists on the address in `IdentityProvider:Url`. The tenant keeps that realm and its users.
  The OIDC client secret comes from `AXIS_OIDC_CLIENT_SECRET`.
- **Gets a new realm.** This is the case when the archive's `realm` is `true` and that realm does not exist there, because it
  is another installation's or was deleted. The `realm` step creates a realm named after the new tenant id, as `tenant create`
  does. The tenant's sign-in settings point at it and its client secrets are generated, so `AXIS_OIDC_CLIENT_SECRET` is not used.

**Refusals.** Each refusal happens before anything changes, exits `2` and names
what is missing:

- The archive is unreadable, has no `manifest.json` or `tenant.dump`, or has a
  `format` this build does not know.
- The archive is not an encrypted backup. This is the case for an archive that `tenant backup` wrote before archives were
  encrypted, which can no longer be restored. The message is `the archive is not an encrypted backup`.
- The backup key does not open the archive. The first chunk does not decrypt, so the key is wrong or the file is cut or changed.
  The message is `the backup key does not open the archive`.
- The archive's `schemaVersion` is newer than this build's, or is not the id of
  one of its migrations.
- The tenant id exists in the directory as `active` or `suspended`.
- A host belongs to another tenant, ignoring letter case.
- The archive is of a preview tenant. Preview data is
  disposable, so the operator creates the preview again with
  [`tenant create --preview-of`](administration.md#tenant-create). An archive written before
  `previewOf` existed is recognised as a preview by its empty OIDC authority and
  client id.
- A package version with `required` set to `true` is not installed under
  `Extensions:Path`. The message lists every missing version, not only the first.
- The archive's `realm` is `true` and `IdentityProvider:Url` is not set, or the identity provider cannot say whether the realm exists.
- The restored tenant keeps the archive's sign-in and `AXIS_OIDC_CLIENT_SECRET` is missing. This one is written to standard error, before any `check` line.

Every failed check is reported, not only the first. Each one is a
`<id> check failed: <message>` line on standard output, in the order of the list
above. A refusal of the archive, the tenant id, the hosts or the packages is
written this way. The message names the cause: the tenant id, the host as the
operator typed it (it does not say which tenant holds it), the schema version
and whether it is newer than this build's or unknown to it, or the package id
and version. When the archive is unreadable, is not encrypted, is not opened by the key, or its format is unknown, the schema
version and package checks are skipped, because the manifest cannot be trusted.
The tenant id and host checks still run. A missing or invalid
setting, a missing `pg_restore`, a missing or malformed `--backup-key-file` and a usage error go to standard error. Both exit
`2`, and nothing has changed.

A module version that is not installed under `Modules:Path` is listed in the
manifest but never refuses a restore. A stored release holds its module files, so
it keeps working without the module installed.

**Steps, in order.** Each step writes its own line.

1. `check`: read the archive and make every check above. It decrypts the archive only as far as the manifest and the start of
   `tenant.dump`. It asks the identity provider whether the archive's realm exists.
2. `entry`: write the directory entry with status `provisioning`. It takes the
   new id and hosts, and everything else from `directory` in the manifest. A `--host` that matches a `sites` entry's `host`, ignoring letter case, gets its site. A tenant that gets a new realm has empty sign-in settings until the `realm` step. A
   `secret` binding is restored by name and kind, without a value. A `url` or `storage`
   binding is restored with its value. The check refuses an archive whose `storage` binding has
   a value other than `database`. The tenant's data keys and lookup keys come back from `keys` with the same numbers, encrypted
   by this installation's key, in the same transaction. An archive with no keys gets number 1 of each kind.
3. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime` with new random
   passwords, as `tenant create` does.
4. `database`: create `axis_<id>`, owned by the schema role.
5. `restore`: run `pg_restore --single-transaction --no-owner --no-privileges`
   as the schema role. It restores everything or nothing. The tool decrypts the archive and streams
   `tenant.dump` to the standard input of `pg_restore`, so no
   unpacked copy of the data is left on disk. It then decrypts the rest of the file, so a chunk that is changed, missing or out
   of order anywhere fails this step with `the archive does not decrypt`. Nothing is restored then, because `pg_restore` runs in one transaction. The schema role's password goes
   in the environment of `pg_restore`, never in its arguments. A database that
   already holds tables counts as restored, and the step runs no `pg_restore`.
6. `grants`: run the grant step. This builds every privilege for the new roles,
   as [D31](../decisions/d31-separate-database-roles.md) describes.
7. `migrations`: run the EF Core migrations. This step runs only when the
   archive's schema version is older than this build's.
8. `grants`: run the grant step again.
9. `work`: give every work item the new tenant id. The worker claims only the
   work items of its own tenant, so without this the work that was due at
   backup would never run.
10. `sessions`: empty `axis.sessions`, so no session of the archive signs
    anyone in. The migrations ran before this step, so an archive older than the
    table has it here.
11. `role-check`: run the runtime role check of D31 over the runtime role.
12. `realm`: only for a tenant that gets a new realm. Create the realm named after the new tenant id, with the clients `axis`,
    `axis-activation` and `axis-users`, and save the generated client secrets, encrypted, as `tenant create` does. Then bring
    the realm in step with the tenant's active releases: a `site-` client for each site, and a brokered provider for each `oidc`
    identity provider. The archive holds no secret value, so a provider whose bindings have no value yet is left out, with a
    `<id> warning ...` line on standard error that names the provider and the binding. The next `activate`, publish or
    server start adds it once the operator has set those bindings.
13. `version`: record the schema version and set the entry `suspended`. The step
    fails unless the database is on the schema of this build, which catches an
    archive whose manifest does not match its dump.

**Running it again.** On a `provisioning` entry, the same command skips every
step already done and finishes the rest, as `tenant create` does. An entry that already holds the secrets of a new realm goes on with the new realm. A failed
`restore` step restored nothing, so the rerun restores again, with the same archive or a good copy of it. A rerun also writes the tenant's keys again from the archive. A database that
already holds tables counts as restored, so the rerun does not restore it a
second time. On an `active` or `suspended` tenant it is refused.

**Exit codes.** `0` when the tenant is `suspended` and restored. `1` when a step
failed, and the entry stays `provisioning`. `2` for a refusal above, a missing
option or a missing setting.

**After a restore.** The tenant is `suspended`, and its hosts answer `503`.

1. Set every `secret` binding again. The archive holds none of them. The restore takes the OIDC client
   secret from `AXIS_OIDC_CLIENT_SECRET`, and a tenant with a new realm has its client secret already.
2. Run [`tenant resume <id>`](tenant-status-commands.md#tenant-resume).

A tenant with a new realm has no accounts, because the archive carries no account, credential or role mapping. Its people
need new invitations. A provider that the `realm` step left out comes with the next activation, once its `secret` bindings are set.

Set each `secret` binding with
[`tenant binding set <id> <name> --secret`](tenant-setting-commands.md#deployment-bindings), see
[tenant setting commands](tenant-setting-commands.md).

**Restoring over an existing tenant.** There is no in-place overwrite.

1. Run [`tenant suspend`](tenant-status-commands.md#tenant-suspend) on the tenant.
2. Restore the archive under a new id, with a temporary host.
3. Set the secrets again and resume the new tenant.
4. Move the hosts from the old tenant to the new one with
   [`tenant host move`](tenant-setting-commands.md#hosts).

The old tenant stays in the directory, because there is no tenant delete. The new tenant keeps the old tenant's realm and its
users, so its people sign in as before once the hosts move.

**What a restore does to running work.** Work goes back to backup time, see
[execution](../domain/knowledge.md#execution):

- Process instances, tasks, work items and connector calls are as they were at
  backup. The worker runs them again once the tenant resumes.
- A call that was staged before the backup and sent after it is sent again,
  with the same idempotency key, so a system that honours the key does not act
  twice.
- A call that was staged after the backup is not in the archive. Its step runs
  again and stages a new call, with a new operation identity and so a new
  idempotency key. The system may already have acted on the first call, so
  even a system that honours the key can act twice. See
  [staging and the call](process-execution.md#staging-and-the-call).
- A connector without `idempotency` sends no key, so the system may act twice
  in either case.
- Sequence counters, inbound endpoint receipts and schedule state go back too.
  A number issued after the backup can be issued again, and an inbound request
  accepted after it is lost.

## Backups in a cell

In a [cell](cells.md), every tenant is backed up each day, as
[D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md) and
[D123](../decisions/d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md) agree.
Everything in this section is _(planned for M31)_. The archive format and the steps above stay, with
the changes below.

- **Two layers.** The managed PostgreSQL keeps point-in-time recovery of the whole cluster for 7 days,
  in a second data centre of the region. It recovers the cell after a lost server. The daily archives
  recover one tenant, and keep 30 days.
- **A backup key for each tenant.** `tenant create` makes a 32-byte backup key for the tenant and
  stores it in `platform.tenant_backup_keys`, encrypted by the cell's backup key, which
  `Cell:BackupKeyFile` names. The cell's key is a Kubernetes Secret that comes from the cloud
  provider's key service, and it is never in the platform database or in an archive. In a cell,
  `tenant backup` and `tenant restore` take the tenant's key from the platform instead of
  `--backup-key-file`.
- **Crypto-shredding.** [Deleting a tenant](tenant-lifecycle.md#deleting-a-tenant) destroys its backup
  key. Its archives stay in object storage until their lock ends, and nothing can read them. One
  leaked tenant key opens only that tenant's archives.
- **Daily job.** A Kubernetes CronJob runs `cell backup` at 01:00 in the region's time zone. It backs up
  every `active` and `suspended` tenant, four at a time, and writes each archive to the region's
  object storage as `<tenant id>/<yyyy-mm-dd>.axisbak`. It writes a platform audit record and the
  metric `axis.backup.last_success` for each tenant. A failed tenant is tried again once, and the
  others go on. The job ends within 6 hours or fails.
- **Object lock.** The bucket has object lock in compliance mode for 30 days and a lifecycle rule
  that deletes an archive after 30 days. Nobody, the operator included, can delete or change an
  archive before then.
- **The realm in the archive.** The archive gets a third entry, `realm.json`: the tenant's realm with
  its users, role mappings, credentials and required actions, as the identity provider's export
  writes it. The credentials are hashes and authenticator secrets, so the entry is
  [secret](keys-and-data-protection.md#secret) data, and the archive's encryption covers it. A
  restore imports it into the new tenant's realm, so its people sign in as before, and needs no new
  invitation. An archive without `realm.json` restores as today.
- **Counts in the manifest.** The manifest gets `counts`: the row count of each table of the tenant
  database, read in the dump's snapshot.

**Weekly restore check.** Each Sunday at 03:00 in the region's time zone, a job checks that a restore
works.

1. It picks a tenant at random among those with an archive of the last 2 days, and takes its newest
   archive.
2. It restores it with `tenant restore` into a scratch tenant, `restore-check-<yyyymmdd>`, with a
   host under `Cell:InternalDomain` that no ingress serves, and a scratch realm.
3. It compares the row count of each table with `counts` in the manifest, checks that the realm has
   the archive's number of users, and runs the scenarios of each application of the tenant's Studio
   folder against the restored releases.
4. It deletes the scratch tenant, its realm and its keys at once, with no grace window.
5. It records the result in the metric `axis.backup.restore_check`, `1` for a pass and `0` for a
   failure, with the tenant id, and in a platform audit record. The [cell console](cell-console.md#views)
   shows it.
