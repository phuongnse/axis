# Administration

Detailed reference for `Axis.Admin`, the command-line host
that a platform operator runs to create tenants, migrate tenant databases,
activate releases, rotate the installation key, and back up and restore tenants. It follows
[D43](../decisions/d43-tenant-provisioning-and-migrations-across-tenant-databases.md)
and
[D45](../decisions/d45-tenant-backup-and-restore.md).
The commands are `tenant create`, `tenant suspend`, `tenant resume`, `tenant rotate-passwords`,
`tenant list`, `tenant backup`, `tenant restore`, `tenant oidc set`, `tenant sign-in set`, `tenant binding list|set|remove`,
`tenant host add|remove|move`, `tenant email set|clear`, `tenant session end`, `migrate`, `activate`, `platform rekey`, `publisher` and `version`.
`platform rekey` is in [installation key](tenant-setting-commands.md#installation-key). `tenant suspend`, `tenant resume` and
`tenant list` are in [tenant status commands](tenant-status-commands.md). The
commands that change a tenant after it is created are in
[tenant setting commands](tenant-setting-commands.md). That page also has the client key options
of `tenant oidc set`. A tenant can also come from the `Tenants`
section, where the server migrates and activates at start, see
[startup activation](host-settings.md#startup-activation).

The conventions of [architecture.md](../architecture.md) apply, and Dn refers
to [decisions.md](../decisions.md). In this page, "database role" always means
the PostgreSQL role.

`Axis.Admin` is the only host that writes the
[tenant directory](../architecture.md#tenant-directory), the only one that
holds an admin connection to the database server, and the only one that holds
the schema database role of a tenant under `Tenancy:Source=directory`. The
[publisher](#publisher) is a command of this host, so it
holds that role as well. See [database roles](storage.md#database-roles) for
which host holds which connection. There is no HTTP API for provisioning.

## Settings

`Axis.Admin` reads its settings from the environment, as the other hosts do.

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Admin:ConnectionString` | `Admin__ConnectionString` | A database role with `CREATEDB` and `CREATEROLE`, not a superuser. It creates the tenant databases and roles. Only `tenant create`, `tenant restore` and `tenant rotate-passwords` read it, so `migrate` needs no admin role. See [admin settings](host-settings.md#admin-settings). |
| `ConnectionStrings:PlatformSchema` | `ConnectionStrings__PlatformSchema` | The role that owns the `platform` schema. The tool writes the tenant directory over it. |
| `Platform:SecretKey` | `Platform__SecretKey` | The installation key that encrypts the generated passwords and the OIDC client secret, see [platform database](storage.md#platform-database). |
| `Platform:PreviousSecretKey` | `Platform__PreviousSecretKey` | Optional. The old installation key during a rotation. Every command decrypts with it when `Platform:SecretKey` does not match. `platform rekey` needs it. A malformed value exits with code 2, naming the setting. See [installation key](tenant-setting-commands.md#installation-key). |
| `IdentityProvider:Url` | `IdentityProvider__Url` | The base address of [Axis's identity provider](identity-provider.md), such as `https://idp.example.com/`. `tenant create` of a main tenant reads it, and `tenant restore` reads it when it is set. `tenant restore` needs it when the archive's tenant signed in through its own realm, see [Tenant restore](#tenant-restore). It must be an absolute URL, and `https` outside `Development` and `Testing`. A path in it is kept. |
| `IdentityProvider:AdminClientId` | `IdentityProvider__AdminClientId` | The client of the identity provider's `master` realm that `tenant create` signs in as, with the client credentials grant, to create a tenant's realm. `tenant create` of a main tenant reads it, and so does `tenant restore` when it is set. |
| `IdentityProvider:AdminClientSecret` | `IdentityProvider__AdminClientSecret` | The secret of that client. `tenant create` of a main tenant reads it, and so does `tenant restore` when `IdentityProvider:Url` is set. It is never stored and never printed. |
| `Email:*` | `Email__...` | The host's mail settings, with the same keys as the worker's, see [mail settings](notifications.md#mail-settings). A tenant without mail settings of its own gets them in its realm's mail server, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). `tenant create`, `tenant restore`, `activate`, `publisher` and `tenant email set` and `clear` read them. A value that is not valid exits with code 2, naming the setting. Without them, a tenant that has no mail settings of its own gets no mail server in its realm. |
| `Tenants` | `Tenants__<id>__...` | The tenants of `Tenancy:Source=configuration`, with each `SchemaConnectionString` and `ConnectionString` and its bindings. Only `publisher` reads them, and only under that source. See [publisher](#publisher). |
| `DOTNET_ENVIRONMENT` | `DOTNET_ENVIRONMENT` | `Development` lets a `url` binding use `http` and a `secret` binding take its value from a configuration file, as on the server and the worker. Only `publisher` under the configuration source reads that. Every command reads it for the TLS check: outside `Development` and `Testing`, a command exits with code 2, naming the setting, when `Admin:ConnectionString`, `ConnectionStrings:PlatformSchema` or `Scenarios:ServerConnectionString` is set with an `SSL Mode` below `VerifyFull` or none. An unset value is `Production`. |
| `Scenarios:ServerConnectionString` | `Scenarios__ServerConnectionString` | A database role with `CREATEDB`, not a superuser, that owns no tenant database. Only `publisher` reads it, to run `test` requests. It creates and drops the `axis_scenario_<uuid>` databases of the scenarios. Empty means a `test` request fails with `AXC0217`, which names this setting. A value that is not a connection string exits with code 2. See [database roles](storage.md#database-roles). |
| `Extensions:Path` | `Extensions__Path` | The installed extension packages. Only `activate`, `publisher` and `tenant restore` read it. |
| `Modules:Path` | `Modules__Path` | The installed modules. Only `activate` and `publisher` read it. `tenant restore` never reads it, because a missing module refuses nothing. |
| `Tenancy:Source` | `Tenancy__Source` | `directory` for the tenant setting commands, which exit with code 2 under any other value. `publisher` reads it too, see [publisher](#publisher). |
| none | `AXIS_BINDING_SECRET` | The value of the `secret` binding that `tenant binding set --secret` sets. When it is unset, the value comes from standard input. It is never a command-line option. Only that command reads it. |
| none | `AXIS_SMTP_PASSWORD` | The SMTP password that `tenant email set --password` stores for the tenant. When it is unset, the password comes from the first line of standard input. It is never a command-line option. Only that command reads it. |
| none | `AXIS_OIDC_CLIENT_SECRET` | The OIDC client secret of a restored tenant that keeps the archive's sign-in. `tenant restore` reads it and needs it only then, because a new realm generates its own client secrets. `tenant create` does not read it. It is never a command-line option, so it does not appear in a process list. |
| none | `PATH` | `tenant backup` and `tenant restore` run `pg_dump` and `pg_restore`, so both must be on `PATH`. Their version must match the database server. A missing tool exits with code 2. |

A missing or invalid setting exits with code 2 before any tenant work. The
message names the setting and never holds its value.

`tenant suspend`, `tenant resume` and `tenant list` read only
`ConnectionStrings:PlatformSchema`. They decrypt nothing and create no role or
database, so they need none of `Platform:SecretKey`, `Admin:ConnectionString`,
the `IdentityProvider` settings or `AXIS_OIDC_CLIENT_SECRET`.

`platform rekey` reads only `ConnectionStrings:PlatformSchema`, `Platform:SecretKey` and
`Platform:PreviousSecretKey`. It needs no admin role, no client secret and no `Tenancy:Source`.

The `tenant binding`, `tenant host` and `tenant email` commands read `ConnectionStrings:PlatformSchema` and
need `Tenancy:Source=directory`. Only `tenant binding set --secret` and the two `tenant email` commands
also read `Platform:SecretKey`, because they encrypt, or, for `tenant email`, decrypt the password to copy it into the realm. See
[tenant setting commands](tenant-setting-commands.md).
`tenant session end` reads `ConnectionStrings:PlatformSchema` and `Platform:SecretKey`, because it connects to the tenant database as the schema role, and needs `Tenancy:Source=directory`. It needs no admin role.

`tenant backup` reads only `ConnectionStrings:PlatformSchema`,
`Platform:SecretKey` and `PATH`. It creates no role or database, so it needs
neither `Admin:ConnectionString` nor `AXIS_OIDC_CLIENT_SECRET`. The tool looks
for `pg_dump` in the directories of the `PATH` setting.

`tenant restore` reads the settings of `tenant create`, `AXIS_OIDC_CLIENT_SECRET`, `PATH` to find
`pg_restore`, and `Extensions:Path` to check the packages that the archive
requires. It needs `Extensions:Path` only when the archive requires a package. It
reads the `IdentityProvider` settings as optional. It needs them only when the archive's tenant
signed in through its own realm, and a value that is given must still be valid.
It needs `AXIS_OIDC_CLIENT_SECRET` only when the restored tenant keeps the archive's sign-in.
It reads the OIDC authority and client id from the archive, so it takes no
`--oidc-*` option.

A new tenant's database goes on the same database server as
`Admin:ConnectionString`. The directory records that server's host and port.
Axis supports a single database server.

## Output and exit codes

Every command writes one line per tenant and step to standard output:

```text
<tenant> <step> ok
<tenant> <step> failed: <message>
```

The step names are `entry`, `realm`, `roles`, `database`, `grants`, `migrations`,
`work`, `sessions`, `role-check`, `version`, `compile`, `activate`, `seed`, `indexes`, `dump`, `archive`,
`check`, `restore`, `status`, `runtime-password`, `schema-password` and `audit`. An `audit` step prints only when it fails, see
[platform audit records](tenant-setting-commands.md#platform-audit-records). The [publisher](#publisher)
adds `claim`, `validate` and `rollback`. A line names the
tenant and never a database host, a password or a connection string. A `failed` message
never holds one either. A restore refusal does name the tenant host the operator gave on the
command line, as typed. A failed `dump` step says only `pg_dump exited with code <n>`
and never repeats the text that `pg_dump` wrote, because that text can name the
database host. To see the cause, run `pg_dump` by hand. A command that does no tenant work, such as a usage
error, writes its message to standard error.

| Exit code | Meaning |
| --- | --- |
| `0` | Every tenant succeeded. |
| `1` | At least one tenant failed. The other tenants still ran. |
| `2` | A usage or settings error before any tenant work: an unknown option, a missing setting, a superuser admin role or an unknown tenant. Nothing ran. |

So a script can tell "some tenants failed" from "nothing ran".

## Tenant create

`tenant create <id> --host <host>... [--administrator <email>]`
creates one tenant and its realm.
`tenant create <id> --preview-of <main> --host <host>... [--binding <name>=<value>]...`
creates a [preview tenant](../architecture.md#preview-tenants) of a main tenant.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. It follows the rule of the `Tenants` section: lowercase letters, digits and hyphens. It is at most 50 characters. It is also the name of the tenant's realm, so `master`, the identity provider's own admin realm, is refused. |
| `--host <host>` | A host of the tenant. Repeat it for more hosts. At least one is needed. |
| `--administrator <email>` | Invites the person with this email as the tenant's first administrator, see [the first administrator](user-administration.md#the-first-administrator). Give it once. The email must be a plain address, such as `name@example.com`. It is a usage error together with `--preview-of`. |
| `--preview-of <main>` | Makes the tenant the preview tenant of the main tenant `<main>`. |
| `--binding <name>=<value>` | A `url` binding of the new tenant. Repeat it for more. The value is an absolute `http` or `https` URL. It is accepted only together with `--preview-of`. |

The command creates the tenant's realm in
[Axis's identity provider](identity-provider.md#keeping-the-realm-in-step), with its three clients, and sets the
tenant's `Oidc` block to the realm itself: the authority `<IdentityProvider:Url>/realms/<id>` and the client `axis`.
The command takes no `--oidc-*` option, and either one is a usage error. It does not read `AXIS_OIDC_CLIENT_SECRET`.
The admin host's `IdentityProvider` settings give it the identity provider's admin client. The realm has no
redirect addresses yet, so nobody can finish a sign-in there until M28 keeps them in step. With `--administrator <email>`, the realm gets one invited account. Its emailed link
names no address to return to, so the person can set a password and an authenticator and cannot yet sign in to Axis.

The command also turns accounts on in the tenant's
[platform sign-in](identity-provider.md#platform-sign-in).
_(planned for M28)_ `tenant host add`, `remove` and `move` do not yet change the realm, see
[keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). A preview tenant gets no realm.

**Preview tenant.** With `--preview-of`, the command creates
a [preview tenant](../architecture.md#preview-tenants) as
[D48](../decisions/d48-preview-tenants.md) agrees. The steps below stay
the same, and the entry also names its main tenant.

- It gets no realm and has no `realm` step. It needs none of the `IdentityProvider` settings,
  because the preview uses its main tenant's sign-in settings. An `--oidc-*`
  option is always a usage error, with or without `--preview-of`.
- `--binding` without `--preview-of` is a usage error. A binding that is not
  `<name>=<value>`, or whose value is not an absolute `http` or `https` URL, is
  a usage error. A binding name that repeats, ignoring letter case, is a usage
  error too. No binding value is printed.
- Without `--binding` the preview has no bindings. See
  [preview tenants](host-settings.md#preview-tenants).
- The command exits `2` when `<main>` is not in the directory, is itself a
  preview tenant, already has a preview tenant, or is the tenant itself. The
  directory stays unchanged. Two operators who create a preview of one main
  tenant at the same moment cannot both succeed: the second also exits `2`.
- The entry stores no sign-in settings of its own. The server reads them from
  the main tenant at every refresh, so a change to the main tenant's settings
  reaches the preview, and the client secret is stored only once. The preview
  never takes the main tenant's endpoint audience or service audience, so it accepts no
  machine-caller token. The preview uses the main tenant's settings whatever the
  main tenant's status.
- A rerun on a `provisioning` preview replaces its main tenant and its bindings,
  as it replaces its hosts.

**Steps, in order.** Each step writes its own line.

1. `entry`: validate the id and check that the hosts are unique across all
   tenants, ignoring letter case. Write the directory entry with status
   `provisioning`.
2. `realm`: generate the secrets of the realm's three clients, store them encrypted in the
   directory and point the entry's `Oidc` block at the realm. Then create the tenant's realm in the
   identity provider, or bring an existing one up to date. A preview tenant has no such step.
3. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime`. Each gets a
   32-byte random password, stored encrypted in the directory as
   [D42](../decisions/d42-tenant-directory-in-the-platform-database.md)
   says.
4. `database`: create `axis_<id>`, owned by the schema role.
5. `grants`: run the grant step.
6. `migrations`: run the configuration, data and processes EF Core migrations.
7. `grants`: run the grant step again. Steps 5 to 7 run as the schema role, as
   [D31](../decisions/d31-separate-database-roles.md) describes.
8. `role-check`: run the runtime role check of D31 over the runtime role.
9. `version`: record the schema version and, without `--administrator`, set the entry `active`.
10. `administrator`: only with `--administrator`. Create the invited account, give it the `administrator` platform
    role, write the `user.invited` and `user.roleGiven` records, have the realm email the link and set the entry `active`.

Hosts serve the tenant after their next refresh, see
[tenant directory](../architecture.md#tenant-directory). The server serves a
new tenant, with its sign-in, after its next refresh. The worker runs the
tenant's work after its next refresh. A new tenant also has no application
until `activate` has run for it.

**Settings and the platform tables.** The command checks its settings before any
tenant work. It refuses an admin role that is a superuser, or a member of one,
and an admin role without `CREATEDB` and `CREATEROLE`. It needs the `platform`
tables to exist, because it does not create them. Start the server once under
`Tenancy:Source=directory`, which creates them. Without the tables it exits
with code 2.

**What the steps do.**

- `realm` saves three random secrets, each 32 bytes written as 64 lowercase hex characters, encrypted, before it calls
  the identity provider: the secret of the `axis` client, which is the entry's `OidcClientSecret`, and those
  of `axis-activation` and `axis-users`. It saves the entry's `Oidc` authority and client id `axis` at the same
  time. A stop between the save and the call never loses a secret. The realm's name is the tenant id, and its display
  name is the tenant id too. The step signs in to the `master` realm with the admin client, creates the realm
  with sessions of at most 8 hours, `en` and `vi` and the three clients, and gives the activation client `manage-realm`
  among its rights, and its mail server, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). A failure names
  the step of the identity provider and its status code, never a response body or a secret.
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
- The command sets no claim names, endpoint audience or service audience. They stay empty, so the
  hosts use their defaults. [`tenant oidc set`](tenant-setting-commands.md#sign-in-settings) sets
  the audiences later. It sets no deployment binding either, except the
  `url` bindings that `--binding` gives a preview tenant.

**Names.** The role and database names come from the id. PostgreSQL names hold
at most 63 bytes, and `axis_<id>_runtime` adds 13 characters, hence the limit of
50. An id with a hyphen, such as `acme-eu`, gives the quoted names
`"axis_acme-eu_schema"`, `"axis_acme-eu_runtime"` and `"axis_acme-eu"`. Quote
them in any SQL you run by hand.

**Running it again.**

- On a `provisioning` entry, it skips every step already done and finishes the
  rest. The `roles` step does not generate new passwords for roles that exist.
  The `entry` step replaces the entry's hosts, main tenant and bindings with the new
  arguments, so an operator can fix a typo before the tenant goes live. A
  `provisioning` tenant is never served, so this is safe. It never changes the
  stored passwords, and the `realm` step never changes the stored client secrets. It updates the
  realm that it made.
- A stop, such as Ctrl+C, writes a `failed` line for the current step and exits
  with code 1. The entry stays `provisioning`, and a rerun finishes it.
- With `--administrator`, the entry stays `provisioning` until the invitation has gone out, so a rerun retries a failed
  `administrator` step. The rerun finds the account by its email, so there is never a second account or a second
  `user.invited` record. It sends the email again only when the account has no password yet.
- On an `active` or `suspended` tenant, it fails with exit code 1 and changes
  nothing.

**Exit codes.** `0` when the tenant is `active`. `1` when a step failed, and
the entry stays `provisioning`. `2` for an invalid id, the id `master`, a missing option, an
`--oidc-*` option, a missing or invalid `IdentityProvider` setting of a main tenant, a superuser admin role, missing `platform`
tables or a host that another tenant holds. `2` also covers an `--administrator` that is not a valid
email address, that repeats or that comes with `--preview-of`. For a preview tenant, `2` also covers
a usage error of `--preview-of` or `--binding`, and a main tenant that is
unknown, is itself a preview tenant, already has a preview tenant or is the
tenant itself. A host conflict is written as an
`entry failed` line that names no host, and it creates no entry, role or
database.

## Tenant backup

`tenant backup <id> --out <file>` writes one
archive of one tenant. See [D45](../decisions/d45-tenant-backup-and-restore.md).

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |
| `--out <file>` | The archive to write. The file must not exist, and its folder must exist. Only its owner can read it, because it holds all of the tenant's business data. |

The archive is one tar file with two entries: `manifest.json` and `tenant.dump`.
It holds no secret. No database password, OIDC client secret or `secret` binding
value is ever in it, so it can be stored like any data file. _(planned for M27)_ The archive is encrypted with a backup key that the operator keeps outside the installation and gives as `--backup-key-file <file>`, and `tenant restore` needs the same key. The archive then carries the tenant's data keys. See [encrypted backups](data-encryption.md#encrypted-backups).

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
exists, a missing option, a missing tool or a missing setting.

## Tenant restore

`tenant restore <file> --tenant <id> --host <host>...`
restores an archive into a new tenant. See [D45](../decisions/d45-tenant-backup-and-restore.md). _(planned for M27)_ It also takes `--backup-key-file <file>` and refuses an archive that is not encrypted, see [encrypted backups](data-encryption.md#encrypted-backups).

| Option | Meaning |
| --- | --- |
| `<file>` | The archive that `tenant backup` wrote. |
| `--tenant <id>` | The id of the new tenant. It follows the rule of `tenant create`. |
| `--host <host>` | A host of the new tenant. Repeat it for more hosts. At least one is needed. |

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
- The archive's `schemaVersion` is newer than this build's, or is not the id of
  one of its migrations.
- The tenant id exists in the directory as `active` or `suspended`.
- A host belongs to another tenant, ignoring letter case.
- The archive is of a preview tenant. Preview data is
  disposable, so the operator creates the preview again with
  [`tenant create --preview-of`](#tenant-create). An archive written before
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
and version. When the archive is unreadable or its format is unknown, the schema
version and package checks are skipped, because the manifest cannot be trusted.
The tenant id and host checks still run. A missing or invalid
setting, a missing `pg_restore` and a usage error go to standard error. Both exit
`2`, and nothing has changed.

A module version that is not installed under `Modules:Path` is listed in the
manifest but never refuses a restore. A stored release holds its module files, so
it keeps working without the module installed.

**Steps, in order.** Each step writes its own line.

1. `check`: read the archive and make every check above. It asks the identity provider whether the archive's realm exists.
2. `entry`: write the directory entry with status `provisioning`. It takes the
   new id and hosts, and everything else from `directory` in the manifest. A `--host` that matches a `sites` entry's `host`, ignoring letter case, gets its site. A tenant that gets a new realm has empty sign-in settings until the `realm` step. A
   `secret` binding is restored by name and kind, without a value. A `url` or `storage`
   binding is restored with its value. The check refuses an archive whose `storage` binding has
   a value other than `database`.
3. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime` with new random
   passwords, as `tenant create` does.
4. `database`: create `axis_<id>`, owned by the schema role.
5. `restore`: run `pg_restore --single-transaction --no-owner --no-privileges`
   as the schema role. It restores everything or nothing. The tool streams
   `tenant.dump` from the archive to the standard input of `pg_restore`, so no
   unpacked copy of the data is left on disk. The schema role's password goes
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
`restore` step restored nothing, so the rerun restores again. A database that
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

**Platform migrations first.** Before it reads the tenants, `migrate` applies the pending EF Core
migrations of the `platform` schema over `ConnectionStrings:PlatformSchema`. So an installation
that upgrades with `migrate` before the new server starts has the audit table in time. It does not
run the grants for the reader role, which the server applies at its next start. A failed platform
migration exits `2`, with the message of the setting that could not be used.

**Steps for each tenant.**

0. `audit`: write the [platform audit record](tenant-setting-commands.md#platform-audit-records)
   of the tenant. It prints a line only when it fails, and that tenant then stops.
1. `grants`: run the grant step.
2. `migrations`: run the configuration, data and processes EF Core migrations.
3. `grants`: run the grant step again.
4. `role-check`: run the runtime role check of D31.
5. `version`: record the schema version in the directory.

Steps 1 to 3 run as the tenant's schema role and step 4 as its runtime role.
Step 5 writes over `ConnectionStrings:PlatformSchema`. `migrate` reaches each
tenant database on the host and port that the directory records for it. It reads
only `ConnectionStrings:PlatformSchema` and `Platform:SecretKey`. It does not
read `Admin:ConnectionString` or `AXIS_OIDC_CLIENT_SECRET`, so a routine upgrade
needs no admin role. A tenant stops at its first failed step. Each step writes
one line, `<tenant> <step> ok` or `<tenant> <step> failed: <message>`, and with
`--parallel` the lines of different tenants can interleave.

**No platform tables.** When the `platform` tables do not exist, there is no
tenant to migrate. `migrate` writes a note to standard error and exits `0`. So
the install can run it before the first server start, see
[installation](installation.md).

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

**Exit codes.** `0` when every tenant succeeded, also when there is none or when
the `platform` tables do not exist. `1`
when at least one tenant failed. `2` for an unknown `--tenant`, a `--tenant`
that is `provisioning`, a `--parallel` below 1 or a missing setting.

## Activate

`activate <folder> [--tenant <id>]` compiles an application folder and
activates its release. A running server serves the new release at its next request,
with no restart.

| Option | Meaning |
| --- | --- |
| `<folder>` | The application folder. A relative path is resolved against the working directory. |
| `--tenant <id>` | Activate for this tenant only. It may be `active` or `suspended`. Without it, activate for every `active` tenant. |

It uses the same compile diagnostics, extension packages, modules, recompute
limit and seeding as
[startup activation](host-settings.md#startup-activation), and does not repeat
them here. Packages come from `Extensions:Path` and modules from `Modules:Path`.
`Activation:InlineRecomputeLimit` applies as it does there, and a value that is
not a positive whole number is a settings error. An unset `Extensions:Path` or
`Modules:Path` gives an empty catalog, as on the server. A set path whose folder
does not exist is a settings error that names the setting and not the path.
A problem of an installed package or module is written to standard error.
The tool always refuses an `http` URL binding, because `http` is allowed only in
Development, which uses startup activation. On a [preview tenant](../architecture.md#preview-tenants),
a missing binding is an `AXC0100` warning and not an error, as
[preview tenants](host-settings.md#preview-tenants) describe.

**Steps for each tenant.**

1. `version`: check that the tenant's schema version is the one of this build.
   If it is not, the tenant fails with a message to run `migrate` first. Nothing
   else runs for it.
2. `compile`: compile the folder against the tenant database and store its
   release.
3. `activate`: activate the release. This adds a row with the actor `operator`
   to the [release history](storage.md#release-history), unless the release is
   already the active one. After the activation has committed, the step brings the
   tenant's realm in step with the tenant's applications: it adds, changes and
   removes the realm's `site-` clients and `oidc-` and `saml-` providers, see
   [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). It
   writes no line of its own. A tenant without an activation secret, such as a
   preview tenant, skips the update. A failure of the identity provider fails the
   step with a line that names the identity provider step and its status code, and
   never a secret. The command reads the identity provider's address and the
   activation secret from the tenant's own entry, so it needs no `IdentityProvider`
   settings. The update also writes the realm's mail server from the tenant's mail settings or the host's `Email` settings. It reaches the identity provider as `tenant create` does, so the admin
   host must trust its certificate.
4. `seed`: insert the seed records and write the schedule trigger state.
5. `indexes`: build the non-unique indexes that activation left out of a table
   with more than `Activation:InlineRecomputeLimit` rows, with
   `CREATE INDEX CONCURRENTLY`. An index that PostgreSQL marks invalid, for
   example after a cancelled build, is dropped and built again. The step holds
   the tenant's schema lock while it builds, so other activations of the tenant
   wait. See [Schema planning](storage.md#schema-planning).

Steps 2, 3 and 5 run as the tenant's schema role. Step 4 runs as its runtime role.
The tool runs no EF Core migrations. Each step writes one `ok` line. Each error
diagnostic of a failed step is written as its own line,
`<tenant> <step> failed: <code> <file> <path>: <message>`, so one run shows every
problem. The warnings of a step that succeeds go to standard error, as
`<tenant> warning <code> <file> <path>: <message>`. A line never names a host,
a password or a connection string.

**Failures.** Each tenant succeeds or fails on its own. The previously active
release of a failed tenant stays active, and the other tenants continue. So
tenants can run different releases until `activate` works for all of them. The
command exits `1` at the end. A `seed` failure happens after activation has
committed, so the new release stays active, as it does with startup activation.
An `indexes` failure also happens after activation has committed, so the new
release stays active, and the next activation builds the index again. Only a
`compile` or `activate` failure keeps the previous release. A failure of the
realm update also happens after activation has committed, so the new release
stays active. The `seed` and `indexes` steps do not run until `activate` is run
again, which updates the realm again and then runs them.

**Exit codes.** `0` when every tenant succeeded, also when none is `active`. `1`
when at least one tenant failed. `2` for a folder that does not exist, an
unknown `--tenant`, a `--tenant` that is `provisioning`, a bad option or a missing
or bad setting.

## Publisher

`publisher` runs the [publish requests](authoring.md#publishing)
that Studio stores, as [D47](../decisions/d47-publishing-from-studio.md)
agrees. It is the only host that activates a release on the author's behalf, so
the server never needs a tenant schema connection. Unlike the other commands, it
runs until it is stopped.

`publisher`

It takes no option. It writes no [platform audit record](tenant-setting-commands.md#platform-audit-records),
because it acts on no tenant on its own: the audit of a publish is the release's own record in the tenant
database. It has no HTTP endpoint and listens on no port, so nothing
can call it. The `axis-admin` image runs it. Both production installs start
one, see [installation](installation.md). `scripts/dev.sh`, the Docker Compose
file in the repository root and the E2E script each start one too.

**What is built.** The command runs `publish`, `validate`, `rollback` and `test` requests for
`Tenancy:Source=directory` and for `Tenancy:Source=configuration`. It also runs
`publish` requests with target `preview`, see [preview requests](#publisher).

**Test requests.** The command also runs `test` requests,
which run an application's [test scenarios](test-scenarios.md) from
Studio, as [D55](../decisions/d55-running-test-scenarios-from-studio.md)
agrees. See [test](authoring.md#test) for the route.

**Settings.** It reads these, with the meaning they have for the other commands:

- `Tenancy:Source`, `configuration` or `directory`. An unset value is
  `configuration`, as for the server and the worker. Any other value exits with
  code 2 with a message that names the setting.
  - Under `directory` it reads `ConnectionStrings:PlatformSchema` and
    `Platform:SecretKey`, and decrypts the schema role of each tenant. See
    [database roles](storage.md#database-roles).
  - Under `configuration` it reads the `Tenants` section, as the server does, and
    connects to each tenant with its `SchemaConnectionString`. It seeds over its
    `ConnectionString`. It also reads `DOTNET_ENVIRONMENT`. A `Tenants` section
    that is not valid, or a tenant with no `SchemaConnectionString`, exits with
    code 2. The message names the setting and never holds a value.
- `Tenancy:RefreshInterval`, how often it reads the tenant directory again. It
  reads and checks it only under `directory`. The `Tenants` section never
  changes while the process runs.
- `Extensions:Path` and `Modules:Path`, as `activate` reads them. An unset path
  gives an empty catalog, and a set path whose folder does not exist is a
  settings error.
- `Activation:InlineRecomputeLimit`, as for `activate`.
- `Scenarios:ServerConnectionString`, the role that creates
  the scenario databases of `test` requests. It is optional. The publisher
  starts without it, and a `test` request then fails with `AXC0217`, which
  names the setting. An empty value counts as not set. A value that is not a
  connection string exits with code 2, and the message names the setting and
  never holds the value.

A missing or invalid setting exits with code 2 before any request runs. It
polls once a second. That interval is fixed and has no setting.

**Which tenants.** It serves only `active` tenants whose schema version is the
one of this build, and it reads the directory again every
`Tenancy:RefreshInterval`. A `suspended`, `provisioning` or out of date tenant
is skipped, and its requests stay `queued`. It runs no migration of the tenant
schema itself. `activate` does none either.

Under `configuration` it serves each tenant of the `Tenants` section once the
server has migrated that tenant's database, as the worker does. A tenant counts as
migrated when none of the configuration, data and processes contexts has a
pending migration. Until then it writes `<tenant> waiting: the database is not
migrated yet.` once, skips the tenant and checks again each poll round. When the
database is migrated it writes `<tenant> ready` once, and claims requests from
then on. It does not check a tenant again after that, so a later schema change
needs a restart of the publisher. The `directory` source writes neither line.

**Claiming.** Each poll round, for each served tenant, it claims the oldest
`queued` `publish`, `validate` or `rollback` request with `FOR UPDATE SKIP LOCKED`.
A second loop of the same process does the same for `test` requests, see below. It sets the request
`running`, `claimed_by` to its own name, `lease_until` and adds 1 to `claims`. A
`running` request whose lease has run out is claimed the same way.

- **Test requests.** `test` requests form their own queue
  for each tenant and application. At most one runs at a time, oldest first,
  independent of the other kinds. A `test` does not wait behind a running
  `publish`, `validate` or `rollback`, and none of them waits behind a `test`.
  The claim that skips an application with a `running` request therefore counts
  only the requests of the same queue. The publisher runs the two queues in two
  polling loops of one process, so a long test holds up no publish. Only the
  first loop reads the tenants again, and both write to the same output.
- **Target.** A request with target `tenant` is claimed as above. A request with
  target `preview` is claimed only when it is a `publish`, and only when the
  tenant has no preview tenant, or when the publisher serves that preview
  tenant. A `validate` or `rollback` with target `preview` is never claimed, and
  stays `queued`. A preview request that is not claimed does not block the
  later requests of its application.

- **One at a time.** For one tenant and application it runs one request at a
  time, oldest first, so when two publishes are queued the newer one ends up
  live, and a rollback that is queued after a publish runs after it. A `validate` waits behind a running `publish` of the same application. A claim skips an application that already has a `running` request.
  Requests of other applications and tenants can run at the same time. Each
  claim takes a lock of the tenant database before it reads the queue, so two
  publishers cannot claim two requests of one application at the same moment.
- **Lease.** The lease is five minutes, fixed. The publisher renews it every
  third of the lease while the request runs, so a long request is not claimed
  twice. A `running` request whose lease has run out is claimed again by any
  publisher, because its publisher died. A request that has been claimed three
  times and whose lease runs out again ends `failed` with `AXC0215`, see the
  [lifecycle](authoring.md#lifecycle). A `publish` gets a
  `release.publishFailed` audit record with it, a `rollback` gets a
  `release.rollbackFailed` one, and a `validate` gets none.
  The `claims` count is the fencing token. Renewal and the final update change
  the request only while its status is `running` and `claims` is still the value
  this publisher claimed. A publisher that lost its claim writes a
  `claim failed` line and no audit record.
- **Two publishers.** More than one publisher is safe, because the claim skips
  rows that another publisher holds. The production installs run one.

**Steps for each request.** Each step writes its own line, as in
[Output and exit codes](#output-and-exit-codes), and runs as the tenant's schema
role unless it says otherwise.

1. `claim`: claim the request, as above. A tenant with nothing to claim writes
   no line. A claim that fails writes `<tenant> claim failed: <message>`, and the
   publisher goes on with the next tenant.
2. `version`: check that the tenant's schema version is the one of this build.
3. For `publish`: `compile` the snapshot against the tenant database and store
   its release, `activate` the release, then `seed` and `indexes`. These are the
   steps of [activate](#activate) and report the same diagnostics. `seed` runs
   as the runtime role, and `indexes` as the schema role. The `activate` step also updates the tenant's realm after the
   activation has committed, as in [activate](#activate). The compile reads the snapshot's files, never the application
   folder on any disk. A snapshot that cannot be read, because it is not an array of
   `{ file, version, content }` or names a file twice, fails at `compile`
   and stores no release. Activation adds a row to the
   [release history](storage.md#release-history) with the requesting user, the
   label, the note and the request id, unless the release is already active.
4. For `validate`: run the `compile` and every check of
   `activate` against the tenant inside one transaction, then roll the
   transaction back. It stores no release, adds no history row and seeds
   nothing, and it starts no schedule trigger. It reports the diagnostics of
   activation, so a clean `validate` means an activation with the same input
   would pass its checks, apart from the seed. It builds no index and prints no
   `indexes` line. The step is called `validate`, and it is the only step after
   `version`. A snapshot that cannot be read
   fails at `validate`. The transaction holds the schema lock until it rolls
   back, so other activations of the tenant wait for it.
5. For `rollback`: the steps are `version`, `rollback`, `activate`, `seed` and `indexes`.
   The `rollback` step reads the stored release the request names and rebuilds
   its model from the stored files, as the server does when it serves a release.
   It reads no folder, compiles nothing and stores no new release. It refuses a
   release that pins a package version that is not installed under
   `Extensions:Path`, with `AXC0146`. A release that is missing, or that belongs
   to another application, fails the step too. The `activate` step then runs
   every activation check, so a migration that was applied after the release
   and is missing from it is `AXC0174`, and a field that the release lacks but
   the tables hold is `AXC0015`. A refused rollback changes nothing. The
   activation adds a row to the [release history](storage.md#release-history)
   with the requesting user and the request id, no label and no note, unless
   the release is already active. The step then updates the tenant's realm to match the
   rolled back release. `seed` then sets the schedule triggers and
   the seed records of that release, and `indexes` builds the indexes of that release
   that its tables lack. See [rollback](authoring.md#rollback).
6. For `test`: `version`, then the step `test`. It checks, in this order, that
   the setting is set (`AXC0217`), that the snapshot can be read, that it
   compiles, and that every name in the request is a scenario of the
   application (`AXC0218`, once for each unknown name). The first check that
   fails ends the request, and no database is created. Then, for each chosen
   scenario in file order, or for every scenario when the request names none,
   one after another:
   1. create the database `axis_scenario_<uuid>` over
      `Scenarios:ServerConnectionString`;
   2. migrate it, compile and activate the snapshot there, and apply its seed
      data;
   3. run the steps with the stubs, as `Axis.Scenarios` does;
   4. drop the database, pass or fail.

   Every step of a scenario runs as the scenario role. The publisher drops only
   databases it created. The work item limit of D32 applies to each scenario,
   see [test scenarios](test-scenarios.md). When the snapshot
   cannot be read or compiled, no scenario runs and the request fails with the
   diagnostics. The same happens for a name in the request that no scenario
   has, and when the setting is empty. Every compile diagnostic, warnings
   included, is stored in `diagnostics`. A failing scenario is stored only in
   `scenarios`. The step writes `<tenant> scenario <name> passed` or
   `<tenant> scenario <name> failed: <failure>` for each scenario, and fails
   when one failed. It then stores the outcomes of the
   scenarios and the request's status over the tenant's schema role. That is the
   only use of the tenant database by a `test`. The scenarios of a request may run
   for 10 minutes, counted from the first scenario of each claim. Past that, the
   scenario that runs fails with a message that names the limit, its database is
   dropped, and the request ends `failed` with `AXC0219`. The scenarios that
   finished keep their outcomes, and those that had not started get none. The step
   writes `<tenant> test failed: AXC0219 …`. A `test` gets no audit record
   and no release id.
7. Write the audit record of a `publish` or a `rollback`, with
   the request's final status, then set the request `succeeded` or `failed` and
   store its diagnostics and its release id. These happen in one transaction.
   A `validate` gets its final status and diagnostics the same way, with no
   audit record and no release id.
   The diagnostics are all of them, warnings included, as in the
   [authoring API](authoring.md#diagnostics). A request that fails with an
   exception rather than diagnostics, such as a lost connection, stores only the
   diagnostics gathered so far, which can be none.

**Preview requests.** A request with target `preview` is stored in the main
tenant's database, see [preview](authoring.md#preview). Only a `publish` is
claimed with that target. The publisher runs its steps against the main tenant's
preview tenant, with that tenant's database, schema role and bindings. It
compiles, activates and seeds there, and adds the history row to the preview
tenant's release history with the request id. A preview tenant has no activation
secret, so the `activate` step does not update a realm. A `validate` and a `test`
never update a realm. The step lines name the preview
tenant, for example `default-preview compile ok`. It writes no audit record. It
sets the request `succeeded` or `failed` in the main tenant's database, with the
renewal of the lease, and leaves the main tenant's release, release history and
audit records as they were. It fails the request with `AXC0216` when the main
tenant has no preview tenant, and writes `<tenant> preview failed: AXC0216 <message>`.
A preview tenant that exists but is not served, because it is not `active`, not
at this build's schema version, or, under `configuration`, not migrated yet,
leaves its requests `queued` until it is served. A preview request that runs out of
claims ends `failed` with `AXC0215` and gets no audit record. Preview and main
tenant requests of one application share the one-at-a-time queue of the main
tenant's database.

Activating the same content hash twice changes nothing, so a request that runs
again does no harm. An activation of the release that is
already active adds no history row.

**Failures.** A request fails on its own and does not stop the others. The
previously active release stays active after a failed `compile` or `activate`.
A `seed` failure happens after activation has committed, so the new release
stays active and the request ends `failed` with its `releaseId`, as with
`activate`. So does an `indexes` failure, and so does a failure of the realm
update: the new release stays active, and `seed` and `indexes` do not run until
the next activation of the tenant. The publisher calls the identity provider of
each tenant that has a realm, so it must reach it and trust its certificate.

**Stopping.** On `SIGTERM` or Ctrl+C the publisher stops claiming, finishes the
request it is running, then exits. A running `test` request is the exception: a
test may run for 10 minutes, which is longer than the stop grace period of both
installs. The publisher stops it at once. The running scenario fails, its
scenario database is dropped, and the request ends `failed` with `AXC0220`. The
step writes `<tenant> test failed: AXC0220 …`. A `publish`, `validate` or
`rollback` still finishes first. A stop that comes mid-request leaves a lease
that runs out. Another publisher then claims the request again. The platform's shutdown grace period must be longer than the longest
`publish`, `validate` or `rollback`, or the request is claimed twice.

**Leftover databases.** A publisher that crashes in the
middle of a scenario leaves its `axis_scenario_<uuid>` database behind. The
publisher does not clean it up, because with several publishers none can tell
which leftover database is still in use. The operator drops the
`axis_scenario_*` databases that the scenario role owns, once no publisher runs
a `test`.

**Exit codes.** `0` after a stop on `SIGTERM` or Ctrl+C. `1` when it cannot read
its tenants at start or stops on an error that it cannot recover from. `2` for a
usage error or a missing or bad setting, which includes a tenant under
`configuration` that has no `SchemaConnectionString`. A request that fails is not an exit
code, because it is stored on the request.

## What else to back up

A tenant archive is not a backup of the
installation. See [D45](../decisions/d45-tenant-backup-and-restore.md). The operator also keeps these safe:

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
see [Not built yet](#not-built-yet).

## Platform rekey

`platform rekey` re-encrypts every tenant secret and every stored data protection key with
`Platform:SecretKey`, after the installation key changed. It works under both tenancy sources, takes
no other argument and writes one platform audit record with no tenant. It prints one line,
`platform rekey ok: <n> tenant secrets, <m> data protection keys`. The steps, the exit codes and
the locks are in [installation key](tenant-setting-commands.md#installation-key).

## Version

`version` prints the Axis version on one line. It takes no
option and needs no settings. It always exits `0`. The production image of the
admin host carries the same version in its OCI label, see
[installation](installation.md#images).

## Not built yet

- Deleting a tenant.
- An HTTP API for provisioning.
- Moving a tenant to another database server.
- Point-in-time recovery of a tenant, and restoring a tenant into another
  installation.
- Overwriting a tenant in place with a restore.
- The platform audit records of the sign-in setting commands are planned with them, see
  [platform audit records](tenant-setting-commands.md#platform-audit-records).
