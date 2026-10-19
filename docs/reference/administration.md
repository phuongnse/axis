# Administration

*(Planned for M9.)* Detailed reference for `Axis.Admin`, the command-line host
that a platform operator runs to create tenants, migrate tenant databases and
activate releases. It follows
[D43](../decisions.md#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).
None of it is built. Today tenants come from the `Tenants` section, and the
server migrates and activates at start, see
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
| `Extensions:Path` | `Extensions__Path` | The installed extension packages. Only `activate` reads it. |
| `Modules:Path` | `Modules__Path` | The installed modules. Only `activate` reads it. |
| none | `AXIS_OIDC_CLIENT_SECRET` | The OIDC client secret of the new tenant. Only `tenant create` reads it. It is never a command-line option, so it does not appear in a process list. |

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
`role-check`, `version`, `compile`, `activate` and `seed`. A line names the
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

`tenant create <id> --host <host>... --oidc-authority <url> --oidc-client-id <id>`
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
[tenant directory](../architecture.md#tenant-directory).

**Names.** The role and database names come from the id. PostgreSQL names hold
at most 63 bytes, and `axis_<id>_runtime` adds 13 characters, hence the limit of
50. An id with a hyphen, such as `acme-eu`, gives the quoted names
`"axis_acme-eu_schema"`, `"axis_acme-eu_runtime"` and `"axis_acme-eu"`. Quote
them in any SQL you run by hand.

**Running it again.**

- On a `provisioning` entry, it skips every step already done and finishes the
  rest. The `roles` step does not generate new passwords for roles that exist.
- On an `active` or `suspended` tenant, it fails with exit code 1 and changes
  nothing.

**Exit codes.** `0` when the tenant is `active`. `1` when a step failed, and
the entry stays `provisioning`. `2` for an invalid id, a missing option, a
missing `AXIS_OIDC_CLIENT_SECRET` or a host that another tenant holds.

## Tenant suspend

`tenant suspend <id>` sets the status of an `active` tenant to `suspended`
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

`tenant resume <id>` sets the status of a `suspended` tenant to `active` and
raises its `version`.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. Resuming an `active` tenant is a
no-op that exits `0`.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning`, or the
write failed. `2` for an unknown tenant.

## Tenant list

`tenant list` prints a table with one row for each tenant, in ordinal order of
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

## Migrate

`migrate [--tenant <id>] [--parallel <n>]` brings tenant databases to the schema
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

`activate <folder> [--tenant <id>]` compiles an application folder and
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

## Not in M9

- Deleting a tenant.
- An HTTP API for provisioning.
- Rotating the generated database passwords.
- Moving a tenant to another database server.
- Commands that set a tenant's deployment bindings, claim names or endpoint
  audience. D43 does not say how they are written. The issue that builds
  provisioning decides.
