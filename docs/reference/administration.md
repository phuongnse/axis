# Administration

*(Partly built for M9.)* Detailed reference for `Axis.Admin`, the command-line host
that a platform operator runs to create tenants, migrate tenant databases,
activate releases, and back up and restore tenants. It follows
[D43](../decisions.md#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed)
and
[D45](../decisions.md#d45-tenant-backup-and-restore--agreed).
Only `tenant create` is built. Every other command is still planned. Today a
tenant can also come from the `Tenants` section, where the server migrates and
activates at start, see
[startup activation](configuration.md#startup-activation).

The conventions of [architecture.md](../architecture.md) apply, and Dn refers
to [decisions.md](../decisions.md). In this page, "database role" always means
the PostgreSQL role.

`Axis.Admin` is the only host that writes the
[tenant directory](../architecture.md#tenant-directory), the only one that
holds an admin connection to the database server, and the only one that holds
the schema database role of a tenant under `Tenancy:Source=directory`. See
[database roles](storage.md#database-roles) for which host holds which
connection. There is no HTTP API for provisioning in M9.

## Settings

`Axis.Admin` reads its settings from the environment, as the other hosts do.

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Admin:ConnectionString` | `Admin__ConnectionString` | A database role with `CREATEDB` and `CREATEROLE`, not a superuser. It creates the tenant databases and roles. See [admin settings](configuration.md#admin-settings). |
| `ConnectionStrings:PlatformSchema` | `ConnectionStrings__PlatformSchema` | The role that owns the `platform` schema. The tool writes the tenant directory over it. |
| `Platform:SecretKey` | `Platform__SecretKey` | The installation key that encrypts the generated passwords and the OIDC client secret, see [platform database](storage.md#platform-database). |
| `Extensions:Path` | `Extensions__Path` | The installed extension packages. Only `activate` and `tenant restore` read it. |
| `Modules:Path` | `Modules__Path` | The installed modules. Only `activate` and `tenant restore` read it. |
| none | `AXIS_OIDC_CLIENT_SECRET` | The OIDC client secret of the new tenant. Only `tenant create` and `tenant restore` read it. It is never a command-line option, so it does not appear in a process list. |
| none | `PATH` | `tenant backup` and `tenant restore` run `pg_dump` and `pg_restore`, so both must be on `PATH`. Their version must match the database server. A missing tool exits with code 2. |

A missing or invalid setting exits with code 2 before any tenant work. The
message names the setting and never holds its value.

A new tenant's database goes on the same database server as
`Admin:ConnectionString`. The directory records that server's host and port.
M9 has a single database server.

## Output and exit codes

Every command writes one line per tenant and step to standard output:

```text
<tenant> <step> ok
<tenant> <step> failed: <message>
```

The step names are `entry`, `roles`, `database`, `grants`, `migrations`,
`role-check`, `version`, `compile`, `activate`, `seed`, `dump`, `archive`,
`check` and `restore`. A line names the
tenant and never a host, a password or a connection string. A `failed` message
never holds one either. A command that does no tenant work, such as a usage
error, writes its message to standard error.

| Exit code | Meaning |
| --- | --- |
| `0` | Every tenant succeeded. |
| `1` | At least one tenant failed. The other tenants still ran. |
| `2` | A usage or settings error before any tenant work: an unknown option, a missing setting, a superuser admin role or an unknown tenant. Nothing ran. |

So a script can tell "some tenants failed" from "nothing ran".

## Tenant create

*(Built for M9.)* `tenant create <id> --host <host>... --oidc-authority <url> --oidc-client-id <id>`
creates one tenant.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. It follows the rule of the `Tenants` section: lowercase letters, digits and hyphens. It is at most 50 characters. |
| `--host <host>` | A host of the tenant. Repeat it for more hosts. At least one is needed. |
| `--oidc-authority <url>` | The authority of the tenant's identity provider. |
| `--oidc-client-id <id>` | The OIDC client id. The client secret comes from `AXIS_OIDC_CLIENT_SECRET`. |

**Steps, in order.** Each step writes its own line.

1. `entry`: validate the id and check that the hosts are unique across all
   tenants, ignoring letter case. Write the directory entry with status
   `provisioning`.
2. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime`. Each gets a
   32-byte random password, stored encrypted in the directory as
   [D42](../decisions.md#d42-tenant-directory-in-the-platform-database--agreed)
   says.
3. `database`: create `axis_<id>`, owned by the schema role.
4. `grants`: run the grant step.
5. `migrations`: run the configuration, data and processes EF Core migrations.
6. `grants`: run the grant step again. Steps 4 to 6 run as the schema role, as
   [D31](../decisions.md#d31-separate-database-roles--agreed) describes.
7. `role-check`: run the runtime role check of D31 over the runtime role.
8. `version`: record the schema version and set the entry `active`.

Hosts serve the tenant after their next refresh, see
[tenant directory](../architecture.md#tenant-directory). The server serves a
new tenant, with its sign-in, after its next refresh. The worker does not read
the directory again, so it runs the tenant's work only after a restart. A new
tenant also has no application until `activate` is built.

**Settings and the platform tables.** The command checks its settings before any
tenant work. It refuses an admin role that is a superuser, or a member of one,
and an admin role without `CREATEDB` and `CREATEROLE`. It needs the `platform`
tables to exist, because it does not create them. Start the server once under
`Tenancy:Source=directory`, which creates them. Without the tables it exits
with code 2.

**What the steps do.**

- `roles` saves each generated password, encrypted, before it creates the role.
  It never alters a role that exists. A role that exists with no password
  stored for this tenant fails the step.
- `database` first gives the admin role `SET` membership, without `INHERIT`, in
  the schema role. PostgreSQL 16 and later need this before a role that is not
  a superuser can create a database owned by another role. The admin role
  already holds `ADMIN OPTION` on the roles it creates, so this adds no power.
  An existing database must already be owned by the schema role.
- `grants` first revokes `CONNECT` and `TEMPORARY` on the database from
  `PUBLIC`, and grants both only to the runtime role. Without this, any login
  role of the database server could connect to the tenant database.
- The command sets no claim names, endpoint audience or deployment binding.
  They stay empty, so the hosts use their defaults.

**Names.** The role and database names come from the id. PostgreSQL names hold
at most 63 bytes, and `axis_<id>_runtime` adds 13 characters, hence the limit of
50. An id with a hyphen, such as `acme-eu`, gives the quoted names
`"axis_acme-eu_schema"`, `"axis_acme-eu_runtime"` and `"axis_acme-eu"`. Quote
them in any SQL you run by hand.

**Running it again.**

- On a `provisioning` entry, it skips every step already done and finishes the
  rest. The `roles` step does not generate new passwords for roles that exist.
  The `entry` step replaces the entry's hosts and OIDC settings with the new
  arguments, so an operator can fix a typo before the tenant goes live. A
  `provisioning` tenant is never served, so this is safe. It never changes the
  stored passwords.
- A stop, such as Ctrl+C, writes a `failed` line for the current step and exits
  with code 1. The entry stays `provisioning`, and a rerun finishes it.
- On an `active` or `suspended` tenant, it fails with exit code 1 and changes
  nothing.

**Exit codes.** `0` when the tenant is `active`. `1` when a step failed, and
the entry stays `provisioning`. `2` for an invalid id, a missing option, a
missing `AXIS_OIDC_CLIENT_SECRET`, a superuser admin role, missing `platform`
tables or a host that another tenant holds. A host conflict is written as an
`entry failed` line that names no host, and it creates no entry, role or
database.

## Tenant suspend

*(Planned for M9.)* `tenant suspend <id>` sets the status of an `active` tenant to `suspended`
and raises its `version`.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. After that the tenant's hosts
answer `503`, and the worker claims none of its new work. Work already claimed
finishes. Suspending a `suspended` tenant is a no-op that exits `0`.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning`, or the
write failed. `2` for an unknown tenant.

## Tenant resume

*(Planned for M9.)* `tenant resume <id>` sets the status of a `suspended` tenant to `active` and
raises its `version`.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. Resuming an `active` tenant is a
no-op that exits `0`.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning`, or the
write failed. `2` for an unknown tenant.

## Tenant list

*(Planned for M9.)* `tenant list` prints a table with one row for each tenant, in ordinal order of
the id:

| Column | Meaning |
| --- | --- |
| `id` | The tenant id. |
| `status` | `provisioning`, `active` or `suspended`. |
| `hosts` | The tenant's hosts, separated by commas. |
| `schema version` | The recorded schema version, or `-` when it is empty. |

It takes no option. It never prints a password, an OIDC secret, a binding value
or a connection string.

**Exit codes.** `0` on success, also when there is no tenant. `1` when it
cannot read the directory.

## Tenant backup

*(Planned for M9, see [D45](../decisions.md#d45-tenant-backup-and-restore--agreed).)* `tenant backup <id> --out <file>` writes one
archive of one tenant.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |
| `--out <file>` | The archive to write. The file must not exist. |

The archive is one tar file with two entries: `manifest.json` and `tenant.dump`.
It holds no secret. No database password, OIDC client secret or `secret` binding
value is ever in it, so it can be stored like any data file.

**Steps, in order.**

1. `dump`: run `pg_dump --format=custom --no-owner --no-privileges` over the
   tenant database as its schema role. It reads one consistent snapshot, so the
   tenant keeps running and its hosts keep answering.
2. `archive`: write the manifest and the dump into `--out`.

A tenant in any status except `provisioning` can be backed up, also a
`suspended` one. A `provisioning` tenant has no complete database.

**Manifest.** `manifest.json` holds these fields:

| Field | Meaning |
| --- | --- |
| `format` | The archive format version. It is `1`. |
| `tenantId` | The id of the backed-up tenant. |
| `axisVersion` | The version of the `Axis.Admin` build that wrote the archive. |
| `schemaVersion` | The tenant's schema version at backup, see [Migrate](#migrate). |
| `createdAt` | The time of the backup, in UTC. |
| `directory` | The directory entry without any secret, see below. |
| `packages` | Every extension package version that a stored release pins. Each has `id`, `version` and `required`. |
| `modules` | Every module version that a stored release pins. Each has `id` and `version`. |

`directory` holds the status at backup, the hosts, the OIDC authority and client
id, the claim names, the endpoint audience and the bindings. A binding holds its
name, its kind and, for a kind other than `secret`, its `url` value. A `secret`
binding holds only its name and kind. The database server, the database name,
the role names and the `version` of the entry are not in the manifest, because a
restore creates new ones.

`required` is `true` for a package version that an active release, or a release
with a running or waiting process instance, pins. These are the releases that the
start check `AXC0146` rebuilds, see
[extension packages](extension-packages.md). Other stored releases are listed
with `required` set to `false`.

**Failures.** A failed dump or write exits `1` and leaves no partial file.

**Exit codes.** `0` when the archive is written. `1` when the dump or the write
failed. `2` for an unknown tenant, a `provisioning` tenant, an `--out` file that
exists, a missing option, a missing tool or a missing setting.

## Tenant restore

*(Planned for M9, see [D45](../decisions.md#d45-tenant-backup-and-restore--agreed).)* `tenant restore <file> --tenant <id> --host <host>...`
restores an archive into a new tenant.

| Option | Meaning |
| --- | --- |
| `<file>` | The archive that `tenant backup` wrote. |
| `--tenant <id>` | The id of the new tenant. It follows the rule of `tenant create`. |
| `--host <host>` | A host of the new tenant. Repeat it for more hosts. At least one is needed. |

The OIDC client secret comes from `AXIS_OIDC_CLIENT_SECRET`, as for
`tenant create`. The authority and client id come from the manifest.

**Refusals.** Each refusal happens before anything changes, exits `2` and names
what is missing:

- The archive is unreadable, has no `manifest.json` or `tenant.dump`, or has a
  `format` this build does not know.
- The archive's `schemaVersion` is newer than this build's, or is not the id of
  one of its migrations.
- The tenant id exists in the directory as `active` or `suspended`.
- A host belongs to another tenant, ignoring letter case.
- A package version with `required` set to `true` is not installed under
  `Extensions:Path`. The message lists every missing version, not only the first.
- `AXIS_OIDC_CLIENT_SECRET` is missing.

A module version that is not installed under `Modules:Path` is listed in the
manifest but never refuses a restore. A stored release holds its module files, so
it keeps working without the module installed.

**Steps, in order.** Each step writes its own line.

1. `check`: read the archive and make every check above.
2. `entry`: write the directory entry with status `provisioning`. It takes the
   new id and hosts, and everything else from `directory` in the manifest. A
   `secret` binding is restored by name and kind, without a value.
3. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime` with new random
   passwords, as `tenant create` does.
4. `database`: create `axis_<id>`, owned by the schema role.
5. `restore`: run `pg_restore --single-transaction --no-owner --no-privileges`
   as the schema role. It restores everything or nothing. It refuses a database
   that already holds tables.
6. `grants`: run the grant step. This builds every privilege for the new roles,
   as [D31](../decisions.md#d31-separate-database-roles--agreed) describes.
7. `migrations`: run the EF Core migrations. This step runs only when the
   archive's schema version is older than this build's.
8. `grants`: run the grant step again.
9. `role-check`: run the runtime role check of D31 over the runtime role.
10. `version`: record the schema version and set the entry `suspended`.

**Running it again.** On a `provisioning` entry, the same command skips every
step already done and finishes the rest, as `tenant create` does. A failed
`restore` step restored nothing, so the rerun restores again. On an `active` or
`suspended` tenant it is refused.

**Exit codes.** `0` when the tenant is `suspended` and restored. `1` when a step
failed, and the entry stays `provisioning`. `2` for a refusal above, a missing
option or a missing setting.

**After a restore.** The tenant is `suspended`, and its hosts answer `503`.

1. Set the OIDC client secret and every `secret` binding again. The archive
   holds none of them.
2. Run [`tenant resume <id>`](#tenant-resume).

No command sets a binding yet, see [Not in M9](#not-in-m9).

**Restoring over an existing tenant.** There is no in-place overwrite.

1. Run [`tenant suspend`](#tenant-suspend) on the tenant.
2. Restore the archive under a new id, with a temporary host.
3. Set the secrets again and resume the new tenant.
4. Move the hosts from the old tenant to the new one. No command does this yet,
   see [Not in M9](#not-in-m9).

The old tenant stays in the directory, because there is no tenant delete.

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
  [staging and the call](processes.md#staging-and-the-call).
- A connector without `idempotency` sends no key, so the system may act twice
  in either case.
- Sequence counters, inbound endpoint receipts and schedule state go back too.
  A number issued after the backup can be issued again, and an inbound request
  accepted after it is lost.

## Migrate

*(Planned for M9.)* `migrate [--tenant <id>] [--parallel <n>]` brings tenant databases to the schema
that this build of `Axis.Admin` ships.

| Option | Meaning |
| --- | --- |
| `--tenant <id>` | Migrate this tenant only. Without it, migrate every tenant. |
| `--parallel <n>` | Migrate up to `n` tenants at a time. It defaults to 1 and must be at least 1. |

It covers every tenant except `provisioning` ones, in ordinal order of the id,
`n` at a time. A `suspended` tenant is migrated too, so it is current when it is
resumed.

**Steps for each tenant.**

1. `grants`: run the grant step.
2. `migrations`: run the configuration, data and processes EF Core migrations.
3. `grants`: run the grant step again.
4. `role-check`: run the runtime role check of D31.
5. `version`: record the schema version in the directory.

Steps 1 to 3 run as the tenant's schema role and step 4 as its runtime role.
Step 5 writes over `ConnectionStrings:PlatformSchema`.

**Schema version.** A tenant's schema version is the id of the newest EF Core
migration across the configuration, data and processes contexts. It is stored
as text in the `schema_version` column of `platform.tenants`, and is empty
until the first migration. Migration ids sort by time, so the newest one names
the schema. It comes from the code, so no one raises a number by hand.

**Failures.** Each tenant migrates on its own. A failed tenant keeps its old
schema version, writes a `failed` line and does not stop the others. The command
exits `1` at the end. Running it again retries the failed tenants, and the
migrations that already ran do nothing.

**Upgrade order.** A host serves a tenant only when the tenant's schema version
is the host's own, see [tenant directory](../architecture.md#tenant-directory).
So an upgrade is:

1. Run `migrate`.
2. Deploy the new server and worker.

Between the two, every migrated tenant answers `503`, because its schema version
is not that of the old hosts. The worker claims none of its work in that time.
The window ends when the new hosts run. A tenant whose migration failed stays
at its old version, so the old hosts keep serving it, and the new hosts answer
`503` for it until `migrate` works for it.

**Exit codes.** `0` when every tenant succeeded, also when there is none. `1`
when at least one tenant failed. `2` for an unknown `--tenant`, a `--tenant`
that is `provisioning`, a `--parallel` below 1 or a missing setting.

## Activate

*(Planned for M9.)* `activate <folder> [--tenant <id>]` compiles an application folder and
activates its release.

| Option | Meaning |
| --- | --- |
| `<folder>` | The application folder. A relative path is resolved against the working directory. |
| `--tenant <id>` | Activate for this tenant only. It may be `active` or `suspended`. Without it, activate for every `active` tenant. |

It uses the same compile diagnostics, extension packages, modules, recompute
limit and seeding as
[startup activation](configuration.md#startup-activation), and does not repeat
them here. Packages come from `Extensions:Path` and modules from `Modules:Path`.
`Activation:InlineRecomputeLimit` applies as it does there.

**Steps for each tenant.**

1. `version`: check that the tenant's schema version is the one of this build.
   If it is not, the tenant fails with a message to run `migrate` first. Nothing
   else runs for it.
2. `compile`: compile the folder against the tenant database and store its
   release.
3. `activate`: activate the release.
4. `seed`: insert the seed records and write the schedule trigger state.

Steps 2 and 3 run as the tenant's schema role. Step 4 runs as its runtime role.
The tool runs no EF Core migrations. A diagnostic is written as a `failed` line
with its code, file and path.

**Failures.** Each tenant succeeds or fails on its own. The previously active
release of a failed tenant stays active, and the other tenants continue. So
tenants can run different releases until `activate` works for all of them. The
command exits `1` at the end.

**Exit codes.** `0` when every tenant succeeded, also when none is `active`. `1`
when at least one tenant failed. `2` for a folder that does not exist, an
unknown `--tenant`, a `--tenant` that is `provisioning` or a missing setting.

## What else to back up

*(Planned for M9, see [D45](../decisions.md#d45-tenant-backup-and-restore--agreed).)* A tenant archive is not a backup of the
installation. The operator also keeps these safe:

- **The platform database.** It holds the tenant directory. Back it up with
  standard PostgreSQL tools, such as `pg_dump` or continuous archiving. Axis does
  not back it up.
- **The installation key.** `Platform:SecretKey` decrypts every stored secret.
  Without it, no database password, OIDC client secret or `secret` binding
  value can be read, and every one must be set again by hand. Keep it apart from
  the backups, so a stolen backup does not hold its own key. See
  [platform database](storage.md#platform-database).
- **Installed packages and modules.** The folders `Extensions:Path` and
  `Modules:Path` hold the installed extension packages and modules. A restore
  needs the package versions that the archive marks `required`.

Point-in-time recovery and restoring into another installation are out of scope,
see [Not in M9](#not-in-m9).

## Version

*(Planned for M9.)* `version` prints the Axis version on one line. It takes no
option and needs no settings. It always exits `0`. The production image of the
admin host carries the same version in its OCI label, see
[installation](installation.md#images).

## Not in M9

- Deleting a tenant.
- An HTTP API for provisioning.
- Rotating the generated database passwords.
- Moving a tenant to another database server.
- Commands that set a tenant's deployment bindings, claim names or endpoint
  audience. D43 does not say how they are written. `tenant create` sets none
  of them, so they stay empty until a later issue decides.
- Point-in-time recovery of a tenant, and restoring a tenant into another
  installation.
- Overwriting a tenant in place with a restore.
- A command that moves a host from one tenant to another. Restoring over an
  existing tenant ends with this step.
